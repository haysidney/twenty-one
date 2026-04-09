using System;
using System.IO;
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

    public ConfigWindow(Configuration config)
        : base("Twenty One — Settings##TwentyOneConfig")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400, 200), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    private static readonly NarrationTemplates Defaults = new();

    private bool   _narrationDirty;
    private double _narrationDirtyAt;

    private readonly FileDialogManager _fileDialogManager = new();

    private void MarkNarrationDirty()
    {
        _narrationDirty   = true;
        _narrationDirtyAt = ImGui.GetTime();
    }

    private static readonly string[] ChatChannels =
    [
        "/say", "/yell", "/shout", "/p", "/a", "/fc", "/echo",
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

        var autoTarget = config.AutoTargetEnabled;
        if (ImGui.Checkbox("Auto-target active player on their turn", ref autoTarget))
        {
            config.AutoTargetEnabled = autoTarget;
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
            NtListRow("Bet req##ntBR",      "{name}",                          Defaults.PlayerBetRequest, ctrlHeld, t.PlayerBetRequest);
            NtListRow("Bet confirm##ntBC",  "{name}  {amount}",                Defaults.PlayerBetConfirm, ctrlHeld, t.PlayerBetConfirm);

            ImGui.Spacing();
            ImGui.TextDisabled("Deal announcements");
            ImGui.Spacing();
            NtListRow("Dealer##ntDAD",      "{dealer}",                        Defaults.DealDealerCard,  ctrlHeld, t.DealDealerCard);
            NtListRow("Player##ntDAP",      "{name}",                          Defaults.DealPlayerHand,  ctrlHeld, t.DealPlayerHand);

            ImGui.Spacing();
            ImGui.TextDisabled("Deal summary (components of a single message)");
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
            NtListRow("Blackjack##ntPBJ",  "{name}  {cards}",                                                   Defaults.PlayerBJ,          ctrlHeld, t.PlayerBJ);
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
