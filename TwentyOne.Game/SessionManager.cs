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
    public long   TotalNet    { get; set; }
}

// Pure static session logic - no Dalamud dependency, fully unit-testable.
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
            s.TotalNet    = 0;
        }
    }
}
