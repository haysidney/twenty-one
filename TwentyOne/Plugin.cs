using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using TwentyOne.Game;
using TwentyOne.Windows;

namespace TwentyOne;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private const string CommandName = "/twentyone";

    // Outdoor housing territory IDs.
    private static readonly HashSet<uint> OutdoorHousingTerritories = [339, 340, 341, 641, 979];

    // Last outdoor housing territory seen — used to resolve indoor addresses.
    internal static uint LastOutdoorHousingTerritoryId { get; private set; }

    /// <summary>Returns the current housing address key, or null if not in a housing zone.</summary>
    internal static unsafe string? GetCurrentHousingAddressKey()
    {
        var territory = ClientState.TerritoryType;
        uint districtTerritory;
        if (OutdoorHousingTerritories.Contains(territory))
        {
            districtTerritory = territory;
        }
        else
        {
            var hmCheck = HousingManager.Instance();
            if (hmCheck == null || !hmCheck->IsInside()) return null;
            if (LastOutdoorHousingTerritoryId == 0) return null;
            districtTerritory = LastOutdoorHousingTerritoryId;
        }
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        var ward = hm->GetCurrentWard();
        var plot = hm->GetCurrentPlot();
        if (ward < 0 || plot < 0) return null;
        return $"{districtTerritory}:{ward + 1}:{plot + 1}";
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (OutdoorHousingTerritories.Contains(territory))
            LastOutdoorHousingTerritoryId = territory;
    }

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("TwentyOne");
    private MainWindow               MainWindow               { get; init; }
    private ConfigWindow             ConfigWindow             { get; init; }
    private BankWindow               BankWindow               { get; init; }
    private PlayerStatsWindow        PlayerStatsWindow        { get; init; }
    private PlayerStatsHistoryWindow PlayerStatsHistoryWindow { get; init; }
    private RoundHistoryWindow       RoundHistoryWindow       { get; init; }
#if DEBUG
    private DebugWindow              DebugWindow              { get; init; }
#endif

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureVenues();

        BankWindow               = new BankWindow(Configuration);
        ConfigWindow             = new ConfigWindow(Configuration, BankWindow);
        PlayerStatsWindow        = new PlayerStatsWindow(Configuration);
        PlayerStatsHistoryWindow = new PlayerStatsHistoryWindow(Configuration);
        PlayerStatsWindow.SetHistoryWindow(PlayerStatsHistoryWindow);
        MainWindow         = new MainWindow(Configuration, ConfigWindow, BankWindow, PlayerStatsWindow, ChatGui, ObjectTable, TargetManager, ClientState);
        RoundHistoryWindow = new RoundHistoryWindow(Configuration, MainWindow);
        MainWindow.SetRoundHistoryWindow(RoundHistoryWindow);
#if DEBUG
        DebugWindow = new DebugWindow(Configuration, MainWindow);
        MainWindow.SetDebugWindow(DebugWindow);
#endif
        ClientState.TerritoryChanged += OnTerritoryChanged;
        ContextMenu.OnMenuOpened += OnMenuOpened;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(BankWindow);
        WindowSystem.AddWindow(PlayerStatsWindow);
        WindowSystem.AddWindow(PlayerStatsHistoryWindow);
        WindowSystem.AddWindow(RoundHistoryWindow);
#if DEBUG
        WindowSystem.AddWindow(DebugWindow);
#endif
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Twenty One blackjack table."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        Log.Information("Twenty One plugin loaded!");
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default) return;
        if (args.Target is not MenuTargetDefault target) return;
        if (Phase != GamePhase.Betting) return;

        string name, world;
        if (args.AddonName == "Social" && target.TargetCharacter is { } character)
        {
            name  = character.Name;
            world = character.HomeWorld.Value.Name.ToString();
        }
        else if (target.TargetHomeWorld.RowId != 0)
        {
            name  = target.TargetName;
            world = target.TargetHomeWorld.Value.Name.ToString();
        }
        else return;

        if (Configuration.GameState.Players.Any(p => p.FullName == name && p.World == world)) return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Add to Blackjack Table",
            OnClicked = _ => MainWindow.AddPlayerFromContext(name, world)
        });
    }

    private GamePhase Phase => Configuration.GameState.Phase;

    public static bool TargetPlayer(string fullName, string world)
    {
        foreach (var obj in ObjectTable.PlayerObjects)
        {
            if (obj is IPlayerCharacter player &&
                player.Name.TextValue == fullName &&
                player.HomeWorld.Value.Name.ToString() == world)
            {
                TargetManager.Target = player;
                return true;
            }
        }
        return false;
    }

    public static void TradePlayer(string fullName, string world)
    {
        uint entityId = 0;
        bool alreadyTargeted = false;
        foreach (var obj in ObjectTable.PlayerObjects)
        {
            if (obj is IPlayerCharacter player &&
                player.Name.TextValue == fullName &&
                player.HomeWorld.Value.Name.ToString() == world)
            {
                alreadyTargeted = TargetManager.Target?.EntityId == player.EntityId;
                if (!alreadyTargeted)
                    TargetManager.Target = player;
                entityId = player.EntityId;
                break;
            }
        }
        if (entityId == 0) return;

        if (alreadyTargeted)
            Framework.RunOnFrameworkThread(OpenTrade(entityId));
        else
            Task.Delay(1000).ContinueWith(_ =>
                Framework.RunOnFrameworkThread(OpenTrade(entityId)));
    }

    private static unsafe Action OpenTrade(uint entityId) => () =>
        InventoryManager.Instance()->SendTradeRequest(entityId);

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ContextMenu.OnMenuOpened -= OnMenuOpened;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        BankWindow.Dispose();
        PlayerStatsWindow.Dispose();
        MainWindow.Dispose();
        // PlayerStatsHistoryWindow and RoundHistoryWindow have no IDisposable resources.

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
