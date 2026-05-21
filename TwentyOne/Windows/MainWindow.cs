using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly Configuration      config;
    private readonly ConfigWindow       configWindow;
    private readonly SessionLedgerWindow sessionLedgerWindow;
    private          HistoryWindow historyWindow = null!;

    // History viewer mode: non-null when viewing a historical round.
    private GameState?       savedCurrentState;
    private List<GameState>? savedUndoStack;
    private List<GameState>? savedRedoStack;
    private bool             isHistoryView => savedCurrentState != null;
    private readonly IChatGui       chatGui;
    private readonly IObjectTable   objectTable;
    private readonly IClientState   clientState;

    // Venue memory suggestion banner state.
    private uint lastSeenTerritory;
    private bool venueMemoryDismissed;
    private bool sessionBannerDismissed;

    // Betting-phase UI state
    private string newPlayerName  = string.Empty;
    private int    renamingIndex  = -1;
    private string renamingBuffer = string.Empty;
    private bool   isReorderMode  = false;
    private List<int> reorderIndices = [];
    // In-progress bet edits (player index → typed string); committed to game state on Enter only.
    private readonly Dictionary<int, string> betEdits = [];
    // Deal-phase bet adjustment: which player's bet is being edited, and the buffer.
    // -1 = none. Cleared on NewRound and when leaving Deal phase.
    private int    adjustBetIndex = -1;
    private string adjustBetBuf   = string.Empty;
    // bank management modal
    private int    bankManagePlayerIndex = -1;
    private string bankDepositBuf        = string.Empty;
    private string bankWithdrawBuf       = string.Empty;
    // Discriminated prompt for trade-result modals. Only one may be active at a time.
    private abstract record PendingPrompt
    {
        public sealed record Bet(int Pi, long Gil) : PendingPrompt;
        public sealed record BankDeposit(int Pi, long Gil) : PendingPrompt;
        public sealed record BankWithdraw(int Pi, long Gil) : PendingPrompt;
        public sealed record BetOrBank(int Pi, long Gil) : PendingPrompt;
    }
    private PendingPrompt? pendingPrompt;

    // pending hit: null = not waiting; IsPublic=true means /random was sent, false means /dice
    private (bool IsDealer, int PlayerIndex, int HandIndex, bool IsPublic)? pendingHit;
    // deferred roll: set by OnChatMessage, applied at the start of the next Draw()
    private (bool IsDealer, int PlayerIndex, int HandIndex, int Roll)?      deferredRoll;
#if DEBUG
    // Scenario runtime state (active scenario, gating, fast-forward, roll queue).
    public readonly TwentyOne.Debug.ScenarioRunner Scenario = new();
    private DebugWindow                            debugWindow = null!;
    public void SetDebugWindow(DebugWindow w) => debugWindow = w;

    // Called by DebugWindow after overwriting GameState so stale bet edits don't index OOB.
    public void ClearBetEdits() => betEdits.Clear();

    // Executes the next scripted action programmatically (Step button fallback).
    public void ExecuteNextScenarioStep() => Scenario.ExecuteNextStep(this);
