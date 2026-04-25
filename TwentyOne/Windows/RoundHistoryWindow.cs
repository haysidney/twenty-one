using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public class RoundHistoryWindow : Window
{
    private readonly Configuration config;
    private readonly MainWindow     mainWindow;

    public RoundHistoryWindow(Configuration config, MainWindow mainWindow)
        : base("Round History##TwentyOneHistory")
    {
        this.config     = config;
        this.mainWindow = mainWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        var history = config.RoundHistory;

        var canClear = history.Count > 0 && ImGui.GetIO().KeyCtrl;
        if (!canClear) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Clear History") && canClear)
        {
            history.Clear();
            config.Save();
        }
        if (!canClear) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to clear all round history");

        if (history.Count == 0)
        {
            ImGui.TextUnformatted("No rounds recorded yet.");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                  | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable
                  | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("##history", 5, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Round"u8,   ImGuiTableColumnFlags.WidthFixed,   50);
        ImGui.TableSetupColumn("Winners"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Losers"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pushes"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed,   80);
        ImGui.TableHeadersRow();

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];
            var state = entry.Snapshot;

            var winners = new System.Collections.Generic.List<string>();
            var losers  = new System.Collections.Generic.List<string>();
            var pushes  = new System.Collections.Generic.List<string>();

            for (var pi = 0; pi < state.Players.Count; pi++)
            {
                var p       = state.Players[pi];
                var results = Enumerable.Range(0, p.Hands.Count)
                    .Select(hi => GameEngine.GetPayoutResult(state, pi, hi))
                    .ToList();

                var anyWin  = results.Any(r => r is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin);
                var anyLose = results.Any(r => r == PayoutResult.Lose);
                var allPush = results.All(r => r == PayoutResult.Push);

                if      (anyWin && !anyLose) winners.Add(p.DisplayName);
                else if (anyLose && !anyWin) losers.Add(p.DisplayName);
                else if (allPush)            pushes.Add(p.DisplayName);
                else if (anyWin)             winners.Add(p.DisplayName); // mixed split result: count as win
                else                         losers.Add(p.DisplayName);
            }

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            var isSelected = false;
            if (ImGui.Selectable($"{entry.RoundNumber}##hist{i}",
                    ref isSelected,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                mainWindow.RestoreHistoricalRound(entry.Snapshot);
                IsOpen = false;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to view this round");

            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            if (winners.Count > 0)
                ImGui.TextColored(new Vector4(0.35f, 0.9f, 0.35f, 1f), string.Join(", ", winners));

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            if (losers.Count > 0)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), string.Join(", ", losers));

            ImGui.TableSetColumnIndex(3);
            ImGui.AlignTextToFramePadding();
            if (pushes.Count > 0)
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), string.Join(", ", pushes));

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            var netColor = entry.BankNet > 0
                ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                : entry.BankNet < 0
                    ? new Vector4(1f, 0.35f, 0.35f, 1f)
                    : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            var netStr = entry.BankNet > 0
                ? $"+{GameEngine.FormatGil(entry.BankNet)}"
                : GameEngine.FormatGil(entry.BankNet);
            ImGui.TextColored(netColor, netStr);
            if (entry.PlayerBanks.Count > 0 && ImGui.IsItemHovered())
            {
                var prevEntry   = i + 1 < history.Count ? history[i + 1] : null;
                var tipLines    = new System.Text.StringBuilder("Bank balances after payout:\n");
                foreach (var (pkey, pbal) in entry.PlayerBanks)
                {
                    var prevBal  = prevEntry != null && prevEntry.PlayerBanks.TryGetValue(pkey, out var pb) ? pb : 0;
                    var delta    = pbal - prevBal;
                    var deltaStr = delta >= 0 ? $"+{GameEngine.FormatGil(delta)}" : GameEngine.FormatGil(delta);
                    var name     = entry.Snapshot.Players
                        .Find(pl => (pl.FullName.Length > 0 ? $"{pl.FullName}@{pl.World}" : pl.Nickname) == pkey)
                        ?.DisplayName ?? pkey;
                    tipLines.AppendLine($"  {name}: {GameEngine.FormatGil(pbal)} ({deltaStr})");
                }
                ImGui.SetTooltip(tipLines.ToString().TrimEnd());
            }
        }

        ImGui.EndTable();
    }
}
