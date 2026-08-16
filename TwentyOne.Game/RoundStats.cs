using System.Collections.Generic;
using System.Linq;

namespace TwentyOne.Game;

/// <summary>
/// One player's outcome for a completed round, as the stats counters see it.
/// <see cref="Net"/> is the signed gil swing from the player's point of view.
/// </summary>
public readonly record struct PlayerRoundResult(
    string  StatsKey,
    string  DisplayName,
    decimal Net,
    bool    Won,
    bool    Lost,
    bool    Pushed,
    bool    HadBlackjack,
    int     Charlies);

/// <summary>Winner / loser / pusher display names for a completed round.</summary>
public sealed record RoundClassification(
    IReadOnlyList<string> Winners,
    IReadOnlyList<string> Losers,
    IReadOnlyList<string> Pushes);

/// <summary>
/// Pure derivation of per-round player statistics from a settled
/// <see cref="GameState"/>. Extracted from MainWindow.UpdatePlayerStats and
/// HistoryWindow.ClassifyRound so the counting rules are testable and stated
/// once - the two used to disagree about players who were not in the round.
/// </summary>
public static class RoundStats
{
    /// <summary>
    /// Stable identity key used by the venue's player-stats store:
    /// <c>"FullName@World"</c> for FFXIV characters, the nickname for manually
    /// added players.
    /// </summary>
    public static string StatsKey(this Player p) =>
        p.FullName.Length > 0 ? $"{p.FullName}@{p.World}" : p.Nickname;

    /// <summary>
    /// Per-player results for everyone who actually played the round. Players who
    /// sat out or withdrew are excluded entirely - they staked nothing and their
    /// hand was never resolved, so counting them would inflate GamesPlayed and
    /// (before this was centralised) record them as losses.
    /// </summary>
    public static IReadOnlyList<PlayerRoundResult> PerPlayer(GameState state)
    {
        var results = new List<PlayerRoundResult>();
        for (var pi = 0; pi < state.Players.Length; pi++)
        {
            var p = state.Players[pi];
            if (!PlayedThisRound(p)) continue;

            var net = 0m;
            for (var hi = 0; hi < p.Hands.Length; hi++)
                net += GameEngine.PayoutDelta(state, pi, hi) ?? 0m;

            var charlies = 0;
            for (var hi = 0; hi < p.Hands.Length; hi++)
                if (GameEngine.GetPayoutResult(state, pi, hi) == PayoutResult.CharlieWin)
                    charlies++;

            results.Add(new PlayerRoundResult(
                StatsKey:     p.StatsKey(),
                DisplayName:  p.DisplayName,
                Net:          net,
                Won:          net > 0,
                Lost:         net < 0,
                Pushed:       net == 0,
                HadBlackjack: p.Hands.Any(h => h.State == HandState.Blackjack),
                Charlies:     charlies));
        }
        return results;
    }

    /// <summary>
    /// Winner / loser / pusher lists for the round-history display. A split player
    /// with one winning and one losing hand counts as a winner.
    /// </summary>
    public static RoundClassification Classify(GameState state)
    {
        var winners = new List<string>();
        var losers  = new List<string>();
        var pushes  = new List<string>();

        for (var pi = 0; pi < state.Players.Length; pi++)
        {
            var p = state.Players[pi];
            if (!PlayedThisRound(p)) continue;

            var results = Enumerable.Range(0, p.Hands.Length)
                .Select(hi => GameEngine.GetPayoutResult(state, pi, hi))
                .ToList();
            var anyWin  = results.Any(r => r is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin);
            var anyLose = results.Any(r => r is PayoutResult.Lose or PayoutResult.Surrender);
            var allPush = results.All(r => r == PayoutResult.Push);

            if      (anyWin)  winners.Add(p.DisplayName);
            else if (allPush) pushes.Add(p.DisplayName);
            else if (anyLose) losers.Add(p.DisplayName);
        }
        return new RoundClassification(winners, losers, pushes);
    }

    // A player is in the round if they are not sitting out and actually hold
    // cards. The cards check catches a player who withdrew mid-round (hand
    // discarded) and a roster row added after the deal.
    private static bool PlayedThisRound(Player p) =>
        !p.SittingOut && p.Hands.Any(h => h.Cards.Length > 0);
}
