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
        public sealed record PromptBet         (int Pi, long Gil) : Outcome;
        public sealed record PromptBankDeposit (int Pi, long Gil) : Outcome;
        public sealed record PromptBankWithdraw(int Pi, long Gil) : Outcome;
        public sealed record PromptBetOrBank   (int Pi, long Gil) : Outcome;
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
    public Outcome OnChat(string text, PlayerPayload? payload, GamePhase phase, GameState state, Configuration cfg)
    {
        var isBetPhase    = cfg.AutoBetFromTrades && phase == GamePhase.Betting;
        var isBankMonitor = cfg.AutoDepositFromTrades;
        if (!isBetPhase && !isBankMonitor) return new Outcome.None();

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

        if (snapGave > 0 && isBankMonitor)
        {
            var wdBank = state.Players[pi].BankBalance(cfg);
            return wdBank > 0
                ? new Outcome.PromptBankWithdraw(pi, snapGave)
                : new Outcome.None();
        }

        if (snapTrade > 0)
        {
            if (isBetPhase && isBankMonitor) return new Outcome.PromptBetOrBank(pi, snapTrade);
            if (isBetPhase)                  return new Outcome.PromptBet(pi, snapTrade);
            if (isBankMonitor)               return new Outcome.PromptBankDeposit(pi, snapTrade);
        }

        return new Outcome.None();
    }

    private void Reset()
    {
        pendingPartner  = null;
        pendingTradeGil = 0;
        pendingGaveGil  = 0;
    }
}
