using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
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
    private readonly BankWindow         bankWindow;
    private readonly PlayerStatsWindow  playerStatsWindow;
    private          RoundHistoryWindow roundHistoryWindow = null!;

    // History viewer mode: non-null when viewing a historical round.
    private GameState?       savedCurrentState;
    private List<GameState>? savedUndoStack;
    private List<GameState>? savedRedoStack;
    private bool             isHistoryView => savedCurrentState != null;
    private readonly IChatGui       chatGui;
    private readonly IObjectTable   objectTable;
    private readonly ITargetManager targetManager;
    private readonly IClientState   clientState;

    // Venue memory suggestion banner state.
    private ushort lastSeenTerritory;
    private bool   venueMemoryDismissed;

    // Betting-phase UI state
    private string newPlayerName  = string.Empty;
    private int    renamingIndex  = -1;
    private string renamingBuffer = string.Empty;
    // In-progress bet edits (player index → typed string); committed to game state on Enter only.
    private readonly Dictionary<int, string> betEdits = [];
    // bank management modal
    private int    bankManagePlayerIndex = -1;
    private string bankDepositBuf        = string.Empty;
    private string bankWithdrawBuf       = string.Empty;
    // prompt shown when a trade with the bank player completes: (playerIndex, amount, isWithdraw)
    private (int PlayerIndex, long Amount, bool IsWithdraw)? pendingBankTradePrompt;

    // pending hit: null = not waiting; IsPublic=true means /random was sent, false means /dice
    private (bool IsDealer, int PlayerIndex, int HandIndex, bool IsPublic)? pendingHit;
    // deferred roll: set by OnChatMessage, applied at the start of the next Draw()
    private (bool IsDealer, int PlayerIndex, int HandIndex, int Roll)?      deferredRoll;
    // pending trade confirmation for double/split: set when dealer clicks the button, cleared on confirm/cancel
    private (int PlayerIndex, int HandIndex)? pendingDouble;
    private (int PlayerIndex, int HandIndex)? pendingSplit;
    // trade bet detection: track in-progress trade partner + received/gave gil
    private (string FullName, string World)? pendingTradePartner;
    private long                              pendingTradeGil;
    private long                              pendingGaveGil;
    // prompt to set a player's bet after a completed trade; shown as a modal
    private (int PlayerIndex, long Gil)?      pendingBetPrompt;
    // prompt shown when both AutoBetFromTrades and AutoDepositFromTrades are on during Betting phase
    private (int PlayerIndex, long Gil)?      pendingBetOrBankPrompt;
    // auto-deal queue: populated by StartDeal; QueueHitRoll is called one at a time as rolls resolve
    // IsFirstCard=true → emit AnnouncePlayerDeal before rolling
    private readonly Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> autoDealQueue = new();

    // rate-limited outgoing queue — narration strings and roll commands share a single FIFO and lastChatSent
    // each entry: (IsRoll, Invoke, MinWaitMs) — narration passes through freely; rolls block until pendingHit is clear
    // MinWaitMs: minimum ms to wait after the *previous* entry before sending this one (0 = use cooldownMs)
    private readonly Queue<(bool IsRoll, Action Invoke, int MinWaitAfterMs, int MinWaitBeforeMs)> chatQueue = new();
    private          DateTime                                                                       lastChatSent      = DateTime.UtcNow;
    private          int                                                                            lastSentMinWaitMs = 0;

    // ── Convenience accessors ─────────────────────────────────────────────────

    private GameState   State             => config.GameState;
    private GamePhase   Phase             => config.GameState.Phase;
    private int         ActivePlayerIndex => config.GameState.ActivePlayerIndex;
    private int         ActiveHandIndex   => config.GameState.ActiveHandIndex;

    // ── Constructor / Dispose ─────────────────────────────────────────────────

    public MainWindow(Configuration config, ConfigWindow configWindow, BankWindow bankWindow,
                      PlayerStatsWindow playerStatsWindow,
                      IChatGui chatGui, IObjectTable objectTable, ITargetManager targetManager,
                      IClientState clientState)
        : base("Twenty One##TwentyOneMain")
    {
        this.config            = config;
        this.configWindow      = configWindow;
        this.bankWindow        = bankWindow;
        this.playerStatsWindow = playerStatsWindow;
        this.chatGui           = chatGui;
        this.objectTable       = objectTable;
        this.targetManager     = targetManager;
        this.clientState       = clientState;
        SizeConstraints   = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        chatGui.ChatMessage += OnChatMessage;
    }

    public void SetRoundHistoryWindow(RoundHistoryWindow w) => roundHistoryWindow = w;

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
        chatGui.ChatMessage -= OnChatMessage;
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
        else if (action is not AnnounceBettingOpen
                         and not AnnounceBetRequest
                         and not AnnounceBetConfirm
                         and not AnnounceBankRemind
                         and not AnnounceBankShortfall
                         and not AnnounceBankDeposit
                         and not AnnounceBankWithdraw
                         and not AnnounceDouble
                         and not AnnounceDoubleConfirm
                         and not AnnounceSplit
                         and not AnnounceDealerHit
                         and not AnnouncePlayerHit
                         and not AnnouncePlayerTurn
                         and not AnnouncePlayerDeal
                         and not AnnounceDealerDeal
                         and not BeginDealerTurn)
        {
            // GameEngine is pure — it never mutates state, so pushing the current
            // reference is safe; future Apply calls create entirely new objects.
            // Skip transient states that auto-resolve immediately in normal play.
            if (!IsTransientSplitState(config.GameState) && !IsTransientDoubleState(config.GameState))
                config.UndoStack.Add(config.GameState);
        }
        config.RedoStack.Clear();

        var (newState, effects) = GameEngine.Apply(config.GameState, action, config.NarrationTemplates, config.DealerName);
        config.GameState = newState;

        if (config.AutoTargetEnabled
            && action is BeginPlayerTurns or AdvanceToNextPlayer
            && newState.Phase == GamePhase.PlayerTurns
            && newState.ActivePlayerIndex >= 0
            && newState.ActivePlayerIndex < newState.Players.Count)
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
                {
                    var raw      = chat.Text;
                    int minWait  = 0;
                    var m = System.Text.RegularExpressions.Regex.Match(raw, @"<wait\.(\d+)>");
                    if (m.Success)
                    {
                        minWait = int.Parse(m.Groups[1].Value) * 1000;
                        raw     = raw.Replace(m.Value, "").Trim();
                    }
                    string msg;
                    if (raw.StartsWith('/'))
                    {
                        if (!config.AllowCrossChannelCommands && IsCrossChannelCommand(raw, config.ChatChannel))
                            raw = "/echo " + raw.Split(' ', 2)[1];
                        msg = raw;
                    }
                    else
                    {
                        msg = config.ChatChannel + " " + raw;
                    }
                    chatQueue.Enqueue((false, () => SendChatMessage(msg), minWait, 0));
                }
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

    private static string PlayerStatKey(Player p) =>
        p.FullName.Length > 0 ? $"{p.FullName}@{p.World}" : p.Nickname;

    private static void AddBankLog(PlayerStat stat, BankTransactionKind kind, long amount, long balance) =>
        stat.BankLog.Add(new BankTransactionEntry { Timestamp = DateTime.Now, Kind = kind, Amount = amount, Balance = balance });

    // Called immediately after Apply(new GoToPayout()) to record round results.
    private void UpdatePlayerStats()
    {
        if (isHistoryView) return;

        var state   = config.GameState;
        var bankNet = 0m;

        for (var pi = 0; pi < state.Players.Count; pi++)
        {
            var p   = state.Players[pi];
            var key = PlayerStatKey(p);
            if (!config.PlayerStatsStore.TryGetValue(key, out var stat))
            {
                stat = new PlayerStat { DisplayName = p.DisplayName };
                config.PlayerStatsStore[key] = stat;
            }
            stat.DisplayName = p.DisplayName; // refresh in case nickname changed

            var net = 0m;
            for (var hi = 0; hi < p.Hands.Count; hi++)
            {
                var result = GameEngine.GetPayoutResult(state, pi, hi);
                var delta  = result switch
                {
                    PayoutResult.Win   => GameEngine.GetEffectiveBet(p, p.Hands[hi]),
                    PayoutResult.BjWin => Math.Round(GameEngine.GetEffectiveBet(p, p.Hands[hi])
                                            * (state.BjPayout switch
                                            {
                                                BlackjackPayout.SixToFive => 1.2m,
                                                BlackjackPayout.EvenMoney => 1.0m,
                                                _                         => 1.5m,
                                            }), 2),
                    PayoutResult.Lose  => -GameEngine.GetEffectiveBet(p, p.Hands[hi]),
                    _                  => 0m,
                };
                net += delta;
            }

            stat.GamesPlayed++;
            if      (net > 0) stat.GamesWon++;
            else if (net < 0) stat.GamesLost++;
            else               stat.GamesPushed++;
            stat.TotalWon += net;
            if (p.Hands.Any(h => h.State == HandState.Blackjack))
                stat.Blackjacks++;

            bankNet -= net; // bank gains when player loses
        }

        // Auto-settle player banks and snapshot balances after settlement
        var playerBanksSnapshot = new Dictionary<string, long>();
        for (var pi2 = 0; pi2 < state.Players.Count; pi2++)
        {
            var p2  = state.Players[pi2];
            var key2 = PlayerStatKey(p2);
            if (!config.PlayerStatsStore.TryGetValue(key2, out var stat2)) continue;
            if (stat2.Bank <= 0) continue;

            var net2 = 0m;
            for (var hi2 = 0; hi2 < p2.Hands.Count; hi2++)
            {
                var eb2     = GameEngine.GetEffectiveBet(p2, p2.Hands[hi2]);
                var result2 = GameEngine.GetPayoutResult(state, pi2, hi2);
                // Return bet + profit for win/BJ, bet only for push, nothing for loss
                var delta2  = result2 switch
                {
                    PayoutResult.Win   => eb2 * 2m,
                    PayoutResult.BjWin => eb2 + Math.Round(eb2 * (state.BjPayout switch
                                            {
                                                BlackjackPayout.SixToFive => 1.2m,
                                                BlackjackPayout.EvenMoney => 1.0m,
                                                _                         => 1.5m,
                                            }), 2),
                    PayoutResult.Push  => eb2,
                    _                  => 0m,
                };
                net2 += delta2;
            }
            var prevBank2 = stat2.Bank;
            stat2.Bank += (long)Math.Round(net2);
            playerBanksSnapshot[key2] = stat2.Bank;
            if (stat2.Bank != prevBank2)
                AddBankLog(stat2, BankTransactionKind.Win, stat2.Bank - prevBank2, stat2.Bank);
        }

        var roundNum = config.RoundHistory.Count + 1;
        config.RoundHistory.Add(new RoundHistoryEntry
        {
            RoundNumber  = roundNum,
            Snapshot     = state,
            BankNet      = bankNet,
            PlayerBanks  = playerBanksSnapshot,
        });

        config.Save();
    }

    // A 1-card Playing split hand is transient: auto-hit resolves it immediately.
    // We skip saving it to the undo stack so undo jumps past it.
    private static bool IsTransientSplitState(GameState s)
    {
        if (s.Phase != GamePhase.PlayerTurns || s.WaitingForNextPlayer) return false;
        if (s.ActivePlayerIndex < 0 || s.ActivePlayerIndex >= s.Players.Count) return false;
        var p = s.Players[s.ActivePlayerIndex];
        if (s.ActiveHandIndex < 0 || s.ActiveHandIndex >= p.Hands.Count) return false;
        var h = p.Hands[s.ActiveHandIndex];
        return h.IsFromSplit && h.Cards.Count == 1 && h.State == HandState.Playing;
    }

    // A Doubled Playing hand with 2 cards is transient: the next hit always force-stands it.
    // We skip saving it to the undo stack so undo jumps past it.
    private static bool IsTransientDoubleState(GameState s)
    {
        if (s.Phase != GamePhase.PlayerTurns || s.WaitingForNextPlayer) return false;
        if (s.ActivePlayerIndex < 0 || s.ActivePlayerIndex >= s.Players.Count) return false;
        var p = s.Players[s.ActivePlayerIndex];
        if (s.ActiveHandIndex < 0 || s.ActiveHandIndex >= p.Hands.Count) return false;
        var h = p.Hands[s.ActiveHandIndex];
        return h.Doubled && h.State == HandState.Playing && h.Cards.Count == 2;
    }

    // ── Chat / roll ───────────────────────────────────────────────────────────

    // Chat commands that send messages visible to other players (not client-side).
    private static readonly HashSet<string> ChannelCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/say", "/s", "/yell", "/y", "/shout", "/sh",
        "/party", "/p", "/alliance", "/a",
        "/fc", "/linkshell", "/l",
        "/ls1", "/ls2", "/ls3", "/ls4", "/ls5", "/ls6", "/ls7", "/ls8",
        "/cwlinkshell", "/cwl",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/tell", "/t", "/reply", "/r", "/novice", "/beginner",
    };

    // Returns true if `raw` is a channel-sending command targeting a different channel than `configChannel`.
    private static bool IsCrossChannelCommand(string raw, string configChannel)
    {
        var cmd = raw.Split(' ', 2)[0];
        if (!ChannelCommands.Contains(cmd)) return false;
        return !string.Equals(cmd, configChannel, StringComparison.OrdinalIgnoreCase);
    }

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
        chatQueue.Enqueue((false, () => Plugin.TradePlayer(fullName, world), 0, minWaitBeforeMs));

    private void QueueTarget(string fullName, string world, int minWaitBeforeMs = 0) =>
        chatQueue.Enqueue((false, () => Plugin.TargetPlayer(fullName, world), 0, minWaitBeforeMs));

    private void QueueHitRoll(bool isDealer, int playerIndex, int handIndex)
    {
        if (!config.ChatEnabled)
        {
            var simRoll = Random.Shared.Next(1, 14);
            LogRoll(isDealer, playerIndex, simRoll);
            deferredRoll = (isDealer, playerIndex, handIndex, simRoll);
            return;
        }
        chatQueue.Enqueue((true, () => SendHitRoll(isDealer, playerIndex, handIndex), 0, 0));
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

    // "Trade request sent to Firstname Lastname." — we initiated trade
    [GeneratedRegex(@"^Trade request sent to (.+)\.$")]
    private static partial Regex TradeSentRegex();

    // "Firstname Lastname wishes to trade with you." — they initiated trade
    [GeneratedRegex(@"^(.+) wishes to trade with you\.$")]
    private static partial Regex TradeWishesRegex();

    // "You receive 1,234 gil." — gil received during trade
    [GeneratedRegex(@"^You receive ([\d,]+) gil\.$")]
    private static partial Regex TradeGilRegex();

    // "You hand over 1,234 gil." — gil given during trade
    [GeneratedRegex(@"^You hand over ([\d,]+) gil\.$")]
    private static partial Regex GaveGilRegex();

    private void OnChatMessage(XivChatType type, int timestamp,
        ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // ── Trade detection (bet auto-fill + bank deposit/withdraw) ──────────
        var isBetPhase    = config.AutoBetFromTrades    && Phase == GamePhase.Betting;
        var isBankMonitor = config.AutoDepositFromTrades;
        if (isBetPhase || isBankMonitor)
        {
            var msgText = message.TextValue;

            var tradeMatch = TradeSentRegex().Match(msgText);
            if (!tradeMatch.Success) tradeMatch = TradeWishesRegex().Match(msgText);
            if (tradeMatch.Success)
            {
                var payload = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
                pendingTradePartner = payload != null
                    ? (payload.PlayerName, payload.World.ValueNullable?.Name.ToString() ?? string.Empty)
                    : (tradeMatch.Groups[1].Value, string.Empty);
            }
            else if (TradeGilRegex().Match(msgText) is { Success: true } m2
                     && long.TryParse(m2.Groups[1].Value.Replace(",", ""), out var gil))
            {
                pendingTradeGil = gil;
            }
            else if (GaveGilRegex().Match(msgText) is { Success: true } m3
                     && long.TryParse(m3.Groups[1].Value.Replace(",", ""), out var gave))
            {
                pendingGaveGil = gave;
            }
            else if (msgText == "Trade complete." && pendingTradePartner.HasValue)
            {
                var (fullName, world) = pendingTradePartner.Value;
                var pi = State.Players.FindIndex(p =>
                    string.Equals(p.FullName, fullName, StringComparison.OrdinalIgnoreCase) &&
                    (world.Length == 0 || string.Equals(p.World, world, StringComparison.OrdinalIgnoreCase)));
                if (pi >= 0)
                {
                    if (pendingGaveGil > 0 && isBankMonitor)
                        pendingBankTradePrompt = (pi, pendingGaveGil, true);
                    else if (pendingTradeGil > 0)
                    {
                        if (isBetPhase && isBankMonitor)
                            pendingBetOrBankPrompt = (pi, pendingTradeGil);
                        else if (isBetPhase)
                            pendingBetPrompt = (pi, pendingTradeGil);
                        else if (isBankMonitor)
                            pendingBankTradePrompt = (pi, pendingTradeGil, false);
                    }
                }
                pendingTradePartner = null;
                pendingTradeGil     = 0;
                pendingGaveGil      = 0;
            }
            else if (msgText == "Trade canceled." || msgText == "Trade cancelled.")
            {
                pendingTradePartner = null;
                pendingTradeGil     = 0;
                pendingGaveGil      = 0;
            }
        }

        // ── Roll detection ────────────────────────────────────────────────────
        if (pendingHit == null) return;

        var (isDealer, pi2, hi, isPublic) = pendingHit.Value;

        if (!isPublic)
        {
            var localName = objectTable.LocalPlayer?.Name.TextValue;
            var payload   = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            if (localName == null || payload != null || !sender.TextValue.Contains(localName)) return;
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
        var hand = State.Players[pi].Hands[hi];
        return Phase switch
        {
            GamePhase.Deal        => hand.State == HandState.Playing && hand.Cards.Count < 2,
            GamePhase.PlayerTurns => pi == ActivePlayerIndex && hi == ActiveHandIndex
                                  && GameEngine.CanHit(hand)
                                  && !pendingDouble.HasValue && !pendingSplit.HasValue
                                  && !State.WaitingForNextPlayer,
            _ => false,
        };
    }

    private static void DrawHandStateLabel(Hand hand)
    {
        switch (hand.State)
        {
            case HandState.Bust:
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Bust"); break;
            case HandState.Blackjack:
                ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), "Blackjack"); break;
            case HandState.Stand:
                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), "Stand"); break;
            case HandState.Playing:
                if (hand.Cards.Count > 0)
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Playing");
                break;
        }
    }

    private static (string Label, Vector4 Color) PayoutDisplay(GameState state, int playerIndex, int handIndex)
    {
        var grey  = new Vector4(0.55f, 0.55f, 0.55f, 1f);
        var red   = new Vector4(1f, 0.35f, 0.35f, 1f);
        var green = new Vector4(0.35f, 0.9f, 0.35f, 1f);
        var gold  = new Vector4(1f, 0.85f, 0f, 1f);

        return GameEngine.GetPayoutResult(state, playerIndex, handIndex) switch
        {
            PayoutResult.Win   => ("Win",    green),
            PayoutResult.BjWin => ("BJ Win", gold),
            PayoutResult.Lose  => ("Lose",   red),
            PayoutResult.Push  => ("Push",   grey),
            _                  => (string.Empty, default),
        };
    }

    private static uint ToU32(Vector4 c) =>
        ((uint)(c.X * 255) & 0xFF) |
        (((uint)(c.Y * 255) & 0xFF) << 8) |
        (((uint)(c.Z * 255) & 0xFF) << 16) |
        (((uint)(c.W * 255) & 0xFF) << 24);

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw()
    {
        // Reset venue-memory banner when the territory changes.
        var currentTerritory = clientState.TerritoryType;
        if (currentTerritory != lastSeenTerritory)
        {
            lastSeenTerritory    = currentTerritory;
            venueMemoryDismissed = false;
        }

        // Drain outgoing queue — hold a roll entry until the previous roll's response has arrived
        var isPublicChannel = config.ChatChannel is "/say" or "/yell" or "/shout";
        var cooldownMs = isPublicChannel ? config.PublicChatCooldownMs : config.PrivateChatCooldownMs;
        if (chatQueue.Count > 0)
        {
            var (isRoll, invoke, minWaitAfterMs, minWaitBeforeMs) = chatQueue.Peek();
            var requiredMs = Math.Max(cooldownMs, lastSentMinWaitMs) + minWaitBeforeMs;
            if ((DateTime.UtcNow - lastChatSent).TotalMilliseconds >= requiredMs && (!isRoll || pendingHit == null))
            {
                chatQueue.Dequeue();
                invoke();
                lastChatSent      = DateTime.UtcNow;
                lastSentMinWaitMs = minWaitAfterMs;
            }
        }

        // Process deferred roll from OnChatMessage
        if (deferredRoll.HasValue)
        {
            var (isDealer, pi, hi, roll) = deferredRoll.Value;
            deferredRoll = null;
            Apply(isDealer ? new AddDealerCard(roll) : new AddPlayerCard(pi, hi, roll));
            // Advance auto-deal if more cards are needed
            if (Phase == GamePhase.Deal && autoDealQueue.TryDequeue(out var next))
            {
                if (next.IsFirstCard)
                {
                    if (config.AutoTargetEnabled)
                    {
                        var dp = config.GameState.Players[next.PlayerIndex];
                        if (dp.World.Length > 0)
                            Plugin.TargetPlayer(dp.FullName, dp.World);
                    }
                    Apply(new AnnouncePlayerDeal(next.PlayerIndex));
                }
                QueueHitRoll(next.IsDealer, next.PlayerIndex, next.HandIndex);
            }
        }

        // ── Bank manage window ─────────────────────────────────────────────────
        if (bankManagePlayerIndex >= 0 && bankManagePlayerIndex < State.Players.Count)
        {
            var bankWinOpen = true;
            var bmp     = State.Players[bankManagePlayerIndex];
            var bmpKey  = PlayerStatKey(bmp);
            ImGui.SetNextWindowSize(new Vector2(380, 480), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Bank##bankManage", ref bankWinOpen, ImGuiWindowFlags.NoCollapse))
            {
                if (!config.PlayerStatsStore.TryGetValue(bmpKey, out var bmpStat))
                {
                    bmpStat = new PlayerStat { DisplayName = bmp.DisplayName };
                    config.PlayerStatsStore[bmpKey] = bmpStat;
                }
                var bmpBank = bmpStat.Bank;

                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{bmp.DisplayName}");
                ImGui.SameLine();
                ImGui.TextDisabled($"Bank: {bmpBank:N0}");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText(bmpBank.ToString());
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to copy");
                if (bmp.World.Length > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Trade##bankmantradetop"))
                        Plugin.TradePlayer(bmp.FullName, bmp.World);
                }
                ImGui.Separator();
                ImGui.Spacing();

                // Deposit
                ImGui.AlignTextToFramePadding(); ImGui.Text("Deposit"); ImGui.SameLine(80);
                ImGui.SetNextItemWidth(140);
                ImGui.InputText("##bankdep", ref bankDepositBuf, 20);
                ImGui.SameLine();
                var canDep2 = long.TryParse(bankDepositBuf, out var depAmt2) && depAmt2 > 0;
                if (!canDep2) ImGui.BeginDisabled();
                if (ImGui.Button("Confirm##bankdepconfirm"))
                {
                    bmpStat.Bank += depAmt2;
                    AddBankLog(bmpStat, BankTransactionKind.Deposit, depAmt2, bmpStat.Bank);
                    Apply(new AnnounceBankDeposit(bankManagePlayerIndex, depAmt2, bmpStat.Bank));
                    bankDepositBuf = string.Empty;
                }
                if (!canDep2) ImGui.EndDisabled();

                ImGui.Spacing();

                // Withdraw
                ImGui.AlignTextToFramePadding(); ImGui.Text("Withdraw"); ImGui.SameLine(80);
                ImGui.SetNextItemWidth(140);
                ImGui.InputText("##bankwd", ref bankWithdrawBuf, 20);
                ImGui.SameLine();
                var canWd2 = long.TryParse(bankWithdrawBuf, out var wdAmt2) && wdAmt2 > 0 && wdAmt2 <= bmpBank;
                if (!canWd2) ImGui.BeginDisabled();
                if (ImGui.Button("Confirm##bankwdconfirm"))
                {
                    bmpStat.Bank = Math.Max(0, bmpBank - wdAmt2);
                    AddBankLog(bmpStat, BankTransactionKind.Withdrawal, wdAmt2, bmpStat.Bank);
                    Apply(new AnnounceBankWithdraw(bankManagePlayerIndex, wdAmt2, bmpStat.Bank));
                    bankWithdrawBuf = string.Empty;
                }
                if (!canWd2) ImGui.EndDisabled();

                ImGui.Spacing();

                // Remind (betting phase + bank > 0 + bet set)
                if (Phase == GamePhase.Betting && bmpBank > 0 && !string.IsNullOrWhiteSpace(bmp.Bet))
                {
                    if (ImGui.Button("Remind##bankremind"))
                        Apply(new AnnounceBankRemind(bankManagePlayerIndex, bmpBank));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remind player of their bet and bank balance");
                }

                // Shortfall announce
                var parsedBet2 = GameEngine.ParseBet(bmp.Bet);
                var shortfall2 = parsedBet2 > 0 ? parsedBet2 - bmpBank : 0m;
                if (shortfall2 > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f),
                        $"Short by {GameEngine.FormatGil(shortfall2)}");
                    ImGui.SameLine();
                    if (ImGui.Button("Announce Shortfall##bankshort"))
                        Apply(new AnnounceBankShortfall(bankManagePlayerIndex, (long)Math.Ceiling(shortfall2)));
                }

                ImGui.Spacing();

                // Transaction history
                ImGui.Separator();
                ImGui.Text("History");
                var log = bmpStat.BankLog;
                var tableH = ImGui.GetContentRegionAvail().Y;
                if (ImGui.BeginTable("##banklog", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
                    new Vector2(0, tableH)))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Time",    ImGuiTableColumnFlags.None, 60);
                    ImGui.TableSetupColumn("Type",    ImGuiTableColumnFlags.None, 80);
                    ImGui.TableSetupColumn("Amount",  ImGuiTableColumnFlags.None, 80);
                    ImGui.TableSetupColumn("Balance", ImGuiTableColumnFlags.None, 80);
                    ImGui.TableHeadersRow();

                    for (var li = log.Count - 1; li >= 0; li--)
                    {
                        var entry = log[li];
                        var isCredit = entry.Kind is BankTransactionKind.Deposit or BankTransactionKind.Win;
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(entry.Timestamp.ToString("HH:mm"));
                        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(entry.Kind switch
                        {
                            BankTransactionKind.Deposit    => "Deposit",
                            BankTransactionKind.Withdrawal => "Withdraw",
                            BankTransactionKind.Bet        => "Bet",
                            BankTransactionKind.Win        => "Win",
                            BankTransactionKind.DoubleDown => "Double",
                            BankTransactionKind.Split      => "Split",
                            _                              => "?"
                        });
                        ImGui.TableSetColumnIndex(2);
                        if (isCredit) ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"+{entry.Amount:N0}");
                        else          ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"-{entry.Amount:N0}");
                        ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{entry.Balance:N0}");
                    }
                    if (log.Count == 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextDisabled("No transactions");
                    }
                    ImGui.EndTable();
                }

            }
            ImGui.End();
            if (!bankWinOpen)
            {
                bankManagePlayerIndex = -1;
                bankDepositBuf        = string.Empty;
                bankWithdrawBuf       = string.Empty;
            }
        }

        // ── Trade bet prompt modal ─────────────────────────────────────────────
        if (pendingBetPrompt.HasValue)
            ImGui.OpenPopup("Set bet from trade?##tradeBet");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0, 0, 0, 0));
        var showBetModal = ImGui.BeginPopupModal("Set bet from trade?##tradeBet", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBetModal)
        {
            var (bpi, bgil) = pendingBetPrompt!.Value;
            var bplayer     = State.Players[bpi];
            ImGui.Text($"Set {bplayer.DisplayName}'s bet to {bgil:N0} gil?");
            ImGui.Spacing();
            if (ImGui.Button("Yes"))
            {
                betEdits.Remove(bpi);
                Apply(new SetPlayerBet(bpi, bgil.ToString()));
                pendingBetPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                pendingBetPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ── Bet-or-bank trade prompt modal (both options on, Betting phase) ──────
        if (pendingBetOrBankPrompt.HasValue)
            ImGui.OpenPopup("Trade received##betOrBank");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0, 0, 0, 0));
        var showBetOrBankModal = ImGui.BeginPopupModal("Trade received##betOrBank", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBetOrBankModal)
        {
            var (bobpi, bobgil) = pendingBetOrBankPrompt!.Value;
            var bobPlayer = State.Players[bobpi];
            ImGui.Text($"Received {bobgil:N0} gil from {bobPlayer.DisplayName}.");
            ImGui.Spacing();
            if (ImGui.Button("Set as bet##bobBet"))
            {
                betEdits.Remove(bobpi);
                Apply(new SetPlayerBet(bobpi, bobgil.ToString()));
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Bank deposit##bobBank"))
            {
                var bobKey = PlayerStatKey(bobPlayer);
                if (!config.PlayerStatsStore.TryGetValue(bobKey, out var bobStat))
                {
                    bobStat = new PlayerStat { DisplayName = bobPlayer.DisplayName };
                    config.PlayerStatsStore[bobKey] = bobStat;
                }
                bobStat.Bank += bobgil;
                AddBankLog(bobStat, BankTransactionKind.Deposit, bobgil, bobStat.Bank);
                Apply(new AnnounceBankDeposit(bobpi, bobgil, bobStat.Bank));
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Ignore##bobIgnore"))
            {
                pendingBetOrBankPrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ── Bank trade prompt modal ────────────────────────────────────────────
        if (pendingBankTradePrompt.HasValue)
            ImGui.OpenPopup("Bank trade##bankTradePrompt");
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0, 0, 0, 0));
        var showBankModal = ImGui.BeginPopupModal("Bank trade##bankTradePrompt", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        if (showBankModal)
        {
            var (btpi, btamt, btwd) = pendingBankTradePrompt!.Value;
            var btplayer = State.Players[btpi];
            var btKey    = PlayerStatKey(btplayer);
            if (!config.PlayerStatsStore.TryGetValue(btKey, out var btStat))
            {
                btStat = new PlayerStat { DisplayName = btplayer.DisplayName };
                config.PlayerStatsStore[btKey] = btStat;
            }
            var verb = btwd ? "Withdraw" : "Deposit";
            ImGui.Text($"{verb} {btamt:N0} gil {(btwd ? "from" : "to")} {btplayer.DisplayName}'s bank?");
            ImGui.Spacing();
            if (ImGui.Button("Yes##bankTradeYes"))
            {
                btStat.Bank = btwd
                    ? Math.Max(0, btStat.Bank - btamt)
                    : btStat.Bank + btamt;
                AddBankLog(btStat, btwd ? BankTransactionKind.Withdrawal : BankTransactionKind.Deposit, btamt, btStat.Bank);
                Apply(btwd
                    ? new AnnounceBankWithdraw(btpi, btamt, btStat.Bank)
                    : new AnnounceBankDeposit (btpi, btamt, btStat.Bank));
                pendingBankTradePrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No##bankTradeNo"))
            {
                pendingBankTradePrompt = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (isHistoryView)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.3f, 1f));
            ImGui.TextUnformatted("Viewing previous round");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("Exit History View"))
                ExitHistoryView();
            ImGui.Separator();
        }

        if (!venueMemoryDismissed && !isHistoryView && GetVenueMemorySuggestion() is var suggestion && suggestion.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.85f, 1f, 1f));
            ImGui.TextUnformatted($"The last time you were here you used \"{suggestion.Value.Name}\". Switch to it?");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("Yes##venueMemoryYes"))
            {
                config.ActiveVenueIndex = suggestion.Value.Index;
                bankWindow.SyncBuffers();
                config.Save();
                venueMemoryDismissed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("X##venueMemoryDismiss"))
                venueMemoryDismissed = true;
            ImGui.Separator();
        }

        if (ImGui.SmallButton("Config"))
            configWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Bank"))
            bankWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Stats"))
            playerStatsWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("History"))
            roundHistoryWindow.Toggle();

        var canUndo = config.UndoStack.Count > 0;
        var canRedo = config.RedoStack.Count > 0;
        var undoW   = ImGui.CalcTextSize("Undo").X + ImGui.GetStyle().FramePadding.X * 2;
        var redoW   = ImGui.CalcTextSize("Redo").X + ImGui.GetStyle().FramePadding.X * 2;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetWindowWidth() - undoW - redoW - spacing * 2
                       - ImGui.GetStyle().WindowPadding.X);
        if (!canUndo) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Undo")) Undo();
        if (!canUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!canRedo) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Redo")) Redo();
        if (!canRedo) ImGui.EndDisabled();

        var uiBusy = chatQueue.Count > 0 || pendingHit != null || deferredRoll.HasValue;
        if (uiBusy) ImGui.BeginDisabled();

        ImGui.Separator();

        var dealerHitActive = GameEngine.CanHitDealer(State);

        // ── Dealer section ────────────────────────────────────────────────────
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        if (State.DealerHand.Cards.Count > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(GameEngine.HandString(State.DealerHand.Cards));
            ImGui.SameLine();

            var val      = GameEngine.HandValue(State.DealerHand.Cards);
            var scoreStr = GameEngine.ScoreString(State.DealerHand.Cards);
            if (val > 21)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), $"= {scoreStr}  BUST");
            else if (val == 21 && State.DealerHand.Cards.Count == 2)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), $"= {scoreStr}  Blackjack");
            else
            {
                ImGui.Text($"= {scoreStr}");
                var rec = GameEngine.DealerRecommendation(State.DealerHand);
                var allBust = State.Players.Count > 0 && State.Players.All(p => p.Hands.All(h => h.State == HandState.Bust));
                if (rec.Length > 0 && Phase == GamePhase.DealerTurn && !allBust)
                {
                    ImGui.SameLine();
                    var rc = rec == "HIT"
                        ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    ImGui.TextColored(rc, $"→ {rec}");
                }
            }
        }

        if (dealerHitActive)
        {
            if (State.DealerHand.Cards.Count > 0) ImGui.SameLine();
            if (ImGui.SmallButton("Hit##dealer"))
            {
                Apply(new AnnounceDealerHit());
                QueueHitRoll(isDealer: true, -1, -1);
            }
        }

        // ── Player table ──────────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Text("-- Players --");
        ImGui.Separator();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##players"u8, 7, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8,      ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8,       ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Bank"u8,      ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Cards"u8,     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Score"u8,     ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Status"u8,    ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableHeadersRow();

            int removeAt = -1;
            for (var pi = 0; pi < State.Players.Count; pi++)
            {
                var p         = State.Players[pi];
                var hasWorld    = p.World.Length > 0;
                var hasNickname = p.Nickname.Length > 0;
                var multiHand = p.Hands.Count > 1;

                for (var hi = 0; hi < p.Hands.Count; hi++)
                {
                    var hand        = p.Hands[hi];
                    var isFirstHand = hi == 0;
                    var isActiveHand = Phase == GamePhase.PlayerTurns
                                    && pi == ActivePlayerIndex && hi == ActiveHandIndex;

                    ImGui.TableNextRow();
                    if (isActiveHand)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1,
                            ToU32(new Vector4(0.25f, 0.45f, 0.75f, 0.35f)));

                    // ── Name column ───────────────────────────────────────────
                    ImGui.TableSetColumnIndex(0);
                    var nameCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (isFirstHand)
                    {
                        if (renamingIndex == pi)
                        {
                            var okW = ImGui.CalcTextSize("OK").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - okW);
                            var submitted = ImGui.InputText($"##rename{pi}", ref renamingBuffer, 64,
                                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                            ImGui.SameLine();
                            var canConfirm = renamingBuffer.Length > 0 || p.World.Length > 0;
                            if (!canConfirm) ImGui.BeginDisabled();
                            if (ImGui.SmallButton($"OK##{pi}ok") || submitted)
                            {
                                if (canConfirm) Apply(new RenamePlayer(pi, renamingBuffer));
                                renamingIndex = -1;
                            }
                            if (!canConfirm) ImGui.EndDisabled();
                        }
                        else
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(p.DisplayName);
                            if (ImGui.IsItemHovered())
                            {
                                if (p.World.Length > 0)
                                    ImGui.SetTooltip($"{p.FullName}@{p.World}");
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                {
                                    renamingIndex  = pi;
                                    renamingBuffer = p.Nickname;
                                }
                            }

                            var winnerKey = p.FullName.Length > 0 ? p.FullName : p.Nickname;
                            var isWinner  = config.GameState.LastRoundWinners.Contains(winnerKey);
                            var isPusher  = !isWinner && config.GameState.LastRoundPushers.Contains(winnerKey);
                            var sp      = ImGui.GetStyle().ItemSpacing.X;
                            var fp      = ImGui.GetStyle().FramePadding.X;
                            float BW(string s) => ImGui.CalcTextSize(s).X + fp * 2;
                            var clearW  = hasWorld && hasNickname ? BW("C") + sp : 0;
                            var targetW = hasWorld               ? BW("@") + sp : 0;
                            var renameW = BW("R");
                            var spadeW  = (isWinner || isPusher) ? ImGui.CalcTextSize("\u2660").X + sp : 0;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(nameCellRight - spadeW - targetW - renameW - clearW);

                            if (isWinner)
                            {
                                ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f), "\u2660");
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Won last round"u8);
                                ImGui.SameLine();
                            }
                            else if (isPusher)
                            {
                                ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 1f, 1f), "\u2660");
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pushed last round"u8);
                                ImGui.SameLine();
                            }

                            if (hasWorld)
                            {
                                if (ImGui.SmallButton($"@##{pi}target"))
                                    Plugin.TargetPlayer(p.FullName, p.World);
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Target {p.FullName}@{p.World}");
                                ImGui.SameLine();
                            }

                            if (ImGui.SmallButton($"R##{pi}rename"))
                            {
                                renamingIndex  = pi;
                                renamingBuffer = p.Nickname;
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rename"u8);

                            if (hasWorld && hasNickname)
                            {
                                ImGui.SameLine();
                                if (ImGui.SmallButton($"C##{pi}clear"))
                                    Apply(new RenamePlayer(pi, ""));
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear nickname"u8);
                            }
                        }
                    }
                    else
                    {
                        // Subsequent split-hand rows: show indented label
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled($"  ↳ Hand {hi + 1}");
                    }

                    // ── Bet column ────────────────────────────────────────────
                    ImGui.TableSetColumnIndex(1);
                    if (isFirstHand)
                    {
                        var betCellRight   = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        var confirmButtonW = Phase == GamePhase.Betting
                            ? ImGui.CalcTextSize("Confirm").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                            : 0;
                        var tradeButtonW = hasWorld
                            ? ImGui.CalcTextSize("Trade").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                            : 0;
                        if (Phase != GamePhase.Betting)
                        {
                            var eb        = GameEngine.GetEffectiveBet(p, hand);
                            var betLabel  = eb > 0 ? GameEngine.FormatGil(eb) : p.Bet;
                            var betCopy   = eb > 0 ? $"{eb:0.##}" : p.Bet;
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextDisabled(betLabel);
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip("Click to copy bet");
                                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    ImGui.SetClipboardText(betCopy);
                            }
                        }
                        else
                        {
                            ImGui.SetNextItemWidth(betCellRight - ImGui.GetCursorPosX() - tradeButtonW - confirmButtonW);
                            var betVal = betEdits.TryGetValue(pi, out var e) ? e : p.Bet;
                            if (ImGui.InputText($"##bet{pi}", ref betVal, 16, ImGuiInputTextFlags.EnterReturnsTrue))
                            {
                                betEdits.Remove(pi);
                                Apply(new SetPlayerBet(pi, betVal));
                            }
                            else
                            {
                                betEdits[pi] = betVal;
                            }
                        }
                        if (hasWorld)
                        {
                            var tradeOnlyW = ImGui.CalcTextSize("Trade").X + ImGui.GetStyle().FramePadding.X * 2;
                            var tradePosX  = betCellRight - tradeOnlyW - confirmButtonW;
                            ImGui.SameLine();
                            if (ImGui.GetCursorPosX() < tradePosX)
                                ImGui.SetCursorPosX(tradePosX);
                            if (ImGui.SmallButton($"Trade##{pi}trade"))
                            {
                                if (ImGui.GetIO().KeyShift)
                                {
                                    Apply(new AnnounceBetRequest(pi));
                                    if (hasWorld)
                                        Plugin.TargetPlayer(p.FullName, p.World);
                                    QueueTrade(p.FullName, p.World, config.PrivateChatCooldownMs);
                                }
                                else
                                    Plugin.TradePlayer(p.FullName, p.World);
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip($"Trade {p.FullName}@{p.World}\nShift+Click to announce bet request then open trade");
                        }
                        if (Phase == GamePhase.Betting)
                        {
                            ImGui.SameLine();
                            var betForConfirm = betEdits.TryGetValue(pi, out var bec) ? bec : p.Bet;
                            var canConfirm = !string.IsNullOrWhiteSpace(betForConfirm);
                            if (!canConfirm) ImGui.BeginDisabled();
                            if (ImGui.SmallButton($"Remind##{pi}confirm"))
                            {
                                if (betEdits.TryGetValue(pi, out var pendingBet))
                                {
                                    betEdits.Remove(pi);
                                    if (pendingBet != p.Bet)
                                        Apply(new SetPlayerBet(pi, pendingBet));
                                }
                                if (config.RemindTargetEnabled && hasWorld)
                                    QueueTarget(p.FullName, p.World);
                                Apply(new AnnounceBetConfirm(pi));
                            }
                            if (!canConfirm) ImGui.EndDisabled();
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                ImGui.SetTooltip("Remind the player of their current bet in chat");
                        }
                    }
                    else
                    {
                        // Show the effective bet for this split hand (read-only)
                        var eb = GameEngine.GetEffectiveBet(p, hand);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextDisabled(eb > 0 ? GameEngine.FormatGil(eb) : (GameEngine.ParseBet(p.Bet) > 0 ? GameEngine.FormatGil(GameEngine.ParseBet(p.Bet)) : p.Bet));
                    }

                    // ── Bank column ───────────────────────────────────────────
                    ImGui.TableSetColumnIndex(2);
                    if (isFirstHand)
                    {
                        var bankKey  = PlayerStatKey(p);
                        if (!config.PlayerStatsStore.TryGetValue(bankKey, out var bankStat))
                        {
                            bankStat = new PlayerStat { DisplayName = p.DisplayName };
                            config.PlayerStatsStore[bankKey] = bankStat;
                        }
                        var bankVal   = bankStat.Bank;
                        var parsedBet = GameEngine.ParseBet(p.Bet);
                        var bankCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                        var fp  = ImGui.GetStyle().FramePadding.X;
                        float BKW(string s) => ImGui.CalcTextSize(s).X + fp * 2;

                        // Compute payout delta for this player (shown during Payout phase)
                        var bankDelta = 0m;
                        if (Phase == GamePhase.Payout && bankVal > 0)
                        {
                            for (var bhi = 0; bhi < p.Hands.Count; bhi++)
                            {
                                var br = GameEngine.GetPayoutResult(State, pi, bhi);
                                bankDelta += br switch
                                {
                                    PayoutResult.Win   => GameEngine.GetEffectiveBet(p, p.Hands[bhi]),
                                    PayoutResult.BjWin => Math.Round(GameEngine.GetEffectiveBet(p, p.Hands[bhi])
                                                            * (State.BjPayout switch
                                                            {
                                                                BlackjackPayout.SixToFive => 1.2m,
                                                                BlackjackPayout.EvenMoney => 1.0m,
                                                                _                         => 1.5m,
                                                            }), 2),
                                    _                  => 0m,
                                };
                            }
                        }

                        // Bank balance (clickable to copy)
                        ImGui.AlignTextToFramePadding();
                        if (bankVal > 0)
                        {
                            var bankLabel = GameEngine.FormatGil(bankVal);
                            var shortfall = parsedBet > 0 ? parsedBet - bankVal : 0m;
                            if (shortfall > 0)
                                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), bankLabel);
                            else
                                ImGui.TextUnformatted(bankLabel);
                            if (ImGui.IsItemHovered())
                            {
                                var tipLines = new System.Text.StringBuilder();
                                if (shortfall > 0)
                                    tipLines.AppendLine($"Short by {GameEngine.FormatGil(shortfall)}");
                                if (Phase == GamePhase.Payout && bankDelta != 0)
                                {
                                    var deltaStr = bankDelta > 0 ? $"+{GameEngine.FormatGil(bankDelta)}" : GameEngine.FormatGil(bankDelta);
                                    tipLines.AppendLine($"This round: {deltaStr}");
                                    tipLines.AppendLine($"After settlement: {GameEngine.FormatGil(Math.Max(0, bankVal + bankDelta))}");
                                }
                                tipLines.Append("Click to copy");
                                ImGui.SetTooltip(tipLines.ToString());
                                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    ImGui.SetClipboardText(bankVal.ToString());
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled("—");
                        }

                        // Manage button
                        var manageW = BKW("Manage");
                        ImGui.SameLine();
                        if (ImGui.GetCursorPosX() < bankCellRight - manageW)
                            ImGui.SetCursorPosX(bankCellRight - manageW);
                        if (ImGui.SmallButton($"Manage##{pi}bank"))
                        {
                            bankManagePlayerIndex = pi;
                            bankDepositBuf        = string.Empty;
                            bankWithdrawBuf       = string.Empty;
                        }
                    }

                    // ── Cards column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(3);
                    if (hand.Cards.Count > 0)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(GameEngine.HandString(hand.Cards));
                        if (hand.Doubled)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "2x");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Player doubled down");
                        }
                    }

                    // ── Score column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(4);
                    if (hand.Cards.Count > 0)
                    {
                        var val      = GameEngine.HandValue(hand.Cards);
                        var scoreStr = hand.State == HandState.Stand
                            ? val.ToString()
                            : GameEngine.ScoreString(hand.Cards);
                        if (val > 21)
                            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), scoreStr);
                        else if (val == 21)
                            ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), scoreStr);
                        else
                            ImGui.Text(scoreStr);
                    }

                    // ── Status column ─────────────────────────────────────────
                    ImGui.TableSetColumnIndex(5);
                    var statusCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (Phase == GamePhase.Payout)
                    {
                        var isMultiHand = p.Hands.Count > 1;
                        var green = new Vector4(0.35f, 0.9f, 0.35f, 1f);
                        var red   = new Vector4(1f, 0.35f, 0.35f, 1f);
                        var grey  = new Vector4(0.55f, 0.55f, 0.55f, 1f);
                        if (isMultiHand && !isFirstHand)
                        {
                            // Non-first split hand: label only, no copy button
                            var (lbl, col) = PayoutDisplay(State, pi, hi);
                            if (lbl.Length > 0)
                            {
                                ImGui.TextColored(col, lbl);
                                if (ImGui.IsItemHovered())
                                {
                                    var amt = GameEngine.PayoutAmountString(State, pi, hi);
                                    ImGui.SetTooltip(amt.Length > 0 ? $"{lbl} {amt}" : lbl);
                                }
                            }
                        }
                        else
                        {
                            // Single hand, or first hand of a split: compute combined totals
                            var handCount = isMultiHand ? p.Hands.Count : 1;
                            var totalOwed = 0m;
                            var netDelta  = 0m;
                            for (var hh = 0; hh < handCount; hh++)
                            {
                                var hhi    = isMultiHand ? hh : hi;
                                var result = GameEngine.GetPayoutResult(State, pi, hhi);
                                var eb     = GameEngine.GetEffectiveBet(p, p.Hands[hhi]);
                                var d      = GameEngine.PayoutDelta(State, pi, hhi) ?? 0m;
                                netDelta  += d;
                                totalOwed += result switch
                                {
                                    PayoutResult.Win or PayoutResult.BjWin => eb + d,
                                    PayoutResult.Push                      => eb,
                                    _                                      => 0m,
                                };
                            }

                            string dispLabel;
                            Vector4 dispColor;
                            if (isMultiHand)
                            {
                                if (netDelta > 0)      (dispLabel, dispColor) = ($"Win +{GameEngine.FormatGil(netDelta)}",  green);
                                else if (netDelta < 0) (dispLabel, dispColor) = ($"Lose {GameEngine.FormatGil(netDelta)}", red);
                                else                   (dispLabel, dispColor) = ("Push",                                    grey);
                            }
                            else
                            {
                                var (lbl, col) = PayoutDisplay(State, pi, hi);
                                var amt = GameEngine.PayoutAmountString(State, pi, hi);
                                dispLabel = amt.Length > 0 ? $"{lbl} {amt}" : lbl;
                                dispColor = col;
                            }

                            if (dispLabel.Length > 0)
                            {
                                ImGui.TextColored(dispColor, dispLabel);
                                if (ImGui.IsItemHovered())
                                {
                                    if (isMultiHand)
                                    {
                                        var lines = new System.Text.StringBuilder();
                                        for (var hh = 0; hh < p.Hands.Count; hh++)
                                        {
                                            var (hlbl, _) = PayoutDisplay(State, pi, hh);
                                            var hamt = GameEngine.PayoutAmountString(State, pi, hh);
                                            lines.AppendLine($"Hand {hh + 1}: {(hamt.Length > 0 ? $"{hlbl} {hamt}" : hlbl)}");
                                        }
                                        var netStr = netDelta > 0 ? $"+{GameEngine.FormatGil(netDelta)}" : GameEngine.FormatGil(netDelta);
                                        lines.Append($"Net: {netStr}");
                                        ImGui.SetTooltip(lines.ToString());
                                    }
                                    else
                                    {
                                        var d = GameEngine.PayoutDelta(State, pi, hi);
                                        if (d != null) ImGui.SetTooltip($"{d.Value:0.##}");
                                    }
                                }
                                if (totalOwed > 0)
                                {
                                    var ctrlHeld   = ImGui.GetIO().KeyCtrl;
                                    var initBet    = GameEngine.ParseBet(p.Bet);
                                    var keepBetVal = totalOwed - initBet;
                                    var copyVal    = ctrlHeld ? $"{keepBetVal:0.##}" : $"{totalOwed:0.##}";
                                    var copyW      = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                                    ImGui.SameLine();
                                    ImGui.SetCursorPosX(statusCellRight - copyW + ImGui.GetStyle().ItemSpacing.X);
                                    if (ImGui.SmallButton($"Copy##{pi}payout"))
                                        ImGui.SetClipboardText(copyVal);
                                    if (ImGui.IsItemHovered())
                                        ImGui.SetTooltip(ctrlHeld
                                            ? $"Copy (keep initial bet): {keepBetVal:0.##}"
                                            : $"Copy total owed: {totalOwed:0.##}\nCtrl+Click to copy minus initial bet: {keepBetVal:0.##}");
                                }
                            }
                        }
                    }
                    else
                    {
                        DrawHandStateLabel(hand);
                        if (isActiveHand && !State.WaitingForNextPlayer)
                        {
                            var remindW = ImGui.CalcTextSize("Remind").X + ImGui.GetStyle().FramePadding.X * 2;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(statusCellRight - remindW);
                            if (ImGui.SmallButton($"Remind##{pi}_{hi}resend"))
                                Apply(new AnnouncePlayerTurn(pi, hi));
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Resend turn start message"u8);
                        }
                    }

                    // ── Actions column ────────────────────────────────────────
                    ImGui.TableSetColumnIndex(6);
                    var actionsCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    var hasAnyPending = pendingDouble.HasValue || pendingSplit.HasValue;
                    var isPendingDouble = pendingDouble.HasValue && pendingDouble.Value == (pi, hi);
                    var isPendingSplit  = pendingSplit.HasValue  && pendingSplit.Value  == (pi, hi);
                    var asp = ImGui.GetStyle().ItemSpacing.X;
                    float ABW(string s) => ImGui.CalcTextSize(s).X + ImGui.GetStyle().FramePadding.X * 2;

                    if (Phase == GamePhase.PlayerTurns && State.WaitingForNextPlayer
                        && pi == ActivePlayerIndex && hi == ActiveHandIndex)
                    {
                        var moreHands = p.Hands.Skip(hi + 1).Any(h => h.State == HandState.Playing);
                        var advLabel  = moreHands ? "Next Hand ↓" : "Next Player ↓";
                        ImGui.SetCursorPosX(actionsCellRight - ABW(advLabel));
                        if (ImGui.SmallButton($"{advLabel}##{pi}_{hi}")) Apply(new AdvanceToNextPlayer());
                    }
                    else if (isPendingDouble)
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Confirm Dbl") - asp - ABW("Cancel"));
                        if (ImGui.SmallButton($"Confirm Dbl##{pi}_{hi}"))
                        {
                            Apply(new AnnounceDoubleConfirm(pi, hi));
                            Apply(new DoubleDown(pi, hi));
                            pendingDouble = null;
                            QueueHitRoll(isDealer: false, pi, hi);
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Cancel##{pi}_{hi}dblcancel")) pendingDouble = null;
                    }
                    else if (isPendingSplit)
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Confirm Spl") - asp - ABW("Cancel"));
                        if (ImGui.SmallButton($"Confirm Spl##{pi}_{hi}"))
                        {
                            Apply(new SplitHand(pi, hi));
                            pendingSplit = null;
                            // The 1-card split hands are auto-hit in Draw()
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Cancel##{pi}_{hi}splcancel")) pendingSplit = null;
                    }
                    else if (Phase == GamePhase.Deal && PlayerHitActive(pi, hi))
                    {
                        ImGui.SetCursorPosX(actionsCellRight - ABW("Draw"));
                        if (ImGui.SmallButton($"Draw##{pi}_{hi}"))
                            QueueHitRoll(isDealer: false, pi, hi);
                    }
                    else
                    {
                        var total = ABW("Stand") + asp + ABW("Hit") + asp + ABW("Dbl") + asp + ABW("Spl")
                                  + (isFirstHand ? asp + ABW("X") : 0);
                        ImGui.SetCursorPosX(actionsCellRight - total);

                        var canStand = !hasAnyPending && Phase == GamePhase.PlayerTurns
                                    && pi == ActivePlayerIndex && hi == ActiveHandIndex
                                    && GameEngine.CanHit(hand);
                        if (!canStand) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"Stand##{pi}_{hi}")) Apply(new StandPlayer(pi, hi));
                        if (!canStand) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var hitActive = PlayerHitActive(pi, hi);
                        if (!hitActive) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"Hit##{pi}_{hi}"))
                        {
                            Apply(new AnnouncePlayerHit(pi, hi));
                            QueueHitRoll(isDealer: false, pi, hi);
                        }
                        if (!hitActive) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var canDouble = !hasAnyPending && isActiveHand
                                     && GameEngine.CanDouble(hand, p.Bet);
                        if (!canDouble) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"Dbl##{pi}_{hi}"))
                        {
                            var dblBet  = GameEngine.GetEffectiveBet(p, hand);
                            var dblKey  = PlayerStatKey(p);
                            var dblBank = config.PlayerStatsStore.TryGetValue(dblKey, out var dblStat) ? dblStat.Bank : 0;
                            pendingDouble = (pi, hi);
                            Apply(new AnnounceDouble(pi, hi));
                            if (dblBank >= (long)Math.Ceiling(dblBet) && dblStat != null)
                            {
                                dblStat.Bank -= (long)Math.Ceiling(dblBet);
                                AddBankLog(dblStat, BankTransactionKind.DoubleDown, (long)Math.Ceiling(dblBet), dblStat.Bank);
                                config.Save();
                            }
                            else if (hasWorld && config.AutoTradeEnabled)
                                QueueTrade(p.FullName, p.World);
                        }
                        if (!canDouble) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var canSplit = !hasAnyPending && isActiveHand
                                    && GameEngine.CanSplit(hand);
                        if (!canSplit) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"Spl##{pi}_{hi}"))
                        {
                            var splBet  = GameEngine.GetEffectiveBet(p, hand);
                            var splKey  = PlayerStatKey(p);
                            var splBank = config.PlayerStatsStore.TryGetValue(splKey, out var splStat) ? splStat.Bank : 0;
                            pendingSplit = (pi, hi);
                            Apply(new AnnounceSplit(pi, hi));
                            if (splBank >= (long)Math.Ceiling(splBet) && splStat != null)
                            {
                                splStat.Bank -= (long)Math.Ceiling(splBet);
                                AddBankLog(splStat, BankTransactionKind.Split, (long)Math.Ceiling(splBet), splStat.Bank);
                                config.Save();
                            }
                            else if (hasWorld && config.AutoTradeEnabled)
                                QueueTrade(p.FullName, p.World);
                        }
                        if (!canSplit) ImGui.EndDisabled();

                        if (isFirstHand)
                        {
                            ImGui.SameLine();
                            var canRemove = Phase == GamePhase.Betting;
                            if (!canRemove) ImGui.BeginDisabled();
                            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.7f, 0.15f, 0.15f, 1f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.25f, 0.25f, 1f));
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.5f, 0.05f, 0.05f, 1f));
                            if (ImGui.SmallButton($"X##{pi}")) removeAt = pi;
                            ImGui.PopStyleColor(3);
                            if (!canRemove) ImGui.EndDisabled();
                        }
                    }
                }
            }

            if (removeAt >= 0)
            {
                betEdits.Remove(removeAt);
                var shifted = betEdits.Where(kv => kv.Key > removeAt).ToList();
                foreach (var kv in shifted) { betEdits.Remove(kv.Key); betEdits[kv.Key - 1] = kv.Value; }
                Apply(new RemovePlayer(removeAt));
            }
            ImGui.EndTable();
        }

        // ── Add player (Betting only) ─────────────────────────────────────────
        ImGui.Spacing();
        if (Phase == GamePhase.Betting)
        {
            ImGui.SetNextItemWidth(200);
            var nameSubmitted = ImGui.InputText("##newName"u8, ref newPlayerName, 64,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            var canAdd = newPlayerName.Length > 0;
            if (!canAdd) ImGui.BeginDisabled();
            if (ImGui.Button("Add Player") || (nameSubmitted && canAdd))
            {
                Apply(new AddPlayer(Nickname: newPlayerName));
                newPlayerName = string.Empty;
            }
            if (!canAdd) ImGui.EndDisabled();
            ImGui.Spacing();
        }

        // ── Phase action bar ──────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.Spacing();

        var dealProgress = Phase == GamePhase.Deal
            ? $"  (dealer: {State.DealerHand.Cards.Count}/1  players: " +
              $"{(State.Players.Count > 0 ? State.Players.Min(p => p.Hands[0].Cards.Count) : 0)}-" +
              $"{(State.Players.Count > 0 ? State.Players.Max(p => p.Hands[0].Cards.Count) : 0)}/2)"
            : string.Empty;
        string phaseLabel;
        if (Phase == GamePhase.PlayerTurns
            && ActivePlayerIndex >= 0 && ActivePlayerIndex < State.Players.Count
            && ActiveHandIndex >= 0)
        {
            var ap   = State.Players[ActivePlayerIndex];
            var ah   = ActiveHandIndex < ap.Hands.Count ? ap.Hands[ActiveHandIndex] : null;
            var name = ap.Hands.Count > 1 ? $"{ap.DisplayName} (Hand {ActiveHandIndex + 1})" : ap.DisplayName;
            var acts = ah != null
                ? GameEngine.ValidActionsString(ah, GameEngine.CanDouble(ah, ap.Bet), GameEngine.CanSplit(ah))
                : string.Empty;
            phaseLabel = $"Phase: Player Actions  ({name}'s turn — {acts})";
        }
        else
        {
            phaseLabel = Phase switch
            {
                GamePhase.Betting    => "Phase: Betting",
                GamePhase.Deal       => $"Phase: Deal{dealProgress}",
                GamePhase.PlayerTurns => "Phase: Player Actions",
                GamePhase.DealerTurn => "Phase: Dealer Turn",
                GamePhase.Payout     => "Phase: Payout",
                _                    => string.Empty,
            };
        }
        ImGui.TextDisabled(phaseLabel);
        ImGui.Spacing();

        switch (Phase)
        {
            case GamePhase.Betting:
                if (ImGui.Button("Announce Betting Open"))
                    Apply(new AnnounceBettingOpen());
                ImGui.SameLine();
                var effectiveBets = State.Players.Select((p, i) =>
                    betEdits.TryGetValue(i, out var e) ? e : p.Bet);
                var canDeal = State.Players.Count > 0
                           && effectiveBets.All(b => !string.IsNullOrWhiteSpace(b));
                if (!canDeal) ImGui.BeginDisabled();
                if (ImGui.Button("Start Deal →"))
                {
                    // Flush uncommitted bet edits before transitioning
                    foreach (var (idx, val) in betEdits.ToList())
                    {
                        betEdits.Remove(idx);
                        if (val != State.Players[idx].Bet)
                            Apply(new SetPlayerBet(idx, val));
                    }
                    Apply(new StartDeal());
                    // Deduct initial bets from player banks
                    foreach (var p in State.Players)
                    {
                        var betAmt = (long)Math.Ceiling(GameEngine.ParseBet(p.Bet));
                        if (betAmt <= 0) continue;
                        var betKey = PlayerStatKey(p);
                        if (!config.PlayerStatsStore.TryGetValue(betKey, out var betStat) || betStat.Bank <= 0) continue;
                        betStat.Bank = Math.Max(0, betStat.Bank - betAmt);
                        AddBankLog(betStat, BankTransactionKind.Bet, betAmt, betStat.Bank);
                    }
                    // Queue initial cards: dealer first, then each player gets both cards in a pair
                    for (var i = 0; i < State.Players.Count; i++)
                    {
                        autoDealQueue.Enqueue((false, i, 0, true));   // first card — announce
                        autoDealQueue.Enqueue((false, i, 0, false));  // second card
                    }
                    Apply(new AnnounceDealerDeal());
                    QueueHitRoll(isDealer: true, -1, -1);
                }
                if (!canDeal) ImGui.EndDisabled();
                if (!canDeal && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(State.Players.Count == 0
                        ? "Add at least one player first."
                        : "All players need a bet before dealing.");
                break;

            case GamePhase.Deal:
                var dealDone = GameEngine.IsDealComplete(State);
                if (!dealDone) ImGui.BeginDisabled();
                if (ImGui.Button("Begin Player Turns →")) Apply(new BeginPlayerTurns());
                if (!dealDone) ImGui.EndDisabled();
                if (!dealDone && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Dealer needs 1 card; each player needs 2 cards."u8);
                break;

            case GamePhase.PlayerTurns:
                break;

            case GamePhase.DealerTurn:
                if (State.WaitingForDealer)
                {
                    if (ImGui.Button("Begin Dealer Turn →")) Apply(new BeginDealerTurn());
                }
                else
                {
                    var canPayout = GameEngine.CanGoToPayout(State);
                    if (!canPayout) ImGui.BeginDisabled();
                    if (ImGui.Button("Go to Payout →")) { Apply(new GoToPayout()); UpdatePlayerStats(); }
                    if (!canPayout) ImGui.EndDisabled();
                    if (!canPayout && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip("Dealer must finish their hand first."u8);
                }
                break;

            case GamePhase.Payout:
                if (ImGui.Button("New Round"))
                {
                    Apply(new NewRound());
                }
                break;
        }

        if (Phase != GamePhase.Payout && Phase != GamePhase.Betting)
        {
            ImGui.SameLine();
            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            if (!ctrlHeld) ImGui.BeginDisabled();
            if (ImGui.Button("Abort Round"))
            {
                config.NarrationLog.Add("Round aborted.");
                chatQueue.Clear();
                deferredRoll = null;
                Apply(new NewRound());
            }
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
                ImGui.SetTooltip("Hold Ctrl to abort the round."u8);
        }

        if (uiBusy) ImGui.EndDisabled();

        // ── Narration panel ───────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Chat Narration");
        ImGui.Separator();
        {
            var narUseCmd = config.NarrationUseChannelCommand;
            if (ImGui.Checkbox("Add channel command", ref narUseCmd))
            {
                config.NarrationUseChannelCommand = narUseCmd;
                config.Save();
            }

            ImGui.SameLine();
            if (config.NarrationLog.Count == 0) ImGui.BeginDisabled();
            if (ImGui.Button("Copy All"))
            {
                var sb = new StringBuilder();
                foreach (var line in config.NarrationLog)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(config.NarrationUseChannelCommand
                        ? config.ChatChannel + " " + line
                        : line);
                }
                ImGui.SetClipboardText(sb.ToString());
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear")) { config.NarrationLog.Clear(); config.Save(); }
            if (config.NarrationLog.Count == 0) ImGui.EndDisabled();

            ImGui.Spacing();
            if (ImGui.BeginChild("##narLog", new Vector2(0, 0), true))
            {
                for (var ni = 0; ni < config.NarrationLog.Count; ni++)
                {
                    var line    = config.NarrationLog[ni];
                    var display = config.NarrationUseChannelCommand
                        ? config.ChatChannel + " " + line
                        : line;
                    ImGui.PushID(ni);
                    if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(display);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy to clipboard"u8);
                    ImGui.PopID();
                    ImGui.SameLine();
                    ImGui.TextUnformatted(display);
                }
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
            }
            ImGui.EndChild();
        }
    }
}
