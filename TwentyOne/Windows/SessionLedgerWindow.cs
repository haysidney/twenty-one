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
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

public unsafe class SessionLedgerWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly FileDialogManager _fileDialogManager = new();
    private HistoryWindow? _historyWindow;

    private string gilStartBuf      = string.Empty;
    private string tipBuf           = string.Empty;
    private string serviceAmountBuf = string.Empty;
    private string serviceNoteBuf   = string.Empty;
    // Inline-edit state for an existing service charge. Set by double-clicking
    // a row's amount or note label; cleared on commit / focus-out / row delete.
    private int    editingServiceIdx   = -1;
    private string editingServiceField = string.Empty; // "amt" or "note"
    private string editingServiceBuf   = string.Empty;

    private readonly EdgeStatsCache _edgeCache = new();

    // Order must match the LossCoverage enum.
    private static readonly string[] LossCoverageLabels =
        ["Venue covers the loss", "Venue covers its cut %", "You absorb the loss"];

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

        DrawSessionControls();

        ImGui.Spacing();
        ImGui.Separator();

        // Gil inputs
        ImGui.Text("Starting Gil"); ImGui.SameLine(110);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputTextWithHint("##gilstart", "amount", ref gilStartBuf, 20) && long.TryParse(gilStartBuf, out var v))
        {
            config.GilStart = v; config.Save();
        }
        ImGui.SameLine();
        var ctrlForStart = ImGui.GetIO().KeyCtrl;
        if (!ctrlForStart) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Current##start") && ctrlForStart)
        {
            var gil = (long)InventoryManager.Instance()->GetGil();
            config.GilStart = gil;
            gilStartBuf = gil.ToString();
            config.Save();
        }
        if (!ctrlForStart) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click to overwrite starting gil with the current value."u8);

        // Ending gil always tracks the live on-hand wallet (polled into
        // config.GilEnd by Plugin's framework tick, even when this window is
        // closed). Read-only display - no manual entry.
        ImGui.Text("Ending Gil"); ImGui.SameLine(110);
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{config.ReconciliationGil:N0}");
        ImGui.SameLine();
        ImGui.TextDisabled(config.SessionOpen ? "(live)" : "(frozen)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(config.SessionOpen
                ? "Tracks your on-hand gil automatically."u8
                : "Frozen at session close. Trades since then don't affect these numbers."u8);

        ImGui.AlignTextToFramePadding(); ImGui.Text("Venue Cut %"); ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        var cut = config.VenueCutPct;
        if (ImGui.InputInt("##venuecut", ref cut))
        {
            config.VenueCutPct = Math.Clamp(cut, 0, 100);
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The venue's share of the table's winnings."u8);

        // Losing nights are a venue-policy question - wins split by the cut, but
        // who eats a loss is an arrangement between dealer and venue.
        ImGui.AlignTextToFramePadding(); ImGui.Text("On a losing night"); ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        var lossIdx = (int)config.LossCoverage;
        if (ImGui.Combo("##losscoverage", ref lossIdx, LossCoverageLabels, LossCoverageLabels.Length))
        {
            config.LossCoverage = (LossCoverage)lossIdx;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "How a losing night is split.\n\n" +
                "Venue covers the loss - you walk away whole.\n" +
                $"Venue covers its {config.VenueCutPct}% - the loss splits the way a win would.\n" +
                "You absorb the loss - the venue pays nothing back.");

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
        var addTip = ImGui.InputTextWithHint("##tipinput", "amount", ref tipBuf, 20, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        addTip |= ImGui.Button("Add Tip");
        if (addTip && long.TryParse(tipBuf, out var tip) && tip != 0)
        {
            config.Tips.Add(tip);
            config.Save();
            tipBuf = string.Empty;
        }

        ImGui.Separator();
        ImGui.Text("Service Charges");

        long serviceTotal         = 0;
        long serviceToDealerTotal = 0;
        long serviceToVenueTotal  = 0;
        for (int i = 0; i < config.ServiceCharges.Count; i++)
        {
            var sc = config.ServiceCharges[i];
            serviceTotal += sc.Amount;
            if (sc.GoesToVenue) serviceToVenueTotal  += sc.Amount;
            else                serviceToDealerTotal += sc.Amount;

            ImGui.PushID($"sc{i}");

            DrawServiceField(i, "amt",  sc.Amount.ToString("N0"), 90);
            ImGui.SameLine();
            var noteLabel = sc.Note.Length > 0 ? sc.Note : "(no description)";
            DrawServiceField(i, "note", noteLabel, 220, noteIsPlaceholder: sc.Note.Length == 0);

            // Dealer/Venue routing toggle
            ImGui.SameLine();
            if (ImGui.SmallButton(sc.GoesToVenue ? "To Venue" : "To Dealer"))
            {
                sc.GoesToVenue = !sc.GoesToVenue;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to toggle whether this charge is routed to the dealer or venue in the payout split.");

            // Delete
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                config.ServiceCharges.RemoveAt(i);
                config.Save();
                CancelServiceEdit();
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }

        ImGui.Text($"Service total: {serviceTotal:N0} gil");

        ImGui.SetNextItemWidth(90);
        var addService = ImGui.InputTextWithHint("##serviceamt", "amount", ref serviceAmountBuf, 20, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        addService |= ImGui.InputTextWithHint("##servicenote", "description", ref serviceNoteBuf, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        addService |= ImGui.Button("Add Service");
        if (addService && long.TryParse(serviceAmountBuf, out var svcAmt) && svcAmt != 0)
        {
            config.ServiceCharges.Add(new ServiceCharge { Amount = svcAmt, Note = serviceNoteBuf.Trim() });
            config.Save();
            serviceAmountBuf = string.Empty;
            serviceNoteBuf   = string.Empty;
        }

        ImGui.Separator();

        // Reconciliation - shared math (see Compute / Reconciliation). creditIssued:
        // venue-funded credits sit in the dealer's pile (pre-loaded into Starting Gil),
        // so they enter the balance like grandTotal does - a phantom contribution that
        // closes the books when credit-funded gil ends up in player banks, gets cashed
        // out, or drains back to the house.
        var rec            = Compute(config);
        var difference     = rec.Difference;
        var grandTotal     = rec.GrandTotal;
        var betsHeld       = rec.BetsHeld;
        var banksHeld      = rec.BanksHeld;
        var creditIssued   = rec.CreditIssued;
        var adjustedDiff   = rec.AdjustedDiff;
        var reconciled     = rec.Reconciled;

        ImGui.Text("House Difference:"); ImGui.SameLine(130); ColoredGilText(difference);
        ImGui.Text("Bets held:");        ImGui.SameLine(130); ImGui.Text($"{betsHeld:N0} gil");
        ImGui.Text("Player banks:");     ImGui.SameLine(130); ImGui.Text($"{banksHeld:N0} gil");
        ImGui.Text("Tips held:");        ImGui.SameLine(130); ImGui.Text($"{tipTotal:N0} gil");
        ImGui.Text("Service revenue:");  ImGui.SameLine(130); ImGui.Text($"{serviceTotal:N0} gil");
        if (creditIssued > 0)
        {
            ImGui.Text("Credits issued:"); ImGui.SameLine(130); ImGui.Text($"{creditIssued:N0} gil");
        }
        // This is the same figure the settlement block below calls "Table net" -
        // shown here because it is the left side of the books-balance check.
        ImGui.Text("Table net:");         ImGui.SameLine(130); ColoredGilText(adjustedDiff);
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

        DrawSettlement(
            Settlement.Compute(
                tableNet:        adjustedDiff,
                tips:            tipTotal,
                serviceToDealer: serviceToDealerTotal,
                serviceToVenue:  serviceToVenueTotal,
                venueCutPct:     config.VenueCutPct,
                lossCoverage:    config.LossCoverage),
            creditIssued);

        ImGui.Separator();

        // Edge stats: theoretical uses the currently configured rules ("what does
        // this session look like under my current rule set?"). Realized is purely
        // observational - bank net per gil wagered.
        var currentRules = new EdgeRules(
            config.BjPayout, config.CharliePayout, config.FiveCardCharlie,
            config.DealerStandThreshold, config.DealerHitsSoftThreshold, config.DoubleAfterSplit,
            config.HitSplitAces, config.ResplitAces, config.AllowSurrender,
            config.ResplitCap, config.DoubleRestriction);
        var liveStats = _edgeCache.Get(config.RoundHistory, currentRules);
        EdgeStatsDisplay.Draw(liveStats, config.RoundHistory.Count,
            "Expected bank gain per gil wagered under your currently configured rules.\nRecomputed across this session's rounds with the current rule set applied.");

        ImGui.Separator();

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
                if (stat.TotalNet > 0)
                    netColor = GameColors.ProfitGreen;
                else if (stat.TotalNet < 0)
                    netColor = GameColors.BustRed;
                else
                    netColor = GameColors.PushGrey;
                var netStr = stat.TotalNet > 0 ? $"+{GameEngine.FormatGil(stat.TotalNet)}" : GameEngine.FormatGil(stat.TotalNet);
                ImGui.TextColored(netColor, netStr);
            }

            ImGui.EndTable();
        }
    }

    private void DrawSessionControls()
    {
        var venue = config.ActiveVenue;

        if (config.SessionOpen)
        {
            var check = SessionManager.CheckClose(
                true, config.GameState.Phase,
                venue.PlayerStatsStore.Select(kv =>
                    new KeyValuePair<string, long>(kv.Value.DisplayName, kv.Value.Bank)));

            if (!check.CanClose) ImGui.BeginDisabled();
            if (ImGui.Button("Close Session"))
                ImGui.OpenPopup("CloseSessionConfirm##TwentyOne");
            if (!check.CanClose) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                var holders = check.BankHolders.Count > 0
                    ? "\n\nStill holding gil:\n- " + string.Join("\n- ", check.BankHolders)
                    : string.Empty;
                ImGui.SetTooltip(check.CanClose
                    ? "Freeze the books for the night. Trades after this won't affect the numbers."
                    : check.Reason + holders);
            }

            if (ImGui.BeginPopup("CloseSessionConfirm##TwentyOne"))
            {
                ImGui.TextUnformatted("Close the session and freeze the books?");
                ImGui.TextUnformatted("The numbers stay on screen for settling up. No rounds can be dealt until you start a new session.");
                ImGui.Spacing();
                if (ImGui.Button("Confirm##closeSession"))
                {
                    CloseSession();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##closeSession"))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"Session open since {venue.ActiveSessionStartedAt:t}");
            return;
        }

        var hasDataToArchive = venue.ActiveSessionStartedAt != null;

        if (ImGui.Button("Start Session"))
        {
            if (hasDataToArchive) ImGui.OpenPopup("StartSessionConfirm##TwentyOne");
            else                  StartSession();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(hasDataToArchive
                ? "Archive the closed session and start a fresh night."u8
                : "Start a session so you can deal."u8);

        if (ImGui.BeginPopup("StartSessionConfirm##TwentyOne"))
        {
            ImGui.TextUnformatted("Archive the closed session and start fresh?");
            ImGui.TextUnformatted("This saves the night's stats to History, then clears tips, round history, the narration log, and the table.");
            ImGui.Spacing();
            if (ImGui.Button("Confirm##startSession"))
            {
                StartSession();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##startSession"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, GameColors.BannerGold);
        ImGui.TextUnformatted(venue.SessionClosedAt != null
            ? $"Session closed {venue.SessionClosedAt:t} - books frozen"
            : "No session running");
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Freezes the books and closes the night. The numbers stay on screen (the
    /// dealer still has to settle up from them); they simply stop tracking the
    /// live wallet, so post-close trading, vendoring, and cash-outs can't move
    /// them. Guarded by <see cref="SessionManager.CheckClose"/>.
    /// </summary>
    public void CloseSession()
    {
        var venue = config.ActiveVenue;
        venue.SessionClosedAt  = DateTime.Now;
        venue.SessionClosedGil = venue.GilEnd;
        config.NarrationLog.Add($"[Audit] Session closed. Books frozen at {venue.GilEnd:N0} gil on hand.");
        config.Save();
    }

    /// <summary>
    /// Archives the closed session (if there is one), clears the night's data,
    /// re-baselines the gil tracker to the live wallet, and opens a new session.
    /// A fresh install has nothing to archive and simply opens.
    /// </summary>
    public void StartSession()
    {
        if (config.ActiveVenue.ActiveSessionStartedAt != null)
            ArchiveSession();
        OpenSession();
    }

    private void OpenSession()
    {
        var venue      = config.ActiveVenue;
        var currentGil = (long)InventoryManager.Instance()->GetGil();

        venue.GilStart = currentGil;
        venue.GilEnd   = currentGil;
        SyncBuffers();

        venue.ActiveSessionStartedAt   = DateTime.Now;
        venue.ActiveSessionLocationKey = Plugin.GetCurrentHousingAddressKey();
        venue.SessionClosedAt          = null;
        venue.SessionClosedGil         = 0;

        config.NarrationLog.Clear();
        config.Save();
    }

    private void ArchiveSession()
    {
        var venue = config.ActiveVenue;

        var statData = venue.PlayerStatsStore.ToDictionary(
            kv => kv.Key,
            kv => new PlayerStatData
            {
                DisplayName = kv.Value.DisplayName,
                GamesPlayed = kv.Value.GamesPlayed,
                GamesWon    = kv.Value.GamesWon,
                GamesPushed = kv.Value.GamesPushed,
                GamesLost   = kv.Value.GamesLost,
                Blackjacks  = kv.Value.Blackjacks,
                Charlies    = kv.Value.Charlies,
                TotalNet    = kv.Value.TotalNet,
            });

        // Snapshot each player's bank transaction log so it survives the
        // PlayerStatsStore.Clear() below.
        var bankLogs = venue.PlayerStatsStore.ToDictionary(
            kv => kv.Key,
            kv => new List<BankTransactionEntry>(kv.Value.BankLog));

        // Edge stats locked in using each round's snapshot rules - the rules that
        // were actually in effect when each round was played.
        var edgeStats = EdgeStats.Aggregate(venue.RoundHistory);

        var session = new PlayerStatsSession
        {
            Id                 = Guid.NewGuid(),
            Date               = DateTime.Now,
            LocationKey        = venue.ActiveSessionLocationKey ?? "",
            Stats              = statData,
            BankNet            = venue.RoundHistory.Sum(r => r.BankNet),
            Rounds             = new List<RoundHistoryEntry>(venue.RoundHistory),
            TotalWagered       = edgeStats.TotalWagered,
            TheoreticalBankNet = edgeStats.TheoreticalBankNet,
            PlayerBankLogs     = bankLogs,
            PluginVersion      = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
        };
        venue.StatsSessions.Add(session);
        SessionStore.Save(venue.Id, session);

        venue.PlayerStatsStore.Clear();

        venue.RoundHistory.Clear();
        venue.Tips.Clear();
        venue.ServiceCharges.Clear();

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

    /// <summary>
    /// End-of-night settlement block. The dealer already physically holds every
    /// gil on this screen, so the only thing they actually <em>do</em> is one
    /// trade with the venue - which is why the headline is a single signed
    /// transfer with an explicit direction, not a pair of "receives" figures the
    /// dealer has to subtract in their head.
    /// </summary>
    private void DrawSettlement(Settlement s, long creditIssued)
    {
        const float col = 170f;

        ImGui.Separator();
        ImGui.TextUnformatted("Settlement");

        // Section 1: everything sitting in the dealer's pile tonight.
        SettlementRow("Table net", s.TableNet, col,
            "What the table won or lost against the players.\nBets in play, player banks, tips and service charges are already out of this.");

        if (s.Tips != 0)
            SettlementRow("Tips", s.Tips, col, "Tips are never split - they pass straight to your take.");
        if (s.ServiceToDealer != 0)
            SettlementRow("Service (to you)", s.ServiceToDealer, col, "Service charges routed to the dealer.");
        if (s.ServiceToVenue != 0)
            SettlementRow("Service (to venue)", s.ServiceToVenue, col, "Service charges routed to the venue.\nDeducted again below - it is collected by you but owed onward.");

        // Credit moves no real gil when issued, so it is not a settlement line -
        // it reaches the books only through gil that actually left the pile,
        // which Table net already measures. Informational only.
        if (creditIssued > 0)
        {
            ImGui.TextDisabled("Credits issued");
            ImGui.SameLine(col);
            ImGui.TextDisabled($"{creditIssued:N0} gil");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Venue-funded free play, for reference.\n\n" +
                    "Not settled separately: credit the player lost back never left your pile,\n" +
                    "and credit they cashed out is already in Table net as a real loss.");
        }

        ImGui.Separator();

        // Section 2: what leaves the pile for the venue. Rendered negated, so a
        // deduction reads red/negative rather than as a gain the dealer pockets.
        var cutLabel = s.TableNet < 0 ? $"Venue covers ({config.VenueCutPct}%)" : $"Venue cut ({config.VenueCutPct}%)";
        SettlementRow(cutLabel, -s.VenueShare, col,
            s.TableNet < 0
                ? "The venue's share of tonight's loss, per the loss-coverage setting.\nPositive here means the venue is paying you back."
                : "The venue's share of the table's winnings.");
        if (s.ServiceToVenue != 0)
            SettlementRow("Service to venue", -s.ServiceToVenue, col, "Venue-routed service charges, owed on top of the cut.");

        ImGui.Separator();

        // Headline: one direction, one amount, click to copy into a trade window.
        string transferLabel;
        Vector4 transferColor;
        if (s.NetTransfer > 0)
        {
            transferLabel = "Pay venue";
            transferColor = GameColors.BannerGold;
        }
        else if (s.NetTransfer < 0)
        {
            transferLabel = "Collect from venue";
            transferColor = GameColors.ProfitGreen;
        }
        else
        {
            transferLabel = "Nothing to settle";
            transferColor = GameColors.PushGrey;
        }

        ImGui.TextColored(transferColor, transferLabel);
        ImGui.SameLine(col);
        ImGui.TextColored(transferColor, $"{s.TransferAmount:N0} gil");
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText(s.TransferAmount.ToString());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(s.NetTransfer >= 0
                ? "Trade this much to the venue. Click to copy."u8
                : "The venue owes you this much. Click to copy."u8);

        SettlementRow("Your take", s.DealerTake, col,
            "What you are left with after settling up, tips included.");
    }

    // Settlement figures get typed into a trade window, so they print in full
    // (250,000) rather than through GameEngine.FormatGil's abbreviation (250K).
    private static void SettlementRow(string label, long value, float col, string tooltip)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(col);
        Vector4 color;
        if (value > 0)      color = GameColors.ProfitGreen;
        else if (value < 0) color = GameColors.BustRed;
        else                color = GameColors.PushGrey;
        ImGui.TextColored(color, value > 0 ? $"+{value:N0} gil" : $"{value:N0} gil");
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText(value.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{tooltip}\n\nClick to copy.");
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

    // Renders one editable field on a service-charge row. While not in edit
    // mode it shows the label text; double-click swaps in an InputText that
    // commits on Enter or focus-out.
    private void DrawServiceField(int idx, string field, string label, float editWidth, bool noteIsPlaceholder = false)
    {
        var sc = config.ServiceCharges[idx];
        var isEditing = editingServiceIdx == idx && editingServiceField == field;

        if (isEditing)
        {
            ImGui.SetNextItemWidth(editWidth);
            ImGui.SetKeyboardFocusHere();
            var commit = field == "amt"
                ? ImGui.InputTextWithHint("##scedit", "amount", ref editingServiceBuf, 20, ImGuiInputTextFlags.EnterReturnsTrue)
                : ImGui.InputTextWithHint("##scedit", "description", ref editingServiceBuf, 80, ImGuiInputTextFlags.EnterReturnsTrue);
            if (commit || ImGui.IsItemDeactivatedAfterEdit())
            {
                CommitServiceEdit(sc);
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                CancelServiceEdit();
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            if (noteIsPlaceholder) ImGui.TextDisabled(label);
            else                   ImGui.TextUnformatted(label);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Double-click to edit");
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    editingServiceIdx   = idx;
                    editingServiceField = field;
                    editingServiceBuf   = field == "amt" ? sc.Amount.ToString() : sc.Note;
                }
            }
        }
    }

    private void CommitServiceEdit(ServiceCharge sc)
    {
        if (editingServiceField == "amt")
        {
            if (long.TryParse(editingServiceBuf, out var v) && v != 0)
            {
                sc.Amount = v;
                config.Save();
            }
        }
        else if (editingServiceField == "note")
        {
            sc.Note = editingServiceBuf.Trim();
            config.Save();
        }
        CancelServiceEdit();
    }

    private void CancelServiceEdit()
    {
        editingServiceIdx   = -1;
        editingServiceField = string.Empty;
        editingServiceBuf   = string.Empty;
    }

    private static long CalcBetsHeld(Configuration config)
    {
        var gs = config.GameState;
        if (gs.Phase == GamePhase.Betting) return 0;
        long total = 0;
        for (var pi = 0; pi < gs.Players.Length; pi++)
        {
            var player = gs.Players[pi];
            if (player.SittingOut) continue;

            if (gs.Phase == GamePhase.Payout)
            {
                // Banking players were auto-settled in UpdatePlayerStats: bet + winnings
                // moved into the bank, so they're already counted in banksHeld. Counting
                // their bets here would double-count. (Bank-only: every tracked player
                // has a stats row, so this skips all of them; the fallback below only
                // fires for an untracked player that somehow reached payout.)
                if (player.TryGetStat(config, out _)) continue;
                for (var hi = 0; hi < player.Hands.Length; hi++)
                    total += (long)GameEngine.PayoutTotalOwed(gs, pi, hi);
            }
            else
            {
                foreach (var hand in player.Hands)
                    total += (long)GameEngine.GetEffectiveBet(player, hand);
            }
        }
        return total;
    }

    /// <summary>
    /// Session-ledger reconciliation snapshot. Single source of truth for the
    /// books-balance math, shared by this window's reconciliation block and the
    /// main-window drift chip so the two can never diverge.
    /// </summary>
    public readonly record struct Reconciliation(
        long Difference, long BetsHeld, long BanksHeld, long TipTotal,
        long ServiceTotal, long CreditIssued, long GrandTotal)
    {
        public long AdjustedDiff => Difference - BetsHeld - BanksHeld - TipTotal - ServiceTotal;
        public long Drift        => AdjustedDiff + GrandTotal + CreditIssued; // 0 == reconciled
        public bool Reconciled   => Drift == 0;
    }

    // ReconciliationGil is the live wallet while open and the frozen close-time
    // snapshot once closed, so end-of-night trading can't move a closed night's books.
    public static Reconciliation Compute(Configuration config) => new(
        Difference:   config.ReconciliationGil - config.GilStart,
        BetsHeld:     CalcBetsHeld(config),
        BanksHeld:    config.PlayerStatsStore.Values.Sum(s => s.Bank),
        TipTotal:     config.Tips.Sum(),
        ServiceTotal: config.ServiceCharges.Sum(s => s.Amount),
        CreditIssued: config.PlayerStatsStore.Values
                          .SelectMany(s => s.BankLog)
                          .Where(e => e.Kind == BankTransactionKind.Credit)
                          .Sum(e => e.Amount),
        GrandTotal:   config.PlayerStatsStore.Values.Sum(s => s.TotalNet));

    private string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Player\tPlayed\tWon\tPushes\tLost\tBJs\t5CCs\tWin%\tNet");
        foreach (var stat in config.PlayerStatsStore.Values.OrderBy(s => s.DisplayName))
        {
            var winPct = stat.GamesPlayed > 0
                ? $"{stat.GamesWon * 100.0 / stat.GamesPlayed:0.#}%"
                : "-";
            var totalStr = stat.TotalNet > 0 ? $"+{GameEngine.FormatGil(stat.TotalNet)}" : GameEngine.FormatGil(stat.TotalNet);
            sb.AppendLine($"{stat.DisplayName}\t{stat.GamesPlayed}\t{stat.GamesWon}\t{stat.GamesPushed}\t{stat.GamesLost}\t{stat.Blackjacks}\t{stat.Charlies}\t{winPct}\t{totalStr}");
        }
        return sb.ToString().TrimEnd();
    }
}
