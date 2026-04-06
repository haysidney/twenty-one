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
    public GameState GameState { get; set; } = new();

    // Snapshots pushed before each Apply; cleared on NewRound. Persisted so undo
    // survives plugin restarts within the same round.
    public List<GameState> UndoStack { get; set; } = [];
    public List<GameState> RedoStack { get; set; } = [];

    // ── Narration ──────────────────────────────────────────────────────────────
    // Kept separate from GameState so it is never rolled back by undo.
    public List<string> NarrationLog { get; set; } = [];
    public bool NarrationUseChannelCommand { get; set; } = false;
    public bool NarrationPanelOpen { get; set; } = true;

    // ── Chat ───────────────────────────────────────────────────────────────────
    public bool ChatEnabled { get; set; } = false;
    public string ChatChannel { get; set; } = "/p";
    public int PublicChatCooldownMs  { get; set; } = 2000;
    public int PrivateChatCooldownMs { get; set; } = 1000;

    // ── Narration templates ────────────────────────────────────────────────────
    public NarrationTemplates NarrationTemplates { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
