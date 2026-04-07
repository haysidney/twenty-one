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

    private static readonly NarrationTemplates Defaults = new();

    private bool   _narrationDirty;
    private double _narrationDirtyAt;

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
        if (!ImGui.CollapsingHeader("Narration Templates##ntHeader"))
            return;

        ImGui.Spacing();
        ImGui.TextDisabled("Use {|} in any template to split it into multiple chat messages.");
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

        ImGui.Spacing();
        var t     = config.NarrationTemplates;
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;

        ImGui.Columns(2, "##ntColumns", false);

        // ── LEFT COLUMN ───────────────────────────────────────────────────────

        // ── Betting open ──────────────────────────────────────────────────────
        ImGui.TextDisabled("Betting");
        if (ImGui.BeginTable("##ntBetting", 3, flags))
        {
            ImGui.TableSetupColumn("##ntBettingLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntBettingValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntBettingReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var vb0 = t.BettingOpen;
            NtRow("Open##ntBO", "", Defaults.BettingOpen, ctrlHeld, ref vb0);
            if (vb0 != t.BettingOpen) { t.BettingOpen = vb0; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Deal announcements ────────────────────────────────────────────────
        ImGui.TextDisabled("Deal announcements");
        if (ImGui.BeginTable("##ntDealAnnounce", 3, flags))
        {
            ImGui.TableSetupColumn("##ntDALabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntDAValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntDAReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var vda0 = t.DealDealerCard;
            NtRow("Dealer##ntDAD",      "(no variables)", Defaults.DealDealerCard, ctrlHeld, ref vda0);
            if (vda0 != t.DealDealerCard) { t.DealDealerCard = vda0; MarkNarrationDirty(); }

            var vda1 = t.DealPlayerHand;
            NtRow("Player##ntDAP",      "{name}",         Defaults.DealPlayerHand, ctrlHeld, ref vda1);
            if (vda1 != t.DealPlayerHand) { t.DealPlayerHand = vda1; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Deal summary ──────────────────────────────────────────────────────
        ImGui.TextDisabled("Deal summary");
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
            NtRow("Dealer##ntDD",      "{cards}",                         Defaults.DealSummaryDealer, ctrlHeld, ref v2);
            if (v2 != t.DealSummaryDealer) { t.DealSummaryDealer = v2; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Players ───────────────────────────────────────────────────────────
        ImGui.TextDisabled("Players (player turns)");
        if (ImGui.BeginTable("##ntPlayers", 3, flags))
        {
            ImGui.TableSetupColumn("##ntPlayersLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntPlayersValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntPlayersReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var v0 = t.PlayerTurnStart;
            NtRow("Turn start##ntPTS",  "{name}  {dealerCards}  {dealerScore}  {actions}", Defaults.PlayerTurnStart, ctrlHeld, ref v0);
            if (v0 != t.PlayerTurnStart) { t.PlayerTurnStart = v0; MarkNarrationDirty(); }

            var v1 = t.PlayerHitAnnounce;
            NtRow("Hit##ntPHA",         "{name}",                                           Defaults.PlayerHitAnnounce, ctrlHeld, ref v1);
            if (v1 != t.PlayerHitAnnounce) { t.PlayerHitAnnounce = v1; MarkNarrationDirty(); }

            var v2 = t.PlayerHit;
            NtRow("Hit result##ntPH",   "{name}  {card}  {cards}  {score}",                Defaults.PlayerHit,      ctrlHeld, ref v2);
            if (v2 != t.PlayerHit)   { t.PlayerHit   = v2; MarkNarrationDirty(); }

            var v2b = t.PlayerAfterHit;
            NtRow("After hit##ntPAH",   "{name}  {cards}  {score}  {actions}",             Defaults.PlayerAfterHit, ctrlHeld, ref v2b);
            if (v2b != t.PlayerAfterHit) { t.PlayerAfterHit = v2b; MarkNarrationDirty(); }

            var v3 = t.PlayerBust;
            NtRow("Bust##ntPB",         "{name}  {cards}  {score}",                        Defaults.PlayerBust,  ctrlHeld, ref v3);
            if (v3 != t.PlayerBust)  { t.PlayerBust  = v3; MarkNarrationDirty(); }

            var v4 = t.PlayerBJ;
            NtRow("Blackjack##ntPBJ",   "{name}  {cards}",                                 Defaults.PlayerBJ,    ctrlHeld, ref v4);
            if (v4 != t.PlayerBJ)   { t.PlayerBJ    = v4; MarkNarrationDirty(); }

            var v5 = t.PlayerStand;
            NtRow("Stand##ntPS",        "{name}  {cards}  {score}",                        Defaults.PlayerStand, ctrlHeld, ref v5);
            if (v5 != t.PlayerStand) { t.PlayerStand = v5; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        // ── RIGHT COLUMN ──────────────────────────────────────────────────────
        ImGui.NextColumn();

        // ── Double / Split ────────────────────────────────────────────────────
        ImGui.TextDisabled("Double / Split");
        if (ImGui.BeginTable("##ntDblSpl", 3, flags))
        {
            ImGui.TableSetupColumn("##ntDSLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntDSValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntDSReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var v0 = t.PlayerDoubleRequest;
            NtRow("Dbl req##ntDR",    "{name}  {amount}", Defaults.PlayerDoubleRequest, ctrlHeld, ref v0);
            if (v0 != t.PlayerDoubleRequest) { t.PlayerDoubleRequest = v0; MarkNarrationDirty(); }

            var v1 = t.PlayerDouble;
            NtRow("Dbl result##ntDD", "{name}  {card}  {cards}  {score}", Defaults.PlayerDouble, ctrlHeld, ref v1);
            if (v1 != t.PlayerDouble) { t.PlayerDouble = v1; MarkNarrationDirty(); }

            var v2 = t.PlayerSplitRequest;
            NtRow("Spl req##ntSR",    "{name}  {amount}", Defaults.PlayerSplitRequest, ctrlHeld, ref v2);
            if (v2 != t.PlayerSplitRequest) { t.PlayerSplitRequest = v2; MarkNarrationDirty(); }

            var v3 = t.PlayerSplit;
            NtRow("Split##ntSP",      "{name}", Defaults.PlayerSplit, ctrlHeld, ref v3);
            if (v3 != t.PlayerSplit) { t.PlayerSplit = v3; MarkNarrationDirty(); }

            var v4 = t.PlayerSplitAce;
            NtRow("Split ace##ntSA",  "{name}  {card}  {cards}  {score}", Defaults.PlayerSplitAce, ctrlHeld, ref v4);
            if (v4 != t.PlayerSplitAce) { t.PlayerSplitAce = v4; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Dealer ────────────────────────────────────────────────────────────
        ImGui.TextDisabled("Dealer (dealer turn)");
        if (ImGui.BeginTable("##ntDealer", 3, flags))
        {
            ImGui.TableSetupColumn("##ntDealerLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntDealerValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntDealerReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var vts = t.DealerTurnStart;
            NtRow("Turn start##ntDTS", "{cards}  {score}", Defaults.DealerTurnStart, ctrlHeld, ref vts);
            if (vts != t.DealerTurnStart) { t.DealerTurnStart = vts; MarkNarrationDirty(); }

            var vh = t.DealerHitAnnounce;
            NtRow("Hit##ntDHA",       "(no variables)",            Defaults.DealerHitAnnounce, ctrlHeld, ref vh);
            if (vh != t.DealerHitAnnounce) { t.DealerHitAnnounce = vh; MarkNarrationDirty(); }

            var v0 = t.DealerHit;
            NtRow("Hit result##ntDH", "{card}  {cards}  {score}", Defaults.DealerHit,  ctrlHeld, ref v0);
            if (v0 != t.DealerHit)   { t.DealerHit  = v0; MarkNarrationDirty(); }

            var v1 = t.DealerBust;
            NtRow("Bust##ntDB",       "{card}  {cards}  {score}", Defaults.DealerBust, ctrlHeld, ref v1);
            if (v1 != t.DealerBust)  { t.DealerBust = v1; MarkNarrationDirty(); }

            var v2 = t.DealerBJ;
            NtRow("Blackjack##ntDBJ", "{card}  {cards}",          Defaults.DealerBJ,   ctrlHeld, ref v2);
            if (v2 != t.DealerBJ)   { t.DealerBJ   = v2; MarkNarrationDirty(); }

            var v3 = t.DealerStand;
            NtRow("Stand##ntDST",     "{cards}  {score}",         Defaults.DealerStand, ctrlHeld, ref v3);
            if (v3 != t.DealerStand) { t.DealerStand = v3; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Payout ────────────────────────────────────────────────────────────
        ImGui.TextDisabled("Payout");
        if (ImGui.BeginTable("##ntPayout", 3, flags))
        {
            ImGui.TableSetupColumn("##ntPayoutLabel", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("##ntPayoutValue", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ntPayoutReset", ImGuiTableColumnFlags.WidthFixed, 48);

            var v0 = t.PayoutDealerBust;
            NtRow("Dlr Bust##ntPDB",   "{score}",                            Defaults.PayoutDealerBust,   ctrlHeld, ref v0);
            if (v0 != t.PayoutDealerBust)   { t.PayoutDealerBust   = v0; MarkNarrationDirty(); }

            var v1 = t.PayoutDealerStands;
            NtRow("Dlr Stands##ntPDS", "{score}",                            Defaults.PayoutDealerStands, ctrlHeld, ref v1);
            if (v1 != t.PayoutDealerStands) { t.PayoutDealerStands = v1; MarkNarrationDirty(); }

            var v2 = t.PayoutPlayer;
            NtRow("Player##ntPP",      "{name}  {result}  {bet}  {amount}", Defaults.PayoutPlayer,       ctrlHeld, ref v2);
            if (v2 != t.PayoutPlayer)       { t.PayoutPlayer       = v2; MarkNarrationDirty(); }

            ImGui.EndTable();
        }

        ImGui.Columns(1);
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
