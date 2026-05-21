using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

public class HistoryWindow : Window
{
    private readonly Configuration config;
    private readonly MainWindow     mainWindow;
#if DEBUG
    private readonly FileDialogManager _fileDialog = new();
#endif

    private int _selectedSessionIndex = -1;

    public HistoryWindow(Configuration config, MainWindow mainWindow)
        : base("History##TwentyOneHistory")
    {
        this.config     = config;
        this.mainWindow = mainWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void OnOpen() => _selectedSessionIndex = -1;

    public override void Draw()
    {
#if DEBUG
        _fileDialog.Draw();
#endif
        if (!ImGui.BeginTabBar("##historyTabs")) return;

        if (ImGui.BeginTabItem("Rounds This Session"))
        {
            DrawRoundsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Previous Sessions"))
        {
            DrawSessionsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // ── Rounds This Session ───────────────────────────────────────────────────

    private void DrawRoundsTab()
    {
        var history = config.RoundHistory;

        var canClear = history.Count > 0 && ImGui.GetIO().KeyCtrl;
        if (!canClear) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Clear History") && canClear)
        {
            history.Clear();
            config.Save();
        }
        if (!canClear) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to clear all round history");

        if (history.Count == 0)
        {
            ImGui.TextUnformatted("No rounds recorded yet.");
            return;
        }

        // Theoretical edge uses each round's snapshot rules - the forensic view
        // of "what should have happened given the rules in effect at the time".
        var stats = EdgeStats.Aggregate(history);
        EdgeStatsDisplay.Draw(stats, history.Count,
            "Expected bank gain per gil wagered, computed using the rules in effect for each round.");

        ImGui.Spacing();

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                  | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable
                  | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("##history", 5, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Round"u8,   ImGuiTableColumnFlags.WidthFixed,   50);
        ImGui.TableSetupColumn("Winners"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Losers"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pushes"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed,   80);
        ImGui.TableHeadersRow();

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];
            var (winners, losers, pushes) = ClassifyRound(entry.Snapshot);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            var isSelected = false;
            if (ImGui.Selectable($"{entry.RoundNumber}##hist{i}",
                    ref isSelected,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                mainWindow.RestoreHistoricalRound(entry.Snapshot);
                IsOpen = false;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to view this round");
#if DEBUG
            if (ImGui.BeginPopupContextItem($"##histCtx{i}"))
            {
                if (ImGui.MenuItem("Save snapshot..."))
                {
                    var snapshot = entry.Snapshot;
                    var json     = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    _fileDialog.SaveFileDialog(
                        "Save Debug Snapshot", "JSON{.json}", $"round-{entry.RoundNumber}", ".json",
                        (ok, path) => { if (ok) File.WriteAllText(path, json); });
                }
                ImGui.EndPopup();
            }
#endif

            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            if (winners.Count > 0)
                ImGui.TextColored(GameColors.ProfitGreen, string.Join(", ", winners));

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            if (losers.Count > 0)
                ImGui.TextColored(GameColors.BustRed, string.Join(", ", losers));

            ImGui.TableSetColumnIndex(3);
            ImGui.AlignTextToFramePadding();
            if (pushes.Count > 0)
                ImGui.TextColored(GameColors.PushGrey, string.Join(", ", pushes));

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            DrawNetCell(entry.BankNet);
            if (entry.PlayerBanks.Count > 0 && ImGui.IsItemHovered())
            {
                var prevEntry = i + 1 < history.Count ? history[i + 1] : null;
                var tip = new System.Text.StringBuilder("Bank balances after payout:\n");
                foreach (var (pkey, pbal) in entry.PlayerBanks)
                {
                    var prevBal  = prevEntry != null && prevEntry.PlayerBanks.TryGetValue(pkey, out var pb) ? pb : 0;
                    var delta    = pbal - prevBal;
                    var deltaStr = delta >= 0 ? $"+{GameEngine.FormatGil(delta)}" : GameEngine.FormatGil(delta);
                    var name     = entry.Snapshot.Players
                        .FirstOrDefault(pl => (pl.FullName.Length > 0 ? $"{pl.FullName}@{pl.World}" : pl.Nickname) == pkey)
                        ?.DisplayName ?? pkey;
                    tip.AppendLine($"  {name}: {GameEngine.FormatGil(pbal)} ({deltaStr})");
                }
                ImGui.SetTooltip(tip.ToString().TrimEnd());
            }
        }

        ImGui.EndTable();
    }

    // ── Previous Sessions ─────────────────────────────────────────────────────

    private void DrawSessionsTab()
    {
        var sessions = config.StatsSessions;

        if (_selectedSessionIndex >= sessions.Count)
            _selectedSessionIndex = sessions.Count - 1;

        if (_selectedSessionIndex >= 0)
            DrawSessionDetail(sessions);
        else
            DrawSessionList(sessions);
    }

    private void DrawSessionList(List<PlayerStatsSession> sessions)
    {
        if (sessions.Count == 0)
        {
            ImGui.TextUnformatted("No sessions recorded yet. Use \"New Session\" in Session Ledger to begin tracking sessions.");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##sessionlist", 4, flags)) return;

        ImGui.TableSetupColumn("Date"u8,    ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Players"u8, ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Rounds"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        for (var i = sessions.Count - 1; i >= 0; i--)
        {
            var s = sessions[i];
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            var sel = false;
            if (ImGui.Selectable($"{s.Date:MM/dd/yy HH:mm}##sess{i}", ref sel, ImGuiSelectableFlags.SpanAllColumns))
                _selectedSessionIndex = i;

            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(s.Stats.Count.ToString());

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(s.Rounds.Count.ToString());

            ImGui.TableSetColumnIndex(3);
            ImGui.AlignTextToFramePadding();
            DrawNetCell(s.BankNet);
        }

        ImGui.EndTable();
    }

    private void DrawSessionDetail(List<PlayerStatsSession> sessions)
    {
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var s = sessions[_selectedSessionIndex];

        if (ImGui.Button("Back"))
        {
            _selectedSessionIndex = -1;
            return;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{s.Date:dddd, MMMM d, yyyy  HH:mm}");

        ImGui.SameLine();
        if (ImGui.Button("Recompute Stats"))
        {
            var recomputed         = EdgeStats.Aggregate(s.Rounds);
            s.TotalWagered         = recomputed.TotalWagered;
            s.TheoreticalBankNet   = recomputed.TheoreticalBankNet;
            s.PluginVersion        = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            SessionStore.Save(config.ActiveVenue.Id, s);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Re-run the solver against this session's round snapshots and overwrite the stored aggregates.\nUseful after solver fixes or new rule axes.");

        ImGui.SameLine();
        const string deleteLabel = "Delete Session";
        var deleteWidth = ImGui.CalcTextSize(deleteLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var targetX    = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - deleteWidth;
        if (ImGui.GetCursorPosX() < targetX) ImGui.SetCursorPosX(targetX);
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.Button(deleteLabel) && ctrlHeld)
        {
            SessionStore.Delete(config.ActiveVenue.Id, s);
            sessions.RemoveAt(_selectedSessionIndex);
            _selectedSessionIndex = -1;
            if (!ctrlHeld) ImGui.EndDisabled();
            return;
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to delete this session");

        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##sessStats", 8, tableFlags))
        {
            ImGui.TableSetupColumn("Player"u8,  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Played"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Won"u8,     ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Pushes"u8,  ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Lost"u8,    ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("BJs"u8,     ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Win %"u8,   ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var stat in s.Stats.Values.OrderBy(v => v.DisplayName))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.DisplayName);
                ImGui.TableSetColumnIndex(1);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesPlayed.ToString());
                ImGui.TableSetColumnIndex(2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesWon.ToString());
                ImGui.TableSetColumnIndex(3);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesPushed.ToString());
                ImGui.TableSetColumnIndex(4);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.GamesLost.ToString());
                ImGui.TableSetColumnIndex(5);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(stat.Blackjacks.ToString());
                ImGui.TableSetColumnIndex(6);
                ImGui.AlignTextToFramePadding();
                var winPct = stat.GamesPlayed > 0
                    ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                    : "-";
                ImGui.TextUnformatted(winPct);
                ImGui.TableSetColumnIndex(7);
                ImGui.AlignTextToFramePadding();
                DrawNetCell(stat.TotalWon);
            }

            ImGui.EndTable();
        }

        var grandTotal = s.Stats.Values.Sum(v => v.TotalWon);
        ImGui.Spacing();
        ImGui.TextUnformatted("Net (all players):");
        ImGui.SameLine();
        DrawNetCell(grandTotal);

        // Edge stats locked in at archive time using each round's snapshot rules.
        // Pre-feature sessions have TotalWagered = 0 and show "-".
        ImGui.Spacing();
        var sessionStats = new AggregateStats(s.TotalWagered, s.BankNet, s.TheoreticalBankNet);
        EdgeStatsDisplay.Draw(sessionStats, s.Rounds.Count,
            "Expected bank gain per gil wagered, computed using the rules that were in effect when this session was played.");

        if (s.Rounds.Count == 0) return;

        ImGui.Spacing();
        ImGui.TextUnformatted("Rounds:");
        var roundTableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                            | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable
                            | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("##sessRounds", 5, roundTableFlags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Round"u8,   ImGuiTableColumnFlags.WidthFixed,   50);
        ImGui.TableSetupColumn("Winners"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Losers"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pushes"u8,  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Net"u8,     ImGuiTableColumnFlags.WidthFixed,   80);
        ImGui.TableHeadersRow();

        for (var i = s.Rounds.Count - 1; i >= 0; i--)
        {
            var r = s.Rounds[i];
            var (winners, losers, pushes) = ClassifyRound(r.Snapshot);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(r.RoundNumber.ToString());

            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            if (winners.Count > 0)
                ImGui.TextColored(GameColors.ProfitGreen, string.Join(", ", winners));

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            if (losers.Count > 0)
                ImGui.TextColored(GameColors.BustRed, string.Join(", ", losers));

            ImGui.TableSetColumnIndex(3);
            ImGui.AlignTextToFramePadding();
            if (pushes.Count > 0)
                ImGui.TextColored(GameColors.PushGrey, string.Join(", ", pushes));

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            DrawNetCell(r.BankNet);
            if (r.PlayerBanks.Count > 0 && ImGui.IsItemHovered())
            {
                var tip = new System.Text.StringBuilder("Player balance deltas:\n");
                foreach (var (pkey, pbal) in r.PlayerBanks)
                {
                    long prevBal;
                    if (r.PrePayoutPlayerBanks.TryGetValue(pkey, out var pre))
                        prevBal = pre;
                    else if (i + 1 < s.Rounds.Count && s.Rounds[i + 1].PlayerBanks.TryGetValue(pkey, out var pb))
                        prevBal = pb;
                    else
                        prevBal = 0;
                    var delta = pbal - prevBal;
                    var ds    = delta >= 0 ? $"+{GameEngine.FormatGil(delta)}" : GameEngine.FormatGil(delta);
                    var name  = r.Snapshot.Players
                        .FirstOrDefault(pl => (pl.FullName.Length > 0 ? $"{pl.FullName}@{pl.World}" : pl.Nickname) == pkey)
                        ?.DisplayName ?? pkey;
                    tip.AppendLine($"  {name}: {GameEngine.FormatGil(pbal)} ({ds})");
                }
                ImGui.SetTooltip(tip.ToString().TrimEnd());
            }
        }

        ImGui.EndTable();
    }

    // Splits a round's players into (winners, losers, pushes) by inspecting each
    // hand's PayoutResult. A player with any winning hand counts as a winner
    // unless they also had a loss, in which case they're a winner-with-mixed.
    private static (List<string> Winners, List<string> Losers, List<string> Pushes) ClassifyRound(GameState state)
    {
        var winners = new List<string>();
        var losers  = new List<string>();
        var pushes  = new List<string>();
        for (var pi = 0; pi < state.Players.Length; pi++)
        {
            var p       = state.Players[pi];
            var results = Enumerable.Range(0, p.Hands.Length)
                .Select(hi => GameEngine.GetPayoutResult(state, pi, hi))
                .ToList();
            var anyWin  = results.Any(r => r is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin);
            var anyLose = results.Any(r => r == PayoutResult.Lose);
            var allPush = results.All(r => r == PayoutResult.Push);
            if      (anyWin && !anyLose) winners.Add(p.DisplayName);
            else if (anyLose && !anyWin) losers.Add(p.DisplayName);
            else if (allPush)            pushes.Add(p.DisplayName);
            else if (anyWin)             winners.Add(p.DisplayName);
            else                         losers.Add(p.DisplayName);
        }
        return (winners, losers, pushes);
    }

    private static void DrawNetCell(long net)
    {
        Vector4 col;
        if (net > 0)
            col = GameColors.ProfitGreen;
        else if (net < 0)
            col = GameColors.BustRed;
        else
            col = GameColors.PushGrey;
        var str = net > 0 ? $"+{GameEngine.FormatGil(net)}" : GameEngine.FormatGil(net);
        ImGui.TextColored(col, str);
    }
}
