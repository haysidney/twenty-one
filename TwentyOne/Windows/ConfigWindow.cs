using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;
using TwentyOne.Game.Edge;

namespace TwentyOne.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly SessionLedgerWindow sessionLedgerWindow;
    private readonly NarrationEditorWindow narrationEditorWindow;

    public ConfigWindow(Configuration config, SessionLedgerWindow sessionLedgerWindow, NarrationEditorWindow narrationEditorWindow)
        : base("Twenty One - Settings##TwentyOneConfig")
    {
        this.config              = config;
        this.sessionLedgerWindow = sessionLedgerWindow;
        this.narrationEditorWindow = narrationEditorWindow;
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private string _renameBuffer     = string.Empty;
    private bool   _renamePending    = false;
    private bool   _duplicatePending = false;

    private double?   _cachedHouseEdge;
    private EdgeRules _cachedEdgeRules;

    private static readonly string[] ChatChannels =
    [
        "/say", "/yell", "/shout", "/p", "/a", "/fc",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8",
    ];

    public override void Draw()
    {
        DrawVenueSelector();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Blackjack Payout");
        ImGui.SameLine();
        var bjMul = (float)config.BjPayout;
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputFloat("##bjpayout", ref bjMul, 0f, 0f, "%.2fx"))
        {
            config.BjPayout = Math.Clamp(bjMul, 1.0f, 3.0f);
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("3:2##bj32")) { config.BjPayout = 1.5; config.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("6:5##bj65")) { config.BjPayout = 1.2; config.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("1:1##bj11")) { config.BjPayout = 1.0; config.Save(); }

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Five Card Charlie");
        ImGui.SameLine();
        var charlieOptions = new[] { "Disabled", "Beats all", "Loses to dealer BJ" };
        var charlieIdx     = (int)config.FiveCardCharlie;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##fiveCardCharlie", ref charlieIdx, charlieOptions, charlieOptions.Length))
        {
            config.FiveCardCharlie = (FiveCardCharlieRule)charlieIdx;
            config.Save();
        }

        if (config.FiveCardCharlie != FiveCardCharlieRule.Disabled)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Charlie Payout");
            ImGui.SameLine();
            var charliePayoutOptions = new[] { "3:2", "6:5", "1:1" };
            var charliePayoutIdx     = (int)config.CharliePayout;
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("##charliepayout", ref charliePayoutIdx, charliePayoutOptions, charliePayoutOptions.Length))
            {
                config.CharliePayout = (PayoutRatio)charliePayoutIdx;
                config.Save();
            }
        }

        var s17 = config.DealerStandsOnSoft17;
        if (ImGui.Checkbox("Dealer stands on soft 17", ref s17))
        {
            config.DealerStandsOnSoft17 = s17;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When off (default), dealer hits soft 17 (H17). When on, dealer stands on soft 17 (S17).\nApplies to the next round - mid-round changes do not affect the running round.");

        var das = config.DoubleAfterSplit;
        if (ImGui.Checkbox("Allow double after split", ref das))
        {
            config.DoubleAfterSplit = das;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When on (default), the player may double down on a hand created by splitting.\nWhen off, only non-split hands can be doubled.\nApplies to the next round.");

        var hsa = config.HitSplitAces;
        if (ImGui.Checkbox("Allow hitting split aces", ref hsa))
        {
            config.HitSplitAces = hsa;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When off (default), a split-ace hand receives exactly one extra card and auto-stands.\nWhen on, split-ace hands may be hit further.\n21 on a split-ace hand is still treated as Stand, not Blackjack.\nApplies to the next round.");

        var rsa = config.ResplitAces;
        if (ImGui.Checkbox("Allow resplitting aces", ref rsa))
        {
            config.ResplitAces = rsa;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When on, a pair of aces produced by an earlier split may be split again.\nWhen off (default), split-ace pairs cannot be resplit.\nApplies to the next round.");

        DrawHouseEdgeControl();

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

            var slash = config.SlashCommandCooldownMs;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Slash command delay (ms)##slashCooldown", ref slash, 100))
            {
                config.SlashCommandCooldownMs = Math.Clamp(slash, 100, 10000);
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("/random and /dice are rate-limited separately.\nThis delay applies when longer than the channel delay above.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Edit Narration Templates\u2026##openNarrationEditor"))
            narrationEditorWindow.Toggle();
    }

    private void DrawHouseEdgeControl()
    {
        var rules = new EdgeRules(
            config.BjPayout,
            config.CharliePayout,
            config.FiveCardCharlie,
            config.DealerStandsOnSoft17,
            config.DoubleAfterSplit,
            config.HitSplitAces,
            config.ResplitAces);

        if (_cachedHouseEdge.HasValue && !_cachedEdgeRules.Equals(rules))
            _cachedHouseEdge = null;

        if (ImGui.Button("Calculate House Edge##calcEdge"))
        {
            _cachedHouseEdge = EdgeSolver.ComputeHouseEdge(rules);
            _cachedEdgeRules = rules;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Computes the expected house edge for the current rule set,\nassuming optimal player strategy and infinite-deck draws.");

        if (_cachedHouseEdge.HasValue)
        {
            ImGui.SameLine();
            var edge = _cachedHouseEdge.Value;
            var pct  = edge * 100;
            var color = edge >= 0
                ? new Vector4(0.65f, 0.85f, 0.65f, 1f)   // house favored: green
                : new Vector4(0.95f, 0.55f, 0.55f, 1f); // player favored: red
            var label = edge >= 0
                ? $"House edge: {pct:F2}%"
                : $"Player edge: {-pct:F2}%";
            ImGui.TextColored(color, label);
        }
    }

    private void DrawVenueSelector()
    {
        var roundInProgress = config.GameState.IsRoundActive();

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
            sessionLedgerWindow.SyncBuffers();
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
                sessionLedgerWindow.SyncBuffers();
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
            sessionLedgerWindow.SyncBuffers();
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
                        sessionLedgerWindow.SyncBuffers();
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

}
