using System;
using System.Collections.Generic;
using System.Linq;

namespace TwentyOne.Game;

// Minimal per-player snapshot stored in archived sessions (no Bank/BankLog).
[Serializable]
public class PlayerStatData
{
    public string DisplayName { get; set; } = string.Empty;
    public int    GamesPlayed { get; set; }
    public int    GamesWon    { get; set; }
    public int    GamesPushed { get; set; }
    public int    GamesLost   { get; set; }
    public int    Blackjacks  { get; set; }
    public int    Charlies    { get; set; }
    public long   TotalWon    { get; set; }
}

// Pure static session logic — no Dalamud dependency, fully unit-testable.
public static class SessionManager
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(8);

    // No-op if startedAt already set.
    public static void TryStartSession(
        ref DateTime? startedAt,
        ref string?   locationKey,
        string?       currentLocation,
        DateTime      now)
    {
        if (startedAt != null) return;
        startedAt   = now;
        locationKey = currentLocation;
    }

    public static bool ShouldShowSessionBanner(
        DateTime? startedAt,
        string?   sessionLocationKey,
        string?   currentLocationKey,
        int       roundCount,
        DateTime  now)
    {
        if (startedAt == null)
            return roundCount > 0;

        if ((now - startedAt.Value) > StaleThreshold)
            return true;

        if (sessionLocationKey != null
            && currentLocationKey != null
            && currentLocationKey != sessionLocationKey)
            return true;

        return false;
    }

    // Builds an archived snapshot from current stats + round history.
    // Caller is responsible for clearing/resetting live data afterward.
    public static (Dictionary<string, PlayerStatData> Stats, long BankNet, List<RoundSummary> Rounds)
        BuildArchive(
            IEnumerable<KeyValuePair<string, PlayerStatData>> stats,
            IEnumerable<RoundSummary>                         rounds)
    {
        var roundList = rounds.Select(r => new RoundSummary
        {
            RoundNumber = r.RoundNumber,
            BankNet     = r.BankNet,
            PlayerBanks = new Dictionary<string, long>(r.PlayerBanks),
        }).ToList();

        var statsCopy = stats.ToDictionary(
            kv => kv.Key,
            kv => new PlayerStatData
            {
                DisplayName = kv.Value.DisplayName,
                GamesPlayed = kv.Value.GamesPlayed,
                GamesWon    = kv.Value.GamesWon,
                GamesPushed = kv.Value.GamesPushed,
                GamesLost   = kv.Value.GamesLost,
                Blackjacks  = kv.Value.Blackjacks,
                Charlies    = kv.Value.Charlies,
                TotalWon    = kv.Value.TotalWon,
            });

        return (statsCopy, roundList.Sum(r => r.BankNet), roundList);
    }

    // Zeroes game-performance stats; preserves Bank/BankLog (handled by caller).
    public static void ResetGameStats(IEnumerable<PlayerStatData> stats)
    {
        foreach (var s in stats)
        {
            s.GamesPlayed = 0;
            s.GamesWon    = 0;
            s.GamesPushed = 0;
            s.GamesLost   = 0;
            s.Blackjacks  = 0;
            s.Charlies    = 0;
            s.TotalWon    = 0;
        }
    }
}
