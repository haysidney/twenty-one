using System;
using Dalamud.Bindings.ImGui;
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

// Renders the "Realized vs theoretical house edge" block used by Session Ledger
// and the History window. Layout is two label/value rows plus a small subtext
// with rounds and total wagered.
public static class EdgeStatsDisplay
{
    // Blackjack's per-round variance dwarfs its edge: realized results only
    // converge on the theoretical figure over thousands of rounds. Below this,
    // the comparison is noise and must not read as a verdict on the night.
    private const int MeaningfulSampleRounds = 1000;

    public static void Draw(AggregateStats stats, int roundCount, string theoreticalTooltip)
    {
        DrawRow("Realized house edge:",    stats.RealizedEdge,    "Actual bank gain per gil wagered, observed over the rounds below.");
        DrawRow("Theoretical house edge:", stats.TheoreticalEdge, theoreticalTooltip);
        if (stats.TotalWagered > 0)
            ImGui.TextDisabled($"({roundCount} round{(roundCount == 1 ? "" : "s")}, {stats.TotalWagered:N0} gil wagered)");

        if (roundCount > 0 && roundCount < MeaningfulSampleRounds)
        {
            ImGui.TextDisabled("Small sample - realized edge is mostly luck at this many rounds.");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    $"Blackjack results swing far more per round than the edge itself.\n" +
                    $"Expect realized and theoretical to disagree wildly until roughly\n" +
                    $"{MeaningfulSampleRounds:N0} rounds. Treat this as a curiosity, not a scorecard.");
        }
    }

    private static void DrawRow(string label, double? edge, string tooltip)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        if (!edge.HasValue)
        {
            ImGui.TextDisabled("-");
        }
        else
        {
            var pct   = edge.Value * 100;
            var color = pct >= 0 ? GameColors.ProfitGreen : GameColors.BustRed;
            ImGui.TextColored(color, $"{pct:+0.00;-0.00}%");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }
}
