using System.Reflection;
using Backpack.Compatibility;
using Backpack.Config;
using Backpack.UI;
using MelonLoader;

#if SCHEDULE_I_BETA_BUILD
[assembly: MelonInfo(typeof(Backpack.BackpackMod), "Inventory Expanded - Backpack & Inventory QoL", "1.3.0-beta.47", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#else
[assembly: MelonInfo(typeof(Backpack.BackpackMod), "Inventory Expanded - Backpack & Inventory QoL", "1.3.1", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#endif
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]
[assembly: AssemblyMetadata("NexusModID", "2465")]

namespace Backpack;

public class BackpackMod : MelonMod
{
    private static int _sessionGeneration;
    private static bool _initialized;

    internal static int SessionGeneration => _sessionGeneration;

    public override void OnInitializeMelon()
    {
        if (LegacyConflictGuard.Detect(this))
            return;

        if (!Il2CppTypeRegistrar.TryRegisterRequiredTypes(out var registrationError))
        {
            Logger.Error($"[Bootstrap] Inventory Expanded custom IL2CPP type registration failed; runtime initialization was stopped: {registrationError}");
            return;
        }
#if IL2CPP
        Logger.Info("[Bootstrap] Registered required IL2CPP custom types after legacy conflict preflight: Backpack.PlayerBackpack.");
#endif
#if SCHEDULE_I_BETA_BUILD
        Logger.Info("[Compatibility] Schedule I 0.4.7 beta build: TryGetPlayerData -> LoadInventory player-data lifecycle enabled.");
#elif SCHEDULE_I_047
        Logger.Info("[Compatibility] Schedule I 0.4.7 MAIN build: TryGetPlayerData -> LoadInventory player-data lifecycle enabled.");
#endif

        Configuration.Instance.Load();
        Configuration.Instance.Save(); // Save the default config to force the creation of the config file
        HarmonyInstance.PatchAll(typeof(BackpackMod).Assembly);
        Logger.Info("Initialized. Open Backpack: {0}; capacity: {1}; contextual panel: {2}.",
            Configuration.Instance.ToggleKey,
            Configuration.Instance.StorageSlots,
            Configuration.Instance.ContextualBackpackEnabled ? "enabled" : "disabled");
        _initialized = true;
    }

    public override void OnPreferencesLoaded()
    {
        if (_initialized)
            Configuration.Instance.SynchronizeLivePreferences("MelonPreferences loaded");
    }

    public override void OnPreferencesSaved()
    {
        if (_initialized)
            Configuration.Instance.SynchronizeLivePreferences("MelonPreferences saved");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (!_initialized)
            return;

        Configuration.Instance.Reset();
        if (sceneName != "Main")
            return;

        ResetSessionUi("Main scene loaded");
        _sessionGeneration++;
        Logger.Diagnostic($"[Lifecycle] Main save session {_sessionGeneration} loaded (buildIndex={buildIndex}).");
        ConfigSyncManager.StartSync();
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (!_initialized || sceneName != "Main")
            return;

        Logger.Diagnostic($"[Lifecycle] Main save session {_sessionGeneration} unloading (buildIndex={buildIndex}).");
        ResetSessionUi("Main scene unloaded");
        PlayerBackpack.ResetSessionReference("Main scene unloaded");
    }

    public override void OnUpdate()
    {
        if (!_initialized)
        {
            LegacyConflictGuard.UpdatePrompt();
            return;
        }

        ResponsiveBackpackUI.Update();
        ContextualBackpackPanel.Update();
        PhoneBackpackUI.Update();
        StorageSortButton.Update();
        StorageHotkeys.Update();
        FilterConfigPanelEnhancements.Update();
    }

    private static void ResetSessionUi(string reason)
    {
#if SCHEDULE_I_047
        Patches.PlayerPatch.ResetPendingHydration(reason);
#endif
        ResponsiveBackpackUI.ResetSession(reason);
        ContextualBackpackPanel.ResetSession(reason);
        PhoneBackpackUI.ResetSession(reason);
        StorageSortButton.ResetSession();
        FilterConfigPanelEnhancements.ResetSession();
        BackpackSearchBar.ResetSession();
        UserSlotLockOverlay.ResetSession();
        UserSlotLockRegistry.ResetSession(reason);
        OvercapTransferContext.End();
        MagazineConsolidator.ResetSession();
        ConfigSyncManager.ResetSession();
    }

}