#endif
    // pending trade confirmation for double/split: set when dealer clicks the button, cleared on confirm/cancel
    private (int PlayerIndex, int HandIndex)? pendingDouble;
    private (int PlayerIndex, int HandIndex)? pendingSplit;
    // chat-stream trade-detection state (partner / received-gil / given-gil) lives in TradeMonitor.
    private readonly TradeMonitor              tradeMonitor = new();
    // auto-deal queue: populated by StartDeal; QueueHitRoll is called one at a time as rolls resolve
    // IsFirstCard=true → emit AnnouncePlayerDeal before rolling
    private readonly Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> autoDealQueue = new();

    // Outgoing FFXIV-chat FIFO with per-message and global cooldowns. See ChatQueue.
    private readonly ChatQueue chatQueue = new();

    // ── Convenience accessors ─────────────────────────────────────────────────

    private GameState   State             => config.GameState;
    private GamePhase   Phase             => config.GameState.Phase;
    private int         ActivePlayerIndex => config.GameState.ActivePlayerIndex;
    private int         ActiveHandIndex   => config.GameState.ActiveHandIndex;

    // ── Constructor / Dispose ─────────────────────────────────────────────────

    public MainWindow(Configuration config, ConfigWindow configWindow, SessionLedgerWindow sessionLedgerWindow,
                      IChatGui chatGui, IObjectTable objectTable,
                      IClientState clientState)
        : base("Twenty One##TwentyOneMain")
    {
        this.config              = config;
        this.configWindow        = configWindow;
        this.sessionLedgerWindow = sessionLedgerWindow;
        this.chatGui           = chatGui;
        this.objectTable       = objectTable;
        this.clientState       = clientState;
        SizeConstraints   = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        chatGui.ChatMessage += OnChatMessage;
    }

    public void SetHistoryWindow(HistoryWindow w) => historyWindow = w;

    // Returns (venueIndex, venueName) if VenueMemory has a suggestion for the current location.
    private (int Index, string Name)? GetVenueMemorySuggestion()
    {
        var addrKey = Plugin.GetCurrentHousingAddressKey();
        if (addrKey == null) return null;
        if (!config.VenueMemory.TryGetValue(addrKey, out var guid)) return null;
        var idx = config.Venues.FindIndex(v => v.Id.ToString() == guid);
        if (idx < 0 || idx == config.ActiveVenueIndex) return null;
        return (idx, config.Venues[idx].Name);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            chatGui.ChatMessage -= OnChatMessage;
        }
    }

    public void RestoreHistoricalRound(GameState snapshot)
    {
        savedCurrentState = config.GameState;
        savedUndoStack    = [..config.UndoStack];
        savedRedoStack    = [..config.RedoStack];
        config.GameState  = snapshot;
        config.UndoStack.Clear();
        config.RedoStack.Clear();
    }

    public void ExitHistoryView()
    {
        if (savedCurrentState == null) return;
        config.GameState  = savedCurrentState;
        config.UndoStack  = savedUndoStack ?? [];
        config.RedoStack  = savedRedoStack ?? [];
        savedCurrentState = null;
        savedUndoStack    = null;
        savedRedoStack    = null;
    }

    // Called from Plugin.OnMenuOpened (runs on framework thread via context menu callback).
    public void AddPlayerFromContext(string fullName, string world)
    {
        Apply(new AddPlayer(Nickname: "", FullName: fullName, World: world));
    }

    // ── Apply / Undo ──────────────────────────────────────────────────────────

    private void Apply(GameAction action)
    {
        if (action is NewRound)
        {
            config.UndoStack.Clear();
            autoDealQueue.Clear();
            pendingHit     = null;
            pendingDouble  = null;
            pendingSplit   = null;
            adjustBetIndex = -1;
            adjustBetBuf   = string.Empty;
        }
        else if (action is BeginPlayerTurns)
        {
            // Leaving the Deal phase: cancel any in-progress bet-adjust editor.
            adjustBetIndex = -1;
            adjustBetBuf   = string.Empty;
        }
        else if (action is AdvanceToNextPlayer)
        {
            // Always push the WaitingForNextPlayer state so undo can return to it.
            if (config.GameState.WaitingForNextPlayer)
                config.UndoStack.Add(config.GameState);
        }
        else if (action.PushesUndo
                 && !IsTransientSplitState(config.GameState)
                 && !IsTransientDoubleState(config.GameState))
        {
            // GameEngine is pure - it never mutates state, so pushing the current
            // reference is safe; future Apply calls create entirely new objects.
            config.UndoStack.Add(config.GameState);
        }
        config.RedoStack.Clear();

        var (newState, effects) = GameEngine.Apply(config.GameState, action, config.NarrationTemplates, config.DealerName);
        config.GameState = newState;

        // Per-venue rule snapshot: a fresh round picks up the latest venue rules.
        // StartDeal also re-seeds so rule changes made during Betting take effect
        // on the round about to start (the round hasn't really "started" until cards
        // are dealt).
        if (action is NewRound or StartDeal) config.SeedRulesIntoGameState();

        if (config.AutoTargetEnabled
            && action is BeginPlayerTurns or AdvanceToNextPlayer
            && newState.Phase == GamePhase.PlayerTurns
            && newState.ActivePlayerIndex >= 0
            && newState.ActivePlayerIndex < newState.Players.Length)
        {
            var ap = newState.Players[newState.ActivePlayerIndex];
            Plugin.TargetPlayer(ap.FullName, ap.World);
        }

        foreach (var effect in effects)
        {
            if (effect is SendChat chat)
            {
                config.NarrationLog.Add(chat.Text);
                if (config.ChatEnabled)
                    chatQueue.EnqueueChat(chat.Text, config.ChatChannel, config.AllowCrossChannelCommands, SendChatMessage);
            }
            else if (effect is AutoHit ah)
            {
                QueueHitRoll(isDealer: false, ah.PlayerIndex, ah.HandIndex);
            }
        }

        config.Save();
    }

    private void Undo()
    {
        if (config.UndoStack.Count == 0) return;
        config.RedoStack.Add(config.GameState);
        config.GameState = config.UndoStack[^1];
        config.UndoStack.RemoveAt(config.UndoStack.Count - 1);
        config.Save();
    }

    private void Redo()
    {
        if (config.RedoStack.Count == 0) return;
        config.UndoStack.Add(config.GameState);
        config.GameState = config.RedoStack[^1];
        config.RedoStack.RemoveAt(config.RedoStack.Count - 1);
        config.Save();
    }

    private static void ApplyBank(PlayerStat stat, IBankTransaction tx)
    {
        var (newBalance, entry) = BankLedger.Apply(stat.Bank, tx, DateTime.Now);
        stat.Bank = newBalance;
        stat.BankLog.Add(entry);
    }

    // Adjust a player's bet during the Deal phase, reconciling their bank in lockstep.
    // Returns (success, message). On shortfall, no state or bank change is made.
    // The bank delta is recorded as a single BankBetAdjust entry (signed: positive = additional
    // deduction, negative = refund) so the audit log clearly attributes the change.
    private (bool Ok, string Message) TryAdjustBet(int pi, string newBetStr)
    {
        if (Phase != GamePhase.Deal) return (false, "Bet adjustments are only allowed during the Deal phase.");
        if (pi < 0 || pi >= State.Players.Length) return (false, "Invalid player.");
        var player = State.Players[pi];
        if (player.SittingOut) return (false, "Player is sitting out.");

        var parsedNew = GameEngine.ParseBet(newBetStr);
        if (parsedNew <= 0) return (false, "Bet must be a positive number.");
        var parsedOld = GameEngine.ParseBet(player.Bet);
        var newAmt    = (long)Math.Ceiling(parsedNew);
        var oldAmt    = (long)Math.Ceiling(parsedOld);
        var delta     = newAmt - oldAmt;

        if (delta == 0)
        {
            // Normalize the stored string (e.g., "  500 " → "500") without touching the bank.
            Apply(new AdjustBet(pi, newAmt.ToString()));
            return (true, $"Bet unchanged ({newAmt:N0}).");
        }

        if (player.TryGetBankingStat(config, out var stat))
        {
            if (delta > 0 && stat.Bank < delta)
            {
                var shortBy = delta - stat.Bank;
                return (false, $"Bank short by {shortBy:N0} gil - trade more before increasing the bet.");
            }
            var beforeBank = stat.Bank;
            ApplyBank(stat, new BankBetAdjust(delta));
            config.NarrationLog.Add(
                $"[Bank] {player.DisplayName}: bet adjusted {oldAmt:N0} → {newAmt:N0} " +
                $"(bank {beforeBank:N0} → {stat.Bank:N0})");
        }
        else
        {
            config.NarrationLog.Add(
                $"[Bet] {player.DisplayName}: bet adjusted {oldAmt:N0} → {newAmt:N0} (no bank)");
        }

        Apply(new AdjustBet(pi, newAmt.ToString()));
        return (true, $"Bet adjusted to {newAmt:N0}.");
    }

    // Called immediately after Apply(new GoToPayout()) to record round results.
    private void UpdatePlayerStats()
    {
        if (isHistoryView) return;

        var state   = config.GameState;
        var bankNet = 0m;

        for (var pi = 0; pi < state.Players.Length; pi++)
        {
            var p   = state.Players[pi];
            if (p.SittingOut) continue;
            var key = p.StatsKey();
            if (!config.PlayerStatsStore.TryGetValue(key, out var stat))
            {
                stat = new PlayerStat { DisplayName = p.DisplayName };
                config.PlayerStatsStore[key] = stat;
            }
            stat.DisplayName = p.DisplayName; // refresh in case nickname changed

            var net = 0m;
            for (var hi = 0; hi < p.Hands.Length; hi++)
                net += GameEngine.PayoutDelta(state, pi, hi) ?? 0m;

            stat.GamesPlayed++;
            if      (net > 0) stat.GamesWon++;
            else if (net < 0) stat.GamesLost++;
            else               stat.GamesPushed++;
            stat.TotalWon += (long)net;
            if (p.Hands.Any(h => h.State == HandState.Blackjack))
                stat.Blackjacks++;
            for (var chi = 0; chi < p.Hands.Length; chi++)
                if (GameEngine.GetPayoutResult(state, pi, chi) == PayoutResult.CharlieWin)
                    stat.Charlies++;

            bankNet -= net; // bank gains when player loses
        }

        // Auto-settle player banks and snapshot balances after settlement
        var playerBanksSnapshot = new Dictionary<string, long>();
        for (var pi2 = 0; pi2 < state.Players.Length; pi2++)
        {
            var p2  = state.Players[pi2];
            if (p2.SittingOut) continue;
            var key2 = p2.StatsKey();
            if (!config.PlayerStatsStore.TryGetValue(key2, out var stat2)) continue;
            if (stat2.Bank <= 0 && stat2.BankLog.Count == 0) continue;

            var net2 = 0m;
            for (var hi2 = 0; hi2 < p2.Hands.Length; hi2++)
                net2 += GameEngine.PayoutTotalOwed(state, pi2, hi2);
            var winAmt2 = (long)Math.Round(net2);
            if (winAmt2 > 0)
            {
                // Surrender is mutually exclusive with split (CanSurrender requires
                // !IsFromSplit), so a surrendered player has exactly one hand.
                var isSurrender = p2.Hands.Length == 1
                               && p2.Hands[0].State == HandState.Surrendered;
                IBankTransaction tx = isSurrender
                    ? new BankSurrender(winAmt2)
                    : new BankWin(winAmt2);
                ApplyBank(stat2, tx);
            }
            playerBanksSnapshot[key2] = stat2.Bank;
        }

        var roundNum = config.RoundHistory.Count + 1;
        config.RoundHistory.Add(new RoundHistoryEntry
        {
            RoundNumber  = roundNum,
            Snapshot     = state,
            BankNet      = (long)bankNet,
            PlayerBanks  = playerBanksSnapshot,
        });

        var venue = config.ActiveVenue;
        var startedAt   = venue.ActiveSessionStartedAt;
        var locationKey = venue.ActiveSessionLocationKey;
        SessionManager.TryStartSession(ref startedAt, ref locationKey, Plugin.GetCurrentHousingAddressKey(), DateTime.Now);
        venue.ActiveSessionStartedAt   = startedAt;
        venue.ActiveSessionLocationKey = locationKey;

        config.Save();
    }

    // A 1-card Playing split hand is transient: auto-hit resolves it immediately.
    // We skip saving it to the undo stack so undo jumps past it.
    private static bool IsTransientSplitState(GameState s)
    {
        if (s.Phase != GamePhase.PlayerTurns || s.WaitingForNextPlayer) return false;
        if (s.ActivePlayerIndex < 0 || s.ActivePlayerIndex >= s.Players.Length) return false;
        var p = s.Players[s.ActivePlayerIndex];
        if (s.ActiveHandIndex < 0 || s.ActiveHandIndex >= p.Hands.Length) return false;
        var h = p.Hands[s.ActiveHandIndex];
        return h.IsFromSplit && h.Cards.Length == 1 && h.State == HandState.Playing;
    }

    // A Doubled Playing hand with 2 cards is transient: the next hit always force-stands it.
    // We skip saving it to the undo stack so undo jumps past it.
    private static bool IsTransientDoubleState(GameState s)
    {
        if (s.Phase != GamePhase.PlayerTurns || s.WaitingForNextPlayer) return false;
        if (s.ActivePlayerIndex < 0 || s.ActivePlayerIndex >= s.Players.Length) return false;
        var p = s.Players[s.ActivePlayerIndex];
        if (s.ActiveHandIndex < 0 || s.ActiveHandIndex >= p.Hands.Length) return false;
        var h = p.Hands[s.ActiveHandIndex];
        return h.Doubled && h.State == HandState.Playing && h.Cards.Length == 2;
    }

    // ── Chat / roll ───────────────────────────────────────────────────────────

    private static unsafe void SendChatMessage(string message)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null) return;
        using var str = new Utf8String(message);
        uiModule->ProcessChatBoxEntry(&str);
    }

    private void LogRoll(bool isDealer, int playerIndex, int roll)
    {
        var who  = isDealer ? "Dealer" : State.Players[playerIndex].DisplayName;
        config.NarrationLog.Add($"[Roll] {who}: {roll}");
    }

    private void QueueTrade(string fullName, string world, int minWaitBeforeMs = 0) =>
        chatQueue.Enqueue(new ChatQueue.Entry(false, () => Plugin.TradePlayer(fullName, world), 0, minWaitBeforeMs, false));

    private void QueueTarget(string fullName, string world, int minWaitBeforeMs = 0) =>
        chatQueue.Enqueue(new ChatQueue.Entry(false, () => Plugin.TargetPlayer(fullName, world), 0, minWaitBeforeMs, false));

    private void QueueHitRoll(bool isDealer, int playerIndex, int handIndex)
    {
#if DEBUG
        if (Scenario.RollQueue.TryDequeue(out var debugRoll))
        {
            LogRoll(isDealer, playerIndex, debugRoll);
            deferredRoll = (isDealer, playerIndex, handIndex, debugRoll);
            return;
        }
#endif
        if (!config.ChatEnabled)
        {
            var simRoll = Random.Shared.Next(1, 14);
            LogRoll(isDealer, playerIndex, simRoll);
            deferredRoll = (isDealer, playerIndex, handIndex, simRoll);
            return;
        }
        chatQueue.Enqueue(new ChatQueue.Entry(true, () => SendHitRoll(isDealer, playerIndex, handIndex), 0, 0, true));
    }

    private unsafe void SendHitRoll(bool isDealer, int playerIndex, int handIndex)
    {
        var channel  = config.ChatChannel;
        var isPublic = channel is "/say" or "/yell" or "/shout";

        var shell = RaptureShellModule.Instance();
        if (shell == null) return;

        pendingHit = (isDealer, playerIndex, handIndex, isPublic);
        if (isPublic)
        {
            SendChatMessage("/random 13");
        }
        else
        {
            var savedChannel = ((ShellCommandModule*)shell)->CurrentChannel.ToString();
            SendChatMessage(channel);
            SendChatMessage("/dice 13");
            if (!string.IsNullOrEmpty(savedChannel))
                SendChatMessage(savedChannel);
        }
    }

    // "You roll a [icon] 7 (out of 13)." - response to /random in public channels
    [GeneratedRegex(@"You roll a\D+(\d+) \(out of 13\)")]
    private static partial Regex RandomRollRegex();

    // "Random! (1-13)[icon] 7" - response to /dice in private channels
    [GeneratedRegex(@"Random! \(1-13\)\D*(\d+)")]
    private static partial Regex DiceRollRegex();

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var sender  = msg.Sender;
        var message = msg.Message;

        // ── Trade detection (bet auto-fill + bank deposit/withdraw) ──────────
        var msgText = message.TextValue;
        var payload = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        switch (tradeMonitor.OnChat(msgText, payload, Phase, State, config))
        {
            case TradeMonitor.Outcome.PromptBet pb:
                pendingPrompt = new PendingPrompt.Bet(pb.Pi, pb.Gil); break;
            case TradeMonitor.Outcome.PromptBankDeposit pbd:
                if (!TryAutoDoubleOrSplitDeposit(pbd.Pi, pbd.Gil)
                    && !TryAutoMaintainBetDeposit(pbd.Pi, pbd.Gil))
                    pendingPrompt = new PendingPrompt.BankDeposit(pbd.Pi, pbd.Gil);
                break;
            case TradeMonitor.Outcome.PromptBankWithdraw pbw:
                if (!TryAutoMaintainBetWithdraw(pbw.Pi, pbw.Gil))
                    pendingPrompt = new PendingPrompt.BankWithdraw(pbw.Pi, pbw.Gil);
                break;
            case TradeMonitor.Outcome.PromptBetOrBank pbob:
                if (!TryAutoMaintainBetDeposit(pbob.Pi, pbob.Gil))
                    pendingPrompt = new PendingPrompt.BetOrBank(pbob.Pi, pbob.Gil);
                break;
        }

        // ── Roll detection ────────────────────────────────────────────────────
        if (pendingHit == null) return;

        var (isDealer, pi2, hi, isPublic) = pendingHit.Value;

        if (!isPublic)
        {
            var localName     = objectTable.LocalPlayer?.Name.TextValue;
            var senderPayload = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            if (localName == null || senderPayload != null || !sender.TextValue.Contains(localName)) return;
        }

        var rollMsgText = message.TextValue;
        var match       = (isPublic ? RandomRollRegex() : DiceRollRegex()).Match(rollMsgText);
        if (!match.Success) return;

        if (!int.TryParse(match.Groups[1].Value, out var roll) || roll < 1 || roll > 13) return;

        pendingHit   = null;
        LogRoll(isDealer, pi2, roll);
        deferredRoll = (isDealer, pi2, hi, roll);
    }

    // Returns true and silently applies if the trade is the exact maintain-bet deposit amount.
    private bool TryAutoMaintainBetDeposit(int pi, long gil)
    {
        if (pi < 0 || pi >= State.Players.Length) return false;
        var p = State.Players[pi];
        if (!p.TryGetBankingStat(config, out var stat) || !stat.MaintainBet) return false;
        if (stat.Bank != 0) return false;
        var bet = (long)Math.Ceiling(GameEngine.ParseBet(p.Bet));
        if (bet <= 0 || gil != bet) return false;
        ApplyBank(stat, new BankDeposit(gil));
        config.Save();
        return true;
    }

    // Returns true and silently applies if the trade is the exact maintain-bet withdrawal amount.
    private bool TryAutoMaintainBetWithdraw(int pi, long gil)
    {
        if (pi < 0 || pi >= State.Players.Length) return false;
        var p = State.Players[pi];
        if (!p.TryGetBankingStat(config, out var stat) || !stat.MaintainBet) return false;
        var bet = (long)Math.Floor(GameEngine.ParseBet(p.Bet));
        var owe = stat.Bank - bet;
        if (owe <= 0 || gil != owe) return false;
        ApplyBank(stat, new BankWithdrawal(gil));
        config.Save();
        return true;
    }

    // Returns true and silently deposits if a double/split is pending for this player
    // and the trade equals either the full bet or the exact shortfall to cover it.
    private bool TryAutoDoubleOrSplitDeposit(int pi, long gil)
    {
        if (pi < 0 || pi >= State.Players.Length) return false;
        var pending = pendingDouble ?? pendingSplit;
        if (pending is null || pending.Value.PlayerIndex != pi) return false;
        var p = State.Players[pi];
        if (!p.TryGetBankingStat(config, out var stat)) return false;
        var hand = p.Hands[pending.Value.HandIndex];
        var bet  = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
        if (bet <= 0) return false;
        var shortfall = bet - stat.Bank;
        if (gil != bet && (shortfall <= 0 || gil != shortfall)) return false;
        ApplyBank(stat, new BankDeposit(gil));
        config.Save();
        return true;
    }

    private void ConfirmDoublePayment(int pi, int hi)
    {
        var p    = State.Players[pi];
        var hand = p.Hands[hi];
        Apply(new AnnounceDoubleConfirm(pi, hi));
        Apply(new DoubleDown(pi, hi));
        var amt = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
        if (amt > 0 && p.TryGetBankingStat(config, out var stat))
        {
            var before = stat.Bank;
            ApplyBank(stat, new BankDoubleDown(amt));
            config.NarrationLog.Add($"[Bank] {p.DisplayName}: doubled - {amt:N0} deducted (was {before:N0} → {stat.Bank:N0})");
            config.Save();
        }
        pendingDouble = null;
        QueueHitRoll(isDealer: false, pi, hi);
    }

    private void ConfirmSplitPayment(int pi, int hi)
    {
        var p    = State.Players[pi];
        var hand = p.Hands[hi];
        var amt  = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
        if (amt > 0 && p.TryGetBankingStat(config, out var stat))
        {
            var before = stat.Bank;
            ApplyBank(stat, new BankSplit(amt));
            config.NarrationLog.Add($"[Bank] {p.DisplayName}: split - {amt:N0} deducted (was {before:N0} → {stat.Bank:N0})");
            config.Save();
        }
        Apply(new SplitHand(pi, hi));
        pendingSplit = null;
    }

    // ── ImGui helpers ─────────────────────────────────────────────────────────

    private bool PlayerHitActive(int pi, int hi)
    {
        if (State.Players[pi].SittingOut) return false;
        var hand = State.Players[pi].Hands[hi];
        return Phase switch
        {
            GamePhase.Deal        => hand.State == HandState.Playing && hand.Cards.Length < 2,
            GamePhase.PlayerTurns => pi == ActivePlayerIndex && hi == ActiveHandIndex
                                  && GameEngine.CanHit(hand, State.HitSplitAces)
                                  && !pendingDouble.HasValue && !pendingSplit.HasValue
                                  && !State.WaitingForNextPlayer,
            _ => false,
        };
    }

}
