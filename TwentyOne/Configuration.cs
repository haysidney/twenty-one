using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
public class VenueSettings
{
    public string Name { get; set; } = "Venue 1";

    // ── Narration ──────────────────────────────────────────────────────────────
    public bool NarrationUseChannelCommand { get; set; } = false;
    public bool NarrationPanelOpen         { get; set; } = true;

    // ── Trade ──────────────────────────────────────────────────────────────────
    public bool AutoTradeEnabled  { get; set; } = false;
    public bool AutoBetFromTrades { get; set; } = false;

    // ── Targeting ──────────────────────────────────────────────────────────────
    public bool AutoTargetEnabled   { get; set; } = false;
    public bool RemindTargetEnabled { get; set; } = false;

    // ── Chat ───────────────────────────────────────────────────────────────────
    public bool   ChatEnabled               { get; set; } = false;
    public string ChatChannel               { get; set; } = "/p";
    public bool   AllowCrossChannelCommands { get; set; } = false;
    public int    PublicChatCooldownMs      { get; set; } = 2000;
    public int    PrivateChatCooldownMs     { get; set; } = 1200;

    // ── Narration templates ────────────────────────────────────────────────────
    public NarrationTemplates NarrationTemplates { get; set; } = new();

    // Used as {dealer} in narration templates.
    public string DealerName { get; set; } = "Dealer";

    // ── Gil tracker ────────────────────────────────────────────────────────────
    public long       GilStart     { get; set; } = 0;
    public long       GilEnd       { get; set; } = 0;
    public int        DealerCutPct { get; set; } = 0;
    public List<long> Tips         { get; set; } = [];

    // ── Player stats ───────────────────────────────────────────────────────────
    // Key: "{FullName}@{World}" for FFXIV players, Nickname for manual players.
    public Dictionary<string, PlayerStat> PlayerStatsStore { get; set; } = [];
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

    // ── Narration log ─────────────────────────────────────────────────────────
    // Kept separate from GameState so it is never rolled back by undo.
    public List<string> NarrationLog { get; set; } = [];

    // ── Venues ────────────────────────────────────────────────────────────────
    public List<VenueSettings> Venues           { get; set; } = [];
    public int                 ActiveVenueIndex { get; set; } = 0;

    // Ensures at least one venue exists (handles first-ever launch / old configs).
    public void EnsureVenues()
    {
        if (Venues.Count == 0) Venues.Add(new VenueSettings());
        ActiveVenueIndex = Math.Clamp(ActiveVenueIndex, 0, Venues.Count - 1);
    }

    [JsonIgnore] public VenueSettings ActiveVenue => Venues[ActiveVenueIndex];

    // ── Proxy properties (delegate to ActiveVenue, not serialized) ────────────
    [JsonIgnore] public bool   NarrationUseChannelCommand { get => ActiveVenue.NarrationUseChannelCommand; set => ActiveVenue.NarrationUseChannelCommand = value; }
    [JsonIgnore] public bool   NarrationPanelOpen         { get => ActiveVenue.NarrationPanelOpen;         set => ActiveVenue.NarrationPanelOpen = value; }
    [JsonIgnore] public bool   AutoTradeEnabled           { get => ActiveVenue.AutoTradeEnabled;            set => ActiveVenue.AutoTradeEnabled = value; }
    [JsonIgnore] public bool   AutoBetFromTrades          { get => ActiveVenue.AutoBetFromTrades;           set => ActiveVenue.AutoBetFromTrades = value; }
    [JsonIgnore] public bool   AutoTargetEnabled          { get => ActiveVenue.AutoTargetEnabled;           set => ActiveVenue.AutoTargetEnabled = value; }
    [JsonIgnore] public bool   RemindTargetEnabled        { get => ActiveVenue.RemindTargetEnabled;         set => ActiveVenue.RemindTargetEnabled = value; }
    [JsonIgnore] public bool   ChatEnabled                { get => ActiveVenue.ChatEnabled;                 set => ActiveVenue.ChatEnabled = value; }
    [JsonIgnore] public string ChatChannel                { get => ActiveVenue.ChatChannel;                 set => ActiveVenue.ChatChannel = value; }
    [JsonIgnore] public bool   AllowCrossChannelCommands  { get => ActiveVenue.AllowCrossChannelCommands;   set => ActiveVenue.AllowCrossChannelCommands = value; }
    [JsonIgnore] public int    PublicChatCooldownMs       { get => ActiveVenue.PublicChatCooldownMs;        set => ActiveVenue.PublicChatCooldownMs = value; }
    [JsonIgnore] public int    PrivateChatCooldownMs      { get => ActiveVenue.PrivateChatCooldownMs;       set => ActiveVenue.PrivateChatCooldownMs = value; }
    [JsonIgnore] public NarrationTemplates NarrationTemplates { get => ActiveVenue.NarrationTemplates;     set => ActiveVenue.NarrationTemplates = value; }
    [JsonIgnore] public string DealerName                 { get => ActiveVenue.DealerName;                 set => ActiveVenue.DealerName = value; }
    [JsonIgnore] public long   GilStart                   { get => ActiveVenue.GilStart;                   set => ActiveVenue.GilStart = value; }
    [JsonIgnore] public long   GilEnd                     { get => ActiveVenue.GilEnd;                     set => ActiveVenue.GilEnd = value; }
    [JsonIgnore] public int    DealerCutPct               { get => ActiveVenue.DealerCutPct;               set => ActiveVenue.DealerCutPct = value; }
    [JsonIgnore] public List<long> Tips                   { get => ActiveVenue.Tips;                       set => ActiveVenue.Tips = value; }
    [JsonIgnore] public Dictionary<string, PlayerStat> PlayerStatsStore { get => ActiveVenue.PlayerStatsStore; set => ActiveVenue.PlayerStatsStore = value; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
