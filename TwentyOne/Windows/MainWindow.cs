using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public enum HandState { Playing, Stand, Bust, Blackjack }
public enum GamePhase { Betting, Deal, PlayerTurns, DealerTurn, Payout }

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
    private readonly List<PlayerRow> players = [];
    private string newPlayerName = string.Empty;
    private readonly Hand dealerHand = new();
    private int renamingIndex = -1;
    private string renamingBuffer = string.Empty;
    private readonly Stack<UndoEntry> undoStack = new();
    private string? focusNextFrame;

    private GamePhase phase = GamePhase.Betting;
    private int activePlayerIndex = -1;

    public MainWindow()
        : base("Twenty One##TwentyOneMain")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
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

    // ── Mutating actions ──────────────────────────────────────────────────────

    private void AddDealerCard(int card)
    {
        dealerHand.Cards.Add(card);
        UpdateHandState(dealerHand);
        undoStack.Push(new UndoEntry(UndoAction.AddCard, IsDealer: true, -1, -1));
    }

    private void AddPlayerCard(int pi, int hi, int card)
    {
        var hand = players[pi].Hands[hi];
        hand.Cards.Add(card);
        UpdateHandState(hand);
        undoStack.Push(new UndoEntry(UndoAction.AddCard, IsDealer: false, pi, hi));

        // Auto-advance when the active player's hand is resolved
        if (phase == GamePhase.PlayerTurns && pi == activePlayerIndex && hand.State != HandState.Playing)
            AdvancePlayerTurn();
    }

    private void StandPlayer(int pi, int hi)
    {
        var hand = players[pi].Hands[hi];
        if (hand.State != HandState.Playing) return;
        hand.State = HandState.Stand;
        undoStack.Push(new UndoEntry(UndoAction.Stand, IsDealer: false, pi, hi));

        if (phase == GamePhase.PlayerTurns && pi == activePlayerIndex)
            AdvancePlayerTurn();
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
    }

    // ── ImGui helpers ─────────────────────────────────────────────────────────

    // Whether card input is active for a given player hand in the current phase.
    private bool PlayerInputActive(int pi, int hi)
    {
        return phase switch
        {
            GamePhase.Deal => players[pi].Hands[hi].State == HandState.Playing,
            GamePhase.PlayerTurns => pi == activePlayerIndex && hi == 0 && players[pi].Hands[0].State == HandState.Playing,
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
        var dealerInputActive = phase == GamePhase.Deal || phase == GamePhase.DealerTurn;

        // ── Dealer section ────────────────────────────────────────────────────
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        var dealerCard = DrawCardEntry("##dealerCardInput", dealerHand, dealerInputActive);
        if (dealerCard > 0) AddDealerCard(dealerCard);

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
            ImGui.TableSetupColumn("Status"u8,  ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 55);
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
                    }
                    if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        renamingIndex = -1;
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
                if (ImGui.InputText($"##bet{i}", ref bet, 16)) p.Bet = bet;
                if (phase != GamePhase.Betting) ImGui.EndDisabled();

                // Cards
                ImGui.TableSetColumnIndex(2);
                var cardInputActive = PlayerInputActive(i, 0);
                var addedCard = DrawCardEntry($"##card{i}", hand, cardInputActive);
                if (addedCard > 0) AddPlayerCard(i, 0, addedCard);

                // Score
                ImGui.TableSetColumnIndex(3);
                if (hand.Cards.Count > 0)
                {
                    var val = HandValue(hand.Cards);
                    var scoreStr = ScoreString(hand.Cards);
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
                    if (label.Length > 0) ImGui.TextColored(color, label);
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

                var canStand = phase is GamePhase.Deal or GamePhase.PlayerTurns
                    && (phase != GamePhase.PlayerTurns || i == activePlayerIndex)
                    && hand.State == HandState.Playing;
                if (!canStand) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"S##{i}")) StandPlayer(i, 0);
                if (!canStand) ImGui.EndDisabled();
            }

            if (removeAt >= 0) players.RemoveAt(removeAt);
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
            }
            if (!canAdd) ImGui.EndDisabled();
            ImGui.Spacing();
        }

        // ── Phase action bar ──────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.Spacing();

        // Phase label
        var phaseLabel = phase switch
        {
            GamePhase.Betting     => "Phase: Betting",
            GamePhase.Deal        => "Phase: Deal",
            GamePhase.PlayerTurns => activePlayerIndex >= 0 && activePlayerIndex < players.Count
                ? $"Phase: Player Turns  ({players[activePlayerIndex].Name}'s turn)"
                : "Phase: Player Turns",
            GamePhase.DealerTurn  => "Phase: Dealer Turn",
            GamePhase.Payout      => "Phase: Payout",
            _                     => string.Empty
        };
        ImGui.TextDisabled(phaseLabel);
        ImGui.Spacing();

        switch (phase)
        {
            case GamePhase.Betting:
                var canDeal = players.Count > 0;
                if (!canDeal) ImGui.BeginDisabled();
                if (ImGui.Button("Start Deal →"))
                    phase = GamePhase.Deal;
                if (!canDeal) ImGui.EndDisabled();
                break;

            case GamePhase.Deal:
                if (ImGui.Button("Begin Play →"))
                    EnterPlayerTurns();
                break;

            case GamePhase.PlayerTurns:
                if (ImGui.Button("Skip Turn →"))
                    AdvancePlayerTurn();
                break;

            case GamePhase.DealerTurn:
                if (ImGui.Button("Go to Payout →"))
                    phase = GamePhase.Payout;
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
    }
}
