using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// Watches the chat stream for FFXIV trade notifications and converts the
/// bet-auto-fill / bank-deposit / bank-withdraw prompts into a single
/// discriminated outcome. Owns the pending partner / received-gil / given-gil
/// state that used to live as four mutually-coupled fields on MainWindow.
/// </summary>
internal sealed partial class TradeMonitor
{
    public abstract record Outcome
    {
        public sealed record None              : Outcome;
        public sealed record PromptBankDeposit (int Pi, long Gil) : Outcome;
        public sealed record PromptBankWithdraw(int Pi, long Gil) : Outcome;
        // Bidirectional trade: both sides put gil in the window. Confirms the
        // give as a withdrawal and the receive as a deposit so neither leg is
        // silently absorbed.
        public sealed record PromptTwoSided    (int Pi, long Gave, long Received) : Outcome;
        public sealed record Canceled          : Outcome;
    }

    [GeneratedRegex(@"^Trade request sent to (.+)\.$")]
    private static partial Regex TradeSentRegex();

    [GeneratedRegex(@"^(.+) wishes to trade with you\.$")]
    private static partial Regex TradeWishesRegex();

    [GeneratedRegex(@"^You receive ([\d,]+) gil\.$")]
    private static partial Regex TradeGilRegex();

    [GeneratedRegex(@"^You hand over ([\d,]+) gil\.$")]
    private static partial Regex GaveGilRegex();

    private (string FullName, string World)? pendingPartner;
    private long                              pendingTradeGil;
    private long                              pendingGaveGil;

    /// <summary>
    /// Process one incoming chat line. Updates internal pending state and, on
    /// "Trade complete." / "Trade canceled.", emits the corresponding
    /// <see cref="Outcome"/>. All other lines return <see cref="Outcome.None"/>.
    /// </summary>
    public Outcome OnChat(string text, PlayerPayload? payload, GameState state, Configuration cfg)
    {
        if (!cfg.AutoDepositFromTrades) return new Outcome.None();

        var sentMatch = TradeSentRegex().Match(text);
        if (!sentMatch.Success) sentMatch = TradeWishesRegex().Match(text);
        if (sentMatch.Success)
        {
            pendingPartner = payload != null
                ? (payload.PlayerName, payload.World.ValueNullable?.Name.ToString() ?? string.Empty)
                : (sentMatch.Groups[1].Value, string.Empty);
            return new Outcome.None();
        }

        if (TradeGilRegex().Match(text) is { Success: true } m2
            && long.TryParse(m2.Groups[1].Value.Replace(",", ""), out var gil))
        {
            pendingTradeGil = gil;
            return new Outcome.None();
        }

        if (GaveGilRegex().Match(text) is { Success: true } m3
            && long.TryParse(m3.Groups[1].Value.Replace(",", ""), out var gave))
        {
            pendingGaveGil = gave;
            return new Outcome.None();
        }

        if (text is "Trade canceled." or "Trade cancelled.")
        {
            Reset();
            return new Outcome.Canceled();
        }

        if (text != "Trade complete." || pendingPartner is null) return new Outcome.None();

        var (fullName, world) = pendingPartner.Value;
        var pi = Enumerable.Range(0, state.Players.Length)
            .FirstOrDefault(i =>
                string.Equals(state.Players[i].FullName, fullName, StringComparison.OrdinalIgnoreCase) &&
                (world.Length == 0 || string.Equals(state.Players[i].World, world, StringComparison.OrdinalIgnoreCase)),
            -1);
        var snapTrade = pendingTradeGil;
        var snapGave  = pendingGaveGil;
        Reset();

        if (pi < 0) return new Outcome.None();

        // Bank-only routing (see TradeRouting): incoming gil always deposits,
        // outgoing always withdraws, a bidirectional trade confirms both legs, and
        // nothing is ever silently absorbed - the outgoing leg prompts even on an
        // empty bank (formerly returned None here, a drift source).
        return TradeRouting.Resolve(snapGave, snapTrade) switch
        {
            TradeDirection.TwoSided => new Outcome.PromptTwoSided(pi, snapGave, snapTrade),
            TradeDirection.Withdraw => new Outcome.PromptBankWithdraw(pi, snapGave),
            TradeDirection.Deposit  => new Outcome.PromptBankDeposit(pi, snapTrade),
            _                       => new Outcome.None(),
        };
    }

    private void Reset()
    {
        pendingPartner  = null;
        pendingTradeGil = 0;
        pendingGaveGil  = 0;
    }
}
