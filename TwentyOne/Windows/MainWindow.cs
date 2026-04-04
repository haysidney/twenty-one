using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public enum HandState { Playing, Stand, Bust, Blackjack }

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
    // Index 0 is the primary hand; future hands are splits
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

    public MainWindow()
        : base("Twenty One##TwentyOneMain")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    // ── Card helpers ──────────────────────────────────────────────────────────

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

    // Best blackjack value: aces start at 11, reduced to 1 to avoid bust
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

    // True when an ace is still counting as 11 (soft hand)
    private static bool IsSoft(List<int> cards)
    {
        var high = HandValue(cards);
        var low = 0;
        foreach (var c in cards) low += c == 1 ? 1 : c >= 10 ? 10 : c;
        return low != high;
    }

    // Score display: "3/13" when soft, single value otherwise
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

    // ── Hand state classification ─────────────────────────────────────────────

    // Re-evaluates state after a card is added. Never clears a Stand.
    private static void UpdateHandState(Hand hand)
    {
        if (hand.State == HandState.Stand) return;
        var val = HandValue(hand.Cards);
        hand.State = val > 21 ? HandState.Bust
            : hand.Cards.Count == 2 && val == 21 ? HandState.Blackjack
            : HandState.Playing;
    }

    // Dealer must hit soft 17 or below; stand on hard 17+
    private static string DealerRecommendation(Hand hand)
    {
        if (hand.Cards.Count == 0) return string.Empty;
        var val = HandValue(hand.Cards);
        if (val > 21) return string.Empty;
        return (val < 17 || (val == 17 && IsSoft(hand.Cards))) ? "HIT" : "STAND";
    }

    // ── Undo / mutating actions ───────────────────────────────────────────────

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
    }

    private void StandPlayer(int pi, int hi)
    {
        var hand = players[pi].Hands[hi];
        if (hand.State != HandState.Playing) return;
        hand.State = HandState.Stand;
        undoStack.Push(new UndoEntry(UndoAction.Stand, IsDealer: false, pi, hi));
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
        }
        else
        {
            var hand = players[entry.PlayerIndex].Hands[entry.HandIndex];
            if (hand.Cards.Count > 0) hand.Cards.RemoveAt(hand.Cards.Count - 1);
            UpdateHandState(hand);
        }
    }

    // ── ImGui helpers ─────────────────────────────────────────────────────────

    private int DrawCardEntry(string inputId, Hand hand)
    {
        var handStr = HandString(hand.Cards);
        if (handStr.Length > 0)
        {
            ImGui.Text(handStr);
            ImGui.SameLine();
        }
        var active = hand.State == HandState.Playing;
        if (!active) ImGui.BeginDisabled();
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
        if (!active) ImGui.EndDisabled();

        if (submitted && active)
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
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Bust");
                break;
            case HandState.Blackjack:
                ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), "Blackjack");
                break;
            case HandState.Stand:
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Stand");
                break;
            case HandState.Playing:
                if (hand.Cards.Count > 0)
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Playing");
                break;
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw()
    {
        // Dealer section
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        var dealerCard = DrawCardEntry("##dealerCardInput", dealerHand);
        if (dealerCard > 0) AddDealerCard(dealerCard);

        if (dealerHand.Cards.Count > 0)
        {
            ImGui.SameLine();
            var val = HandValue(dealerHand.Cards);
            var scoreStr = ScoreString(dealerHand.Cards);
            if (val > 21)
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), $"= {scoreStr}  BUST");
            else if (val == 21 && dealerHand.Cards.Count == 2)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), $"= {scoreStr}  Blackjack");
            else
            {
                ImGui.Text($"= {scoreStr}");
                var rec = DealerRecommendation(dealerHand);
                if (rec.Length > 0)
                {
                    ImGui.SameLine();
                    var color = rec == "HIT"
                        ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    ImGui.TextColored(color, $"→ {rec}");
                }
            }
        }

        ImGui.Spacing();
        ImGui.Text("-- Players --");
        ImGui.Separator();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##players"u8, 6, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8, ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Cards"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Score"u8, ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Status"u8, ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableHeadersRow();

            int removeAt = -1;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var hand = p.Hands[0]; // primary hand; splits will add rows later
                ImGui.TableNextRow();

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

                // Bet
                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(-1);
                var bet = p.Bet;
                if (ImGui.InputText($"##bet{i}", ref bet, 16)) p.Bet = bet;

                // Cards: history label + small input
                ImGui.TableSetColumnIndex(2);
                var addedCard = DrawCardEntry($"##card{i}", hand);
                if (addedCard > 0) AddPlayerCard(i, 0, addedCard);

                // Score (auto)
                ImGui.TableSetColumnIndex(3);
                if (hand.Cards.Count > 0)
                {
                    var val = HandValue(hand.Cards);
                    var scoreStr = ScoreString(hand.Cards);
                    if (val > 21)
                        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), scoreStr);
                    else if (val == 21)
                        ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), scoreStr);
                    else
                        ImGui.Text(scoreStr);
                }

                // Status label
                ImGui.TableSetColumnIndex(4);
                DrawHandStateLabel(hand);

                // Actions: X (remove), R (rename), S (stand)
                ImGui.TableSetColumnIndex(5);
                if (ImGui.SmallButton($"X##{i}")) removeAt = i;
                ImGui.SameLine();
                if (ImGui.SmallButton($"R##{i}"))
                {
                    renamingIndex = i;
                    renamingBuffer = p.Name;
                }
                ImGui.SameLine();
                if (hand.State != HandState.Playing) ImGui.BeginDisabled();
                if (ImGui.SmallButton($"S##{i}")) StandPlayer(i, 0);
                if (hand.State != HandState.Playing) ImGui.EndDisabled();
            }

            if (removeAt >= 0) players.RemoveAt(removeAt);

            ImGui.EndTable();
        }

        // Add player row
        ImGui.Spacing();
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
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("New Round"))
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
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clears cards and statuses. Player names and bets are kept."u8);

        ImGui.SameLine();

        var canUndo = undoStack.Count > 0;
        if (!canUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) Undo();
        if (!canUndo) ImGui.EndDisabled();
    }
}
