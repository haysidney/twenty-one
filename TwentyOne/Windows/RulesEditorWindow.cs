using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

public class RulesEditorWindow : Window
{
    private readonly Configuration config;

    public RulesEditorWindow(Configuration config)
        : base("Twenty One - Blackjack Rules##RulesEditor")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
    }

    private double?   _cachedHouseEdge;
    private double?   _edgeS17AtDefault;
    private double?   _edgeDASAtDefault;
    private double?   _edgeHSAAtDefault;
    private double?   _edgeRSAAtDefault;
    private double?   _edgeSurrenderAtDefault;
    private double?   _edgeBjPayoutAtDefault;
    private double?   _edgeFiveCardCharlieAtDefault;
    private double?   _edgeCharliePayoutAtDefault;
    private double?   _edgeResplitCapAtDefault;
    private EdgeRules _cachedEdgeRules;

    public override void Draw()
    {
        ImGui.TextDisabled("Rule changes take effect when the next round is dealt. Edits made during");
        ImGui.TextDisabled("the Betting phase apply to the round about to start.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        EnsureEdgeCache();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Blackjack Payout");
        ImGui.SameLine();
        var bjMul = (float)config.BjPayout;
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputFloat("##bjpayout", ref bjMul, 0f, 0f, "%.2fx"))
        {
            config.BjPayout = Math.Clamp(bjMul, 1.0f, 3.0f);
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("3:2##bj32")) { config.BjPayout = 1.5; config.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("6:5##bj65")) { config.BjPayout = 1.2; config.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("1:1##bj11")) { config.BjPayout = 1.0; config.Save(); }
        DrawDefaultDelta(_edgeBjPayoutAtDefault, "3:2");

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Five Card Charlie");
        ImGui.SameLine();
        var charlieOptions = new[] { "Disabled", "Beats all", "Loses to dealer BJ" };
        var charlieIdx     = (int)config.FiveCardCharlie;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##fiveCardCharlie", ref charlieIdx, charlieOptions, charlieOptions.Length))
        {
            config.FiveCardCharlie = (FiveCardCharlieRule)charlieIdx;
            config.Save();
        }
        DrawDefaultDelta(_edgeFiveCardCharlieAtDefault, "Disabled");

        if (config.FiveCardCharlie != FiveCardCharlieRule.Disabled)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Charlie Payout");
            ImGui.SameLine();
            var charliePayoutOptions = new[] { "3:2", "6:5", "1:1" };
            var charliePayoutIdx     = (int)config.CharliePayout;
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("##charliepayout", ref charliePayoutIdx, charliePayoutOptions, charliePayoutOptions.Length))
            {
                config.CharliePayout = (PayoutRatio)charliePayoutIdx;
                config.Save();
            }
            DrawDefaultDelta(_edgeCharliePayoutAtDefault, "1:1");
        }

        var s17 = config.DealerStandsOnSoft17;
        if (ImGui.Checkbox("Dealer stands on soft 17", ref s17))
        {
            config.DealerStandsOnSoft17 = s17;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When off (default), dealer hits soft 17 (H17). When on, dealer stands on soft 17 (S17).");
        DrawDefaultDelta(_edgeS17AtDefault, "off");

        var das = config.DoubleAfterSplit;
        if (ImGui.Checkbox("Allow double after split", ref das))
        {
            config.DoubleAfterSplit = das;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When on (default), the player may double down on a hand created by splitting.\nWhen off, only non-split hands can be doubled.");
        DrawDefaultDelta(_edgeDASAtDefault, "on");

        var hsa = config.HitSplitAces;
        if (ImGui.Checkbox("Allow hitting split aces", ref hsa))
        {
            config.HitSplitAces = hsa;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When off (default), a split-ace hand receives exactly one extra card and auto-stands.\nWhen on, split-ace hands may be hit further.\n21 on a split-ace hand is still treated as Stand, not Blackjack.");
        DrawDefaultDelta(_edgeHSAAtDefault, "off");

        var rsa = config.ResplitAces;
        if (ImGui.Checkbox("Allow resplitting aces", ref rsa))
        {
            config.ResplitAces = rsa;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When on, a pair of aces produced by an earlier split may be split again.\nWhen off (default), split-ace pairs cannot be resplit.");
        DrawDefaultDelta(_edgeRSAAtDefault, "off");

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Resplit cap");
        ImGui.SameLine();
        var capOptions = new[] { "Max 2 hands", "Max 3 hands", "Max 4 hands", "Unlimited" };
        var capIdx     = (int)config.ResplitCap;
        ImGui.SetNextItemWidth(140);
        if (ImGui.Combo("##resplitcap", ref capIdx, capOptions, capOptions.Length))
        {
            config.ResplitCap = (ResplitCap)capIdx;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Maximum number of hands a non-ace pair may be split into.\nAces ignore this cap and follow the Resplit aces toggle.");
        DrawDefaultDelta(_edgeResplitCapAtDefault, "Max 4 hands");

        var surrender = config.AllowSurrender;
        if (ImGui.Checkbox("Allow surrender", ref surrender))
        {
            config.AllowSurrender = surrender;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When on, the player may surrender an initial 2-card hand for half their bet.\nNot available after hit, split, or double.");
        DrawDefaultDelta(_edgeSurrenderAtDefault, "off");

        DrawHouseEdgeLabel();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.Button("Reset Rules##rulesReset"))
        {
            var defaults = new GameState();
            config.BjPayout             = defaults.BjPayout;
            config.CharliePayout        = defaults.CharliePayout;
            config.FiveCardCharlie      = defaults.FiveCardCharlie;
            config.DealerStandsOnSoft17 = defaults.DealerStandsOnSoft17;
            config.DoubleAfterSplit     = defaults.DoubleAfterSplit;
            config.HitSplitAces         = defaults.HitSplitAces;
            config.ResplitAces          = defaults.ResplitAces;
            config.ResplitCap           = defaults.ResplitCap;
            config.AllowSurrender       = defaults.AllowSurrender;
            config.Save();
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset all rules to their default values\n(3:2 BJ, Charlie disabled, H17, DAS on, no HSA / RSA / Surrender)."u8);
    }

    private void EnsureEdgeCache()
    {
        var rules = new EdgeRules(
            config.BjPayout,
            config.CharliePayout,
            config.FiveCardCharlie,
            config.DealerStandsOnSoft17,
            config.DoubleAfterSplit,
            config.HitSplitAces,
            config.ResplitAces,
            config.AllowSurrender,
            config.ResplitCap);

        if (_cachedHouseEdge.HasValue && _cachedEdgeRules.Equals(rules)) return;

        _cachedHouseEdge = EdgeSolver.ComputeHouseEdge(rules);

        _edgeS17AtDefault = !rules.DealerStandsOnSoft17
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { DealerStandsOnSoft17 = false });
        _edgeDASAtDefault = rules.DoubleAfterSplit
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { DoubleAfterSplit = true });
        _edgeHSAAtDefault = !rules.HitSplitAces
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { HitSplitAces = false });
        _edgeRSAAtDefault = !rules.ResplitAces
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { ResplitAces = false });
        _edgeSurrenderAtDefault = !rules.AllowSurrender
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { AllowSurrender = false });

        _edgeBjPayoutAtDefault = Math.Abs(rules.BjPayout - 1.5) < 1e-9
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { BjPayout = 1.5 });
        _edgeFiveCardCharlieAtDefault = rules.FiveCardCharlie == FiveCardCharlieRule.Disabled
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { FiveCardCharlie = FiveCardCharlieRule.Disabled });
        _edgeCharliePayoutAtDefault = rules.CharliePayout == PayoutRatio.EvenMoney
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { CharliePayout = PayoutRatio.EvenMoney });
        _edgeResplitCapAtDefault = rules.ResplitCap == ResplitCap.Max4
            ? null
            : EdgeSolver.ComputeHouseEdge(rules with { ResplitCap = ResplitCap.Max4 });

        _cachedEdgeRules = rules;
    }

    // Shows the edge effect of a knob being at its current setting vs the default,
    // from the dealer/house perspective: positive = current setting raises house
    // edge (green, more house money), negative = current setting lowers house edge
    // (red, less house money). At default, shows a greyed "(0.00%)".
    private void DrawDefaultDelta(double? edgeAtDefault, string defaultLabel)
    {
        if (!_cachedHouseEdge.HasValue) return;
        ImGui.SameLine();
        if (!edgeAtDefault.HasValue)
        {
            ImGui.TextDisabled("(0.00%)");
        }
        else
        {
            var delta = (_cachedHouseEdge.Value - edgeAtDefault.Value) * 100;
            if (Math.Abs(delta) < 0.005)
            {
                ImGui.TextDisabled("(0.00%)");
            }
            else
            {
                var color = delta > 0
                    ? new Vector4(0.65f, 0.85f, 0.65f, 1f)   // raises house edge: green
                    : new Vector4(0.95f, 0.55f, 0.55f, 1f); // lowers house edge: red
                ImGui.TextColored(color, $"({delta:+0.00;-0.00}%)");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"House edge vs the default value ({defaultLabel}).");
    }

    private void DrawHouseEdgeLabel()
    {
        if (!_cachedHouseEdge.HasValue) return;
        var edge = _cachedHouseEdge.Value;
        var pct  = edge * 100;
        var color = edge >= 0
            ? new Vector4(0.65f, 0.85f, 0.65f, 1f)
            : new Vector4(0.95f, 0.55f, 0.55f, 1f);
        var label = edge >= 0
            ? $"House edge: {pct:F2}%"
            : $"Player edge: {-pct:F2}%";
        ImGui.TextColored(color, label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Expected house edge under optimal player strategy, infinite-deck draws.");
    }
}
