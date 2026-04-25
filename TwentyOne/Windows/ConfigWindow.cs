using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly BankWindow    bankWindow;

    public ConfigWindow(Configuration config, BankWindow bankWindow)
        : base("Twenty One — Settings##TwentyOneConfig")
    {
        this.config     = config;
        this.bankWindow = bankWindow;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400, 200), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    private static readonly NarrationTemplates Defaults = new();

    private bool   _narrationDirty;
    private double _narrationDirtyAt;

    private string _renameBuffer     = string.Empty;
    private bool   _renamePending    = false;
    private bool   _duplicatePending = false;

    private readonly FileDialogManager _fileDialogManager = new();

    private void MarkNarrationDirty()
    {
        _narrationDirty   = true;
        _narrationDirtyAt = ImGui.GetTime();
    }

    private static readonly string[] ChatChannels =
    [
        "/say", "/yell", "/shout", "/p", "/a", "/fc",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8",
    ];

    public override void Draw()
    {
        _fileDialogManager.Draw();
        if (_narrationDirty && ImGui.GetTime() - _narrationDirtyAt > 1.0)
        {
            config.Save();
            _narrationDirty = false;
        }

        DrawVenueSelector();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Blackjack Payout");
        ImGui.SameLine();
        var bjOptions = new[] { "3:2", "6:5", "1:1" };
        var bjIdx     = (int)config.GameState.BjPayout;
        ImGui.SetNextItemWidth(70);
        if (ImGui.Combo("##bjpayout", ref bjIdx, bjOptions, bjOptions.Length))
        {
            // BjPayout is a venue setting, not an undoable game action.
            config.GameState.BjPayout = (BlackjackPayout)bjIdx;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Dealer Name");
        ImGui.SameLine();
        var dealerName = config.DealerName;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##dealerName", ref dealerName, 64))
        {
            config.DealerName = dealerName;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Used as {dealer} in narration templates.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var autoTrade = config.AutoTradeEnabled;
        if (ImGui.Checkbox("Auto-open trade for Double Down / Split", ref autoTrade))
        {
            config.AutoTradeEnabled = autoTrade;
            config.Save();
        }

        var autoBet = config.AutoBetFromTrades;
        if (ImGui.Checkbox("Auto-fill bet from trade (betting phase)", ref autoBet))
        {
            config.AutoBetFromTrades = autoBet;
            config.Save();
        }

        var autoDeposit = config.AutoDepositFromTrades;
        if (ImGui.Checkbox("Prompt to update bank when trade detected", ref autoDeposit))
        {
            config.AutoDepositFromTrades = autoDeposit;
            config.Save();
        }

        var autoTarget = config.AutoTargetEnabled;
        if (ImGui.Checkbox("Auto-target active player on their turn", ref autoTarget))
        {
            config.AutoTargetEnabled = autoTarget;
            config.Save();
        }

        var remindTarget = config.RemindTargetEnabled;
        if (ImGui.Checkbox("Target player before sending Remind message", ref remindTarget))
        {
            config.RemindTargetEnabled = remindTarget;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var chatEnabled = config.ChatEnabled;
        if (ImGui.Checkbox("Enable FFXIV chat (narration + rolls)", ref chatEnabled))
        {
            config.ChatEnabled = chatEnabled;
            config.Save();
        }

        if (chatEnabled)
        {
            ImGui.SameLine();
            var currentChannel = config.ChatChannel;
            var channelIdx     = Array.IndexOf(ChatChannels, currentChannel);
            if (channelIdx < 0) channelIdx = 3; // fallback to /p
            ImGui.SetNextItemWidth(80);
            if (ImGui.Combo("##chatchannel", ref channelIdx, ChatChannels, ChatChannels.Length))
            {
                config.ChatChannel = ChatChannels[channelIdx];
                config.Save();
            }

            var allowCross = config.AllowCrossChannelCommands;
            if (ImGui.Checkbox("Allow cross-channel commands in templates", ref allowCross))
            {
                config.AllowCrossChannelCommands = allowCross;
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When disabled, narration commands that target a channel other than the one\nselected above are redirected to /echo so only you see them.");

            var isPublic = currentChannel is "/say" or "/yell" or "/shout";
            if (isPublic)
            {
                var pub = config.PublicChatCooldownMs;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Time between messages (ms)##pubCooldown", ref pub, 100))
                {
                    config.PublicChatCooldownMs = Math.Clamp(pub, 100, 10000);
                    config.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Public channels: /say, /yell, /shout\nThese are rate limited more aggressively than private channels.");
            }
            else
            {
                var priv = config.PrivateChatCooldownMs;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Time between messages (ms)##privCooldown", ref priv, 100))
                {
                    config.PrivateChatCooldownMs = Math.Clamp(priv, 100, 10000);
                    config.Save();
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNarrationTemplates();
    }

    private void DrawVenueSelector()
    {
        var roundInProgress = config.GameState.Phase != TwentyOne.Game.GamePhase.Betting;

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Venue");
        ImGui.SameLine();

        var venueNames = config.Venues.ConvertAll(v => v.Name).ToArray();
        var idx = config.ActiveVenueIndex;
        ImGui.SetNextItemWidth(180);
        if (roundInProgress) ImGui.BeginDisabled();
        if (ImGui.Combo("##venue", ref idx, venueNames, venueNames.Length) && idx != config.ActiveVenueIndex)
        {
            if (Plugin.GetCurrentHousingAddressKey() is { } addrKey)
                config.VenueMemory[addrKey] = config.Venues[idx].Id.ToString();
            config.ActiveVenueIndex = idx;
            bankWindow.SyncBuffers();
            config.Save();
        }
        if (roundInProgress) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && roundInProgress)
            ImGui.SetTooltip("Cannot switch venues while a round is in progress."u8);

        ImGui.SameLine();
        if (ImGui.Button("+##venueAdd"))
        {
            config.Venues.Add(new VenueSettings { Name = $"Venue {config.Venues.Count + 1}" });
            if (!roundInProgress)
            {
                config.ActiveVenueIndex = config.Venues.Count - 1;
                bankWindow.SyncBuffers();
            }
            config.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a new venue."u8);

        ImGui.SameLine();
        if (ImGui.Button("Rename##venueRename"))
        {
            _renameBuffer  = config.ActiveVenue.Name;
            _renamePending = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Duplicate##venueDuplicate"))
        {
            _renameBuffer     = config.ActiveVenue.Name + " (copy)";
            _duplicatePending = true;
        }

        var canDelete = config.Venues.Count > 1 && ImGui.GetIO().KeyCtrl;
        ImGui.SameLine();
        if (!canDelete) ImGui.BeginDisabled();
        if (ImGui.Button("Delete##venueDelete") && canDelete)
        {
            var removeIdx  = config.ActiveVenueIndex;
            var removedGuid = config.Venues[removeIdx].Id.ToString();
            config.Venues.RemoveAt(removeIdx);
            config.ActiveVenueIndex = Math.Min(removeIdx, config.Venues.Count - 1);
            foreach (var k in config.VenueMemory.Keys.Where(k => config.VenueMemory[k] == removedGuid).ToList())
                config.VenueMemory.Remove(k);
            bankWindow.SyncBuffers();
            config.Save();
        }
        if (!canDelete) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (config.Venues.Count == 1)
                ImGui.SetTooltip("Cannot delete the only venue."u8);
            else if (!ImGui.GetIO().KeyCtrl)
                ImGui.SetTooltip("Hold Ctrl to delete this venue."u8);
        }

        // ── Rename popup ──────────────────────────────────────────────────────
        if (_renamePending)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 0), ImGuiCond.Always);
            ImGui.OpenPopup("Rename Venue##venueRenamePopup");
        }

        if (ImGui.BeginPopupModal("Rename Venue##venueRenamePopup", ref _renamePending, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            var confirmed = ImGui.InputText("##venueRenameInput", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
            if (confirmed || ImGui.Button("OK##venueRenameOK"))
            {
                if (!string.IsNullOrWhiteSpace(_renameBuffer))
                    config.ActiveVenue.Name = _renameBuffer.Trim();
                config.Save();
                _renamePending = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##venueRenameCancel"))
            {
                _renamePending = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ── Duplicate popup ───────────────────────────────────────────────────
        if (_duplicatePending)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 0), ImGuiCond.Always);
            ImGui.OpenPopup("Duplicate Venue##venueDuplicatePopup");
        }

        if (ImGui.BeginPopupModal("Duplicate Venue##venueDuplicatePopup", ref _duplicatePending, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            var confirmed = ImGui.InputText("##venueDuplicateInput", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
            if (confirmed || ImGui.Button("OK##venueDuplicateOK"))
            {
                if (!string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(config.ActiveVenue);
                    var copy = System.Text.Json.JsonSerializer.Deserialize<VenueSettings>(json)!;
                    copy.Name = _renameBuffer.Trim();
                    copy.Id   = Guid.NewGuid();
                    config.Venues.Add(copy);
                    if (!roundInProgress)
                    {
                        config.ActiveVenueIndex = config.Venues.Count - 1;
                        bankWindow.SyncBuffers();
                    }
                    config.Save();
                }
                _duplicatePending = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##venueDuplicateCancel"))
            {
                _duplicatePending = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawNarrationTemplates()
    {
        ImGui.TextDisabled("Narration Templates");
        ImGui.Spacing();

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.Button("Reset to Defaults##ntReset"))
        {
            config.NarrationTemplates = new();
            _narrationDirty = false;
            config.Save();
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset narration templates to defaults."u8);

        var shiftHeld = ImGui.GetIO().KeyShift;

        ImGui.SameLine();
        if (ImGui.Button("Export##ntExport"))
        {
            var json = JsonSerializer.Serialize(config.NarrationTemplates, new JsonSerializerOptions { WriteIndented = true });
            if (shiftHeld)
            {
                _fileDialogManager.SaveFileDialog(
                    "Export Narration Templates", "JSON{.json}", "narration-templates", ".json",
                    (ok, path) => { if (ok) File.WriteAllText(path, json); });
            }
            else
            {
                ImGui.SetClipboardText(json);
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shiftHeld
                ? "Save narration templates to a file."u8
                : "Copy narration templates to clipboard as JSON. Shift+click to save to file."u8);

        ImGui.SameLine();
        if (ImGui.Button("Import##ntImport"))
        {
            if (shiftHeld)
            {
                _fileDialogManager.OpenFileDialog(
                    "Import Narration Templates", "JSON{.json}",
                    (ok, path) =>
                    {
                        if (!ok) return;
                        try
                        {
                            var text = File.ReadAllText(path);
                            var imported = JsonSerializer.Deserialize<NarrationTemplates>(text);
                            if (imported != null) { config.NarrationTemplates = imported; _narrationDirty = false; config.Save(); }
                        }
                        catch { }
                    });
            }
            else
            {
                try
                {
                    var json = ImGui.GetClipboardText();
                    var imported = JsonSerializer.Deserialize<NarrationTemplates>(json);
                    if (imported != null) { config.NarrationTemplates = imported; _narrationDirty = false; config.Save(); }
                }
                catch { }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shiftHeld
                ? "Load narration templates from a file."u8
                : "Load narration templates from clipboard JSON. Shift+click to load from file."u8);

        ImGui.Spacing();
        var t = config.NarrationTemplates;
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;

        if (!ImGui.BeginTabBar("##ntTabs"))
            return;

        // ── Betting & Deal ────────────────────────────────────────────────────
        if (ImGui.BeginTabItem("Betting & Deal##ntTabBD"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Betting");
            ImGui.Spacing();
            NtListRow("Open##ntBO",         "",                                Defaults.BettingOpen,     ctrlHeld, t.BettingOpen);
            NtListRow("Bet req##ntBR",      "{name}",                          Defaults.PlayerBetRequest,    ctrlHeld, t.PlayerBetRequest);
            NtListRow("Bet confirm##ntBC",  "{name}  {amount}",                Defaults.PlayerBetConfirm,    ctrlHeld, t.PlayerBetConfirm);
            NtListRow("Bank remind##ntBKR",   "{name}  {amount}  {bank}", Defaults.PlayerBankRemind,    ctrlHeld, t.PlayerBankRemind);
            NtListRow("Bank short##ntBKS",    "{name}  {amount}",         Defaults.PlayerBankShortfall, ctrlHeld, t.PlayerBankShortfall);
            NtListRow("Bank deposit##ntBKD",  "{name}  {amount}  {bank}", Defaults.PlayerBankDeposit,   ctrlHeld, t.PlayerBankDeposit);
            NtListRow("Bank withdraw##ntBKW", "{name}  {amount}  {bank}", Defaults.PlayerBankWithdraw,  ctrlHeld, t.PlayerBankWithdraw);

            ImGui.Spacing();
            ImGui.TextDisabled("Deal announcements");
            ImGui.Spacing();
            NtListRow("Dealer##ntDAD",      "{dealer}",                        Defaults.DealDealerCard,  ctrlHeld, t.DealDealerCard);
            NtListRow("Player##ntDAP",      "{name}",                          Defaults.DealPlayerHand,  ctrlHeld, t.DealPlayerHand);

            ImGui.Spacing();
            ImGui.TextDisabled("Deal summary (components of a single message)");
            var skipOne = config.GameState.SkipDealSummaryOnePlayer;
            if (ImGui.Checkbox("Skip deal summary when only one player##skipDS1P", ref skipOne))
            {
                config.GameState.SkipDealSummaryOnePlayer = skipOne;
                config.Save();
            }
            if (ImGui.BeginTable("##ntDeal", 3, flags))
            {
                ImGui.TableSetupColumn("##ntDealLabel", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("##ntDealValue", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##ntDealReset", ImGuiTableColumnFlags.WidthFixed, 48);

                var v0 = t.DealSummaryPrefix;
                NtRow("Prefix##ntDP",      "(no variables)",                  Defaults.DealSummaryPrefix, ctrlHeld, ref v0);
                if (v0 != t.DealSummaryPrefix) { t.DealSummaryPrefix = v0; MarkNarrationDirty(); }

                var v1 = t.DealSummaryPlayer;
                NtRow("Per player##ntDPP", "{name}  {cards}  {score}  {bj}", Defaults.DealSummaryPlayer, ctrlHeld, ref v1);
                if (v1 != t.DealSummaryPlayer) { t.DealSummaryPlayer = v1; MarkNarrationDirty(); }

                var v2 = t.DealSummaryDealer;
                NtRow("Dealer##ntDD",      "{dealer}  {cards}",               Defaults.DealSummaryDealer, ctrlHeld, ref v2);
                if (v2 != t.DealSummaryDealer) { t.DealSummaryDealer = v2; MarkNarrationDirty(); }

                ImGui.EndTable();
            }

            ImGui.EndTabItem();
        }

        // ── Players ───────────────────────────────────────────────────────────
        if (ImGui.BeginTabItem("Players##ntTabP"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Players (player turns)");
            ImGui.Spacing();
            NtListRow("Turn start##ntPTS", "{name}  {cards}  {score}  {dealerCards}  {dealerScore}  {actions}", Defaults.PlayerTurnStart,   ctrlHeld, t.PlayerTurnStart);
            NtListRow("Hit##ntPHA",        "{name}",                                                            Defaults.PlayerHitAnnounce, ctrlHeld, t.PlayerHitAnnounce);
            NtListRow("Hit result##ntPH",  "{name}  {card}  {cards}  {score}",                                  Defaults.PlayerHit,         ctrlHeld, t.PlayerHit);
            NtListRow("After hit##ntPAH",  "{name}  {cards}  {score}  {actions}",                               Defaults.PlayerAfterHit,    ctrlHeld, t.PlayerAfterHit);
            NtListRow("Bust##ntPB",        "{name}  {cards}  {score}",                                          Defaults.PlayerBust,        ctrlHeld, t.PlayerBust);
            NtListRow("Blackjack##ntPBJ",       "{name}  {cards}",  Defaults.PlayerBJ,            ctrlHeld, t.PlayerBJ);
            NtListRow("BJ moving along##ntPBJMA", "{name}  {cards}", Defaults.PlayerBJMovingAlong, ctrlHeld, t.PlayerBJMovingAlong);
            NtListRow("Stand##ntPS",       "{name}  {cards}  {score}",                                          Defaults.PlayerStand,       ctrlHeld, t.PlayerStand);

            ImGui.EndTabItem();
        }

        // ── Double / Split ────────────────────────────────────────────────────
        if (ImGui.BeginTabItem("Double/Split##ntTabDS"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Double / Split");
            ImGui.Spacing();
            NtListRow("Dbl req##ntDR",     "{name}  {amount}",                 Defaults.PlayerDoubleRequest, ctrlHeld, t.PlayerDoubleRequest);
            NtListRow("Dbl confirm##ntDC", "{name}",                           Defaults.PlayerDoubleConfirm, ctrlHeld, t.PlayerDoubleConfirm);
            NtListRow("Dbl result##ntDD",  "{name}  {card}  {cards}  {score}", Defaults.PlayerDouble,        ctrlHeld, t.PlayerDouble);
            NtListRow("Spl req##ntSR",    "{name}  {amount}",                Defaults.PlayerSplitRequest,  ctrlHeld, t.PlayerSplitRequest);
            NtListRow("Split##ntSP",      "{name}",                          Defaults.PlayerSplit,         ctrlHeld, t.PlayerSplit);
            NtListRow("Spl roll##ntSPR",  "{name}",                          Defaults.PlayerSplitRoll,     ctrlHeld, t.PlayerSplitRoll);
            NtListRow("Split ace##ntSA",  "{name}  {card}  {cards}  {score}", Defaults.PlayerSplitAce,      ctrlHeld, t.PlayerSplitAce);

            ImGui.EndTabItem();
        }

        // ── Dealer ────────────────────────────────────────────────────────────
        if (ImGui.BeginTabItem("Dealer##ntTabD"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Dealer (dealer turn)");
            ImGui.Spacing();
            NtListRow("Turn start##ntDTS", "{dealer}  {cards}  {score}",         Defaults.DealerTurnStart,   ctrlHeld, t.DealerTurnStart);
            NtListRow("Hit##ntDHA",        "{dealer}",                           Defaults.DealerHitAnnounce, ctrlHeld, t.DealerHitAnnounce);
            NtListRow("BJ check##ntDBJC",  "(no variables)",                     Defaults.DealerBJCheck,     ctrlHeld, t.DealerBJCheck);
            NtListRow("Hit result##ntDH",  "{dealer}  {card}  {cards}  {score}", Defaults.DealerHit,         ctrlHeld, t.DealerHit);
            NtListRow("Bust##ntDB",        "{dealer}  {card}  {cards}  {score}", Defaults.DealerBust,        ctrlHeld, t.DealerBust);
            NtListRow("Blackjack##ntDBJ",  "{dealer}  {card}  {cards}",          Defaults.DealerBJ,          ctrlHeld, t.DealerBJ);
            NtListRow("Stand##ntDST",      "{dealer}  {cards}  {score}",         Defaults.DealerStand,       ctrlHeld, t.DealerStand);

            ImGui.EndTabItem();
        }

        // ── Payout ────────────────────────────────────────────────────────────
        if (ImGui.BeginTabItem("Payout##ntTabPay"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Payout");
            ImGui.Spacing();
            NtListRow("Header##ntPH",      "(no variables)",          Defaults.PayoutHeader,        ctrlHeld, t.PayoutHeader);
            NtListRow("Dlr Bust##ntPDB",   "{dealer}  {score}",      Defaults.PayoutDealerBust,    ctrlHeld, t.PayoutDealerBust);
            NtListRow("Dlr Stands##ntPDS", "{dealer}  {score}",      Defaults.PayoutDealerStands,  ctrlHeld, t.PayoutDealerStands);
            NtListRow("Win##ntPW",         "{name}  {bet}  {amount}", Defaults.PayoutWin,           ctrlHeld, t.PayoutWin);
            NtListRow("BJ Win##ntPBJ",     "{name}  {bet}  {amount}", Defaults.PayoutBjWin,         ctrlHeld, t.PayoutBjWin);
            NtListRow("Lose##ntPL",        "{name}  {bet}  {amount}", Defaults.PayoutLose,          ctrlHeld, t.PayoutLose);
            NtListRow("Push##ntPPush",     "{name}  {bet}",           Defaults.PayoutPush,          ctrlHeld, t.PayoutPush);
            NtListRow("Split win##ntPSW",  "{name}  {amount}",        Defaults.PayoutSplitCombined, ctrlHeld, t.PayoutSplitCombined);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void NtListRow(string id, string hint, System.Collections.Generic.List<string> defaultValue, bool ctrlHeld, System.Collections.Generic.List<string> value)
    {
        var label = id.Contains("##") ? id[..id.IndexOf("##", StringComparison.Ordinal)] : id;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (!string.IsNullOrEmpty(hint) && ImGui.IsItemHovered())
            ImGui.SetTooltip(hint);

        ImGui.SameLine();
        if (ImGui.SmallButton($"+##{id}Plus"))
        { value.Add(""); MarkNarrationDirty(); }

        ImGui.SameLine();
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Reset##{id}R"))
        { value.Clear(); value.AddRange(defaultValue); MarkNarrationDirty(); }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset this template to its default.");

        int? toRemove = null;
        (int A, int B)? swap = null;
        var style = ImGui.GetStyle();
        var upW   = ImGui.CalcTextSize("↑").X + style.FramePadding.X * 2;
        var downW = ImGui.CalcTextSize("↓").X + style.FramePadding.X * 2;
        var xW    = ImGui.CalcTextSize("X").X    + style.FramePadding.X * 2;
        var btnW  = upW + downW + xW + style.ItemSpacing.X * 3;
        for (var i = 0; i < value.Count; i++)
        {
            var line = value[i];
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - btnW);
            if (ImGui.InputText($"##{id}_{i}", ref line, 512))
            { value[i] = line; MarkNarrationDirty(); }

            ImGui.SameLine();
            if (i == 0) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"↑##{id}_{i}U")) swap = (i, i - 1);
            if (i == 0) ImGui.EndDisabled();

            ImGui.SameLine();
            if (i == value.Count - 1) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"↓##{id}_{i}D")) swap = (i, i + 1);
            if (i == value.Count - 1) ImGui.EndDisabled();

            ImGui.SameLine();
            if (!ctrlHeld) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"X##{id}_{i}X")) toRemove = i;
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
                ImGui.SetTooltip("Hold Ctrl to delete this line.");
        }
        if (swap.HasValue) { (value[swap.Value.A], value[swap.Value.B]) = (value[swap.Value.B], value[swap.Value.A]); MarkNarrationDirty(); }
        if (toRemove.HasValue) { value.RemoveAt(toRemove.Value); MarkNarrationDirty(); }

        ImGui.Spacing();
    }

    private static void NtRow(string id, string hint, string defaultValue, bool ctrlHeld, ref string value)
    {
        var label = id.Contains("##") ? id[..id.IndexOf("##", StringComparison.Ordinal)] : id;

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(hint);

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText($"##{id}", ref value, 512);

        ImGui.TableSetColumnIndex(2);
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Reset##{id}R"))
            value = defaultValue;
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset this template to its default.");
    }
}
