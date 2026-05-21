using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Newtonsoft.Json;
using TwentyOne.Game;

namespace TwentyOne;

[Serializable]
public class PlayerStatsSession
{
    public Guid                                Id          { get; set; } = Guid.NewGuid();
    public DateTime                            Date        { get; set; } = DateTime.Now;
    public string                              LocationKey { get; set; } = "";
    public Dictionary<string, PlayerStatData>  Stats       { get; set; } = [];
    public long                                BankNet     { get; set; } = 0;
    // Full per-round snapshots, including GameState (cards, players, rules) and
    // the bank deltas. Carrying full RoundHistoryEntry instead of a degraded
    // summary lets future code re-derive any per-session aggregate from raw data.
    public List<RoundHistoryEntry>             Rounds      { get; set; } = [];
    // Edge stats locked in at session-archive time: total original bets wagered
    // across the session, and the expected bank gain under the rules that were
    // actually in effect for each round (sum of bet × edge per round). May be
    // recomputed via the History detail "Recompute" button if the solver changes.
    public long                                TotalWagered       { get; set; } = 0;
    public double                              TheoreticalBankNet { get; set; } = 0;
    // Per-player bank transaction log as it stood at session-archive time. Keyed
    // by PlayerStatsKey. Lets a session retain the full deposit/bet/win/withdraw
    // history even after PlayerStatsStore is cleared for the next session.
    public Dictionary<string, List<BankTransactionEntry>> PlayerBankLogs { get; set; } = [];
    // Plugin version that wrote this session. Used to detect stored aggregates
    // computed by an older solver and trigger recomputation.
    public string                              PluginVersion { get; set; } = "";
}

[Serializable]
public class PlayerStat
{
    public string  DisplayName { get; set; } = string.Empty;
    public int     GamesPlayed { get; set; } = 0;
    public int     GamesWon    { get; set; } = 0;
    public int     GamesPushed { get; set; } = 0;
    public int     GamesLost   { get; set; } = 0;
    public int     Blackjacks  { get; set; } = 0;
    public int     Charlies    { get; set; } = 0;
    public long    TotalWon    { get; set; } = 0;
    public long    Bank        { get; set; } = 0;
    public bool    MaintainBet { get; set; } = false;
    public List<BankTransactionEntry> BankLog { get; set; } = [];
}

[Serializable]
public class VenueSettings
{
    public string Name { get; set; } = "Venue 1";
    public Guid   Id   { get; set; } = Guid.NewGuid();

    // ── Narration ──────────────────────────────────────────────────────────────
    public bool NarrationUseChannelCommand { get; set; } = false;

    // ── Trade ──────────────────────────────────────────────────────────────────
    public bool AutoTradeEnabled       { get; set; } = false;
    public bool AutoBetFromTrades      { get; set; } = false;
    public bool AutoDepositFromTrades  { get; set; } = false;

    // ── Targeting ──────────────────────────────────────────────────────────────
    public bool AutoTargetEnabled   { get; set; } = false;
    public bool RemindTargetEnabled { get; set; } = false;

    // ── Chat ───────────────────────────────────────────────────────────────────
    public bool   ChatEnabled               { get; set; } = false;
    public string ChatChannel               { get; set; } = "/p";
    public bool   AllowCrossChannelCommands { get; set; } = false;
    public int    PublicChatCooldownMs      { get; set; } = 2000;
    public int    PrivateChatCooldownMs     { get; set; } = 1200;
    public int    SlashCommandCooldownMs    { get; set; } = 1200;

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

    // ── Round history ──────────────────────────────────────────────────────────
    public List<RoundHistoryEntry> RoundHistory { get; set; } = [];

    // ── Stats sessions (one per night) ─────────────────────────────────────────
    // Not persisted in the main config JSON - each session lives in its own
    // file under {ConfigDirectory}/sessions/{venueId}/. SessionStore loads them
    // into this list at plugin startup and writes new entries to disk.
    [JsonIgnore]
    public List<PlayerStatsSession> StatsSessions { get; set; } = [];

    // ── Active session tracking ────────────────────────────────────────────────
    // Set on first GoToPayout; null = no session started yet this night.
    public DateTime? ActiveSessionStartedAt   { get; set; }
    public string?   ActiveSessionLocationKey { get; set; }

