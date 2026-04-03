using System;
using Dalamud.Configuration;

namespace TwentyOne;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
