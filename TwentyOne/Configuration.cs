using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using TwentyOne.Windows;

namespace TwentyOne;

[Serializable]
public class SavedHand
{
    public List<int> Cards { get; set; } = [];
    public HandState State { get; set; } = HandState.Playing;
}

[Serializable]
public class SavedPlayer
{
    public string Name { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public List<SavedHand> Hands { get; set; } = [];
}

[Serializable]
public class GameState
{
    public List<SavedPlayer> Players { get; set; } = [];
    public SavedHand DealerHand { get; set; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Betting;
    public int ActivePlayerIndex { get; set; } = -1;
    public BlackjackPayout BjPayout { get; set; } = BlackjackPayout.ThreeToTwo;
    public List<string> NarrationLog { get; set; } = [];
    public bool NarrationUseChannelCommand { get; set; } = false;
    public bool NarrationPanelOpen { get; set; } = true;
    public bool ChatEnabled { get; set; } = false;
    public string ChatChannel { get; set; } = "/p";
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public GameState GameState { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
