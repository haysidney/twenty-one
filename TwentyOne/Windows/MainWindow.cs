using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public class PlayerRow
{
    public string Name { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public string Cards { get; set; } = string.Empty;
    public string Score { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class MainWindow : Window, IDisposable
{
    private readonly List<PlayerRow> players = [];
    private string newPlayerName = string.Empty;
    private string dealerCards = string.Empty;
    private string dealerScore = string.Empty;
    private int renamingIndex = -1;
    private string renamingBuffer = string.Empty;

    public MainWindow()
        : base("Twenty One##TwentyOneMain")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Dealer section
        ImGui.Text("-- Dealer --");
        ImGui.Separator();

        ImGui.Text("Cards:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##dealerCards"u8, ref dealerCards, 64);
        ImGui.SameLine();
        ImGui.Text("Score:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        ImGui.InputText("##dealerScore"u8, ref dealerScore, 8);

        ImGui.Spacing();
        ImGui.Text("-- Players --");
        ImGui.Separator();

        // Player table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##players"u8, 6, tableFlags))
        {
            ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Bet"u8, ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Cards"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Score"u8, ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Status"u8, ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("##actions"u8, ImGuiTableColumnFlags.WidthFixed, 60);
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

                // Cards
                ImGui.TableSetColumnIndex(2);
                ImGui.SetNextItemWidth(-1);
                var cards = p.Cards;
                if (ImGui.InputText($"##cards{i}", ref cards, 64)) p.Cards = cards;

                // Score
                ImGui.TableSetColumnIndex(3);
                ImGui.SetNextItemWidth(-1);
                var score = p.Score;
                if (ImGui.InputText($"##score{i}", ref score, 8)) p.Score = score;

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
                p.Cards = string.Empty;
                p.Score = string.Empty;
                p.Status = string.Empty;
            }
            dealerCards = string.Empty;
            dealerScore = string.Empty;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clears bets, cards, scores, and statuses. Player names are kept."u8);
    }
}
