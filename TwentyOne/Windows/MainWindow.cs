using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly ConfigWindow  configWindow;
    private readonly IChatGui      chatGui;
    private readonly IObjectTable  objectTable;

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
    // auto-deal queue: populated by StartDeal; QueueHitRoll is called one at a time as rolls resolve
    // IsFirstCard=true → emit AnnouncePlayerDeal before rolling
    private readonly Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> autoDealQueue = new();

    // rate-limited outgoing queue — narration strings and roll commands share a single FIFO and lastChatSent
    // each entry: (IsRoll, Invoke) — narration passes through freely; rolls block until pendingHit is clear
    private readonly Queue<(bool IsRoll, Action Invoke)> chatQueue = new();
    private          DateTime                             lastChatSent = DateTime.MinValue;

    // ── Convenience accessors ─────────────────────────────────────────────────

    private GameState   State             => config.GameState;
    private GamePhase   Phase             => config.GameState.Phase;
    private int         ActivePlayerIndex => config.GameState.ActivePlayerIndex;

    // ── Constructor / Dispose ─────────────────────────────────────────────────

    public MainWindow(Configuration config, ConfigWindow configWindow,
                      IChatGui chatGui, IObjectTable objectTable)
        : base("Twenty One##TwentyOneMain")
    {
        this.config       = config;
        this.configWindow = configWindow;
        this.chatGui      = chatGui;
        this.objectTable  = objectTable;
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

    // ── Apply / Undo ──────────────────────────────────────────────────────────

    private void Apply(GameAction action)
    {
        if (action is NewRound)
        {
            config.UndoStack.Clear();
            autoDealQueue.Clear();
            pendingHit = null;
        }
        else
        {
            // GameEngine is pure — it never mutates state, so pushing the current
            // reference is safe; future Apply calls create entirely new objects.
            config.UndoStack.Add(config.GameState);
        }
        config.RedoStack.Clear();

        var (newState, effects) = GameEngine.Apply(config.GameState, action, config.NarrationTemplates);
        config.GameState = newState;

        foreach (var effect in effects)
        {
            if (effect is SendChat chat)
            {
                config.NarrationLog.Add(chat.Text);
                if (config.ChatEnabled)
                {
                    var msg = config.ChatChannel + " " + chat.Text;
                    chatQueue.Enqueue((false, () => SendChatMessage(msg)));
                }
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
        var who  = isDealer ? "Dealer" : State.Players[playerIndex].Name;
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
        chatQueue.Enqueue((true, () => SendHitRoll(isDealer, playerIndex, handIndex)));
    }

    private unsafe void SendHitRoll(bool isDealer, int playerIndex, int handIndex)
    {
        var channel  = config.ChatChannel;
        var isPublic = channel is "/say" or "/yell" or "/shout";

        var shell = RaptureShellModule.Instance();
        if (shell == null) return;

        pendingHit = (isDealer, playerIndex, handIndex, isPublic);
        var savedChatType = shell->ChatType;
        SendChatMessage(channel);
        SendChatMessage(isPublic ? "/random 13" : "/dice 13");
        shell->ChangeChatChannel(savedChatType, 0, null, true);
    }

    // "You roll a [icon] 7 (out of 13)." — response to /random in public channels
    [GeneratedRegex(@"You roll a\D+(\d+) \(out of 13\)")]
    private static partial Regex RandomRollRegex();

    // "Random! (1-13)[icon] 7" — response to /dice in private channels
    [GeneratedRegex(@"Random! \(1-13\)\D+(\d+)")]
    private static partial Regex DiceRollRegex();

    private void OnChatMessage(XivChatType type, int timestamp,
        ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (pendingHit == null) return;

        var (isDealer, pi, hi, isPublic) = pendingHit.Value;

        if (!isPublic && sender.TextValue != objectTable.LocalPlayer?.Name.TextValue) return;

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
            GamePhase.PlayerTurns => pi == ActivePlayerIndex && hi == 0 && hand.State == HandState.Playing,
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

    private static (string Label, Vector4 Color) PayoutDisplay(GameState state, int playerIndex)
    {
        var grey  = new Vector4(0.55f, 0.55f, 0.55f, 1f);
        var red   = new Vector4(1f, 0.35f, 0.35f, 1f);
        var green = new Vector4(0.35f, 0.9f, 0.35f, 1f);
        var gold  = new Vector4(1f, 0.85f, 0f, 1f);

        return GameEngine.GetPayoutResult(state, playerIndex) switch
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
        if (chatQueue.Count > 0 && (DateTime.UtcNow - lastChatSent).TotalMilliseconds >= 2000)
        {
            var (isRoll, invoke) = chatQueue.Peek();
            if (!isRoll || pendingHit == null)
            {
                chatQueue.Dequeue();
                invoke();
                lastChatSent = DateTime.UtcNow;
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

        ImGui.Separator();

        var dealerShouldStop = GameEngine.DealerRecommendation(State.DealerHand) == "STAND"
                            || GameEngine.HandValue(State.DealerHand.Cards) > 21;
        var dealerHitActive  = (Phase == GamePhase.Deal && State.DealerHand.Cards.Count < 1)
                            || (Phase == GamePhase.DealerTurn && !dealerShouldStop);

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
            if (ImGui.SmallButton("Hit##dealer")) QueueHitRoll(isDealer: true, -1, -1);
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
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableHeadersRow();

            int removeAt = -1;
            for (var i = 0; i < State.Players.Count; i++)
            {
                var p      = State.Players[i];
                var hand   = p.Hands[0];
                var isActive = Phase == GamePhase.PlayerTurns && i == ActivePlayerIndex;

                ImGui.TableNextRow();
                if (isActive)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1,
                        ToU32(new Vector4(0.25f, 0.45f, 0.75f, 0.35f)));

                // Name
                ImGui.TableSetColumnIndex(0);
                if (renamingIndex == i)
                {
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText($"##rename{i}", ref renamingBuffer, 64,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                    {
                        if (renamingBuffer.Length > 0) Apply(new RenamePlayer(i, renamingBuffer));
                        renamingIndex = -1;
                    }
                    if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        if (renamingBuffer.Length > 0) Apply(new RenamePlayer(i, renamingBuffer));
                        renamingIndex = -1;
                    }
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(p.Name);
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        renamingIndex  = i;
                        renamingBuffer = p.Name;
                    }
                    var renameW = ImGui.CalcTextSize("Rename").X + ImGui.GetStyle().FramePadding.X * 2;
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - renameW
                                   - ImGui.GetScrollX() - ImGui.GetStyle().ItemSpacing.X * 0.5f);
                    if (ImGui.SmallButton($"Rename##{i}rename"))
                    {
                        renamingIndex  = i;
                        renamingBuffer = p.Name;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rename"u8);
                }

                // Bet — editable only in Betting phase; buffer in betEdits to avoid per-keystroke Apply
                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(-1);
                if (Phase != GamePhase.Betting) ImGui.BeginDisabled();
                var bet = betEdits.TryGetValue(i, out var e) ? e : p.Bet;
                if (ImGui.InputText($"##bet{i}", ref bet, 16, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    betEdits.Remove(i);
                    Apply(new SetPlayerBet(i, bet));
                }
                else
                {
                    betEdits[i] = bet; // track in-progress, don't push to undo stack
                }
                if (Phase != GamePhase.Betting) ImGui.EndDisabled();

                // Cards
                ImGui.TableSetColumnIndex(2);
                if (hand.Cards.Count > 0)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(GameEngine.HandString(hand.Cards));
                }

                // Score
                ImGui.TableSetColumnIndex(3);
                if (hand.Cards.Count > 0)
                {
                    var val = GameEngine.HandValue(hand.Cards);
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

                // Status
                ImGui.TableSetColumnIndex(4);
                if (Phase == GamePhase.Payout)
                {
                    var (label, color) = PayoutDisplay(State, i);
                    if (label.Length > 0)
                    {
                        var amount  = GameEngine.PayoutAmountString(State, i);
                        var display = amount.Length > 0 ? $"{label} {amount}" : label;
                        ImGui.TextColored(color, display);
                    }
                }
                else
                {
                    DrawHandStateLabel(hand);
                }

                // Actions
                ImGui.TableSetColumnIndex(5);
                var canStand = Phase == GamePhase.PlayerTurns
                            && i == ActivePlayerIndex
                            && hand.State == HandState.Playing;
                if (!canStand) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"Stand##{i}")) Apply(new StandPlayer(i, 0));
                if (!canStand) ImGui.EndDisabled();

                ImGui.SameLine();
                var hitActive = PlayerHitActive(i, 0);
                if (!hitActive) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"Hit##{i}")) QueueHitRoll(isDealer: false, i, 0);
                if (!hitActive) ImGui.EndDisabled();

                ImGui.SameLine();
                var canRemove = Phase == GamePhase.Betting;
                if (!canRemove) ImGui.BeginDisabled();
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.7f, 0.15f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.25f, 0.25f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.5f, 0.05f, 0.05f, 1f));
                if (ImGui.SmallButton($"X##{i}")) removeAt = i;
                ImGui.PopStyleColor(3);
                if (!canRemove) ImGui.EndDisabled();
            }

            if (removeAt >= 0) { betEdits.Remove(removeAt); Apply(new RemovePlayer(removeAt)); }
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
                Apply(new AddPlayer(newPlayerName));
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
        var phaseLabel = Phase switch
        {
            GamePhase.Betting     => "Phase: Betting",
            GamePhase.Deal        => $"Phase: Deal{dealProgress}",
            GamePhase.PlayerTurns => ActivePlayerIndex >= 0 && ActivePlayerIndex < State.Players.Count
                ? $"Phase: Player Actions  ({State.Players[ActivePlayerIndex].Name}'s turn — Hit, Stand, Double, Split)"
                : "Phase: Player Actions",
            GamePhase.DealerTurn  => "Phase: Dealer Turn",
            GamePhase.Payout      => "Phase: Payout",
            _                     => string.Empty,
        };
        ImGui.TextDisabled(phaseLabel);
        ImGui.Spacing();

        switch (Phase)
        {
            case GamePhase.Betting:
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
                var dealDone = State.DealerHand.Cards.Count >= 1
                            && State.Players.Count > 0
                            && State.Players.TrueForAll(p => p.Hands[0].Cards.Count >= 2);
                if (!dealDone) ImGui.BeginDisabled();
                if (ImGui.Button("Begin Player Turns →")) Apply(new BeginPlayerTurns());
                if (!dealDone) ImGui.EndDisabled();
                if (!dealDone && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Dealer needs 1 card; each player needs 2 cards."u8);
                break;

            case GamePhase.PlayerTurns:
                ImGui.BeginDisabled();
                ImGui.Button("Go to Payout →");
                ImGui.EndDisabled();
                break;

            case GamePhase.DealerTurn:
                var canPayout = GameEngine.CanGoToPayout(State);
                if (!canPayout) ImGui.BeginDisabled();
                if (ImGui.Button("Go to Payout →")) Apply(new GoToPayout());
                if (!canPayout) ImGui.EndDisabled();
                if (!canPayout && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Dealer must finish their hand first."u8);
                break;

            case GamePhase.Payout:
                if (ImGui.Button("New Round")) Apply(new NewRound());
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
                Apply(new NewRound());
            }
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
                ImGui.SetTooltip("Hold Ctrl to abort the round."u8);
        }

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
