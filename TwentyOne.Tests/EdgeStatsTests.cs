using System.Collections.Generic;
using System.Collections.Immutable;
using TwentyOne.Game;
using TwentyOne.Game.Edge;
using Xunit;

namespace TwentyOne.Tests;

public class EdgeStatsTests
{
    private static GameState StateWithBets(params (string Name, string Bet, bool SittingOut)[] players)
    {
        var ps = new List<Player>();
        foreach (var (name, bet, sitting) in players)
            ps.Add(new Player { Nickname = name, Bet = bet, SittingOut = sitting, Hands = [new Hand()] });
        return new GameState { Players = [..ps] };
    }

    private static RoundHistoryEntry Entry(int round, long bankNet, GameState snapshot)
        => new() { RoundNumber = round, BankNet = bankNet, Snapshot = snapshot };

    [Fact]
    public void RoundWagered_SumsBetsOfNonSittingOutPlayers()
    {
        var state = StateWithBets(
            ("Lorah", "1000", false),
            ("Bekki",  "500", false),
            ("Nolla", "9999", true)); // sitting out
        Assert.Equal(1500, EdgeStats.RoundWagered(state));
    }

    [Fact]
    public void RoundWagered_IgnoresUnparseableBets()
    {
        var state = StateWithBets(("Lorah", "abc", false), ("Bekki", "200", false));
        Assert.Equal(200, EdgeStats.RoundWagered(state));
    }

    [Fact]
    public void Aggregate_Empty_ReturnsZeros()
    {
        var stats = EdgeStats.Aggregate([]);
        Assert.Equal(0,    stats.TotalWagered);
        Assert.Equal(0,    stats.RealizedBankNet);
        Assert.Equal(0.0,  stats.TheoreticalBankNet);
        Assert.Null(stats.RealizedEdge);
        Assert.Null(stats.TheoreticalEdge);
    }

    [Fact]
    public void Aggregate_SingleRound_RealizedAndTheoreticalScaleWithBet()
    {
        var snap  = StateWithBets(("Lorah", "1000", false));
        var stats = EdgeStats.Aggregate([Entry(1, bankNet: 25, snap)]);

        Assert.Equal(1000, stats.TotalWagered);
        Assert.Equal(25,   stats.RealizedBankNet);
        Assert.NotNull(stats.RealizedEdge);
        Assert.Equal(0.025, stats.RealizedEdge!.Value, precision: 5);

        var expectedEdge = EdgeSolver.ComputeHouseEdge(EdgeStats.RulesFromState(snap));
        Assert.Equal(1000 * expectedEdge, stats.TheoreticalBankNet, precision: 5);
        Assert.Equal(expectedEdge,        stats.TheoreticalEdge!.Value, precision: 6);
    }

    [Fact]
    public void Aggregate_MultipleRounds_AccumulatesBetAndBankNet()
    {
        var snap = StateWithBets(("Lorah", "100", false));
        var rounds = new[]
        {
            Entry(1, bankNet:  10, snap),
            Entry(2, bankNet: -50, snap),
            Entry(3, bankNet:  20, snap),
        };
        var stats = EdgeStats.Aggregate(rounds);
        Assert.Equal(300, stats.TotalWagered);
        Assert.Equal(-20, stats.RealizedBankNet);
    }

    [Fact]
    public void Aggregate_OverrideRules_AppliedToAllRoundsRegardlessOfSnapshot()
    {
        // Snapshot says 3:2 BJ; override says 6:5. Theoretical should reflect 6:5.
        var snap = StateWithBets(("Lorah", "1000", false));
        snap.BjPayout = 1.5;
        var round = Entry(1, bankNet: 0, snap);

        var override65 = EdgeStats.RulesFromState(snap) with { BjPayout = 1.2 };
        var stats      = EdgeStats.Aggregate([round], override65);

        var expectedEdgeAt65 = EdgeSolver.ComputeHouseEdge(override65);
        Assert.Equal(1000 * expectedEdgeAt65, stats.TheoreticalBankNet, precision: 5);
    }

    [Fact]
    public void Aggregate_DifferentSnapshotRules_PerRoundTheoretical()
    {
        // Two rounds, one at 3:2 and one at 6:5. Without an override, each round
        // uses its own snapshot's rules - the theoretical should be the sum, not
        // either single edge × total.
        var snap32 = StateWithBets(("Lorah", "1000", false));
        snap32.BjPayout = 1.5;
        var snap65 = StateWithBets(("Lorah", "1000", false));
        snap65.BjPayout = 1.2;

        var stats = EdgeStats.Aggregate([
            Entry(1, bankNet: 0, snap32),
            Entry(2, bankNet: 0, snap65),
        ]);

        var edge32 = EdgeSolver.ComputeHouseEdge(EdgeStats.RulesFromState(snap32));
        var edge65 = EdgeSolver.ComputeHouseEdge(EdgeStats.RulesFromState(snap65));
        var expected = 1000 * edge32 + 1000 * edge65;
        Assert.Equal(expected, stats.TheoreticalBankNet, precision: 5);
    }
}
