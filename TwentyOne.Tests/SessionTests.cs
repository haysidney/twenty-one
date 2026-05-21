using System;
using System.Collections.Generic;
using System.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class SessionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // ── ResetGameStats ────────────────────────────────────────────────────────

    [Fact]
    public void ResetGameStats_ZeroesAllPerfFields()
    {
        var stats = new List<PlayerStatData>
        {
            new() { DisplayName = "Lorah", GamesPlayed = 5, GamesWon = 3, GamesPushed = 1, GamesLost = 1, Blackjacks = 2, TotalWon = 500 },
        };
        SessionManager.ResetGameStats(stats);
        var s = stats[0];
        Assert.Equal(0, s.GamesPlayed);
        Assert.Equal(0, s.GamesWon);
        Assert.Equal(0, s.GamesPushed);
        Assert.Equal(0, s.GamesLost);
        Assert.Equal(0, s.Blackjacks);
        Assert.Equal(0, s.TotalWon);
        Assert.Equal("Lorah", s.DisplayName); // preserved
    }

    // ── TryStartSession ───────────────────────────────────────────────────────

    [Fact]
    public void TryStartSession_SetsOnFirstCall()
    {
        DateTime? startedAt   = null;
        string?   locationKey = null;
        var now = new DateTime(2026, 5, 3, 20, 0, 0);

        SessionManager.TryStartSession(ref startedAt, ref locationKey, "123:1:1", now);

        Assert.Equal(now, startedAt);
        Assert.Equal("123:1:1", locationKey);
    }

    [Fact]
    public void TryStartSession_NoopIfAlreadySet()
    {
        var original = new DateTime(2026, 5, 3, 18, 0, 0);
        DateTime? startedAt   = original;
        string?   locationKey = "orig";

        SessionManager.TryStartSession(ref startedAt, ref locationKey, "new", DateTime.Now);

        Assert.Equal(original, startedAt);
        Assert.Equal("orig", locationKey);
    }

    // ── ShouldShowSessionBanner ───────────────────────────────────────────────

    [Fact]
    public void SessionBanner_RoundsButNoSession_Shows()
    {
        Assert.True(SessionManager.ShouldShowSessionBanner(null, null, null, 1, DateTime.Now));
    }

    [Fact]
    public void SessionBanner_NoRoundsNoSession_Hidden()
    {
        Assert.False(SessionManager.ShouldShowSessionBanner(null, null, null, 0, DateTime.Now));
    }

    [Fact]
    public void SessionBanner_NotStale_SameLocation_Hidden()
    {
        var started = DateTime.Now.AddHours(-1);
        Assert.False(SessionManager.ShouldShowSessionBanner(started, "loc:1:1", "loc:1:1", 5, DateTime.Now));
    }

    [Fact]
    public void SessionBanner_StaleTime_Shows()
    {
        var started = DateTime.Now.AddHours(-9);
        Assert.True(SessionManager.ShouldShowSessionBanner(started, "loc:1:1", "loc:1:1", 5, DateTime.Now));
    }

    [Fact]
    public void SessionBanner_WrongLocation_Shows()
    {
        var started = DateTime.Now.AddHours(-1);
        Assert.True(SessionManager.ShouldShowSessionBanner(started, "loc:1:1", "loc:2:2", 5, DateTime.Now));
    }

    [Fact]
    public void SessionBanner_NullCurrentLocation_NotShownForLocationMismatch()
    {
        // If current location is unknown, don't show banner just for location reason.
        var started = DateTime.Now.AddHours(-1);
        Assert.False(SessionManager.ShouldShowSessionBanner(started, "loc:1:1", null, 5, DateTime.Now));
    }
}
