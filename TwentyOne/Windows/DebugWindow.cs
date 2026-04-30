#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public class DebugWindow : Window
{
    private readonly Configuration    config;
    private readonly MainWindow       mainWindow;
    private readonly FileDialogManager _fileDialog = new();

    private string _rollInput = string.Empty;

    public DebugWindow(Configuration config, MainWindow mainWindow)
        : base("Debug##TwentyOneDebug")
    {
        this.config     = config;
        this.mainWindow = mainWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        _fileDialog.Draw();

        // ── Roll queue ────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Roll Queue");

        var queue = mainWindow.DebugRollQueue;
        ImGui.TextUnformatted($"Queued rolls: {queue.Count}");
        if (queue.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##clearQueue"))
                queue.Clear();

            var preview = string.Join(", ", queue.Take(20)) + (queue.Count > 20 ? ", ..." : "");
            ImGui.TextUnformatted(preview);
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("##rollInput", ref _rollInput, 512);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Comma-separated card values (1–13). Enqueued in order."u8);
        ImGui.SameLine();
        if (ImGui.SmallButton("Enqueue##enqRolls"))
            EnqueueRollString(_rollInput);

        if (ImGui.SmallButton("Load scenario##loadScenario"))
        {
            _fileDialog.OpenFileDialog("Load Roll Scenario", "JSON{.json}", (ok, path) =>
            {
                if (!ok) return;
                try
                {
                    var text = File.ReadAllText(path);
                    var scenario = JsonSerializer.Deserialize<DebugScenario>(text);
                    if (scenario?.Rolls != null)
                        foreach (var r in scenario.Rolls)
                            if (r >= 1 && r <= 13)
                                queue.Enqueue(r);
                }
                catch { }
            });
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("JSON: { \"name\": \"...\", \"rolls\": [1, 10, 7, 6] }\nAppends rolls to queue."u8);

        // ── Snapshot save/load ────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Game State Snapshot");

        if (ImGui.SmallButton("Save snapshot##saveSnap"))
        {
            var json = JsonSerializer.Serialize(config.GameState, new JsonSerializerOptions { WriteIndented = true });
            _fileDialog.SaveFileDialog("Save Debug Snapshot", "JSON{.json}", "snapshot", ".json",
                (ok, path) => { if (ok) File.WriteAllText(path, json); });
        }

        ImGui.SameLine();

        if (ImGui.SmallButton("Load snapshot##loadSnap"))
        {
            _fileDialog.OpenFileDialog("Load Debug Snapshot", "JSON{.json}", (ok, path) =>
            {
                if (!ok) return;
                try
                {
                    var text  = File.ReadAllText(path);
                    var state = JsonSerializer.Deserialize<GameState>(text);
                    if (state == null) return;
                    config.GameState  = state;
                    config.UndoStack.Clear();
                    config.RedoStack.Clear();
                    config.Save();
                }
                catch { }
            });
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Overwrites current game state. Clears undo/redo stack."u8);
    }

    private void EnqueueRollString(string input)
    {
        var queue = mainWindow.DebugRollQueue;
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var r) && r >= 1 && r <= 13)
                queue.Enqueue(r);
        _rollInput = string.Empty;
    }
}

public class DebugScenario
{
    public string?    Name  { get; set; }
    public List<int>? Rolls { get; set; }
}
#endif
