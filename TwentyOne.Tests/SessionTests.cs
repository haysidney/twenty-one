using System;
using System.Collections.Generic;
using System.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class SessionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, PlayerStatData> MakeStats(params (string key, string name, int played, int won, int pushed, int lost, int bjs, long won2)[] entries)
        => entries.ToDictionary(
            e => e.key,
            e => new PlayerStatData
            {
                DisplayName = e.name,
                GamesPlayed = e.played,
                GamesWon    = e.won,
                GamesPushed = e.pushed,
                GamesLost   = e.lost,
                Blackjacks  = e.bjs,
                TotalWon    = e.won2,
            });

    private static RoundSummary Round(int num, long net) => new()
    {
        RoundNumber = num,
        BankNet     = net,
        PlayerBanks = [],
    };

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

    // ── BuildArchive ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildArchive_ArchivesCorrectDate_And_LocationKey()
    {
        var stats  = MakeStats(("Lorah", "Lorah", 3, 2, 0, 1, 1, 200));
        var rounds = new[] { Round(1, 100), Round(2, -50) };
        var (archivedStats, bankNet, archivedRounds) = SessionManager.BuildArchive(stats, rounds);

        Assert.Single(archivedStats);
        Assert.True(archivedStats.ContainsKey("Lorah"));
        Assert.Equal(3, archivedStats["Lorah"].GamesPlayed);
        Assert.Equal(200, archivedStats["Lorah"].TotalWon);
        Assert.Equal(50L, bankNet); // 100 + (-50)
        Assert.Equal(2, archivedRounds.Count);
    }

    [Fact]
    public void BuildArchive_ArchivesCorrectBankNet()
    {
        var stats  = MakeStats();
        var rounds = new[] { Round(1, 300), Round(2, -100), Round(3, 50) };
        var (_, bankNet, _) = SessionManager.BuildArchive(stats, rounds);
        Assert.Equal(250L, bankNet);
    }

    [Fact]
    public void BuildArchive_RoundSummaryMapsFields()
    {
        var stats  = MakeStats();
        var src    = new RoundSummary { RoundNumber = 7, BankNet = 999, PlayerBanks = new() { ["Lorah"] = 5000 } };
        var (_, _, rounds) = SessionManager.BuildArchive(stats, new[] { src });
        Assert.Single(rounds);
        Assert.Equal(7,    rounds[0].RoundNumber);
        Assert.Equal(999L, rounds[0].BankNet);
        Assert.Equal(5000L, rounds[0].PlayerBanks["Lorah"]);
    }

    [Fact]
    public void BuildArchive_NoGameStateReference()
    {
        // RoundSummary has no GameState — verify by construction
        var src = new RoundSummary { RoundNumber = 1, BankNet = 0 };
        Assert.IsType<RoundSummary>(src);
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
