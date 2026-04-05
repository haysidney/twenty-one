using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
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

    private static readonly string[] ChatChannels =
    [
        "/say", "/yell", "/shout", "/p", "/a", "/fc",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/ls1", "/ls2", "/ls3", "/ls4", "/ls5", "/ls6", "/ls7", "/ls8",
    ];

    public override void Draw()
    {
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
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNarrationTemplates();
    }

    private void DrawNarrationTemplates()
    {
        if (!ImGui.CollapsingHeader("Narration Templates##ntHeader"))
            return;

        ImGui.Spacing();
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.Button("Reset to Defaults##ntReset"))
        {
            config.NarrationTemplates = new();
            config.Save();
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset narration templates to defaults."u8);

        ImGui.Spacing();
        var t     = config.NarrationTemplates;
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;

        // ── Dealer ────────────────────────────────────────────────────────────
        ImGui.TextDisabled("Dealer (dealer turn)");
        if (ImGui.BeginTable("##ntDealer", 2, flags))
        {
            ImGui.TableSetupColumn("##ntDealerLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntDealerValue", ImGuiTableColumnFlags.WidthStretch);

            var v0 = t.DealerHit;
            NtRow("Hit##ntDH",        "{card}  {cards}  {score}", ref v0);
            if (v0 != t.DealerHit)   { t.DealerHit  = v0; config.Save(); }

            var v1 = t.DealerBust;
            NtRow("Bust##ntDB",       "{card}  {cards}  {score}", ref v1);
            if (v1 != t.DealerBust)  { t.DealerBust = v1; config.Save(); }

            var v2 = t.DealerBJ;
            NtRow("Blackjack##ntDBJ", "{card}  {cards}",          ref v2);
            if (v2 != t.DealerBJ)   { t.DealerBJ   = v2; config.Save(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Players ───────────────────────────────────────────────────────────
        ImGui.TextDisabled("Players (player turns)");
        if (ImGui.BeginTable("##ntPlayers", 2, flags))
        {
            ImGui.TableSetupColumn("##ntPlayersLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntPlayersValue", ImGuiTableColumnFlags.WidthStretch);

            var v0 = t.PlayerHit;
            NtRow("Hit##ntPH",        "{name}  {card}  {cards}  {score}", ref v0);
            if (v0 != t.PlayerHit)   { t.PlayerHit   = v0; config.Save(); }

            var v1 = t.PlayerBust;
            NtRow("Bust##ntPB",       "{name}  {cards}  {score}",         ref v1);
            if (v1 != t.PlayerBust)  { t.PlayerBust  = v1; config.Save(); }

            var v2 = t.PlayerBJ;
            NtRow("Blackjack##ntPBJ", "{name}  {cards}",                  ref v2);
            if (v2 != t.PlayerBJ)   { t.PlayerBJ    = v2; config.Save(); }

            var v3 = t.PlayerStand;
            NtRow("Stand##ntPS",      "{name}  {cards}  {score}",         ref v3);
            if (v3 != t.PlayerStand) { t.PlayerStand = v3; config.Save(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Deal summary ──────────────────────────────────────────────────────
        ImGui.TextDisabled("Deal summary");
        if (ImGui.BeginTable("##ntDeal", 2, flags))
        {
            ImGui.TableSetupColumn("##ntDealLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntDealValue", ImGuiTableColumnFlags.WidthStretch);

            var v0 = t.DealSummaryPrefix;
            NtRow("Prefix##ntDP",     "(no variables)",                           ref v0);
            if (v0 != t.DealSummaryPrefix) { t.DealSummaryPrefix = v0; config.Save(); }

            var v1 = t.DealSummaryPlayer;
            NtRow("Per player##ntDPP", "{name}  {cards}  {score}  {bj}",          ref v1);
            if (v1 != t.DealSummaryPlayer) { t.DealSummaryPlayer = v1; config.Save(); }

            var v2 = t.DealSummaryDealer;
            NtRow("Dealer##ntDD",     "{cards}",                                   ref v2);
            if (v2 != t.DealSummaryDealer) { t.DealSummaryDealer = v2; config.Save(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Payout ────────────────────────────────────────────────────────────
        ImGui.TextDisabled("Payout");
        if (ImGui.BeginTable("##ntPayout", 2, flags))
        {
            ImGui.TableSetupColumn("##ntPayoutLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntPayoutValue", ImGuiTableColumnFlags.WidthStretch);

            var v0 = t.PayoutDealerBust;
            NtRow("Dlr Bust##ntPDB",   "{score}",                                 ref v0);
            if (v0 != t.PayoutDealerBust)   { t.PayoutDealerBust   = v0; config.Save(); }

            var v1 = t.PayoutDealerStands;
            NtRow("Dlr Stands##ntPDS", "{score}",                                 ref v1);
            if (v1 != t.PayoutDealerStands) { t.PayoutDealerStands = v1; config.Save(); }

            var v2 = t.PayoutPlayer;
            NtRow("Player##ntPP",      "{name}  {result}  {bet}  {amount}",       ref v2);
            if (v2 != t.PayoutPlayer)       { t.PayoutPlayer       = v2; config.Save(); }

            ImGui.EndTable();
        }
    }

    /// <summary>Draws one label+input row inside a 2-column table.</summary>
    /// <param name="id">ImGui ID — the part before ## is shown as the label.</param>
    /// <param name="hint">Tooltip text describing available variables.</param>
    private static void NtRow(string id, string hint, ref string value)
    {
        // Extract display label (everything before ##)
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
    }
}
