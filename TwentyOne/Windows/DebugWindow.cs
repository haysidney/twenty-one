#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

// ── Scenario data model ───────────────────────────────────────────────────────

public class DebugScenarioFile
{
    public string?                   Name            { get; set; }
    public List<DebugScenarioPlayer>? Players        { get; set; }
    public List<int>?                Rolls           { get; set; }
    public List<string>?             Actions         { get; set; }
    public FiveCardCharlieRule?      FiveCardCharlie { get; set; }
    public double?               BjPayout        { get; set; }
}

public class DebugScenarioPlayer
{
    public string Name      { get; set; } = string.Empty;
    public string Bet       { get; set; } = "0";
    public bool   SittingOut { get; set; } = false;
}

// ── Runtime scenario state ────────────────────────────────────────────────────

public class ActiveScenario
{
    public string Name      { get; }
    public int    Remaining => _actions.Count;

    private readonly Queue<string> _actions;

    public ActiveScenario(string name, IEnumerable<string> actions)
    {
        Name     = name;
        _actions = new Queue<string>(actions);
    }

    public string? PeekNext() => _actions.TryPeek(out var s) ? s : null;
    public bool    Advance()  => _actions.TryDequeue(out _);
}

// ── Debug window ──────────────────────────────────────────────────────────────

public class DebugWindow : Window
{
    private readonly Configuration    config;
    private readonly MainWindow       mainWindow;
    private readonly FileDialogManager _fileDialog = new();

    private string       _rollInput      = string.Empty;
    private List<string> _playlist       = [];
    private int          _playlistIndex  = -1;
    private string       _playlistFile   = string.Empty;  // filename of loaded scenario

    public DebugWindow(Configuration config, MainWindow mainWindow)
        : base("Debug##TwentyOneDebug")
    {
        this.config     = config;
        this.mainWindow = mainWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        _fileDialog.Draw();

        // ── Scenario ──────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Scenario");

        var active = mainWindow.Scenario.ActiveScenario;
        if (active != null)
        {
            ImGui.TextColored(GameColors.ActiveOrange, $"Active: {active.Name}");
            if (_playlistFile.Length > 0)
                ImGui.TextDisabled(_playlistFile);
            ImGui.TextUnformatted($"Next: {active.PeekNext() ?? "(done)"}  ({active.Remaining} remaining)");

            if (ImGui.SmallButton("Step##scenStep"))
                mainWindow.ExecuteNextScenarioStep();
            ImGui.SameLine();
            if (ImGui.SmallButton("Fast Forward##scenFF"))
                mainWindow.Scenario.FastForward = true;
            ImGui.SameLine();
            if (ImGui.SmallButton("Abort##scenAbort"))
            {
                mainWindow.Scenario.ActiveScenario = null;
                mainWindow.Scenario.FastForward = false;
                mainWindow.Scenario.RollQueue.Clear();
            }
            var gate = mainWindow.Scenario.GateButtons;
            if (ImGui.Checkbox("Gate buttons##scenGate", ref gate))
                mainWindow.Scenario.GateButtons = gate;
        }
        else
        {
            ImGui.TextDisabled("No scenario loaded.");
        }

        // Playlist navigation
        if (_playlist.Count > 0)
        {
            var hasPrev = _playlistIndex > 0;
            var hasNext = _playlistIndex >= 0 && _playlistIndex < _playlist.Count - 1;
            if (!hasPrev) ImGui.BeginDisabled();
            if (ImGui.SmallButton("< Prev##scenPrev"))
                LoadScenario(_playlist[_playlistIndex - 1]);
            if (!hasPrev) ImGui.EndDisabled();
            ImGui.SameLine();
            if (_playlistIndex >= 0)
            {
                if (ImGui.SmallButton("Replay##scenReplay"))
                    LoadScenario(_playlist[_playlistIndex]);
                ImGui.SameLine();
                ImGui.TextDisabled($"{_playlistIndex + 1}/{_playlist.Count}");
                ImGui.SameLine();
            }
            if (!hasNext) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Next >##scenNext"))
                LoadScenario(_playlist[_playlistIndex + 1]);
            if (!hasNext) ImGui.EndDisabled();
        }

        if (ImGui.SmallButton("Load scenario##loadScen"))
        {
            _fileDialog.OpenFileDialog("Load Scenario", "JSON{.json}", (ok, path) =>
            {
                if (!ok) return;
                try { LoadScenario(path); } catch { /* Swallow load failures silently */ }
            });
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "JSON: { \"name\": \"...\", \"players\": [{\"name\":\"Lorah\",\"bet\":\"1000\"}],\n" +
                "        \"rolls\": [1,10,6,9], \"actions\": [\"StartDeal\",\"BeginPlayerTurns\",\n" +
                "        \"Stand:0:0\",\"BeginDealerTurn\",\"GoToPayout\",\"NewRound\"] }");

        // ── Roll queue ────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Roll Queue");

        var queue = mainWindow.Scenario.RollQueue;
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
                catch { /* Swallow deserialization failures silently */ }
            });
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Overwrites current game state. Clears undo/redo stack."u8);
    }

    private void LoadScenario(string path)
    {
        var text = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<DebugScenarioFile>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
        if (file == null) return;

        // Rebuild playlist if directory changed
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        if (_playlist.Count == 0 || Path.GetDirectoryName(_playlist[0]) != dir)
        {
            _playlist = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToList();
        }
        _playlistIndex = _playlist.IndexOf(path);
        _playlistFile  = Path.GetFileName(path);

        // Build initial GameState: Betting phase with players set
        var state = new GameState
        {
            BjPayout        = file.BjPayout ?? config.GameState.BjPayout,
            FiveCardCharlie = file.FiveCardCharlie ?? FiveCardCharlieRule.Disabled,
        };
        foreach (var sp in file.Players ?? [])
        {
            (state, _) = GameEngine.Apply(state, new AddPlayer(sp.Name));
            var pi = state.Players.Length - 1;
            if (sp.SittingOut)
                (state, _) = GameEngine.Apply(state, new ToggleSittingOut(pi));
            else
                (state, _) = GameEngine.Apply(state, new SetPlayerBet(pi, sp.Bet));
        }

        config.GameState = state;
        config.UndoStack.Clear();
        config.RedoStack.Clear();
        config.Save();
        mainWindow.ClearBetEdits();
        mainWindow.Scenario.FastForward = false;

        // Enqueue rolls
        var queue = mainWindow.Scenario.RollQueue;
        queue.Clear();
        foreach (var r in (file.Rolls ?? []).Where(r => r >= 1 && r <= 13))
            queue.Enqueue(r);

        // Set active scenario
        mainWindow.Scenario.ActiveScenario = new ActiveScenario(
            file.Name ?? Path.GetFileNameWithoutExtension(path),
            file.Actions ?? []);
    }

    private void EnqueueRollString(string input)
    {
        var queue = mainWindow.Scenario.RollQueue;
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var r) && r >= 1 && r <= 13)
                queue.Enqueue(r);
        _rollInput = string.Empty;
    }
}
#endif
