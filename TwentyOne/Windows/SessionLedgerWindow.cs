using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public unsafe class SessionLedgerWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly FileDialogManager _fileDialogManager = new();
    private HistoryWindow? _historyWindow;

    private string gilStartBuf = string.Empty;
    private string gilEndBuf   = string.Empty;
    private string tipBuf      = string.Empty;

    public void SetHistoryWindow(HistoryWindow w) => _historyWindow = w;

    public SessionLedgerWindow(Configuration config)
        : base("Session Ledger##TwentyOneSessionLedger")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoCollapse;
        SyncBuffers();
    }

    public void SyncBuffers()
    {
        gilStartBuf = config.GilStart.ToString();
        gilEndBuf   = config.GilEnd.ToString();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    public override void Draw()
    {
        _fileDialogManager.Draw();

        if (ImGui.Button("New Session"))
            ImGui.OpenPopup("NewSessionConfirm##TwentyOne");

        if (ImGui.BeginPopup("NewSessionConfirm##TwentyOne"))
        {
            ImGui.TextUnformatted("Save current stats as a session and start fresh?");
            ImGui.TextUnformatted("This will also clear tips, reset the bank tracker, and remove all players from the table.");
            ImGui.Spacing();
            if (ImGui.Button("Confirm"))
            {
                NewSession();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Save the current session's stats and reset for a new session."u8);

        ImGui.Spacing();
        ImGui.Separator();

        // Gil inputs
        ImGui.Text("Starting Gil"); ImGui.SameLine(110);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##gilstart", ref gilStartBuf, 20) && long.TryParse(gilStartBuf, out var v))
        {
            config.GilStart = v; config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Current##start"))
        {
            var gil = (long)InventoryManager.Instance()->GetGil();
            config.GilStart = gil;
            gilStartBuf = gil.ToString();
            config.Save();
        }

        ImGui.Text("Ending Gil"); ImGui.SameLine(110);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##gilend", ref gilEndBuf, 20) && long.TryParse(gilEndBuf, out var v2))
        {
            config.GilEnd = v2; config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Current##end"))
        {
            var gil = (long)InventoryManager.Instance()->GetGil();
            config.GilEnd = gil;
            gilEndBuf = gil.ToString();
            config.Save();
        }

        ImGui.AlignTextToFramePadding(); ImGui.Text("Venue Cut %"); ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        var cut = config.DealerCutPct;
        if (ImGui.InputInt("##dealercut", ref cut))
        {
            config.DealerCutPct = Math.Clamp(cut, 0, 100);
            config.Save();
        }

        ImGui.Separator();
        ImGui.Text("Tips");

        long tipTotal = 0;
        for (int i = 0; i < config.Tips.Count; i++)
        {
            tipTotal += config.Tips[i];
            ImGui.Text($"  {config.Tips[i]:N0}");
            ImGui.SameLine();
            ImGui.PushID(i);
            if (ImGui.SmallButton("X"))
            {
                config.Tips.RemoveAt(i);
                config.Save();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }

        ImGui.Text($"Tips total: {tipTotal:N0} gil");

        ImGui.SetNextItemWidth(120);
        var addTip = ImGui.InputText("##tipinput", ref tipBuf, 20, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        addTip |= ImGui.Button("Add Tip");
        if (addTip && long.TryParse(tipBuf, out var tip) && tip != 0)
        {
            config.Tips.Add(tip);
            config.Save();
            tipBuf = string.Empty;
        }

        ImGui.Separator();

        // Reconciliation
        var difference   = config.GilEnd - config.GilStart;
        var grandTotal   = config.PlayerStatsStore.Values.Sum(s => s.TotalWon);
        var betsHeld     = CalcBetsHeld();
        var banksHeld    = config.PlayerStatsStore.Values.Sum(s => s.Bank);
        var adjustedDiff = difference - betsHeld - banksHeld - tipTotal;
        var reconciled   = adjustedDiff + grandTotal == 0;

        ImGui.Text("House Difference:"); ImGui.SameLine(130); ColoredGilText(difference);
        ImGui.Text("Bets held:");        ImGui.SameLine(130); ImGui.Text($"{betsHeld:N0} gil");
        ImGui.Text("Player banks:");     ImGui.SameLine(130); ImGui.Text($"{banksHeld:N0} gil");
        ImGui.Text("Tips held:");        ImGui.SameLine(130); ImGui.Text($"{tipTotal:N0} gil");
        ImGui.Text("Adjusted:");         ImGui.SameLine(130); ColoredGilText(adjustedDiff);
        ImGui.SameLine(0, 20);
        ImGui.Text("Player Net:"); ImGui.SameLine();
        ColoredGilText(grandTotal);
        ImGui.SameLine(0, 8);
        if (reconciled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GameColors.ProfitGreen);
            ImGui.TextUnformatted("OK");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GameColors.BustRed);
            ImGui.TextUnformatted("MISMATCH");
            ImGui.PopStyleColor();
        }

        var profit      = difference - betsHeld - banksHeld - tipTotal;
        var venueOwes   = profit > 0 ? (long)Math.Floor(profit * config.DealerCutPct / 100.0) : 0;
        var dealerKeeps = profit > 0 ? profit - venueOwes + tipTotal : tipTotal;
        ImGui.Separator();
        CopyableGilRow("Profit", profit);
        ImGui.Separator();
        CopyableGilRow("Dealer receives", dealerKeeps);
        CopyableGilRow("Venue receives", venueOwes);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Player stats controls
        if (ImGui.Button("History") && _historyWindow != null)
        {
            _historyWindow.IsOpen = !_historyWindow.IsOpen;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset Stats"))
        {
            config.PlayerStatsStore.Clear();
            config.Save();
        }
        ImGui.SameLine();
        var shiftHeld = ImGui.GetIO().KeyShift;
        if (ImGui.Button("Export"))
        {
            if (shiftHeld)
            {
                _fileDialogManager.SaveFileDialog(
                    "Export Player Stats", "TSV{.tsv}", "player-stats", ".tsv",
                    (ok, path) => { if (ok) File.WriteAllText(path, BuildExportText()); });
            }
            else
            {
                ImGui.SetClipboardText(BuildExportText());
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shiftHeld
                ? "Save player stats to a TSV file."u8
                : "Copy player stats to clipboard as TSV. Shift+click to save to file."u8);

        ImGui.Spacing();

        // Player stats table
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (ImGui.BeginTable("##stats", 9, flags))
        {
            ImGui.TableSetupColumn("Player"u8,    ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Played"u8,    ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Won"u8,       ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Pushes"u8,    ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Lost"u8,      ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("BJs"u8,       ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("5CCs"u8,      ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Win %"u8,     ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Net"u8,       ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableHeadersRow();

            foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
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
                ImGui.TextUnformatted(stat.Charlies.ToString());

                ImGui.TableSetColumnIndex(7);
                ImGui.AlignTextToFramePadding();
                var winPct = stat.GamesPlayed > 0
                    ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                    : "-";
                ImGui.TextUnformatted(winPct);

                ImGui.TableSetColumnIndex(8);
                ImGui.AlignTextToFramePadding();
                Vector4 netColor;
                if (stat.TotalWon > 0)
                    netColor = GameColors.ProfitGreen;
                else if (stat.TotalWon < 0)
                    netColor = GameColors.BustRed;
                else
                    netColor = GameColors.PushGrey;
                var netStr = stat.TotalWon > 0 ? $"+{GameEngine.FormatGil(stat.TotalWon)}" : GameEngine.FormatGil(stat.TotalWon);
                ImGui.TextColored(netColor, netStr);
            }

            ImGui.EndTable();
        }
    }

    public void NewSession()
    {
        var venue = config.ActiveVenue;

        var roundSummaries = venue.RoundHistory.Select(r =>
        {
            var state   = r.Snapshot;
            var winners = new List<string>();
            var losers  = new List<string>();
            var pushes  = new List<string>();
            for (var pi = 0; pi < state.Players.Length; pi++)
            {
                var p       = state.Players[pi];
                var results = Enumerable.Range(0, p.Hands.Length)
                    .Select(hi => GameEngine.GetPayoutResult(state, pi, hi))
                    .ToList();
                var anyWin  = results.Any(r2 => r2 is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin);
                var anyLose = results.Any(r2 => r2 == PayoutResult.Lose);
                var allPush = results.All(r2 => r2 == PayoutResult.Push);
                if      (anyWin && !anyLose) winners.Add(p.DisplayName);
                else if (anyLose && !anyWin) losers.Add(p.DisplayName);
                else if (allPush)            pushes.Add(p.DisplayName);
                else if (anyWin)             winners.Add(p.DisplayName);
                else                         losers.Add(p.DisplayName);
            }
            return new RoundSummary
            {
                RoundNumber = r.RoundNumber,
                BankNet     = r.BankNet,
                PlayerBanks = new Dictionary<string, long>(r.PlayerBanks),
                Winners     = winners,
                Losers      = losers,
                Pushes      = pushes,
            };
        });
        var statData = venue.PlayerStatsStore.Select(kv =>
            KeyValuePair.Create(kv.Key, new PlayerStatData
            {
                DisplayName = kv.Value.DisplayName,
                GamesPlayed = kv.Value.GamesPlayed,
                GamesWon    = kv.Value.GamesWon,
                GamesPushed = kv.Value.GamesPushed,
                GamesLost   = kv.Value.GamesLost,
                Blackjacks  = kv.Value.Blackjacks,
                Charlies    = kv.Value.Charlies,
                TotalWon    = kv.Value.TotalWon,
            }));
        var (archivedStats, bankNet, archivedRounds) = SessionManager.BuildArchive(statData, roundSummaries);

        venue.StatsSessions.Add(new PlayerStatsSession
        {
            Date        = DateTime.Now,
            LocationKey = venue.ActiveSessionLocationKey ?? "",
            Stats       = archivedStats,
            BankNet     = bankNet,
            Rounds      = archivedRounds,
        });

        venue.PlayerStatsStore.Clear();

        venue.RoundHistory.Clear();
        venue.Tips.Clear();
        var currentGil = (long)InventoryManager.Instance()->GetGil();
        venue.GilStart = currentGil;
        venue.GilEnd   = currentGil;
        SyncBuffers();
        venue.ActiveSessionStartedAt   = null;
        venue.ActiveSessionLocationKey = null;

        var gs = config.GameState;
        config.GameState = new GameState
        {
            BjPayout                 = gs.BjPayout,
            FiveCardCharlie          = gs.FiveCardCharlie,
            SkipDealSummaryOnePlayer = gs.SkipDealSummaryOnePlayer,
        };
        config.UndoStack.Clear();
        config.RedoStack.Clear();

        config.Save();
    }

    private static void ColoredGilText(long value)
    {
        Vector4 color;
        if (value > 0)
            color = GameColors.ProfitGreen;
        else if (value < 0)
            color = GameColors.BustRed;
        else
            color = GameColors.PushGrey;
        var text = value > 0 ? $"+{GameEngine.FormatGil(value)}" : GameEngine.FormatGil(value);
        ImGui.TextColored(color, text);
    }

    private long CalcBetsHeld()
    {
        var gs = config.GameState;
        if (gs.Phase == GamePhase.Betting) return 0;
        long total = 0;
        foreach (var player in gs.Players)
            foreach (var hand in player.Hands)
                total += (long)GameEngine.GetEffectiveBet(player, hand);
        return total;
    }

    private static void CopyableGilRow(string label, long value)
    {
        ImGui.Text($"{label}: {value:N0} gil");
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText(value.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to copy"u8);
    }

    private string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Player\tPlayed\tWon\tPushes\tLost\tBJs\t5CCs\tWin%\tNet");
        foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
        {
            var winPct = stat.GamesPlayed > 0
                ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                : "-";
            var totalStr = stat.TotalWon > 0 ? $"+{GameEngine.FormatGil(stat.TotalWon)}" : GameEngine.FormatGil(stat.TotalWon);
            sb.AppendLine($"{stat.DisplayName}\t{stat.GamesPlayed}\t{stat.GamesWon}\t{stat.GamesPushed}\t{stat.GamesLost}\t{stat.Blackjacks}\t{stat.Charlies}\t{winPct}\t{totalStr}");
        }
        return sb.ToString().TrimEnd();
    }
}
