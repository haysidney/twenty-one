using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public class PlayerStatsWindow : Window, IDisposable
{
    private readonly Configuration config;

    public PlayerStatsWindow(Configuration config)
        : base("Player Stats##TwentyOneStats")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Reset Stats"))
        {
            config.PlayerStatsStore.Clear();
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Export"))
            ImGui.SetClipboardText(BuildExportText());

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##stats", 6, flags))
        {
            ImGui.TableSetupColumn("Player"u8,  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Played"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Won"u8,     ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Lost"u8,    ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Win %"u8,   ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Total Won"u8, ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
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
                ImGui.TextUnformatted(stat.GamesLost.ToString());

                ImGui.TableSetColumnIndex(4);
                ImGui.AlignTextToFramePadding();
                var winPct = stat.GamesPlayed > 0
                    ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                    : "-";
                ImGui.TextUnformatted(winPct);

                ImGui.TableSetColumnIndex(5);
                ImGui.AlignTextToFramePadding();
                var totalColor = stat.TotalWon > 0
                    ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                    : stat.TotalWon < 0
                        ? new Vector4(1f, 0.35f, 0.35f, 1f)
                        : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                var totalStr = stat.TotalWon > 0 ? $"+{stat.TotalWon:0.##}" : $"{stat.TotalWon:0.##}";
                ImGui.TextColored(totalColor, totalStr);
            }

            ImGui.EndTable();
        }
    }

    private string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Player\tPlayed\tWon\tLost\tWin%\tTotal Won");
        foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
        {
            var winPct = stat.GamesPlayed > 0
                ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                : "-";
            var totalStr = stat.TotalWon > 0 ? $"+{stat.TotalWon:0.##}" : $"{stat.TotalWon:0.##}";
            sb.AppendLine($"{stat.DisplayName}\t{stat.GamesPlayed}\t{stat.GamesWon}\t{stat.GamesLost}\t{winPct}\t{totalStr}");
        }
        return sb.ToString().TrimEnd();
    }
}
