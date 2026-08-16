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
            new() { DisplayName = "Lorah", GamesPlayed = 5, GamesWon = 3, GamesPushed = 1, GamesLost = 1, Blackjacks = 2, TotalNet = 500 },
        };
        SessionManager.ResetGameStats(stats);
        var s = stats[0];
        Assert.Equal(0, s.GamesPlayed);
        Assert.Equal(0, s.GamesWon);
        Assert.Equal(0, s.GamesPushed);
        Assert.Equal(0, s.GamesLost);
        Assert.Equal(0, s.Blackjacks);
        Assert.Equal(0, s.TotalNet);
        Assert.Equal("Lorah", s.DisplayName); // preserved
    }

    // ── CheckClose ────────────────────────────────────────────────────────────

    private static KeyValuePair<string, long>[] Banks(params (string Name, long Bank)[] rows) =>
        rows.Select(r => new KeyValuePair<string, long>(r.Name, r.Bank)).ToArray();

    [Fact]
    public void CheckClose_AllowsAtBettingWithSettledBanks()
    {
        var check = SessionManager.CheckClose(true, GamePhase.Betting,
            Banks(("Lorah", 0), ("Bekki", 0)));

        Assert.True(check.CanClose);
        Assert.Null(check.Reason);
        Assert.Empty(check.BankHolders);
    }

    [Fact]
    public void CheckClose_BlocksWhenNoSessionOpen()
    {
        var check = SessionManager.CheckClose(false, GamePhase.Betting, Banks());

        Assert.False(check.CanClose);
        Assert.Equal("No session is open.", check.Reason);
    }

    [Theory]
    [InlineData(GamePhase.Deal)]
    [InlineData(GamePhase.PlayerTurns)]
    [InlineData(GamePhase.DealerTurn)]
    [InlineData(GamePhase.Payout)]
    public void CheckClose_BlocksMidRound(GamePhase phase)
    {
        var check = SessionManager.CheckClose(true, phase, Banks(("Lorah", 0)));

        Assert.False(check.CanClose);
        Assert.Equal("Finish or abort the current round first.", check.Reason);
    }

    [Fact]
    public void CheckClose_BlocksAndNamesPlayersStillHoldingGil()
    {
        var check = SessionManager.CheckClose(true, GamePhase.Betting,
            Banks(("Lorah", 5000), ("Bekki", 0), ("Nolla", 12000)));

        Assert.False(check.CanClose);
        Assert.Equal(2, check.BankHolders.Count);
        // Largest balance first - the dealer settles the big one first.
        Assert.Contains("Nolla", check.BankHolders[0]);
        Assert.Contains("12,000", check.BankHolders[0]);
        Assert.Contains("Lorah", check.BankHolders[1]);
        Assert.DoesNotContain(check.BankHolders, h => h.Contains("Bekki"));
    }

    [Fact]
    public void CheckClose_TreatsNegativeBankAsUnsettled()
    {
        var check = SessionManager.CheckClose(true, GamePhase.Betting, Banks(("Lorah", -300)));

        Assert.False(check.CanClose);
        Assert.Single(check.BankHolders);
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
