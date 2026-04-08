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
    private readonly Configuration    config;
    private readonly ConfigWindow     configWindow;
    private readonly BankWindow       bankWindow;
    private readonly PlayerStatsWindow playerStatsWindow;
    private readonly IChatGui      chatGui;
    private readonly IObjectTable  objectTable;
    private readonly ITargetManager targetManager;

    // Betting-phase UI state
    private string newPlayerName  = string.Empty;
    private int    renamingIndex  = -1;
    private string renamingBuffer = string.Empty;
    // In-progress bet edits (player index → typed string); committed to game state on Enter only.
    private readonly Dictionary<int, string> betEdits = [];

    // pending hit: null = not waiting; IsPublic=true means /random was sent, false means /dice
    private (bool IsDealer, int PlayerIndex, int HandIndex, bool IsPublic)? pendingHit;
    // deferred roll: set by OnChatMessage, applied at the start of the next Draw()
    private (bool IsDealer, int PlayerIndex, int HandIndex, int Roll)?      deferredRoll;
    // pending trade confirmation for double/split: set when dealer clicks the button, cleared on confirm/cancel
    private (int PlayerIndex, int HandIndex)? pendingDouble;
    private (int PlayerIndex, int HandIndex)? pendingSplit;
    // auto-deal queue: populated by StartDeal; QueueHitRoll is called one at a time as rolls resolve
    // IsFirstCard=true → emit AnnouncePlayerDeal before rolling
    private readonly Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> autoDealQueue = new();

    // rate-limited outgoing queue — narration strings and roll commands share a single FIFO and lastChatSent
    // each entry: (IsRoll, Invoke, MinWaitMs) — narration passes through freely; rolls block until pendingHit is clear
    // MinWaitMs: minimum ms to wait after the *previous* entry before sending this one (0 = use cooldownMs)
    private readonly Queue<(bool IsRoll, Action Invoke, int MinWaitMs)> chatQueue = new();
    private          DateTime                                             lastChatSent      = DateTime.MinValue;
    private          int                                                  lastSentMinWaitMs = 0;

    // ── Convenience accessors ─────────────────────────────────────────────────

    private GameState   State             => config.GameState;
    private GamePhase   Phase             => config.GameState.Phase;
    private int         ActivePlayerIndex => config.GameState.ActivePlayerIndex;
    private int         ActiveHandIndex   => config.GameState.ActiveHandIndex;

    // ── Constructor / Dispose ─────────────────────────────────────────────────

    public MainWindow(Configuration config, ConfigWindow configWindow, BankWindow bankWindow,
                      PlayerStatsWindow playerStatsWindow,
                      IChatGui chatGui, IObjectTable objectTable, ITargetManager targetManager)
        : base("Twenty One##TwentyOneMain")
    {
        this.config            = config;
        this.configWindow      = configWindow;
        this.bankWindow        = bankWindow;
        this.playerStatsWindow = playerStatsWindow;
        this.chatGui           = chatGui;
        this.objectTable    = objectTable;
        this.targetManager  = targetManager;
        SizeConstraints   = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
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
                         and not AnnounceDouble
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
                    if (raw.StartsWith('/'))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(raw, @"<wait\.(\d+)>");
                        if (m.Success)
                        {
                            minWait = int.Parse(m.Groups[1].Value) * 1000;
                            raw     = raw.Replace(m.Value, "").Trim();
                        }
                    }
                    var msg = raw.StartsWith('/') ? raw : config.ChatChannel + " " + raw;
                    chatQueue.Enqueue((false, () => SendChatMessage(msg), minWait));
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

    // Called immediately after Apply(new GoToPayout()) to record round results.
    private void UpdatePlayerStats()
    {
        var state = config.GameState;
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
        }
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

    private void QueueHitRoll(bool isDealer, int playerIndex, int handIndex)
    {
        if (!config.ChatEnabled)
        {
            var simRoll = Random.Shared.Next(1, 14);
            LogRoll(isDealer, playerIndex, simRoll);
            deferredRoll = (isDealer, playerIndex, handIndex, simRoll);
            return;
        }
        chatQueue.Enqueue((true, () => SendHitRoll(isDealer, playerIndex, handIndex), 0));
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

    private void OnChatMessage(XivChatType type, int timestamp,
        ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (pendingHit == null) return;

        var (isDealer, pi, hi, isPublic) = pendingHit.Value;

        if (!isPublic)
        {
            var localName = objectTable.LocalPlayer?.Name.TextValue;
            var payload   = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            if (localName == null || payload != null || !sender.TextValue.Contains(localName)) return;
        }

        var msgText = message.TextValue;
        var match   = (isPublic ? RandomRollRegex() : DiceRollRegex()).Match(msgText);
        if (!match.Success) return;

        if (!int.TryParse(match.Groups[1].Value, out var roll) || roll < 1 || roll > 13) return;

        pendingHit   = null;
        LogRoll(isDealer, pi, roll);
        deferredRoll = (isDealer, pi, hi, roll);
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
        // Drain outgoing queue — hold a roll entry until the previous roll's response has arrived
        var isPublicChannel = config.ChatChannel is "/say" or "/yell" or "/shout";
        var cooldownMs = isPublicChannel ? config.PublicChatCooldownMs : config.PrivateChatCooldownMs;
        var waitMs = Math.Max(cooldownMs, lastSentMinWaitMs);
        if (chatQueue.Count > 0 && (DateTime.UtcNow - lastChatSent).TotalMilliseconds >= waitMs)
        {
            var (isRoll, invoke, minWaitMs) = chatQueue.Peek();
            if (!isRoll || pendingHit == null)
            {
                chatQueue.Dequeue();
                invoke();
                lastChatSent      = DateTime.UtcNow;
                lastSentMinWaitMs = minWaitMs;
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
                if (next.IsFirstCard) Apply(new AnnouncePlayerDeal(next.PlayerIndex));
                QueueHitRoll(next.IsDealer, next.PlayerIndex, next.HandIndex);
            }
        }

        if (ImGui.SmallButton("Config"))
            configWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Bank"))
            bankWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.SmallButton("Stats"))
            playerStatsWindow.Toggle();

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
                if (rec.Length > 0 && Phase == GamePhase.DealerTurn)
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
        if (ImGui.BeginTable("##players"u8, 6, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8,      ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8,       ImGuiTableColumnFlags.WidthFixed, 70);
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
                            var sp      = ImGui.GetStyle().ItemSpacing.X;
                            var fp      = ImGui.GetStyle().FramePadding.X;
                            float BW(string s) => ImGui.CalcTextSize(s).X + fp * 2;
                            var clearW  = hasWorld && hasNickname ? BW("C") + sp : 0;
                            var targetW = hasWorld               ? BW("@") + sp : 0;
                            var renameW = BW("R");
                            var spadeW  = isWinner               ? ImGui.CalcTextSize("\u2660").X + sp : 0;
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(nameCellRight - spadeW - targetW - renameW - clearW);

                            if (isWinner)
                            {
                                ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f), "\u2660");
                                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Won last round"u8);
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
                        ImGui.SetNextItemWidth(betCellRight - ImGui.GetCursorPosX() - tradeButtonW - confirmButtonW);
                        if (Phase != GamePhase.Betting) ImGui.BeginDisabled();
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
                        if (Phase != GamePhase.Betting) ImGui.EndDisabled();
                        if (Phase != GamePhase.Betting && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        {
                            ImGui.SetTooltip("Click to copy bet");
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                ImGui.SetClipboardText(betVal);
                        }
                        if (hasWorld)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Trade##{pi}trade"))
                            {
                                if (ImGui.GetIO().KeyShift)
                                    Apply(new AnnounceBetRequest(pi));
                                else
                                    Plugin.TradePlayer(p.FullName, p.World);
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip($"Trade {p.FullName}@{p.World}\nShift+Click to announce bet request in chat");
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
                        ImGui.TextDisabled(eb > 0 ? eb.ToString("0.##") : p.Bet);
                    }

                    // ── Cards column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(2);
                    if (hand.Cards.Count > 0)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(GameEngine.HandString(hand.Cards));
                    }

                    // ── Score column ──────────────────────────────────────────
                    ImGui.TableSetColumnIndex(3);
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
                    ImGui.TableSetColumnIndex(4);
                    var statusCellRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (Phase == GamePhase.Payout)
                    {
                        // Check if all hands for this player win (combined display)
                        var allHandsWin = p.Hands.Count > 1
                            && p.Hands.Select((_, hh) => GameEngine.GetPayoutResult(State, pi, hh))
                                      .All(r => r == PayoutResult.Win || r == PayoutResult.BjWin);

                        if (allHandsWin && isFirstHand)
                        {
                            // Combined payout display on first hand row
                            var combinedNet = 0m;
                            for (var hh = 0; hh < p.Hands.Count; hh++)
                            {
                                var eb = GameEngine.GetEffectiveBet(p, p.Hands[hh]);
                                combinedNet += GameEngine.GetPayoutResult(State, pi, hh) == PayoutResult.BjWin
                                    ? Math.Round(eb * (State.BjPayout switch
                                        { BlackjackPayout.SixToFive => 1.2m, BlackjackPayout.EvenMoney => 1.0m, _ => 1.5m }), 2)
                                    : eb;
                            }
                            var green = new Vector4(0.35f, 0.9f, 0.35f, 1f);
                            var combinedAmtStr = $"+{combinedNet:0.##}";
                            var shiftHeld2 = ImGui.GetIO().KeyShift;
                            var totalBet   = p.Hands.Sum(h => GameEngine.GetEffectiveBet(p, h));
                            var withBet    = $"{combinedNet + totalBet:0.##}";
                            var copyVal2   = shiftHeld2 ? withBet : $"{combinedNet:0.##}";
                            var copyW2     = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                            ImGui.TextColored(green, $"Win {combinedAmtStr}");
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(statusCellRight - copyW2 + ImGui.GetStyle().ItemSpacing.X);
                            if (ImGui.SmallButton($"Copy##{pi}cpayout"))
                                ImGui.SetClipboardText(copyVal2);
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(shiftHeld2 ? $"Copy with bets: {withBet}" : $"Copy: {combinedNet:0.##}\nShift+Click to copy with bets: {withBet}");
                        }
                        else if (!allHandsWin)
                        {
                            var (label, color) = PayoutDisplay(State, pi, hi);
                            if (label.Length > 0)
                            {
                                var amount  = GameEngine.PayoutAmountString(State, pi, hi);
                                var display = amount.Length > 0 ? $"{label} {amount}" : label;
                                ImGui.TextColored(color, display);
                                var result = GameEngine.GetPayoutResult(State, pi, hi);
                                if (amount.Length > 0 && (result == PayoutResult.Win || result == PayoutResult.BjWin))
                                {
                                    var shiftHeld  = ImGui.GetIO().KeyShift;
                                    var winnings   = amount.TrimStart('+');
                                    var bet        = GameEngine.GetEffectiveBet(p, hand);
                                    var total      = (decimal.TryParse(winnings, out var w) && bet > 0)
                                                     ? $"{w + bet:0.##}" : winnings;
                                    var copyVal    = shiftHeld ? total : winnings;
                                    var copyW      = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
                                    ImGui.SameLine();
                                    ImGui.SetCursorPosX(statusCellRight - copyW + ImGui.GetStyle().ItemSpacing.X);
                                    if (ImGui.SmallButton($"Copy##{pi}_{hi}payout"))
                                        ImGui.SetClipboardText(copyVal);
                                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(shiftHeld ? $"Copy with bet: {total}" : $"Copy: {winnings}\nShift+Click to copy with bet: {total}");
                                }
                            }
                        }
                        // else allHandsWin && !isFirstHand: show per-hand label only (no amount)
                        else
                        {
                            var (label, color) = PayoutDisplay(State, pi, hi);
                            if (label.Length > 0)
                                ImGui.TextColored(color, label);
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
                    ImGui.TableSetColumnIndex(5);
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
                            pendingDouble = (pi, hi);
                            Apply(new AnnounceDouble(pi, hi));
                            if (hasWorld && config.AutoTradeEnabled) Plugin.TradePlayer(p.FullName, p.World);
                        }
                        if (!canDouble) ImGui.EndDisabled();

                        ImGui.SameLine();
                        var canSplit = !hasAnyPending && isActiveHand
                                    && GameEngine.CanSplit(hand);
                        if (!canSplit) ImGui.BeginDisabled();
                        if (ImGui.SmallButton($"Spl##{pi}_{hi}"))
                        {
                            pendingSplit = (pi, hi);
                            Apply(new AnnounceSplit(pi, hi));
                            if (hasWorld && config.AutoTradeEnabled) Plugin.TradePlayer(p.FullName, p.World);
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

        var narPanelOpen = config.NarrationPanelOpen;
        if (ImGui.CollapsingHeader("Chat Narration", ref narPanelOpen, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (narPanelOpen != config.NarrationPanelOpen) { config.NarrationPanelOpen = narPanelOpen; config.Save(); }

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
        else if (narPanelOpen != config.NarrationPanelOpen)
        {
            config.NarrationPanelOpen = narPanelOpen;
            config.Save();
        }
    }
}
