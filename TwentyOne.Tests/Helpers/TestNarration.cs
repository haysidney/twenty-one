using System;

namespace TwentyOne.Tests;

internal static class TestNarration
{
    /// Deterministic narration-variant selector for tests: always the first
    /// variant. Pass as GameEngine.Apply(..., pickVariant: TestNarration.First) in
    /// any test that asserts on narration text, so the assertion never depends on
    /// Random.Shared picking a particular variant.
    public static readonly Func<int, int> First = _ => 0;
}
