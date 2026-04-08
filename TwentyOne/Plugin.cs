using System;
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

    private const string CommandName = "/twentyone";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("TwentyOne");
    private MainWindow       MainWindow       { get; init; }
    private ConfigWindow     ConfigWindow     { get; init; }
    private BankWindow BankWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(Configuration);
        BankWindow   = new BankWindow(Configuration);
        MainWindow   = new MainWindow(Configuration, ConfigWindow, BankWindow, ChatGui, ObjectTable, TargetManager);
        ContextMenu.OnMenuOpened += OnMenuOpened;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(BankWindow);
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
        ContextMenu.OnMenuOpened -= OnMenuOpened;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        BankWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
