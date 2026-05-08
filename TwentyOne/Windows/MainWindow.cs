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
    public void ExecuteNextScenarioStep()
    {
        var step = Scenario.ActiveScenario?.PeekNext();
        if (step == null) return;
        Scenario.Advance();
        switch (step)
        {
            case "StartDeal":
                foreach (var (idx, val) in betEdits.ToList())
                {
                    betEdits.Remove(idx);
                    if (val != State.Players[idx].Bet)
                        Apply(new SetPlayerBet(idx, val));
                }
                Apply(new StartDeal());
                foreach (var p in State.Players)
                {
                    if (p.SittingOut) continue;
                    var betAmt = (long)Math.Ceiling(GameEngine.ParseBet(p.Bet));
                    if (betAmt <= 0) continue;
                    if (!p.TryGetBankingStat(config, out var betStat)) continue;
                    ApplyBank(betStat, new BankBet(betAmt));
                }
                for (var i = 0; i < State.Players.Length; i++)
                {
                    if (State.Players[i].SittingOut) continue;
                    autoDealQueue.Enqueue((false, i, 0, true));
                    autoDealQueue.Enqueue((false, i, 0, false));
                }
                Apply(new AnnounceDealerDeal());
                QueueHitRoll(isDealer: true, -1, -1);
                break;
            case "BeginPlayerTurns":
                Apply(new BeginPlayerTurns());
                break;
            case "BeginDealerTurn":
                Apply(new BeginDealerTurn());
                break;
            case "GoToPayout":
                Apply(new GoToPayout());
                UpdatePlayerStats();
                break;
            case "NewRound":
                Apply(new NewRound());
                break;
            case "DealerHit":
                Apply(new AnnounceDealerHit());
                QueueHitRoll(isDealer: true, -1, -1);
                break;
            case "AdvancePlayer":
                Apply(new AdvanceToNextPlayer());
                break;
            default:
            {
                // Hit:pi:hi / Stand:pi:hi / Dbl:pi:hi / Spl:pi:hi / ConfirmDbl:pi:hi / ConfirmSpl:pi:hi
                var parts = step.Split(':');
                if (parts.Length < 3 || !int.TryParse(parts[1], out var pi) || !int.TryParse(parts[2], out var hi))
                    break;
                var p    = pi < State.Players.Length ? State.Players[pi] : null;
                var hand = p != null && hi < p.Hands.Length ? p.Hands[hi] : null;
                if (p == null || hand == null) break;
                switch (parts[0])
                {
                    case "Hit":
                        Apply(new AnnouncePlayerHit(pi, hi));
                        QueueHitRoll(isDealer: false, pi, hi);
                        break;
                    case "Stand":
                        Apply(new StandPlayer(pi, hi));
                        break;
                    case "Dbl":
                    {
                        var dblBet     = GameEngine.GetEffectiveBet(p, hand);
                        var dblBank    = p.BankBalance(config);
                        var dblRounded = (long)Math.Ceiling(dblBet);
                        var fromBank   = dblBank >= dblRounded;
                        // fromBank=true: BankAfter = balance after deduction; fromBank=false: BankAfter = shortfall (trade amount needed)
                        var bankAfter  = fromBank ? dblBank - dblRounded : dblRounded - dblBank;
                        pendingDouble  = (pi, hi);
                        Apply(new AnnounceDouble(pi, hi, fromBank, bankAfter));
                        break;
                    }
                    case "Spl":
                    {
                        var splBet     = GameEngine.GetEffectiveBet(p, hand);
                        var splBank    = p.BankBalance(config);
                        var splRounded = (long)Math.Ceiling(splBet);
                        var fromBank   = splBank >= splRounded;
                        // fromBank=true: BankAfter = balance after deduction; fromBank=false: BankAfter = shortfall (trade amount needed)
                        var bankAfter  = fromBank ? splBank - splRounded : splRounded - splBank;
                        pendingSplit   = (pi, hi);
                        Apply(new AnnounceSplit(pi, hi, fromBank, bankAfter));
                        break;
                    }
                    case "ConfirmDbl":
                        Apply(new AnnounceDoubleConfirm(pi, hi));
                        Apply(new DoubleDown(pi, hi));
                        var dblKey2 = p.StatsKey();
                        var dblAmt2 = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
                        if (config.PlayerStatsStore.TryGetValue(dblKey2, out var dblStat2) && dblAmt2 > 0)
                        {
                            var before2 = dblStat2.Bank;
                            ApplyBank(dblStat2, new BankDoubleDown(dblAmt2));
                            config.NarrationLog.Add($"[Bank] {p.DisplayName}: doubled — {dblAmt2:N0} deducted (was {before2:N0} → {dblStat2.Bank:N0})");
                            config.Save();
                        }
                        pendingDouble = null;
                        QueueHitRoll(isDealer: false, pi, hi);
                        break;
                    case "ConfirmSpl":
                        var splKey2 = p.StatsKey();
                        var splAmt2 = (long)Math.Ceiling(GameEngine.GetEffectiveBet(p, hand));
                        if (config.PlayerStatsStore.TryGetValue(splKey2, out var splStat2) && splAmt2 > 0)
                        {
                            var before2 = splStat2.Bank;
                            ApplyBank(splStat2, new BankSplit(splAmt2));
                            config.NarrationLog.Add($"[Bank] {p.DisplayName}: split — {splAmt2:N0} deducted (was {before2:N0} → {splStat2.Bank:N0})");
                            config.Save();
                        }
                        Apply(new SplitHand(pi, hi));
                        pendingSplit = null;
                        break;
                }
                break;
            }
        }
    }
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
            pendingHit    = null;
            pendingDouble = null;
            pendingSplit  = null;
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
            // GameEngine is pure — it never mutates state, so pushing the current
            // reference is safe; future Apply calls create entirely new objects.
            config.UndoStack.Add(config.GameState);
        }
        config.RedoStack.Clear();

        var (newState, effects) = GameEngine.Apply(config.GameState, action, config.NarrationTemplates, config.DealerName);
        config.GameState = newState;

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
                ApplyBank(stat2, new BankWin(winAmt2));
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

    // "You roll a [icon] 7 (out of 13)." — response to /random in public channels
    [GeneratedRegex(@"You roll a\D+(\d+) \(out of 13\)")]
    private static partial Regex RandomRollRegex();

    // "Random! (1-13)[icon] 7" — response to /dice in private channels
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
                pendingPrompt = new PendingPrompt.BankDeposit(pbd.Pi, pbd.Gil); break;
            case TradeMonitor.Outcome.PromptBankWithdraw pbw:
                pendingPrompt = new PendingPrompt.BankWithdraw(pbw.Pi, pbw.Gil); break;
            case TradeMonitor.Outcome.PromptBetOrBank pbob:
                pendingPrompt = new PendingPrompt.BetOrBank(pbob.Pi, pbob.Gil); break;
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

    // ── ImGui helpers ─────────────────────────────────────────────────────────

    private bool PlayerHitActive(int pi, int hi)
    {
        if (State.Players[pi].SittingOut) return false;
        var hand = State.Players[pi].Hands[hi];
        return Phase switch
        {
            GamePhase.Deal        => hand.State == HandState.Playing && hand.Cards.Length < 2,
            GamePhase.PlayerTurns => pi == ActivePlayerIndex && hi == ActiveHandIndex
                                  && GameEngine.CanHit(hand)
                                  && !pendingDouble.HasValue && !pendingSplit.HasValue
                                  && !State.WaitingForNextPlayer,
            _ => false,
        };
    }

}
