using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace TwentyOne.Windows;

public unsafe class BankWindow : Window, IDisposable
{
    private readonly Configuration config;

    private string gilStartBuf = string.Empty;
    private string gilEndBuf   = string.Empty;
    private string tipBuf      = string.Empty;

    public BankWindow(Configuration config)
        : base("Bank##TwentyOneBank")
    {
        this.config = config;
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        SyncBuffers();
    }

    public void SyncBuffers()
    {
        gilStartBuf = config.GilStart.ToString();
        gilEndBuf   = config.GilEnd.ToString();
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Starting Gil"); ImGui.SameLine(110);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##gilstart", ref gilStartBuf, 20))
        {
            if (long.TryParse(gilStartBuf, out var v)) { config.GilStart = v; config.Save(); }
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
        if (ImGui.InputText("##gilend", ref gilEndBuf, 20))
        {
            if (long.TryParse(gilEndBuf, out var v)) { config.GilEnd = v; config.Save(); }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Current##end"))
        {
            var gil = (long)InventoryManager.Instance()->GetGil();
            config.GilEnd = gil;
            gilEndBuf = gil.ToString();
            config.Save();
        }

        var difference = config.GilEnd - config.GilStart;

        ImGui.Separator();

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
        if (addTip)
        {
            if (long.TryParse(tipBuf, out var tip) && tip != 0)
            {
                config.Tips.Add(tip);
                config.Save();
                tipBuf = string.Empty;
            }
        }

        var profit = difference - tipTotal;
        ImGui.Separator();
        var venueOwes   = profit > 0 ? (long)Math.Floor(profit * config.DealerCutPct / 100.0) : 0;
        var dealerKeeps = profit > 0 ? profit - venueOwes + tipTotal : tipTotal;
        CopyableGilRow("Difference", difference);
        CopyableGilRow("Profit", profit);
        ImGui.Separator();
        CopyableGilRow("Dealer receives", dealerKeeps);
        CopyableGilRow("Venue receives", venueOwes);
    }

    private static void CopyableGilRow(string label, long value)
    {
        ImGui.Text($"{label}: {value:N0} gil");
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText(value.ToString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to copy"u8);
    }
}
