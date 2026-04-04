using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TwentyOne.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;

    public ConfigWindow(Configuration config)
        : base("Twenty One — Settings##TwentyOneConfig")
    {
        this.config = config;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Blackjack Payout");
        ImGui.SameLine();
        var bjOptions = new[] { "3:2", "6:5", "1:1" };
        var bjIdx = (int)config.GameState.BjPayout;
        ImGui.SetNextItemWidth(70);
        if (ImGui.Combo("##bjpayout", ref bjIdx, bjOptions, bjOptions.Length))
        {
            config.GameState.BjPayout = (BlackjackPayout)bjIdx;
            config.Save();
        }
    }
}
