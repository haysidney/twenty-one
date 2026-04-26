using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public unsafe class PlayerStatsWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly FileDialogManager _fileDialogManager = new();
    private PlayerStatsHistoryWindow? _historyWindow;

    public void SetHistoryWindow(PlayerStatsHistoryWindow w) => _historyWindow = w;

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
        _fileDialogManager.Draw();

        if (ImGui.Button("Start Night"))
            ImGui.OpenPopup("StartNightConfirm##TwentyOne");

        if (ImGui.BeginPopup("StartNightConfirm##TwentyOne"))
        {
            ImGui.TextUnformatted("Save current stats as a session and start fresh?");
            ImGui.TextUnformatted("This will also clear tips and reset the bank tracker.");
            ImGui.Spacing();
            if (ImGui.Button("Confirm"))
            {
                StartNight();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Save the current night's stats as a session and reset for a new night."u8);

        ImGui.SameLine();
        if (ImGui.Button("History"))
        {
            if (_historyWindow != null)
                _historyWindow.IsOpen = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Stats"))
        {
            config.PlayerStatsStore.Clear();
            config.Save();
        }
        ImGui.SameLine();
        var shiftHeld = ImGui.GetIO().KeyShift;
        if (ImGui.Button("Export"))
        {
            if (shiftHeld)
            {
                _fileDialogManager.SaveFileDialog(
                    "Export Player Stats", "TSV{.tsv}", "player-stats", ".tsv",
                    (ok, path) => { if (ok) File.WriteAllText(path, BuildExportText()); });
            }
            else
            {
                ImGui.SetClipboardText(BuildExportText());
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shiftHeld
                ? "Save player stats to a TSV file."u8
                : "Copy player stats to clipboard as TSV. Shift+click to save to file."u8);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##stats", 8, flags))
        {
            ImGui.TableSetupColumn("Player"u8,    ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Played"u8,    ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Won"u8,       ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Pushes"u8,    ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Lost"u8,      ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("BJs"u8,       ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Win %"u8,     ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net"u8,       ImGuiTableColumnFlags.WidthFixed, 90);
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
                var totalColor = stat.TotalWon > 0
                    ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                    : stat.TotalWon < 0
                        ? new Vector4(1f, 0.35f, 0.35f, 1f)
                        : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                var totalStr = stat.TotalWon > 0 ? $"+{GameEngine.FormatGil(stat.TotalWon)}" : GameEngine.FormatGil(stat.TotalWon);
                ImGui.TextColored(totalColor, totalStr);
            }

            ImGui.EndTable();
        }

        var grandTotal = config.PlayerStatsStore.Values.Sum(s => s.TotalWon);
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
    }

    private void StartNight()
    {
        // Save current stats as a session (even if empty, to mark the boundary)
        if (config.PlayerStatsStore.Count > 0)
        {
            var bankNet = config.RoundHistory.Sum(r => r.BankNet);
            var snapshot = new PlayerStatsSession
            {
                Date    = DateTime.Now,
                Stats   = new System.Collections.Generic.Dictionary<string, PlayerStat>(
                              config.PlayerStatsStore.ToDictionary(kv => kv.Key,
                                  kv => new PlayerStat
                                  {
                                      DisplayName = kv.Value.DisplayName,
                                      GamesPlayed = kv.Value.GamesPlayed,
                                      GamesWon    = kv.Value.GamesWon,
                                      GamesPushed = kv.Value.GamesPushed,
                                      GamesLost   = kv.Value.GamesLost,
                                      Blackjacks  = kv.Value.Blackjacks,
                                      TotalWon    = kv.Value.TotalWon,
                                  })),
                BankNet = bankNet,
            };
            config.StatsSessions.Add(snapshot);
        }

        config.PlayerStatsStore.Clear();
        config.Tips.Clear();
        config.RoundHistory.Clear();
        var currentGil = (long)InventoryManager.Instance()->GetGil();
        config.GilStart = currentGil;
        config.GilEnd   = currentGil;
        config.Save();
    }

    private string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Player\tPlayed\tWon\tPushes\tLost\tBJs\tWin%\tNet");
        foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
        {
            var winPct = stat.GamesPlayed > 0
                ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                : "-";
            var totalStr = stat.TotalWon > 0 ? $"+{GameEngine.FormatGil(stat.TotalWon)}" : GameEngine.FormatGil(stat.TotalWon);
            sb.AppendLine($"{stat.DisplayName}\t{stat.GamesPlayed}\t{stat.GamesWon}\t{stat.GamesPushed}\t{stat.GamesLost}\t{stat.Blackjacks}\t{winPct}\t{totalStr}");
        }
        return sb.ToString().TrimEnd();
    }
}
