#if DEBUG
using System.Collections.Generic;
using TwentyOne.Windows;

namespace TwentyOne.Debug;

/// <summary>
/// Runtime state for the in-game scenario test harness (DEBUG-only). Holds the
/// active scripted scenario, the gating + fast-forward toggles, and the
/// pre-loaded debug roll queue. Pulled out of MainWindow so the scenario state
/// lives in one place; the action-dispatch (StartDeal / Hit:pi:hi / etc.)
/// remains in MainWindow because it touches MainWindow internals.
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>Non-null while a scripted test scenario is running.</summary>
    public ActiveScenario? ActiveScenario { get; set; }

    /// <summary>When true, only the button matching the next scenario action is enabled.</summary>
    public bool GateButtons { get; set; } = true;

    /// <summary>When true, auto-steps through scenario actions as the chat queue drains each frame.</summary>
    public bool FastForward { get; set; } = false;

    /// <summary>Pre-loaded card values consumed by QueueHitRoll instead of /random rolls.</summary>
    public readonly Queue<int> RollQueue = new();

    /// <summary>True if no scenario is active, gating is off, or the next scripted step matches <paramref name="key"/>.</summary>
    public bool IsStep(string key) =>
        ActiveScenario == null || !GateButtons || ActiveScenario.PeekNext() == key;

    /// <summary>Advance the scenario pointer after a scripted button has been clicked.</summary>
    public void Advance() => ActiveScenario?.Advance();
}
#endif
