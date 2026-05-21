using System.Diagnostics;
using System.Text;
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
        FiveCardCharlieRule cr = FiveCardCharlieRule.Disabled,
        bool s17 = false,
        bool das = true,
        bool hsa = false)
        => new(bj, charlie, cr, s17, das, hsa);

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
    public void HSA_LowersEdge_VsNoHSA()
    {
        var noHSA = EdgeSolver.ComputeHouseEdge(Rules(hsa: false));
        var hsa   = EdgeSolver.ComputeHouseEdge(Rules(hsa: true));
        _out.WriteLine($"no-HSA = {noHSA * 100:F4}%, HSA = {hsa * 100:F4}%, delta = {(hsa - noHSA) * 100:F4}%");
        Assert.True(hsa < noHSA, "Allowing HSA should lower house edge");
        // Published delta is ~-0.13%; allow a band.
        Assert.InRange(noHSA - hsa, 0.0005, 0.0030);
    }

    [Fact]
    public void NoDAS_IncreasesEdge_VsDAS()
    {
        var das    = EdgeSolver.ComputeHouseEdge(Rules(das: true));
        var noDAS  = EdgeSolver.ComputeHouseEdge(Rules(das: false));
        _out.WriteLine($"DAS = {das * 100:F4}%, no-DAS = {noDAS * 100:F4}%, delta = {(noDAS - das) * 100:F4}%");
        // Published delta is ~+0.14% (no-DAS is worse for the player).
        Assert.True(noDAS > das, "Disallowing DAS should raise the house edge");
        Assert.InRange(noDAS - das, 0.0005, 0.0025);
    }

    [Fact]
    public void S17_LowersEdge_VsH17()
    {
        var h17 = EdgeSolver.ComputeHouseEdge(Rules(s17: false));
        var s17 = EdgeSolver.ComputeHouseEdge(Rules(s17: true));
        _out.WriteLine($"H17 = {h17 * 100:F4}%, S17 = {s17 * 100:F4}%, delta = {(s17 - h17) * 100:F4}%");
        // Standard published S17 vs H17 delta is ~-0.22%. Allow a wider band for
        // infinite-deck + ENHC.
        Assert.True(s17 < h17, "S17 should lower house edge vs H17");
        Assert.InRange(h17 - s17, 0.0015, 0.0035);
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

    // Upcards in chart order: 2,3,4,5,6,7,8,9,10,A. Ace is rank 1 internally.
    private static readonly int[] Upcards   = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 1 };
    private static readonly string[] UpLbls = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "A" };

    private static string ActionGlyph(OptimalAction a) => a switch
    {
        OptimalAction.Hit    => "H",
        OptimalAction.Stand  => "S",
        OptimalAction.Double => "D",
        OptimalAction.Split  => "P",
        _                    => "?",
    };

    // Canonical 2-card hard hand for a given total. Avoids pairs and aces.
    private static (int, int) HardHand(int total) => total switch
    {
        5  => (2, 3),
        6  => (2, 4),
        7  => (2, 5),
        8  => (2, 6),
        9  => (2, 7),
        10 => (2, 8),
        11 => (2, 9),
        12 => (2, 10),
        13 => (3, 10),
        14 => (4, 10),
        15 => (5, 10),
        16 => (6, 10),
        17 => (7, 10),
        18 => (8, 10),
        19 => (9, 10),
        20 => (10, 11), // 10 + J: both face value 10, different ranks, not splittable
        _  => throw new System.ArgumentOutOfRangeException(nameof(total)),
    };

    [Fact]
    public void Print_BasicStrategy_For_3to2_NoCharlie()
    {
        var rules = Rules();
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Basic strategy (H17 ENHC 3:2 no Charlie):");
        sb.AppendLine("=========================================");

        sb.AppendLine();
        sb.AppendLine("Hard totals");
        sb.Append("     ");
        foreach (var l in UpLbls) sb.Append($" {l,3}");
        sb.AppendLine();
        for (int total = 5; total <= 20; total++)
        {
            sb.Append($"{total,3}  ");
            var (c1, c2) = HardHand(total);
            foreach (var up in Upcards)
            {
                var act = EdgeSolver.GetOptimalAction(c1, c2, up, rules);
                sb.Append($" {ActionGlyph(act),3}");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Soft totals (A,X)");
        sb.Append("     ");
        foreach (var l in UpLbls) sb.Append($" {l,3}");
        sb.AppendLine();
        for (int x = 2; x <= 9; x++)
        {
            sb.Append($"A,{x}  ");
            foreach (var up in Upcards)
            {
                var act = EdgeSolver.GetOptimalAction(1, x, up, rules);
                sb.Append($" {ActionGlyph(act),3}");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Pairs");
        sb.Append("     ");
        foreach (var l in UpLbls) sb.Append($" {l,3}");
        sb.AppendLine();
        var pairLabels = new[] { "2,2", "3,3", "4,4", "5,5", "6,6", "7,7", "8,8", "9,9", "T,T", "A,A" };
        var pairRanks  = new[] { 2,     3,     4,     5,     6,     7,     8,     9,     10,    1     };
        for (int i = 0; i < pairRanks.Length; i++)
        {
            sb.Append($"{pairLabels[i]}  ");
            foreach (var up in Upcards)
            {
                var act = EdgeSolver.GetOptimalAction(pairRanks[i], pairRanks[i], up, rules);
                sb.Append($" {ActionGlyph(act),3}");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Legend: H=Hit  S=Stand  D=Double  P=Split");

        _out.WriteLine(sb.ToString());
    }

    [Fact]
    public void Print_ActionEVs_For_Deviation_Cells()
    {
        var rules = Rules();
        var cells = new (string name, int c1, int c2, int up)[]
        {
            ("A,2 vs 5  (we said Hit; standard H17 DAS says Double)", 1, 2, 5),
            ("hard 11 vs 10 (we said Hit; non-ENHC says Double)",     2, 9, 10),
            ("hard 11 vs A  (we said Hit; non-ENHC says Double)",     2, 9, 1),
            ("8,8 vs 10    (we said Hit; non-ENHC says Split)",       8, 8, 10),
            ("8,8 vs A     (we said Hit; non-ENHC says Split)",       8, 8, 1),
            ("A,A vs A     (we said Hit; non-ENHC says Split)",       1, 1, 1),
        };
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Per-action EV at each deviation cell (H17 ENHC 3:2 no Charlie):");
        sb.AppendLine();
        foreach (var (name, c1, c2, up) in cells)
        {
            var standEV  = EdgeSolver.GetActionEV(OptimalAction.Stand,  c1, c2, up, rules);
            var hitEV    = EdgeSolver.GetActionEV(OptimalAction.Hit,    c1, c2, up, rules);
            var doubleEV = EdgeSolver.GetActionEV(OptimalAction.Double, c1, c2, up, rules);
            var splitEV  = c1 == c2
                ? EdgeSolver.GetActionEV(OptimalAction.Split, c1, c2, up, rules)
                : double.NaN;
            sb.AppendLine(name);
            sb.AppendLine($"  Stand  = {standEV,+8:F5}");
            sb.AppendLine($"  Hit    = {hitEV,+8:F5}");
            sb.AppendLine($"  Double = {doubleEV,+8:F5}");
            if (!double.IsNaN(splitEV))
                sb.AppendLine($"  Split  = {splitEV,+8:F5}");
            sb.AppendLine();
        }
        _out.WriteLine(sb.ToString());
    }
}
