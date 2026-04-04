using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public class PlayerRow
{
    public string Name { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public List<int> Cards { get; } = [];
    public string Status { get; set; } = string.Empty;
    public string CardInput { get; set; } = string.Empty;
}

internal record struct UndoEntry(bool IsDealer, int PlayerIndex);

public class MainWindow : Window, IDisposable
{
    private readonly List<PlayerRow> players = [];
    private string newPlayerName = string.Empty;
    private readonly List<int> dealerCards = [];
    private string dealerCardInput = string.Empty;
    private int renamingIndex = -1;
    private string renamingBuffer = string.Empty;
    private readonly Stack<UndoEntry> undoStack = new();
    // Input ID that should be focused on the next frame
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

    // Returns the best blackjack value (aces as 11, reduced to avoid bust)
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

    // Returns score display string. Shows "low/high" when both are valid (e.g. "3/13").
    private static string ScoreString(List<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var high = HandValue(cards);
        // low = treat every ace as 1
        var low = 0;
        foreach (var c in cards)
            low += c == 1 ? 1 : c >= 10 ? 10 : c;
        if (low != high && high <= 21)
            return $"{low}/{high}";
        return high.ToString();
    }

    private static int ParseCard(string input)
    {
        if (int.TryParse(input.Trim(), out var n) && n >= 1 && n <= 13)
            return n;
        return 0;
    }

    private void AddDealerCard(int card)
    {
        dealerCards.Add(card);
        undoStack.Push(new UndoEntry(IsDealer: true, PlayerIndex: -1));
    }

    private void AddPlayerCard(int playerIndex, int card)
    {
        players[playerIndex].Cards.Add(card);
        undoStack.Push(new UndoEntry(IsDealer: false, PlayerIndex: playerIndex));
    }

    private void Undo()
    {
        if (!undoStack.TryPop(out var entry)) return;
        if (entry.IsDealer)
        {
            if (dealerCards.Count > 0) dealerCards.RemoveAt(dealerCards.Count - 1);
        }
        else
        {
            var idx = entry.PlayerIndex;
            if (idx >= 0 && idx < players.Count && players[idx].Cards.Count > 0)
                players[idx].Cards.RemoveAt(players[idx].Cards.Count - 1);
        }
    }

    // Draws hand history label + small card input. Returns added card (0 = none).
    private int DrawCardEntry(string inputId, string handStr, ref string inputBuf)
    {
        if (handStr.Length > 0)
        {
            ImGui.Text(handStr);
            ImGui.SameLine();
        }
        if (focusNextFrame == inputId)
        {
            ImGui.SetKeyboardFocusHere();
            focusNextFrame = null;
        }
        ImGui.SetNextItemWidth(32);
        var submitted = ImGui.InputText(inputId, ref inputBuf, 3,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsDecimal);
        if (submitted)
        {
            var card = ParseCard(inputBuf);
            inputBuf = string.Empty;
            if (card > 0)
            {
                focusNextFrame = inputId;
                return card;
            }
        }
        return 0;
    }

    public override void Draw()
    {
        // Dealer section
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        var dealerHandStr = HandString(dealerCards);
        var dealerScoreStr = ScoreString(dealerCards);
        var dealerValue = HandValue(dealerCards);

        var dealerCard = DrawCardEntry("##dealerCardInput", dealerHandStr, ref dealerCardInput);
        if (dealerCard > 0) AddDealerCard(dealerCard);

        if (dealerScoreStr.Length > 0)
        {
            ImGui.SameLine();
            if (dealerValue > 21)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"= {dealerScoreStr} BUST");
            }
            else if (dealerValue == 21)
            {
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), $"= {dealerScoreStr} 21!");
            }
            else
            {
                ImGui.Text($"= {dealerScoreStr}");
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
            ImGui.TableSetupColumn("Status"u8, ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableHeadersRow();

            int removeAt = -1;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                ImGui.TableNextRow();

                // Name
                ImGui.TableSetColumnIndex(0);
                if (renamingIndex == i)
                {
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText($"##rename{i}", ref renamingBuffer, 64,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                    {
                        if (renamingBuffer.Length > 0)
                            p.Name = renamingBuffer;
                        renamingIndex = -1;
                    }
                    if (!ImGui.IsItemActive() && renamingIndex == i && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
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
                var handStr = HandString(p.Cards);
                var cardInput = p.CardInput;
                var addedCard = DrawCardEntry($"##card{i}", handStr, ref cardInput);
                p.CardInput = cardInput;
                if (addedCard > 0) AddPlayerCard(i, addedCard);

                // Score (auto)
                ImGui.TableSetColumnIndex(3);
                if (p.Cards.Count > 0)
                {
                    var scoreStr = ScoreString(p.Cards);
                    var val = HandValue(p.Cards);
                    if (val > 21)
                        ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), scoreStr);
                    else if (val == 21)
                        ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), "21!");
                    else
                        ImGui.Text(scoreStr);
                }

                // Status
                ImGui.TableSetColumnIndex(4);
                ImGui.SetNextItemWidth(-1);
                var status = p.Status;
                if (ImGui.InputText($"##status{i}", ref status, 16)) p.Status = status;

                // Actions
                ImGui.TableSetColumnIndex(5);
                if (ImGui.SmallButton($"X##{i}"))
                    removeAt = i;
                ImGui.SameLine();
                if (ImGui.SmallButton($"R##{i}"))
                {
                    renamingIndex = i;
                    renamingBuffer = p.Name;
                }
            }

            if (removeAt >= 0)
                players.RemoveAt(removeAt);

            ImGui.EndTable();
        }

        // Add player row
        ImGui.Spacing();
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##newName"u8, ref newPlayerName, 64);
        ImGui.SameLine();
        var canAdd = newPlayerName.Length > 0;
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button("Add Player"))
        {
            players.Add(new PlayerRow { Name = newPlayerName });
            newPlayerName = string.Empty;
        }
        if (!canAdd) ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("New Round"))
        {
            foreach (var p in players)
            {
                p.Bet = string.Empty;
                p.Cards.Clear();
                p.Status = string.Empty;
                p.CardInput = string.Empty;
            }
            dealerCards.Clear();
            dealerCardInput = string.Empty;
            undoStack.Clear();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clears bets, cards, scores, and statuses. Player names are kept."u8);

        ImGui.SameLine();

        var canUndo = undoStack.Count > 0;
        if (!canUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo"))
            Undo();
        if (!canUndo) ImGui.EndDisabled();
    }
}
