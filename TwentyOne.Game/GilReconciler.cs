using System;
using System.Collections.Generic;

namespace TwentyOne.Game;

/// <summary>
/// Continuous reconciliation of the dealer's on-hand gil against the trades the
/// plugin actually detected. Only trades move on-hand gil (bets / wins / double
/// / split / credit are internal bank relabeling), so every observed wallet
/// delta should pair with a recorded trade. An observed delta that finds no
/// matching expected trade within the grace window is surfaced as a
/// <see cref="Finding"/> - this catches a dropped trade-detection (the +1M
/// silent drift of 2026-06-26, where a chat line in the trade handshake was
/// missed) and any non-game wallet movement, live, instead of only post-hoc in
/// the audit file.
///
/// Pure and Dalamud-free: fed <see cref="RecordExpected"/> (a detected trade)
/// and <see cref="Observe"/> (a poll delta) with a monotonic clock;
/// <see cref="Tick"/> emits aged-out findings. The host wires it to the
/// framework-tick gil poll and the chat trade detector.
/// </summary>
public sealed class GilReconciler
{
    /// <summary>
    /// An unmatched delta surfaced past the grace window.
    /// <list type="bullet">
    /// <item><see cref="Phantom"/> = false: on-hand gil moved with no recorded
    /// trade (a missed trade, or non-game movement). <see cref="Tag"/> is null.</item>
    /// <item><see cref="Phantom"/> = true: a trade was recorded but no on-hand
    /// gil change matched it (the bank was likely over-credited - gil never
    /// arrived). <see cref="Tag"/> carries the player stats-key passed to
    /// <see cref="RecordExpected"/>.</item>
    /// </list>
    /// </summary>
    public readonly record struct Finding(long Delta, bool Phantom, string? Tag);

    private readonly record struct Pending(long Amount, DateTime At, string? Tag);

    private readonly List<Pending> expected = [];  // trades detected, awaiting a wallet observation
    private readonly List<Pending> observed = [];  // wallet deltas awaiting a matching trade
    private readonly TimeSpan      grace;

    public GilReconciler(TimeSpan? grace = null)
        => this.grace = grace ?? TimeSpan.FromSeconds(3);

    /// <summary>
    /// A trade the plugin detected and applied. Signed on-hand change:
    /// +deposit / -withdrawal / net two-sided. <paramref name="tag"/> identifies
    /// the credited player (stats-key) so a phantom finding can name / reverse it.
    /// </summary>
    public void RecordExpected(long signedDelta, DateTime now, string? tag = null)
    {
        if (signedDelta == 0) return;
        if (!CancelAgainst(observed, signedDelta))
            expected.Add(new Pending(signedDelta, now, tag));
    }

    /// <summary>An on-hand gil change seen by the wallet poll.</summary>
    public void Observe(long actualDelta, DateTime now)
    {
        if (actualDelta == 0) return;
        if (!CancelAgainst(expected, actualDelta))
            observed.Add(new Pending(actualDelta, now, null));
    }

    // Remove the first opposite-side entry of equal signed amount. Trades are
    // discrete, so whole-entry matching is enough (no partial splits).
    private static bool CancelAgainst(List<Pending> pool, long amount)
    {
        var i = pool.FindIndex(p => p.Amount == amount);
        if (i < 0) return false;
        pool.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// Surface every entry that has gone unmatched past the grace window:
    /// observed deltas become non-phantom findings (gil present, no trade);
    /// expected entries become phantom findings (trade recorded, gil absent -
    /// likely an over-credited bank). Both are removed so they can't later mask a
    /// real drift by matching an unrelated event.
    /// </summary>
    public IReadOnlyList<Finding> Tick(DateTime now)
    {
        var cutoff = now - grace;
        List<Finding>? findings = null;
        foreach (var o in observed)
            if (o.At <= cutoff) (findings ??= []).Add(new Finding(o.Amount, false, null));
        foreach (var e in expected)
            if (e.At <= cutoff) (findings ??= []).Add(new Finding(e.Amount, true, e.Tag));
        observed.RemoveAll(p => p.At <= cutoff);
        expected.RemoveAll(p => p.At <= cutoff);
        return findings ?? (IReadOnlyList<Finding>)[];
    }
}
