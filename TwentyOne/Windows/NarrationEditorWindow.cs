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

namespace TwentyOne.Windows;

public class NarrationEditorWindow : Window
{
    private readonly Configuration config;

    public NarrationEditorWindow(Configuration config)
        : base("Twenty One - Narration Templates##NarrationEditor")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(500, 300), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    private static readonly NarrationTemplates Defaults = new();

    private bool   _narrationDirty;
    private (string Id, int Index)? _pendingVariantSelect;
    private (string Id, int Index, List<List<string>> Value)? _pendingVariantDelete;
    private double _narrationDirtyAt;
    private Action? _pendingImport;
    private readonly FileDialogManager _fileDialogManager = new();

    private void MarkNarrationDirty()
    {
        _narrationDirty   = true;
        _narrationDirtyAt = ImGui.GetTime();
    }

    public override void Draw()
    {
        _fileDialogManager.Draw();
        if (_narrationDirty && ImGui.GetTime() - _narrationDirtyAt > 1.0)
        {
            config.Save();
            _narrationDirty = false;
        }

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
                        _pendingImport = () =>
                        {
                            try
                            {
                                var text = File.ReadAllText(path);
                                var imported = JsonSerializer.Deserialize<NarrationTemplates>(text);
                                if (imported != null) { config.NarrationTemplates = imported; _narrationDirty = false; config.Save(); }
                            }
                            catch { /* Swallow import deserialization failures silently */ }
                        };
                    });
            }
            else
            {
                _pendingImport = () =>
                {
                    try
                    {
                        var json = ImGui.GetClipboardText();
                        var imported = JsonSerializer.Deserialize<NarrationTemplates>(json);
                        if (imported != null) { config.NarrationTemplates = imported; _narrationDirty = false; config.Save(); }
                    }
                    catch { /* Swallow import deserialization failures silently */ }
                };
                ImGui.OpenPopup("Confirm Import##ntImportConfirm");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shiftHeld
                ? "Load narration templates from a file."u8
                : "Load narration templates from clipboard JSON. Shift+click to load from file."u8);

        if (_pendingImport != null && !ImGui.IsPopupOpen("Confirm Import##ntImportConfirm"))
            ImGui.OpenPopup("Confirm Import##ntImportConfirm");

        if (ImGui.BeginPopupModal("Confirm Import##ntImportConfirm", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Overwrite all narration templates? This cannot be undone.");
            ImGui.Spacing();
            if (ImGui.Button("Overwrite"))
            {
                _pendingImport?.Invoke();
                _pendingImport = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _pendingImport = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.Spacing();
        var t = config.NarrationTemplates;
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;

        if (!ImGui.BeginTabBar("##ntTabs"))
            return;

        if (ImGui.BeginTabItem("Betting & Deal##ntTabBD"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Betting");
            ImGui.Spacing();
            NtListRow("Open##ntBO",         "",                                Defaults.BettingOpen,     ctrlHeld, t.BettingOpen);
            NtListRow("Bet req##ntBR",      "{name}",                          Defaults.PlayerBetRequest,    ctrlHeld, t.PlayerBetRequest);
            NtListRow("Bet confirm##ntBC",      "{name}  {amount}",                          Defaults.PlayerBetConfirm,    ctrlHeld, t.PlayerBetConfirm);
            NtListRow("Bet confirm (bank)##ntBCB", "{name}  {amount}  {bank}  {bank-after-bet}", Defaults.PlayerBetConfirmBank, ctrlHeld, t.PlayerBetConfirmBank);
            NtListRow("Bank remind##ntBKR",     "{name}  {amount}  {bank}",                   Defaults.PlayerBankRemind,   ctrlHeld, t.PlayerBankRemind);
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
                config.SetGameRule(s => s.SkipDealSummaryOnePlayer = skipOne);
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
            NtListRow("Surrender##ntPSR",  "{name}",                                                            Defaults.PlayerSurrender,   ctrlHeld, t.PlayerSurrender);
            NtListRow("Charlie##ntPC",     "{name}  {card}  {cards}  {score}",                                  Defaults.PlayerCharlie,     ctrlHeld, t.PlayerCharlie);

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Double/Split##ntTabDS"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Double / Split");
            ImGui.Spacing();
            NtListRow("Dbl req##ntDR",      "{name}  {amount}",                 Defaults.PlayerDoubleRequest,     ctrlHeld, t.PlayerDoubleRequest);
            NtListRow("Dbl req (bank)##ntDRB", "{name}  {amount}  {bank}",    Defaults.PlayerDoubleRequestBank, ctrlHeld, t.PlayerDoubleRequestBank);
            NtListRow("Dbl confirm##ntDC", "{name}",                           Defaults.PlayerDoubleConfirm,     ctrlHeld, t.PlayerDoubleConfirm);
            NtListRow("Dbl result##ntDD",  "{name}  {card}  {cards}  {score}", Defaults.PlayerDouble,            ctrlHeld, t.PlayerDouble);
            NtListRow("Spl req##ntSR",     "{name}  {amount}",                 Defaults.PlayerSplitRequest,      ctrlHeld, t.PlayerSplitRequest);
            NtListRow("Spl req (bank)##ntSRB", "{name}  {amount}  {bank}",    Defaults.PlayerSplitRequestBank,  ctrlHeld, t.PlayerSplitRequestBank);
            NtListRow("Split##ntSP",      "{name}",                          Defaults.PlayerSplit,         ctrlHeld, t.PlayerSplit);
            NtListRow("Spl roll##ntSPR",  "{name}",                          Defaults.PlayerSplitRoll,     ctrlHeld, t.PlayerSplitRoll);
            NtListRow("Split ace##ntSA",  "{name}  {card}  {cards}  {score}", Defaults.PlayerSplitAce,      ctrlHeld, t.PlayerSplitAce);

            ImGui.EndTabItem();
        }

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

        if (ImGui.BeginTabItem("Payout##ntTabPay"))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Payout");
            ImGui.Spacing();
            NtListRow("Header##ntPH",      "(no variables)",          Defaults.PayoutHeader,        ctrlHeld, t.PayoutHeader);
            NtListRow("Dlr Bust##ntPDB",   "{dealer}  {score}",      Defaults.PayoutDealerBust,    ctrlHeld, t.PayoutDealerBust);
            NtListRow("Dlr Stands##ntPDS", "{dealer}  {score}",      Defaults.PayoutDealerStands,  ctrlHeld, t.PayoutDealerStands);
            NtListRow("Win##ntPW",         "{name}  {bet}  {amount}", Defaults.PayoutWin,        ctrlHeld, t.PayoutWin);
            NtListRow("BJ Win##ntPBJ",     "{name}  {bet}  {amount}", Defaults.PayoutBjWin,      ctrlHeld, t.PayoutBjWin);
            NtListRow("Charlie##ntPCW",    "{name}  {bet}  {amount}", Defaults.PayoutCharlieWin, ctrlHeld, t.PayoutCharlieWin);
            NtListRow("Lose##ntPL",        "{name}  {bet}  {amount}", Defaults.PayoutLose,       ctrlHeld, t.PayoutLose);
            NtListRow("Push##ntPPush",     "{name}  {bet}",           Defaults.PayoutPush,       ctrlHeld, t.PayoutPush);
            NtListRow("Surrender##ntPSur", "{name}  {bet}  {amount}", Defaults.PayoutSurrender,  ctrlHeld, t.PayoutSurrender);
            NtListRow("Split win##ntPSW",  "{name}  {amount}",        Defaults.PayoutSplitCombined, ctrlHeld, t.PayoutSplitCombined);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void NtListRow(string id, string hint, List<List<string>> defaultValue, bool ctrlHeld, List<List<string>> value)
    {
        var label = id.Contains("##") ? id[..id.IndexOf("##", StringComparison.Ordinal)] : id;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (!string.IsNullOrEmpty(hint) && ImGui.IsItemHovered())
            ImGui.SetTooltip(hint);

        ImGui.SameLine();
        if (ImGui.SmallButton($"+##{id}AddVariant"))
        {
            value.Add(new List<string>(defaultValue.Count > 0 ? defaultValue[0] : [""]));
            _pendingVariantSelect = (id, value.Count - 1);
            MarkNarrationDirty();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a random variant"u8);

        var style = ImGui.GetStyle();
        var upW   = ImGui.CalcTextSize("\u2191").X + style.FramePadding.X * 2;
        var downW = ImGui.CalcTextSize("\u2193").X + style.FramePadding.X * 2;
        var xW    = ImGui.CalcTextSize("X").X  + style.FramePadding.X * 2;
        var btnW  = upW + downW + xW + style.ItemSpacing.X * 3;

        if (value.Count == 1)
        {
            DrawVariantLines(id, 0, value[0], defaultValue, ctrlHeld, btnW);
        }
        else if (value.Count > 1 && ImGui.BeginTabBar($"##{id}VarTabs"))
        {
            for (var vi = 0; vi < value.Count; vi++)
            {
                var open = true;
                var selectFlags = _pendingVariantSelect == (id, vi) || _pendingVariantDelete == (id, vi, value)
                    ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (vi == value.Count - 1) _pendingVariantSelect = null;
                if (ImGui.BeginTabItem($"Variant {vi + 1}##{id}V{vi}", ref open, selectFlags))
                {
                    DrawVariantLines(id, vi, value[vi], defaultValue, ctrlHeld, btnW);
                    ImGui.EndTabItem();
                }
                if (!open) _pendingVariantDelete = (id, vi, value);
            }
            ImGui.EndTabBar();
        }

        var popupId = $"Delete Variant?##{id}";
        if (_pendingVariantDelete.HasValue && _pendingVariantDelete.Value.Id == id)
            ImGui.OpenPopup(popupId);

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Delete this variant?");
            ImGui.Spacing();
            if (ImGui.Button("Delete", new Vector2(80, 0)))
            {
                if (_pendingVariantDelete is { } d) { d.Value.RemoveAt(d.Index); MarkNarrationDirty(); }
                _pendingVariantDelete = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                _pendingVariantDelete = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.Spacing();
    }

    private void DrawVariantLines(string id, int vi, List<string> lines, List<List<string>> defaultValue, bool ctrlHeld, float btnW)
    {
        int? toRemove = null;
        (int A, int B)? swap = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - btnW);
            if (ImGui.InputText($"##{id}_{vi}_{i}", ref line, 512))
            { lines[i] = line; MarkNarrationDirty(); }

            ImGui.SameLine();
            if (i == 0) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"\u2191##{id}_{vi}_{i}U")) swap = (i, i - 1);
            if (i == 0) ImGui.EndDisabled();

            ImGui.SameLine();
            if (i == lines.Count - 1) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"\u2193##{id}_{vi}_{i}D")) swap = (i, i + 1);
            if (i == lines.Count - 1) ImGui.EndDisabled();

            ImGui.SameLine();
            if (!ctrlHeld) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"X##{id}_{vi}_{i}X")) toRemove = i;
            if (!ctrlHeld) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
                ImGui.SetTooltip("Hold Ctrl to delete this line.");
        }
        if (swap.HasValue) { (lines[swap.Value.A], lines[swap.Value.B]) = (lines[swap.Value.B], lines[swap.Value.A]); MarkNarrationDirty(); }
        if (toRemove.HasValue) { lines.RemoveAt(toRemove.Value); MarkNarrationDirty(); }

        if (ImGui.SmallButton($"Add Message##{id}_{vi}AddLine"))
        { lines.Add(""); MarkNarrationDirty(); }

        ImGui.SameLine();
        if (!ctrlHeld) ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Reset to Default##{id}_{vi}R"))
        {
            var def = defaultValue.Count > 0 ? defaultValue[0] : (List<string>)[""];
            lines.Clear();
            lines.AddRange(def);
            MarkNarrationDirty();
        }
        if (!ctrlHeld) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ctrlHeld)
            ImGui.SetTooltip("Hold Ctrl to reset this variant to the default template.");
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
