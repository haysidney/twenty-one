using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace TwentyOne.Windows;

public enum HandState { Playing, Stand, Bust, Blackjack }
public enum GamePhase { Betting, Deal, PlayerTurns, DealerTurn, Payout }
public enum BlackjackPayout { ThreeToTwo, SixToFive, EvenMoney }

public class Hand
{
    public List<int> Cards { get; } = [];
    public HandState State { get; set; } = HandState.Playing;
    public string CardInput { get; set; } = string.Empty;
}

public class PlayerRow
{
    public string Name { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public List<Hand> Hands { get; } = [new Hand()];
}

internal enum UndoAction { AddCard, Stand }
internal record struct UndoEntry(UndoAction Action, bool IsDealer, int PlayerIndex, int HandIndex);

public class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly ConfigWindow configWindow;
    private readonly List<PlayerRow> players = [];
    private string newPlayerName = string.Empty;
    private readonly Hand dealerHand = new();
    private int renamingIndex = -1;
    private string renamingBuffer = string.Empty;
    private readonly Stack<UndoEntry> undoStack = new();
    private string? focusNextFrame;

    private GamePhase phase = GamePhase.Betting;
    private int activePlayerIndex = -1;

    // ── Narration ─────────────────────────────────────────────────────────────
    private readonly List<string> narrationLog = [];
    private bool narrationUsePrefix = false;
    private string narrationPrefix = "/p ";
    private bool narrationPanelOpen = true;

    public MainWindow(Configuration config, ConfigWindow configWindow)
        : base("Twenty One##TwentyOneMain")
    {
        this.config = config;
        this.configWindow = configWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        LoadState();
    }

    public void Dispose() { }

    // ── Card / hand helpers ───────────────────────────────────────────────────