    // ── House rules ────────────────────────────────────────────────────────────
    // Canonical home for the rules that apply to the next round. The current
    // GameState mirrors these so undo snapshots and history viewer mode stay
    // faithful to the rules at time of recording. Configuration.SeedRulesIntoGameState
    // copies these into GameState on NewRound.
    public double              BjPayout             { get; set; } = 1.5;
    public PayoutRatio         CharliePayout        { get; set; } = PayoutRatio.EvenMoney;
    public FiveCardCharlieRule FiveCardCharlie      { get; set; } = FiveCardCharlieRule.Disabled;
    public bool                DealerStandsOnSoft17 { get; set; } = false;
    public bool                DoubleAfterSplit     { get; set; } = true;
    public bool                HitSplitAces         { get; set; } = false;
    public bool                ResplitAces          { get; set; } = false;
    public ResplitCap          ResplitCap           { get; set; } = ResplitCap.Max4;
    public bool                AllowSurrender       { get; set; } = false;
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

    // Global address → venue GUID map. Key: "{district}:{ward}:{plot}" (1-indexed).
    public Dictionary<string, string> VenueMemory { get; set; } = [];

    // Ensures at least one venue exists (handles first-ever launch / old configs).
    public void EnsureVenues()
    {
        if (Venues.Count == 0) Venues.Add(new VenueSettings());
        ActiveVenueIndex = Math.Clamp(ActiveVenueIndex, 0, Venues.Count - 1);
        // Migration: assign GUIDs to venues that were saved before this field existed.
        foreach (var v in Venues)
            if (v.Id == Guid.Empty) v.Id = Guid.NewGuid();
    }

    [JsonIgnore] public VenueSettings ActiveVenue => Venues[ActiveVenueIndex];

    // ── Proxy properties (delegate to ActiveVenue, not serialized) ────────────
    [JsonIgnore] public bool   NarrationUseChannelCommand { get => ActiveVenue.NarrationUseChannelCommand; set => ActiveVenue.NarrationUseChannelCommand = value; }
    [JsonIgnore] public bool   AutoTradeEnabled           { get => ActiveVenue.AutoTradeEnabled;            set => ActiveVenue.AutoTradeEnabled = value; }
    [JsonIgnore] public bool   AutoBetFromTrades          { get => ActiveVenue.AutoBetFromTrades;           set => ActiveVenue.AutoBetFromTrades = value; }
    [JsonIgnore] public bool   AutoDepositFromTrades      { get => ActiveVenue.AutoDepositFromTrades;       set => ActiveVenue.AutoDepositFromTrades = value; }
    [JsonIgnore] public bool   AutoTargetEnabled          { get => ActiveVenue.AutoTargetEnabled;           set => ActiveVenue.AutoTargetEnabled = value; }
    [JsonIgnore] public bool   RemindTargetEnabled        { get => ActiveVenue.RemindTargetEnabled;         set => ActiveVenue.RemindTargetEnabled = value; }
    [JsonIgnore] public bool   ChatEnabled                { get => ActiveVenue.ChatEnabled;                 set => ActiveVenue.ChatEnabled = value; }
    [JsonIgnore] public string ChatChannel                { get => ActiveVenue.ChatChannel;                 set => ActiveVenue.ChatChannel = value; }
    [JsonIgnore] public bool   AllowCrossChannelCommands  { get => ActiveVenue.AllowCrossChannelCommands;   set => ActiveVenue.AllowCrossChannelCommands = value; }
    [JsonIgnore] public int    PublicChatCooldownMs       { get => ActiveVenue.PublicChatCooldownMs;        set => ActiveVenue.PublicChatCooldownMs = value; }
    [JsonIgnore] public int    PrivateChatCooldownMs      { get => ActiveVenue.PrivateChatCooldownMs;       set => ActiveVenue.PrivateChatCooldownMs = value; }
    [JsonIgnore] public int    SlashCommandCooldownMs     { get => ActiveVenue.SlashCommandCooldownMs;      set => ActiveVenue.SlashCommandCooldownMs = value; }
    [JsonIgnore] public NarrationTemplates NarrationTemplates { get => ActiveVenue.NarrationTemplates;     set => ActiveVenue.NarrationTemplates = value; }
    [JsonIgnore] public string DealerName                 { get => ActiveVenue.DealerName;                 set => ActiveVenue.DealerName = value; }
    [JsonIgnore] public long   GilStart                   { get => ActiveVenue.GilStart;                   set => ActiveVenue.GilStart = value; }
    [JsonIgnore] public long   GilEnd                     { get => ActiveVenue.GilEnd;                     set => ActiveVenue.GilEnd = value; }
    [JsonIgnore] public int    DealerCutPct               { get => ActiveVenue.DealerCutPct;               set => ActiveVenue.DealerCutPct = value; }
    [JsonIgnore] public List<long> Tips                   { get => ActiveVenue.Tips;                       set => ActiveVenue.Tips = value; }
    [JsonIgnore] public Dictionary<string, PlayerStat> PlayerStatsStore { get => ActiveVenue.PlayerStatsStore; set => ActiveVenue.PlayerStatsStore = value; }
    [JsonIgnore] public List<RoundHistoryEntry> RoundHistory { get => ActiveVenue.RoundHistory; set => ActiveVenue.RoundHistory = value; }
    [JsonIgnore] public List<PlayerStatsSession> StatsSessions { get => ActiveVenue.StatsSessions; set => ActiveVenue.StatsSessions = value; }

