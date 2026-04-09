using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using TwentyOne.Game;

namespace TwentyOne;

[Serializable]
public class PlayerStat
{
    public string  DisplayName { get; set; } = string.Empty;
    public int     GamesPlayed { get; set; } = 0;
    public int     GamesWon    { get; set; } = 0;
    public int     GamesPushed { get; set; } = 0;
    public int     GamesLost   { get; set; } = 0;
    public int     Blackjacks  { get; set; } = 0;
    public decimal TotalWon    { get; set; } = 0;
}

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

    // ── Trade ──────────────────────────────────────────────────────────────────
    public bool AutoTradeEnabled   { get; set; } = false;
    public bool AutoBetFromTrades  { get; set; } = false;

    // ── Targeting ──────────────────────────────────────────────────────────────
    public bool AutoTargetEnabled      { get; set; } = false;
    public bool RemindTargetEnabled    { get; set; } = false;

    // ── Chat ───────────────────────────────────────────────────────────────────
    public bool ChatEnabled { get; set; } = false;
    public string ChatChannel { get; set; } = "/p";
    public bool AllowCrossChannelCommands { get; set; } = false;
    public int PublicChatCooldownMs  { get; set; } = 2000;
    public int PrivateChatCooldownMs { get; set; } = 1200;

    // ── Narration templates ────────────────────────────────────────────────────
    public NarrationTemplates NarrationTemplates { get; set; } = new();

    // Used as {dealer} in narration templates.
    public string DealerName { get; set; } = "Dealer";

    // ── Gil tracker ────────────────────────────────────────────────────────────
    public long GilStart     { get; set; } = 0;
    public long GilEnd       { get; set; } = 0;
    public int  DealerCutPct { get; set; } = 0;
    public List<long> Tips   { get; set; } = [];

    // ── Player stats ───────────────────────────────────────────────────────────
    // Key: "{FullName}@{World}" for FFXIV players, Nickname for manual players.
    public Dictionary<string, PlayerStat> PlayerStatsStore { get; set; } = [];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