    private static string CardLabel(int card) => card switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => card.ToString()
    };

    private static string HandString(List<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in cards)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(CardLabel(c));
        }
        return sb.ToString();
    }

    private static int HandValue(List<int> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var c in cards)
        {
            if (c == 1) { aces++; total += 11; }
            else if (c >= 10) total += 10;
            else total += c;
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    private static bool IsSoft(List<int> cards)
    {
        var high = HandValue(cards);
        var low = 0;
        foreach (var c in cards) low += c == 1 ? 1 : c >= 10 ? 10 : c;
        return low != high;
    }

    private static string ScoreString(List<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var high = HandValue(cards);
        var low = 0;
        foreach (var c in cards) low += c == 1 ? 1 : c >= 10 ? 10 : c;
        return (low != high && high <= 21) ? $"{low}/{high}" : high.ToString();
    }

    private static int ParseCard(string input)
    {
        if (int.TryParse(input.Trim(), out var n) && n >= 1 && n <= 13) return n;
        return 0;
    }

    // ── Hand state ────────────────────────────────────────────────────────────

    private static void UpdateHandState(Hand hand)
    {
        if (hand.State == HandState.Stand) return;
        var val = HandValue(hand.Cards);
        hand.State = val > 21 ? HandState.Bust
            : hand.Cards.Count == 2 && val == 21 ? HandState.Blackjack
            : HandState.Playing;
    }

    private static string DealerRecommendation(Hand hand)
    {
        if (hand.Cards.Count == 0) return string.Empty;
        var val = HandValue(hand.Cards);
        if (val > 21) return string.Empty;
        return (val < 17 || (val == 17 && IsSoft(hand.Cards))) ? "HIT" : "STAND";
    }

    // ── Narration helpers ─────────────────────────────────────────────────────

    private void Narrate(string text)
    {
        narrationLog.Add(text);
        if (config.GameState.ChatEnabled)
            SendChatMessage(config.GameState.ChatChannel + " " + text);
    }

    private static unsafe void SendChatMessage(string message)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null) return;
        using var str = new Utf8String(message);
        uiModule->ProcessChatBoxEntry(&str);
    }

    private void SendHitRoll()
    {
        if (!config.GameState.ChatEnabled) return;
        var channel = config.GameState.ChatChannel;
        var isPublic = channel is "/say" or "/yell" or "/shout";
        var cmd = isPublic ? $"{channel} /random 13" : $"{channel} /dice 13";
        SendChatMessage(cmd);
    }

    private void NarratePlayerAction(int pi, int hi, int card)
    {
        var p = players[pi];
        var hand = p.Hands[hi];
        var name = p.Name;
        var cards = HandString(hand.Cards);
        var score = ScoreString(hand.Cards);
        switch (hand.State)
        {
            case HandState.Bust:
                Narrate($"{name} busts! {cards} = {score}");
                break;
            case HandState.Blackjack:
                Narrate($"{name} — Blackjack! {cards}");
                break;
            default:
                Narrate($"{name} hits → {CardLabel(card)} | {cards} = {score}");
                break;
        }
    }

    private void NarrateDealSummary()
    {
        var sb = new StringBuilder("Deal — ");
        for (var i = 0; i < players.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var p = players[i];
            var hand = p.Hands[0];
            sb.Append(p.Name).Append(": ").Append(HandString(hand.Cards))
              .Append(" (").Append(ScoreString(hand.Cards)).Append(')');
            if (hand.State == HandState.Blackjack) sb.Append(" BJ!");
        }
        sb.Append(" | Dealer shows ").Append(HandString(dealerHand.Cards));
        Narrate(sb.ToString());
    }

    private void NarratePayouts()
    {
        var dealerScore = HandValue(dealerHand.Cards);
        var dealerBust = dealerHand.Cards.Count > 0 && dealerScore > 21;
        var dealerStr = dealerBust ? $"Dealer busts ({dealerScore})" : $"Dealer {ScoreString(dealerHand.Cards)}";
        Narrate(dealerStr);
        foreach (var p in players)
        {
            var (label, _) = GetPayoutDisplay(p);
            if (label.Length == 0) continue;
            var amount = PayoutAmountString(p);
            var bet = string.IsNullOrWhiteSpace(p.Bet) ? string.Empty : $" (bet: {p.Bet})";
            var amountStr = amount.Length > 0 ? $" {amount}" : string.Empty;
            Narrate($"{p.Name}: {label}{bet}{amountStr}");
        }
    }

    // ── Phase state machine ───────────────────────────────────────────────────

    // Advances to the next Playing player; transitions to DealerTurn if none remain.
    private void AdvancePlayerTurn()
    {
        for (var i = activePlayerIndex + 1; i < players.Count; i++)
        {
            if (players[i].Hands[0].State == HandState.Playing)
            {
                activePlayerIndex = i;
                return;
            }
        }
        // No more playing players
        phase = GamePhase.DealerTurn;
        activePlayerIndex = -1;
    }

    private void EnterPlayerTurns()
    {
        phase = GamePhase.PlayerTurns;
        activePlayerIndex = -1;
        AdvancePlayerTurn();
    }

    private void NewRound()
    {
        foreach (var p in players)
        {
            p.Hands.Clear();
            p.Hands.Add(new Hand());
        }
        dealerHand.Cards.Clear();
        dealerHand.State = HandState.Playing;
        dealerHand.CardInput = string.Empty;
        undoStack.Clear();
        phase = GamePhase.Betting;
        activePlayerIndex = -1;
        SaveState();
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void SaveState()
    {
        var gs = config.GameState;
        gs.Players = players.ConvertAll(p => new SavedPlayer
        {
            Name = p.Name,
            Bet  = p.Bet,
            Hands = p.Hands.ConvertAll(h => new SavedHand { Cards = [..h.Cards], State = h.State }),
        });
        gs.DealerHand        = new SavedHand { Cards = [..dealerHand.Cards], State = dealerHand.State };
        gs.Phase             = phase;
        gs.ActivePlayerIndex = activePlayerIndex;
        gs.BjPayout          = config.GameState.BjPayout;
        gs.NarrationLog       = [..narrationLog];
        gs.NarrationUsePrefix = narrationUsePrefix;
        gs.NarrationPrefix    = narrationPrefix;
        gs.NarrationPanelOpen = narrationPanelOpen;
        // ChatEnabled and ChatChannel are config-only settings, not game state
        config.Save();
    }

    private void LoadState()
    {
        var gs = config.GameState;
        players.Clear();
        foreach (var sp in gs.Players)
        {
            var pr = new PlayerRow { Name = sp.Name, Bet = sp.Bet };
            pr.Hands.Clear();
            foreach (var sh in sp.Hands)
            {
                var h = new Hand();
                h.Cards.AddRange(sh.Cards);
                h.State = sh.State;
                pr.Hands.Add(h);
            }
            if (pr.Hands.Count == 0) pr.Hands.Add(new Hand());
            players.Add(pr);
        }
        dealerHand.Cards.Clear();
        dealerHand.Cards.AddRange(gs.DealerHand.Cards);
        dealerHand.State      = gs.DealerHand.State;
        phase                 = gs.Phase;
        activePlayerIndex     = gs.ActivePlayerIndex;
        config.GameState.BjPayout = gs.BjPayout;
        narrationLog.Clear();
        narrationLog.AddRange(gs.NarrationLog);
        narrationUsePrefix    = gs.NarrationUsePrefix;
        narrationPrefix       = gs.NarrationPrefix;
        narrationPanelOpen    = gs.NarrationPanelOpen;
    }

    // ── Payout ────────────────────────────────────────────────────────────────

    private (string Label, Vector4 Color) GetPayoutDisplay(PlayerRow player)
    {
        var hand = player.Hands[0];
        if (hand.Cards.Count == 0) return (string.Empty, default);

        var grey = new Vector4(0.55f, 0.55f, 0.55f, 1f);
        var red  = new Vector4(1f, 0.35f, 0.35f, 1f);
        var green = new Vector4(0.35f, 0.9f, 0.35f, 1f);
        var gold  = new Vector4(1f, 0.85f, 0f, 1f);

        if (hand.State == HandState.Bust) return ("Lose", red);

        var dealerVal   = HandValue(dealerHand.Cards);
        var dealerBust  = dealerHand.Cards.Count > 0 && dealerVal > 21;
        var dealerBJ    = dealerHand.Cards.Count == 2 && dealerVal == 21;
        var playerBJ    = hand.State == HandState.Blackjack;

        if (playerBJ && dealerBJ) return ("Push", grey);
        if (playerBJ)             return ("BJ Win", gold);
        if (dealerBust)           return ("Win", green);

        if (dealerHand.Cards.Count == 0) return (string.Empty, default);

        var pv = HandValue(hand.Cards);
        if (pv > dealerVal) return ("Win", green);
        if (pv < dealerVal) return ("Lose", red);
        return ("Push", grey);
    }

    private decimal BjMultiplier() => config.GameState.BjPayout switch
    {
        BlackjackPayout.SixToFive => 1.2m,
        BlackjackPayout.EvenMoney => 1.0m,
        _                         => 1.5m,
    };

    private static decimal ParseBet(string bet) =>
        decimal.TryParse(bet.Trim(), out var v) && v > 0 ? v : 0;

    // Returns the net amount won/lost as a signed string, or empty if no valid bet.
    private string PayoutAmountString(PlayerRow player)
    {
        var bet = ParseBet(player.Bet);
        if (bet <= 0) return string.Empty;
        var (label, _) = GetPayoutDisplay(player);
        var delta = label switch
        {
            "Win"    => bet,
            "BJ Win" => Math.Round(bet * BjMultiplier(), 2),
            "Lose"   => -bet,
            _        => 0m,
        };
        if (delta == 0) return string.Empty;
        return delta > 0 ? $"+{delta:G}" : $"{delta:G}";
    }

    // ── Mutating actions ──────────────────────────────────────────────────────

    private void AddDealerCard(int card)
    {
        dealerHand.Cards.Add(card);
        UpdateHandState(dealerHand);
        undoStack.Push(new UndoEntry(UndoAction.AddCard, IsDealer: true, -1, -1));

        if (phase == GamePhase.DealerTurn)
        {
            var cards = HandString(dealerHand.Cards);
            var score = ScoreString(dealerHand.Cards);
            var val = HandValue(dealerHand.Cards);
            if (val > 21)
                Narrate($"Dealer draws {CardLabel(card)} → {cards} = {score} — Bust!");
            else if (dealerHand.Cards.Count == 2 && val == 21)
                Narrate($"Dealer draws {CardLabel(card)} → {cards} — Blackjack!");
            else
                Narrate($"Dealer draws {CardLabel(card)} → {cards} = {score}");
        }
        SaveState();
    }

    private void AddPlayerCard(int pi, int hi, int card)
    {
        var hand = players[pi].Hands[hi];
        hand.Cards.Add(card);
        UpdateHandState(hand);
        undoStack.Push(new UndoEntry(UndoAction.AddCard, IsDealer: false, pi, hi));

        if (phase == GamePhase.PlayerTurns)
            NarratePlayerAction(pi, hi, card);

        // Auto-advance when the active player's hand is resolved
        if (phase == GamePhase.PlayerTurns && pi == activePlayerIndex && hand.State != HandState.Playing)
            AdvancePlayerTurn();
        SaveState();
    }

    private void StandPlayer(int pi, int hi)
    {
        var hand = players[pi].Hands[hi];
        if (hand.State != HandState.Playing) return;
        hand.State = HandState.Stand;
        undoStack.Push(new UndoEntry(UndoAction.Stand, IsDealer: false, pi, hi));

        if (phase == GamePhase.PlayerTurns)
        {
            var p = players[pi];
            Narrate($"{p.Name} stands. {HandString(hand.Cards)} = {ScoreString(hand.Cards)}");
        }

        if (phase == GamePhase.PlayerTurns && pi == activePlayerIndex)
            AdvancePlayerTurn();
        SaveState();
    }

    private void Undo()
    {
        if (!undoStack.TryPop(out var entry)) return;
        if (entry.IsDealer)
        {
            if (dealerHand.Cards.Count > 0) dealerHand.Cards.RemoveAt(dealerHand.Cards.Count - 1);
            UpdateHandState(dealerHand);
        }
        else if (entry.Action == UndoAction.Stand)
        {
            players[entry.PlayerIndex].Hands[entry.HandIndex].State = HandState.Playing;
            // Re-activate this player if we're past their turn
            if (phase == GamePhase.DealerTurn || phase == GamePhase.PlayerTurns)
            {
                phase = GamePhase.PlayerTurns;
                activePlayerIndex = entry.PlayerIndex;
            }
        }
        else
        {
            var hand = players[entry.PlayerIndex].Hands[entry.HandIndex];
            if (hand.Cards.Count > 0) hand.Cards.RemoveAt(hand.Cards.Count - 1);
            UpdateHandState(hand);
        }
        SaveState();
    }

    // ── ImGui helpers ─────────────────────────────────────────────────────────

    // Whether card input is active for a given player hand in the current phase.
    private bool PlayerInputActive(int pi, int hi)
    {
        var hand = players[pi].Hands[hi];
        return phase switch
        {
            // During Deal: accept up to 2 cards per player
            GamePhase.Deal        => hand.State == HandState.Playing && hand.Cards.Count < 2,
            // During PlayerTurns: only the active player, only if still Playing
            GamePhase.PlayerTurns => pi == activePlayerIndex && hi == 0 && hand.State == HandState.Playing,
            _ => false
        };
    }

    private int DrawCardEntry(string inputId, Hand hand, bool inputEnabled)
    {
        var handStr = HandString(hand.Cards);
        if (handStr.Length > 0)
        {
            ImGui.Text(handStr);
            ImGui.SameLine();
        }
        if (!inputEnabled) ImGui.BeginDisabled();
        if (focusNextFrame == inputId)
        {
            ImGui.SetKeyboardFocusHere();
            focusNextFrame = null;
        }
        ImGui.SetNextItemWidth(32);
        var buf = hand.CardInput;
        var submitted = ImGui.InputText(inputId, ref buf, 3,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsDecimal);
        hand.CardInput = buf;
        if (!inputEnabled) ImGui.EndDisabled();

        if (submitted && inputEnabled)
        {
            var card = ParseCard(hand.CardInput);
            hand.CardInput = string.Empty;
            if (card > 0)
            {
                focusNextFrame = inputId;
                return card;
            }
        }
        return 0;
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

    private static uint ToU32(Vector4 c) =>
        ((uint)(c.X * 255) & 0xFF) |
        (((uint)(c.Y * 255) & 0xFF) << 8) |
        (((uint)(c.Z * 255) & 0xFF) << 16) |
        (((uint)(c.W * 255) & 0xFF) << 24);

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw()
    {
        if (ImGui.SmallButton("Config"))
            configWindow.Toggle();
        ImGui.Separator();

        // During Deal: dealer gets exactly 1 card; DealerTurn: draws until should stand/bust
        var dealerShouldStop = DealerRecommendation(dealerHand) == "STAND"
                            || HandValue(dealerHand.Cards) > 21;
        var dealerInputActive = (phase == GamePhase.Deal && dealerHand.Cards.Count < 1)
                             || (phase == GamePhase.DealerTurn && !dealerShouldStop);

        // ── Dealer section ────────────────────────────────────────────────────
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        var dealerCard = DrawCardEntry("##dealerCardInput", dealerHand, dealerInputActive);
        if (dealerCard > 0) AddDealerCard(dealerCard);

        if (dealerInputActive && config.GameState.ChatEnabled)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Hit##dealer")) SendHitRoll();
        }

        if (dealerHand.Cards.Count > 0)
        {
            ImGui.SameLine();
            var val = HandValue(dealerHand.Cards);
            var scoreStr = ScoreString(dealerHand.Cards);
            if (val > 21)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), $"= {scoreStr}  BUST");
            else if (val == 21 && dealerHand.Cards.Count == 2)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), $"= {scoreStr}  Blackjack");
            else
            {
                ImGui.Text($"= {scoreStr}");
                var rec = DealerRecommendation(dealerHand);
                if (rec.Length > 0 && phase == GamePhase.DealerTurn)
                {
                    ImGui.SameLine();
                    var rc = rec == "HIT"
                        ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    ImGui.TextColored(rc, $"→ {rec}");
                }
            }
        }

        // ── Player table ──────────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Text("-- Players --");
        ImGui.Separator();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##players"u8, 6, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8,    ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8,     ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Cards"u8,   ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Score"u8,   ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Status"u8,  ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();

            int removeAt = -1;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var hand = p.Hands[0];
                var isActive = phase == GamePhase.PlayerTurns && i == activePlayerIndex;

                ImGui.TableNextRow();

                // Highlight active player row
                if (isActive)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ToU32(new Vector4(0.25f, 0.45f, 0.75f, 0.35f)));

                // Name
                ImGui.TableSetColumnIndex(0);
                if (renamingIndex == i)
                {
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText($"##rename{i}", ref renamingBuffer, 64,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                    {
                        if (renamingBuffer.Length > 0) p.Name = renamingBuffer;
                        renamingIndex = -1;
                        SaveState();
                    }
                    if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        if (renamingBuffer.Length > 0) p.Name = renamingBuffer;
                        renamingIndex = -1;
                        SaveState();
                    }
                }
                else
                {
                    ImGui.Text(p.Name);
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        renamingIndex = i;
                        renamingBuffer = p.Name;
                    }
                }

                // Bet — editable only in Betting phase
                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(-1);
                if (phase != GamePhase.Betting) ImGui.BeginDisabled();
                var bet = p.Bet;
                if (ImGui.InputText($"##bet{i}", ref bet, 16, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    p.Bet = bet;
                    SaveState();
                }
                else if (bet != p.Bet) p.Bet = bet; // keep in sync while typing without saving
                if (phase != GamePhase.Betting) ImGui.EndDisabled();

                // Cards
                ImGui.TableSetColumnIndex(2);
                var cardInputActive = PlayerInputActive(i, 0);
                var addedCard = DrawCardEntry($"##card{i}", hand, cardInputActive);
                if (addedCard > 0) AddPlayerCard(i, 0, addedCard);

                // Score — stood hands with a soft value show the high number only
                ImGui.TableSetColumnIndex(3);
                if (hand.Cards.Count > 0)
                {
                    var val = HandValue(hand.Cards);
                    var scoreStr = hand.State == HandState.Stand
                        ? val.ToString()
                        : ScoreString(hand.Cards);
                    if (val > 21)
                        ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), scoreStr);
                    else if (val == 21)
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), scoreStr);
                    else
                        ImGui.Text(scoreStr);
                }

                // Status: payout result in Payout phase, hand state otherwise
                ImGui.TableSetColumnIndex(4);
                if (phase == GamePhase.Payout)
                {
                    var (label, color) = GetPayoutDisplay(p);
                    if (label.Length > 0)
                    {
                        var amount = PayoutAmountString(p);
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
                var canRemove = phase == GamePhase.Betting;
                if (!canRemove) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"X##{i}")) removeAt = i;
                if (!canRemove) ImGui.EndDisabled();

                ImGui.SameLine();

                if (ImGui.SmallButton($"R##{i}"))
                {
                    renamingIndex = i;
                    renamingBuffer = p.Name;
                }

                ImGui.SameLine();

                var canStand = phase == GamePhase.PlayerTurns
                    && i == activePlayerIndex
                    && hand.State == HandState.Playing;
                if (!canStand) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"S##{i}")) StandPlayer(i, 0);
                if (!canStand) ImGui.EndDisabled();

                if (config.GameState.ChatEnabled)
                {
                    ImGui.SameLine();
                    var canHit = canStand;
                    if (!canHit) ImGui.BeginDisabled();
                    if (ImGui.SmallButton($"H##{i}")) SendHitRoll();
                    if (!canHit) ImGui.EndDisabled();
                }
            }

            if (removeAt >= 0) { players.RemoveAt(removeAt); SaveState(); }
            ImGui.EndTable();
        }

        // ── Add player (Betting only) ──────────────────────────────────────────
        ImGui.Spacing();
        if (phase == GamePhase.Betting)
        {
            ImGui.SetNextItemWidth(200);
            var nameSubmitted = ImGui.InputText("##newName"u8, ref newPlayerName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            var canAdd = newPlayerName.Length > 0;
            if (!canAdd) ImGui.BeginDisabled();
            if (ImGui.Button("Add Player") || (nameSubmitted && canAdd))
            {
                players.Add(new PlayerRow { Name = newPlayerName });
                newPlayerName = string.Empty;
                focusNextFrame = "##newName";
                SaveState();
            }
            if (!canAdd) ImGui.EndDisabled();
            ImGui.Spacing();
        }

        // ── Phase action bar ──────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.Spacing();

        // Phase label
        var dealProgress = phase == GamePhase.Deal
            ? $"  (dealer: {dealerHand.Cards.Count}/1  players: {(players.Count > 0 ? players.Min(p => p.Hands[0].Cards.Count) : 0)}-{(players.Count > 0 ? players.Max(p => p.Hands[0].Cards.Count) : 0)}/2)"
            : string.Empty;
        var phaseLabel = phase switch
        {
            GamePhase.Betting     => "Phase: Betting",
            GamePhase.Deal        => $"Phase: Deal{dealProgress}",
            GamePhase.PlayerTurns => activePlayerIndex >= 0 && activePlayerIndex < players.Count
                ? $"Phase: Player Actions  ({players[activePlayerIndex].Name}'s turn — Hit, Stand, Double, Split)"
                : "Phase: Player Actions",
            GamePhase.DealerTurn  => "Phase: Dealer Turn",
            GamePhase.Payout      => "Phase: Payout",
            _                     => string.Empty
        };
        ImGui.TextDisabled(phaseLabel);
        ImGui.Spacing();

        switch (phase)
        {
            case GamePhase.Betting:
                var canDeal = players.Count > 0 && players.All(p => !string.IsNullOrWhiteSpace(p.Bet));
                if (!canDeal) ImGui.BeginDisabled();
                if (ImGui.Button("Start Deal →"))
                {
                    phase = GamePhase.Deal;
                    SaveState();
                }
                if (!canDeal) ImGui.EndDisabled();
                break;

            case GamePhase.Deal:
                var dealDone = dealerHand.Cards.Count >= 1
                    && players.Count > 0
                    && players.TrueForAll(p => p.Hands[0].Cards.Count >= 2);
                if (!dealDone) ImGui.BeginDisabled();
                if (ImGui.Button("Begin Player Turns →"))
                {
                    NarrateDealSummary();
                    EnterPlayerTurns();
                    SaveState();
                }
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
                var allBJ = players.Count > 0 && players.TrueForAll(p => p.Hands[0].State == HandState.Blackjack);
                var canPayout = allBJ || (dealerShouldStop && dealerHand.Cards.Count > 0);
                if (!canPayout) ImGui.BeginDisabled();
                if (ImGui.Button("Go to Payout →"))
                {
                    NarratePayouts();
                    phase = GamePhase.Payout;
                    SaveState();
                }
                if (!canPayout) ImGui.EndDisabled();
                if (!canPayout && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Dealer must finish their hand first."u8);
                break;

            case GamePhase.Payout:
                if (ImGui.Button("New Round"))
                    NewRound();
                break;
        }

        if (phase != GamePhase.Payout)
        {
            ImGui.SameLine();
            var canUndo = undoStack.Count > 0;
            if (!canUndo) ImGui.BeginDisabled();
            if (ImGui.Button("Undo")) Undo();
            if (!canUndo) ImGui.EndDisabled();

            if (phase != GamePhase.Betting)
            {
                ImGui.SameLine();
                if (ImGui.Button("Abort Round"))
                    NewRound();
            }
        }

        // ── Narration panel ───────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Chat Narration", ref narrationPanelOpen, ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Prefix controls
            ImGui.Checkbox("Add prefix", ref narrationUsePrefix);
            if (narrationUsePrefix)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.InputText("##narPrefix", ref narrationPrefix, 32);
            }

            // Copy All / Clear
            ImGui.SameLine();
            if (narrationLog.Count == 0) ImGui.BeginDisabled();
            if (ImGui.Button("Copy All"))
            {
                var sb = new StringBuilder();
                foreach (var line in narrationLog)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(narrationUsePrefix ? narrationPrefix + line : line);
                }
                ImGui.SetClipboardText(sb.ToString());
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear")) { narrationLog.Clear(); SaveState(); }
            if (narrationLog.Count == 0) ImGui.EndDisabled();

            // Scrollable log — fills remaining window height
            ImGui.Spacing();
            if (ImGui.BeginChild("##narLog", new Vector2(0, 0), true))
            {
                for (var ni = narrationLog.Count - 1; ni >= 0; ni--)
                {
                    var line = narrationLog[ni];
                    var display = narrationUsePrefix ? narrationPrefix + line : line;
                    ImGui.PushID(ni);
                    if (ImGui.SmallButton("C"))
                        ImGui.SetClipboardText(display);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Copy to clipboard"u8);
                    ImGui.PopID();
                    ImGui.SameLine();
                    ImGui.TextUnformatted(display);
                }
            }
            ImGui.EndChild();
        }
    }
}
