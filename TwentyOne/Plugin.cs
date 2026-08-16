using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

    // Last outdoor housing territory seen - used to resolve indoor addresses.
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

    private static void OnTerritoryChanged(uint territory)
    {
        if (OutdoorHousingTerritories.Contains(territory))
            LastOutdoorHousingTerritoryId = territory;
    }

    // Polls the dealer's on-hand gil into config.GilEnd every frame (when logged
    // in), regardless of which windows are open. This keeps the reconciliation
    // (and the main-window drift chip) live even with the Session Ledger closed.
    // Each real change also writes a forensic 'wallet' checkpoint for the audit
    // log and feeds the reconciler. There is no on/off toggle - the wallet is
    // always tracked.
    private bool gilPollInitialized;
    // Continuous on-hand-gil reconciliation: trades feed RecordExpected (in
    // MainWindow), the poll below feeds Observe, and an unmatched delta is
    // surfaced to MainWindow as an "unexplained gil" prompt. Shared instance.
    internal readonly GilReconciler Reconciler = new();

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Runs unconditionally (and before the gil poll's login/inventory gates):
        // narration and card processing must keep flowing whether or not the main
        // window is being drawn. ImGui skips Draw() on a collapsed window, which is
        // why this cannot live there.
        MainWindow.Pump();
        PollGil();
    }

    private unsafe void PollGil()
    {
        // While not logged in the wallet reads 0 (or a stale value); polling it
        // produced the spurious 32M log-out/log-in deltas in the audit log. Skip
        // and re-baseline on next login so no bogus delta is observed.
        if (!ClientState.IsLoggedIn)
        {
            gilPollInitialized = false;
            return;
        }
        var mgr = InventoryManager.Instance();
        if (mgr == null) return;
        var liveGil = (long)mgr->GetGil();

        // First poll after enabling: adopt the live value as the baseline
        // without logging a (meaningless) delta against a stale GilEnd.
        if (!gilPollInitialized)
        {
            Configuration.GilEnd = liveGil;
            gilPollInitialized = true;
            return;
        }

        var prev = Configuration.GilEnd;
        // The reconciler only makes sense while a session is open. Between nights
        // the dealer trades, vendors, repairs and cashes out freely; feeding those
        // deltas in would queue unexplained-gil findings for gil that was never
        // part of a game. GilEnd keeps polling regardless, so the accumulated
        // non-game movement folds into the next session's baseline instead.
        // (This replaced an older gate on MainWindow.IsOpen - "is a session open"
        // is the honest signal for "is the dealer running the table".)
        // Findings raised while the main window is closed or collapsed simply queue
        // and surface on next draw - they are real in-game trades either way.
        var tableLive = Configuration.SessionOpen;
        if (liveGil != prev)
        {
            Configuration.GilEnd = liveGil;
            AuditLog.Wallet(Configuration.ActiveVenue.Id.ToString(), liveGil, liveGil - prev);
            if (tableLive)
                Reconciler.Observe(liveGil - prev, DateTime.Now);
        }

        // Tick every frame (even when gil is unchanged) so pending observations
        // age out into findings - but only while the table is live.
        if (tableLive)
            foreach (var finding in Reconciler.Tick(DateTime.Now))
                MainWindow.RaiseFinding(finding);
    }

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("TwentyOne");
    private MainWindow             MainWindow             { get; init; }
    private ConfigWindow           ConfigWindow           { get; init; }
    private SessionLedgerWindow    SessionLedgerWindow    { get; init; }
    private HistoryWindow          HistoryWindow          { get; init; }
    private NarrationEditorWindow  NarrationEditorWindow  { get; init; }
    private RulesEditorWindow      RulesEditorWindow      { get; init; }
#if DEBUG
    private DebugWindow            DebugWindow            { get; init; }
