using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public class PlayerStatsHistoryWindow : Window
{
    private readonly Configuration config;
    private int _selectedIndex = -1;

    public PlayerStatsHistoryWindow(Configuration config)
        : base("Player Stats History##TwentyOneStatsHistory")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void OnOpen() => _selectedIndex = -1;

    public override void Draw()
    {
        var sessions = config.StatsSessions;

        if (sessions.Count == 0)
        {
            ImGui.TextUnformatted("No sessions recorded yet. Use \"Start Night\" in the Player Stats window to begin tracking sessions.");
            return;
        }

        // Clamp selection in case sessions were deleted
        if (_selectedIndex >= sessions.Count) _selectedIndex = sessions.Count - 1;

        var ctrlHeld = ImGui.GetIO().KeyCtrl;

        // Session list on the left
        ImGui.BeginGroup();
        var listFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY;
        var listHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginTable("##sessionlist", 3, listFlags, new Vector2(280, listHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Date"u8,    ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Players"u8, ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            for (var i = sessions.Count - 1; i >= 0; i--)
            {
                var session = sessions[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                var isSelected = _selectedIndex == i;
                if (ImGui.Selectable($"{session.Date:MM/dd/yy HH:mm}##sess{i}",
                        ref isSelected,
                        ImGuiSelectableFlags.SpanAllColumns))
                    _selectedIndex = i;

                ImGui.TableSetColumnIndex(1);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(session.Stats.Count.ToString());

                ImGui.TableSetColumnIndex(2);
                ImGui.AlignTextToFramePadding();
                var netColor = session.BankNet > 0
                    ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                    : session.BankNet < 0
                        ? new Vector4(1f, 0.35f, 0.35f, 1f)
                        : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                var netStr = session.BankNet > 0
                    ? $"+{GameEngine.FormatGil(session.BankNet)}"
                    : GameEngine.FormatGil(session.BankNet);
                ImGui.TextColored(netColor, netStr);
            }

            ImGui.EndTable();
        }

        // Delete button below list
        if (_selectedIndex >= 0)
        {
            if (!ctrlHeld) ImGui.BeginDisabled();
            if (ImGui.Button("Delete Session") && ctrlHeld)
            {
                sessions.RemoveAt(_selectedIndex);
                config.Save();
                _selectedIndex = Math.Min(_selectedIndex, sessions.Count - 1);
            }
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold Ctrl and click to delete this session");
        }
        ImGui.EndGroup();

        if (_selectedIndex < 0 || _selectedIndex >= sessions.Count) return;

        ImGui.SameLine();

        // Stats table for selected session
        ImGui.BeginGroup();
        var sel = sessions[_selectedIndex];
        ImGui.Text($"Session: {sel.Date:dddd, MMMM d, yyyy  HH:mm}");
        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (ImGui.BeginTable("##sessStats", 8, tableFlags))
        {
            ImGui.TableSetupColumn("Player"u8,  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Played"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Won"u8,     ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Pushes"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Lost"u8,    ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("BJs"u8,     ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Win %"u8,   ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var stat in sel.Stats.Values.OrderBy(s => s.DisplayName))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.DisplayName);
                ImGui.TableSetColumnIndex(1);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesPlayed.ToString());
                ImGui.TableSetColumnIndex(2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesWon.ToString());
                ImGui.TableSetColumnIndex(3);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesPushed.ToString());
                ImGui.TableSetColumnIndex(4);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesLost.ToString());
                ImGui.TableSetColumnIndex(5);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.Blackjacks.ToString());
                ImGui.TableSetColumnIndex(6);
                ImGui.AlignTextToFramePadding();
                var winPct = stat.GamesPlayed > 0
                    ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                    : "-";
                ImGui.TextUnformatted(winPct);
                ImGui.TableSetColumnIndex(7);
                ImGui.AlignTextToFramePadding();
                var col = stat.TotalWon > 0
                    ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                    : stat.TotalWon < 0
                        ? new Vector4(1f, 0.35f, 0.35f, 1f)
                        : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                var totalStr = stat.TotalWon > 0 ? $"+{GameEngine.FormatGil(stat.TotalWon)}" : GameEngine.FormatGil(stat.TotalWon);
                ImGui.TextColored(col, totalStr);
            }

            ImGui.EndTable();
        }

        var grandTotal = sel.Stats.Values.Sum(s => s.TotalWon);
        var grandColor = grandTotal > 0
            ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
            : grandTotal < 0
                ? new Vector4(1f, 0.35f, 0.35f, 1f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1f);
        var grandStr = grandTotal > 0 ? $"+{GameEngine.FormatGil(grandTotal)}" : GameEngine.FormatGil(grandTotal);
        ImGui.Spacing();
        ImGui.Text("Net (all players):");
        ImGui.SameLine();
        ImGui.TextColored(grandColor, grandStr);
        ImGui.EndGroup();
    }
}
