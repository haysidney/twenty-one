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
            MinimumSize = new Vector2(300, 1),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
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

        if (_selectedIndex >= sessions.Count) _selectedIndex = sessions.Count - 1;

        if (_selectedIndex >= 0)
            DrawDetail(sessions);
        else
            DrawList(sessions);
    }

    private void DrawList(System.Collections.Generic.List<PlayerStatsSession> sessions)
    {
        var listFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##sessionlist", 3, listFlags))
        {
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
                var isSelected = false;
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
    }

    private void DrawDetail(System.Collections.Generic.List<PlayerStatsSession> sessions)
    {
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var sel = sessions[_selectedIndex];

        if (ImGui.Button("Back"))
            _selectedIndex = -1;

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{sel.Date:dddd, MMMM d, yyyy  HH:mm}");

        ImGui.SameLine();
        var deleteLabel = "Delete Session";
        var deleteWidth = ImGui.CalcTextSize(deleteLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var targetX = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - deleteWidth;
        if (ImGui.GetCursorPosX() < targetX) ImGui.SetCursorPosX(targetX);
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.Button(deleteLabel) && ctrlHeld)
        {
            sessions.RemoveAt(_selectedIndex);
            config.Save();
            _selectedIndex = -1;
            if (!ctrlHeld) ImGui.EndDisabled();
            return;
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to delete this session");

        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
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
        ImGui.TextUnformatted("Net (all players):");
        ImGui.SameLine();
        ImGui.TextColored(grandColor, grandStr);
    }
}
