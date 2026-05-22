using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly SessionLedgerWindow sessionLedgerWindow;
    private readonly NarrationEditorWindow narrationEditorWindow;
    private readonly RulesEditorWindow rulesEditorWindow;

    public ConfigWindow(Configuration config, SessionLedgerWindow sessionLedgerWindow, NarrationEditorWindow narrationEditorWindow, RulesEditorWindow rulesEditorWindow)
        : base("Twenty One - Settings##TwentyOneConfig")
    {
        this.config                = config;
        this.sessionLedgerWindow   = sessionLedgerWindow;
        this.narrationEditorWindow = narrationEditorWindow;
        this.rulesEditorWindow     = rulesEditorWindow;
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
        ImGui.Text("Dealer Name");
        ImGui.SameLine();
        var dealerName = config.DealerName;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputTextWithHint("##dealerName", "name", ref dealerName, 64))
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

        if (ImGui.Button("Edit Blackjack Rules\u2026##openRulesEditor"))
            rulesEditorWindow.Toggle();
        ImGui.SameLine();
        if (ImGui.Button("Edit Narration Templates\u2026##openNarrationEditor"))
            narrationEditorWindow.Toggle();
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
            var confirmed = ImGui.InputTextWithHint("##venueRenameInput", "new name", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
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
            var confirmed = ImGui.InputTextWithHint("##venueDuplicateInput", "new name", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
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
