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
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
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
    }
}
