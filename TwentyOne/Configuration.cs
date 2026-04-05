using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using TwentyOne.Game;

namespace TwentyOne;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // ── Game state ─────────────────────────────────────────────────────────────
    // Property name intentionally kept as "GameState" for JSON backward compatibility.
    public GameState GameState { get; set; } = new();

    // Snapshots pushed before each Apply; cleared on NewRound. Persisted so undo
    // survives plugin restarts within the same round.
    public List<GameState> UndoStack { get; set; } = [];

    // ── Narration ──────────────────────────────────────────────────────────────
    // Kept separate from GameState so it is never rolled back by undo.
    public List<string> NarrationLog { get; set; } = [];
    public bool NarrationUseChannelCommand { get; set; } = false;
    public bool NarrationPanelOpen { get; set; } = true;

    // ── Chat ───────────────────────────────────────────────────────────────────
    public bool ChatEnabled { get; set; } = false;
    public string ChatChannel { get; set; } = "/p";

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