    // House rules - canonical on VenueSettings. Editing these does NOT affect a
    // round that has already started (Deal phase or later). SeedRulesIntoGameState
    // copies the latest venue rules onto GameState at StartDeal time, so Bet-phase
    // edits land on the round about to be dealt.
    [JsonIgnore] public double              BjPayout             { get => ActiveVenue.BjPayout;             set => ActiveVenue.BjPayout = value; }
    [JsonIgnore] public PayoutRatio         CharliePayout        { get => ActiveVenue.CharliePayout;        set => ActiveVenue.CharliePayout = value; }
    [JsonIgnore] public FiveCardCharlieRule FiveCardCharlie      { get => ActiveVenue.FiveCardCharlie;      set => ActiveVenue.FiveCardCharlie = value; }
    [JsonIgnore] public bool                DealerStandsOnSoft17 { get => ActiveVenue.DealerStandsOnSoft17; set => ActiveVenue.DealerStandsOnSoft17 = value; }
    [JsonIgnore] public bool                DoubleAfterSplit     { get => ActiveVenue.DoubleAfterSplit;     set => ActiveVenue.DoubleAfterSplit = value; }
    [JsonIgnore] public bool                HitSplitAces         { get => ActiveVenue.HitSplitAces;         set => ActiveVenue.HitSplitAces = value; }
    [JsonIgnore] public bool                ResplitAces          { get => ActiveVenue.ResplitAces;          set => ActiveVenue.ResplitAces = value; }
    [JsonIgnore] public ResplitCap          ResplitCap           { get => ActiveVenue.ResplitCap;           set => ActiveVenue.ResplitCap = value; }
    [JsonIgnore] public bool                AllowSurrender       { get => ActiveVenue.AllowSurrender;       set => ActiveVenue.AllowSurrender = value; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>
    /// Copies the active venue's house rules into the current GameState. Called by
    /// MainWindow.Apply on the StartDeal action so each round uses the latest venue
    /// rules - including changes the dealer made during the Betting phase. Edits
    /// made during Deal phase or later do not affect the already-running round.
    /// </summary>
    public void SeedRulesIntoGameState()
    {
        GameState.BjPayout             = ActiveVenue.BjPayout;
        GameState.CharliePayout        = ActiveVenue.CharliePayout;
        GameState.FiveCardCharlie      = ActiveVenue.FiveCardCharlie;
        GameState.DealerStandsOnSoft17 = ActiveVenue.DealerStandsOnSoft17;
        GameState.DoubleAfterSplit     = ActiveVenue.DoubleAfterSplit;
        GameState.HitSplitAces         = ActiveVenue.HitSplitAces;
        GameState.ResplitAces          = ActiveVenue.ResplitAces;
        GameState.ResplitCap           = ActiveVenue.ResplitCap;
        GameState.AllowSurrender       = ActiveVenue.AllowSurrender;
    }

    /// <summary>
    /// Mutate <see cref="GameState"/> directly and persist. Reserved for "house rule"
    /// settings that intentionally live on <c>GameState</c> (so they snapshot with
    /// undo entries) but are NOT themselves undoable game actions - payout ratios,
    /// charlie rules, and similar UI-driven knobs. All such writes should go through
    /// here so the exception to "no direct GameState writes outside Apply" is named
    /// and greppable.
    /// </summary>
    public void SetGameRule(Action<GameState> mutate)
    {
        mutate(GameState);
        Save();
    }
}