#endif

    public Plugin()
    {
        MigrateConfigFileIfNeeded();
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureVenues();

        // Defense-in-depth (cheap, runs every load): a live venue's RoundHistory
        // has unique RoundNumbers, so collapsing repeats can only remove
        // corruption, never real rounds. Bounds any future regression that might
        // re-duplicate the list.
        foreach (var venue in Configuration.Venues)
            ConfigMigrations.DedupRoundHistory(venue.RoundHistory);

        // Clear orphaned [JsonExtensionData] keys unless the config is from a
        // future schema version. SchemaVersion still holds the on-disk value here
        // (StampPluginVersion overwrites it below).
        if (Configuration.SchemaVersion <= ConfigMigrations.CurrentSchemaVersion)
        {
            try { ExtensionDataCleaner.ClearAll(Configuration); }
            catch (Exception ex) { Log.Error(ex, "ExtensionData cleanup failed; continuing."); }
        }
        StampPluginVersion();

        // Load each venue's archived sessions from disk. StatsSessions is
        // [JsonIgnore] on VenueSettings so the per-session JSON files under
        // {ConfigDirectory}/sessions/ are the canonical store.
        foreach (var venue in Configuration.Venues)
            venue.StatsSessions = SessionStore.LoadAll(venue.Id);

        AuditLog.Root = Path.Combine(PluginInterface.ConfigDirectory.FullName, "audit");

        SessionLedgerWindow   = new SessionLedgerWindow(Configuration);
        NarrationEditorWindow = new NarrationEditorWindow(Configuration);
        RulesEditorWindow     = new RulesEditorWindow(Configuration);
        ConfigWindow          = new ConfigWindow(Configuration, SessionLedgerWindow, NarrationEditorWindow, RulesEditorWindow);
        MainWindow            = new MainWindow(Configuration, ConfigWindow, SessionLedgerWindow, ChatGui, ObjectTable, ClientState, Reconciler);
        HistoryWindow       = new HistoryWindow(Configuration, MainWindow);
        MainWindow.SetHistoryWindow(HistoryWindow);
        SessionLedgerWindow.SetHistoryWindow(HistoryWindow);
#if DEBUG
        DebugWindow = new DebugWindow(Configuration, MainWindow);
        MainWindow.SetDebugWindow(DebugWindow);
#endif
        ClientState.TerritoryChanged += OnTerritoryChanged;
        ContextMenu.OnMenuOpened += OnMenuOpened;
        Framework.Update += OnFrameworkUpdate;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(SessionLedgerWindow);
        WindowSystem.AddWindow(HistoryWindow);
        WindowSystem.AddWindow(NarrationEditorWindow);
        WindowSystem.AddWindow(RulesEditorWindow);
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

    /// <summary>
    /// Reads the on-disk plugin config as a raw JObject, runs ConfigMigrations,
    /// and writes the migrated JSON back before Dalamud's strong-typed loader
    /// runs. On schema bumps, snapshots the pre-migration file as a sibling
    /// backup so the user has a recovery path if a migration corrupts state.
    /// No-op for fresh installs (no file on disk).
    /// </summary>
    private static void MigrateConfigFileIfNeeded()
    {
        var path = PluginInterface.ConfigFile.FullName;
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var root = JObject.Parse(json);
            var oldVersion = (int?)root["SchemaVersion"] ?? 0;
            if (oldVersion >= ConfigMigrations.CurrentSchemaVersion) return;

            var backup = path + $".bak-schema-{oldVersion}-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(path, backup, overwrite: false);

            ConfigMigrations.Migrate(root);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            Log.Information($"Migrated config schema {oldVersion} -> {ConfigMigrations.CurrentSchemaVersion}. Backup: {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config migration failed; leaving file as-is.");
        }
    }

    private void StampPluginVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
        Configuration.PluginVersion = version;
        Configuration.SchemaVersion = ConfigMigrations.CurrentSchemaVersion;
        foreach (var venue in Configuration.Venues)
            venue.LastModifiedPluginVersion = version;
        Configuration.Save();
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
        Framework.Update -= OnFrameworkUpdate;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        SessionLedgerWindow.Dispose();
        MainWindow.Dispose();
        // HistoryWindow has no IDisposable resources.

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
