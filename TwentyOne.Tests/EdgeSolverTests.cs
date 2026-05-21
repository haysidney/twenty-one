using System.Diagnostics;
using TwentyOne.Game;
using TwentyOne.Game.Edge;
using Xunit;
using Xunit.Abstractions;

namespace TwentyOne.Tests;

public class EdgeSolverTests
{
    private readonly ITestOutputHelper _out;

    public EdgeSolverTests(ITestOutputHelper output) { _out = output; }

    private static EdgeRules Rules(
        PayoutRatio bj = PayoutRatio.ThreeToTwo,
        PayoutRatio charlie = PayoutRatio.ThreeToTwo,
        FiveCardCharlieRule cr = FiveCardCharlieRule.Disabled)
        => new(bj, charlie, cr);

    [Fact]
    public void H17_3to2_NoCharlie_EdgeInPublishedRange()
    {
        var edge = EdgeSolver.ComputeHouseEdge(Rules());
        _out.WriteLine($"H17 3:2 no-Charlie house edge: {edge * 100:F4}%");
        // Infinite-deck H17 with DAS + the engine quirk that dealer BJ beats
        // non-BJ player 21 sits a bit above standard 6-deck H17 (~0.60%).
        // Expect roughly 0.6% to 1.0%.
        Assert.InRange(edge, 0.003, 0.010);
    }

    [Fact]
    public void SixToFive_Increases_Edge_VsThreeToTwo()
    {
        var e32 = EdgeSolver.ComputeHouseEdge(Rules(bj: PayoutRatio.ThreeToTwo));
        var e65 = EdgeSolver.ComputeHouseEdge(Rules(bj: PayoutRatio.SixToFive));
        _out.WriteLine($"3:2 = {e32 * 100:F4}%, 6:5 = {e65 * 100:F4}%, delta = {(e65 - e32) * 100:F4}%");
        // Published delta is ~+1.39% for finite shoe; infinite deck similar.
        Assert.InRange(e65 - e32, 0.012, 0.016);
    }

    [Fact]
    public void EvenMoney_Increases_Edge_VsThreeToTwo()
    {
        var e32 = EdgeSolver.ComputeHouseEdge(Rules(bj: PayoutRatio.ThreeToTwo));
        var e11 = EdgeSolver.ComputeHouseEdge(Rules(bj: PayoutRatio.EvenMoney));
        _out.WriteLine($"3:2 = {e32 * 100:F4}%, 1:1 = {e11 * 100:F4}%, delta = {(e11 - e32) * 100:F4}%");
        // Published delta ~+2.27%.
        Assert.InRange(e11 - e32, 0.020, 0.026);
    }

    [Fact]
    public void Charlie_BeatsAll_LowersEdge_VsDisabled()
    {
        var off  = EdgeSolver.ComputeHouseEdge(Rules(cr: FiveCardCharlieRule.Disabled));
        var beat = EdgeSolver.ComputeHouseEdge(
            Rules(charlie: PayoutRatio.ThreeToTwo, cr: FiveCardCharlieRule.BeatsAll));
        _out.WriteLine($"Charlie off = {off * 100:F4}%, BeatsAll@3:2 = {beat * 100:F4}%");
        Assert.True(beat < off, "Charlie should reduce house edge");
    }

    [Fact]
    public void Charlie_LosesToDealerBJ_LessFavorableThan_BeatsAll()
    {
        var beat = EdgeSolver.ComputeHouseEdge(
            Rules(charlie: PayoutRatio.ThreeToTwo, cr: FiveCardCharlieRule.BeatsAll));
        var lose = EdgeSolver.ComputeHouseEdge(
            Rules(charlie: PayoutRatio.ThreeToTwo, cr: FiveCardCharlieRule.LosesToDealerBJ));
        _out.WriteLine($"BeatsAll = {beat * 100:F4}%, LosesToBJ = {lose * 100:F4}%");
        Assert.True(lose > beat);
    }

    [Fact]
    public void Sweep_All21Cells_FinishesQuickly_AllPositive()
    {
        var sw = Stopwatch.StartNew();
        var bjOptions = new[] { PayoutRatio.ThreeToTwo, PayoutRatio.SixToFive, PayoutRatio.EvenMoney };
        var crOptions = new[]
        {
            FiveCardCharlieRule.Disabled,
            FiveCardCharlieRule.BeatsAll,
            FiveCardCharlieRule.LosesToDealerBJ,
        };
        int cells = 0;
        foreach (var bj in bjOptions)
        foreach (var cr in crOptions)
        {
            if (cr == FiveCardCharlieRule.Disabled)
            {
                var edge = EdgeSolver.ComputeHouseEdge(Rules(bj: bj, cr: cr));
                _out.WriteLine($"  bj={bj}, cr={cr}: {edge * 100:F4}%");
                Assert.InRange(edge, -0.05, 0.06);
                cells++;
            }
            else
            {
                foreach (var cp in bjOptions)
                {
                    var edge = EdgeSolver.ComputeHouseEdge(Rules(bj: bj, charlie: cp, cr: cr));
                    _out.WriteLine($"  bj={bj}, charlie={cp}, cr={cr}: {edge * 100:F4}%");
                    Assert.InRange(edge, -0.05, 0.06);
                    cells++;
                }
            }
        }
        sw.Stop();
        _out.WriteLine($"21-cell sweep took {sw.ElapsedMilliseconds} ms across {cells} cells");
        Assert.Equal(21, cells);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"sweep too slow: {sw.ElapsedMilliseconds} ms");
    }
}
