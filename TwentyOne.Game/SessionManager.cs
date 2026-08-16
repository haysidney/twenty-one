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

/// <summary>
/// Result of asking whether the dealer may close the session right now.
/// <see cref="BankHolders"/> names the players who still hold gil, so the UI can
/// tell the dealer exactly who to settle up with.
/// </summary>
public sealed record CloseCheck(bool CanClose, string? Reason, IReadOnlyList<string> BankHolders);

// Pure static session logic - no Dalamud dependency, fully unit-testable.
public static class SessionManager
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(8);

    /// <summary>
    /// Closing freezes the books, so it is only safe at a clean boundary: between
    /// rounds, with every player's bank settled to zero. Otherwise the frozen
    /// numbers would be missing gil that is still in play or still owed out.
    /// </summary>
    public static CloseCheck CheckClose(
        bool                                 sessionOpen,
        GamePhase                            phase,
        IEnumerable<KeyValuePair<string, long>> banks)
    {
        if (!sessionOpen)
            return new CloseCheck(false, "No session is open.", []);

        if (phase != GamePhase.Betting)
            return new CloseCheck(false, "Finish or abort the current round first.", []);

        var holders = banks.Where(b => b.Value != 0)
                           .OrderByDescending(b => b.Value)
                           .Select(b => $"{b.Key} ({b.Value:N0} gil)")
                           .ToList();

        return holders.Count > 0
            ? new CloseCheck(false, "Players still hold gil in their banks. Cash them out first.", holders)
            : new CloseCheck(true, null, []);
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
