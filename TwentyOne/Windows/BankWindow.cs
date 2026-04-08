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
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Flags = ImGuiWindowFlags.NoCollapse;

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

        var profit = config.GilEnd - config.GilStart;
        ImGui.Text($"Profit: {profit:N0} gil");

        ImGui.Separator();

        ImGui.AlignTextToFramePadding(); ImGui.Text("Venue Cut %"); ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        var cut = config.DealerCutPct;
        if (ImGui.InputInt("##dealercut", ref cut))
        {
            config.DealerCutPct = Math.Clamp(cut, 0, 100);
            config.Save();
        }

        var profitAfterTips = profit;

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

        ImGui.Separator();
        var split = profitAfterTips - tipTotal;
        var venueOwes   = split > 0 ? (long)Math.Floor(split * config.DealerCutPct / 100.0) : 0;
        var dealerKeeps = split > 0 ? split - venueOwes + tipTotal : tipTotal;
        ImGui.Text($"Dealer receives: {dealerKeeps:N0} gil");
        ImGui.Text($"Venue receives: {venueOwes:N0} gil");
    }
}
