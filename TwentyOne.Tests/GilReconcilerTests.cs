using System;
using System.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class GilReconcilerTests
{
    private static readonly DateTime T0 = new(2026, 6, 26, 3, 0, 0, DateTimeKind.Local);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    private static GilReconciler New() => new(Grace);

    [Fact]
    public void ExpectedThenObserved_Cancels_NoFinding()
    {
        var r = New();
        r.RecordExpected(1_000_000, T0);
        r.Observe(1_000_000, T0.AddMilliseconds(15));
        Assert.Empty(r.Tick(T0.AddSeconds(10)));
    }

    [Fact]
    public void ObservedThenExpected_Cancels_NoFinding()
    {
        // Poll can fire before the trade chat line is parsed.
        var r = New();
        r.Observe(1_000_000, T0);
        r.RecordExpected(1_000_000, T0.AddMilliseconds(15));
        Assert.Empty(r.Tick(T0.AddSeconds(10)));
    }

    [Fact]
    public void ObservedWithNoExpected_AgesOut_ToFinding()
    {
        // The 2026-06-26 +1M drift: gil arrived, trade never detected.
        var r = New();
        r.Observe(1_000_000, T0);
        Assert.Empty(r.Tick(T0.AddSeconds(1)));          // still inside grace
        var findings = r.Tick(T0.AddSeconds(4));
        var f = Assert.Single(findings);
        Assert.Equal(1_000_000, f.Delta);
        Assert.False(f.Phantom);
    }

    [Fact]
    public void ExpectedWithNoObservation_AgesOut_ToPhantomFinding()
    {
        // A trade recorded but the gil never moved: bank over-credited.
        var r = New();
        r.RecordExpected(1_000_000, T0, tag: "lorah@gilgamesh");
        Assert.Empty(r.Tick(T0.AddSeconds(1)));          // still inside grace
        var f = Assert.Single(r.Tick(T0.AddSeconds(4)));
        Assert.Equal(1_000_000, f.Delta);
        Assert.True(f.Phantom);
        Assert.Equal("lorah@gilgamesh", f.Tag);
    }

    [Fact]
    public void Finding_EmittedOnce()
    {
        var r = New();
        r.Observe(500_000, T0);
        Assert.Single(r.Tick(T0.AddSeconds(4)));
        Assert.Empty(r.Tick(T0.AddSeconds(8)));
    }

    [Fact]
    public void OneMissedAmongMany_OnlyMissedSurfaces()
    {
        // Five 1M deposits arrive; only four were detected as trades.
        var r = New();
        for (var i = 0; i < 5; i++) r.Observe(1_000_000, T0);
        for (var i = 0; i < 4; i++) r.RecordExpected(1_000_000, T0.AddMilliseconds(10));
        var f = Assert.Single(r.Tick(T0.AddSeconds(4)));
        Assert.Equal(1_000_000, f.Delta);
    }

    [Fact]
    public void WithdrawalAndDeposit_MatchBySign()
    {
        var r = New();
        r.RecordExpected(1_000_000, T0);
        r.RecordExpected(-400_000, T0);
        r.Observe(-400_000, T0.AddMilliseconds(10));
        r.Observe(1_000_000, T0.AddMilliseconds(20));
        Assert.Empty(r.Tick(T0.AddSeconds(4)));
    }

    [Fact]
    public void StaleExpected_DroppedAndDoesNotMaskLaterDrift()
    {
        // A recorded trade whose gil never moved surfaces as a phantom and is
        // dropped; a real unexplained delta arriving later must still surface,
        // not silently cancel against the now-gone stale expected.
        var r = New();
        r.RecordExpected(1_000_000, T0);
        var stale = Assert.Single(r.Tick(T0.AddSeconds(4)));   // phantom, expected dropped
        Assert.True(stale.Phantom);
        r.Observe(1_000_000, T0.AddSeconds(5));
        var f = Assert.Single(r.Tick(T0.AddSeconds(9)));
        Assert.Equal(1_000_000, f.Delta);
        Assert.False(f.Phantom);
    }

    [Fact]
    public void ZeroDeltas_Ignored()
    {
        var r = New();
        r.RecordExpected(0, T0);
        r.Observe(0, T0);
        Assert.Empty(r.Tick(T0.AddSeconds(10)));
    }
}
