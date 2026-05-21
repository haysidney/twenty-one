using System.Collections.Generic;
using TwentyOne.Game;
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

// Per-window cache for live EdgeStats.Aggregate results so we don't re-run
// the solver every frame an open window redraws. Keyed on (rules, round count,
// last round number) - any of those changing means we must recompute. A
// missing override rules ("each round uses its own snapshot rules") is a
// distinct cache key from any concrete EdgeRules.
internal sealed class EdgeStatsCache
{
    private AggregateStats _stats;
    private bool           _have;
    private EdgeRules?     _rules;
    private int            _count   = -1;
    private int            _lastNum = -1;

    public AggregateStats Get(List<RoundHistoryEntry> rounds, EdgeRules? rules = null)
    {
        var count = rounds.Count;
        var last  = count > 0 ? rounds[^1].RoundNumber : 0;
        var rulesMatch = _rules.HasValue == rules.HasValue
                      && (!_rules.HasValue || _rules.Value.Equals(rules!.Value));
        if (_have && rulesMatch && _count == count && _lastNum == last)
            return _stats;

        _stats   = EdgeStats.Aggregate(rounds, rules);
        _have    = true;
        _rules   = rules;
        _count   = count;
        _lastNum = last;
        return _stats;
    }
}
