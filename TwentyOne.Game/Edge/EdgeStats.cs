using System.Collections.Generic;

namespace TwentyOne.Game.Edge;

/// Helpers for comparing a venue's realized bank-net against the expected value
/// under perfect play. Theoretical edge = bets wagered × house-edge fraction;
/// realized edge = actual bank net per unit wagered.
public static class EdgeStats
{
    /// Sum of original (non-doubled, non-split) bets for non-sitting-out players
    /// in the round. House-edge percentages are expressed per unit of original
    /// bet, so doubles/splits should not be amplified here.
    public static long RoundWagered(GameState state)
    {
        long total = 0;
        foreach (var p in state.Players)
        {
            if (p.SittingOut) continue;
            if (long.TryParse(p.Bet, out var b)) total += b;
        }
        return total;
    }

    /// Aggregate stats across a sequence of completed rounds. If overrideRules is
    /// provided, every round is evaluated under those rules ("what would have
    /// happened with the venue's current rules"); otherwise each round uses the
    /// rules captured in its own snapshot ("what should have happened given the
    /// rules in effect at the time").
    public static AggregateStats Aggregate(
        IEnumerable<RoundHistoryEntry> rounds,
        EdgeRules? overrideRules = null)
    {
        long   totalWagered       = 0;
        long   realizedBankNet    = 0;
        double theoreticalBankNet = 0;
        var    cache              = new Dictionary<EdgeRules, double>();

        foreach (var r in rounds)
        {
            var wagered = RoundWagered(r.Snapshot);
            totalWagered    += wagered;
            realizedBankNet += r.BankNet;

            var rules = overrideRules ?? RulesFromState(r.Snapshot);
            if (!cache.TryGetValue(rules, out var edge))
            {
                edge = EdgeSolver.ComputeHouseEdge(rules);
                cache[rules] = edge;
            }
            theoreticalBankNet += wagered * edge;
        }

        return new AggregateStats(totalWagered, realizedBankNet, theoreticalBankNet);
    }

    public static EdgeRules RulesFromState(GameState s) => new(
        s.BjPayout, s.CharliePayout, s.FiveCardCharlie,
        s.DealerStandsOnSoft17, s.DoubleAfterSplit,
        s.HitSplitAces, s.ResplitAces, s.AllowSurrender, s.ResplitCap);
}

public readonly record struct AggregateStats(
    long   TotalWagered,
    long   RealizedBankNet,
    double TheoreticalBankNet)
{
    /// Fraction of total wagered that the bank actually kept. Null when nothing
    /// was wagered (no rounds, or every round had no bets).
    public double? RealizedEdge => TotalWagered > 0 ? (double)RealizedBankNet / TotalWagered : null;

    /// Fraction of total wagered the bank was expected to keep under optimal play.
    public double? TheoreticalEdge => TotalWagered > 0 ? TheoreticalBankNet / TotalWagered : null;
}
