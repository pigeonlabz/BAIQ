using System.Reflection;
using Backpack.Compatibility;
using Backpack.Config;
using Backpack.UI;
using MelonLoader;

#if FISHNET_AUTH_DIAGNOSTICS
[assembly: MelonInfo(typeof(Backpack.BackpackMod), "Inventory Expanded - FishNet Authorization Diagnostic", "1.3.1-FISHNET-AUTH-DIAGNOSTIC", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#elif EXPERIMENTAL_UI_WORKSPACE
[assembly: MelonInfo(typeof(Backpack.BackpackMod), Backpack.BuildMetadata.MelonName, "1.3.2-exp.ui-workspace.7", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#elif MULTIPLAYER_PERSISTENCE_EXPERIMENTAL
[assembly: MelonInfo(typeof(Backpack.BackpackMod), "Inventory Expanded - Backpack & Inventory QoL", "1.3.1-MPTEST1", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#else
[assembly: MelonInfo(typeof(Backpack.BackpackMod), Backpack.BuildMetadata.MelonName, "1.3.3", "PigeonLabs", "https://www.nexusmods.com/schedule1/mods/2465")]
#endif
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]
[assembly: AssemblyMetadata("NexusModID", "2465")]

namespace Backpack;

internal static class BuildMetadata
{
    public const string MelonName = "Inventory Expanded - Backpack & Inventory QoL";
}

public class BackpackMod : MelonMod
{
    private static int _sessionGeneration;
    private static bool _initialized;

    internal static int SessionGeneration => _sessionGeneration;

    public override void OnInitializeMelon()
    {
#if FISHNET_AUTH_DIAGNOSTICS
        Logger.Warning("[FishNetAuthDiagnostic] Read-only Schedule I 0.4.7f12 server-RPC observer; no RPCs are blocked and inventory persistence is unchanged. Install only in an isolated two-client test with Diagnostic Logging enabled.");
#elif EXPERIMENTAL_UI_WORKSPACE
        Logger.Warning("[ExperimentalUIWorkspace] Development-only movable basic storage windows prototype; do not publish or install alongside another BAIQ DLL.");
#elif MULTIPLAYER_PERSISTENCE_EXPERIMENTAL
        Logger.Warning("[MPTEST1] Experimental host-authoritative guest Backpack persistence build; do not publish. Install only in a disposable two-client test environment.");
#elif MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
        Logger.Warning("[DiagnosticBuild] MAIN 1.3.1 multiplayer persistence tracing DLL; development use only, do not publish. Enable Diagnostic Logging on both peers.");
#endif
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
        Logger.Info("[Compatibility] Schedule I 0.4.7 MAIN build: TryGetPlayerData -> LoadInventory player-data lifecycle enabled.");

        Configuration.Instance.Load();
        Configuration.Instance.Save(); // Save the default config to force the creation of the config file
        HarmonyInstance.PatchAll(typeof(BackpackMod).Assembly);
#if IL2CPP
        Patches.PackagingStationsProductionPatch.ReportPatchRegistration(HarmonyInstance);
#endif
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
#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
        Patches.BackpackPersistenceDiagnostics.LogLifecycleEvent("main-scene-loaded", _sessionGeneration, sceneName);
#endif
        ConfigSyncManager.StartSync();
        GameplaySettingsSyncManager.Update(force: true);
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (!_initialized || sceneName != "Main")
            return;

        Logger.Diagnostic($"[Lifecycle] Main save session {_sessionGeneration} unloading (buildIndex={buildIndex}).");
#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
        Patches.BackpackPersistenceDiagnostics.LogLifecycleEvent("main-scene-unloading", _sessionGeneration, sceneName);
#endif
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

        GameplaySettingsSyncManager.Update();
#if EXPERIMENTAL_UI_WORKSPACE
        MovableStorageWindow.Update();
#endif
        var gameplayPolicyReady = GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures;
        // Phone presentation owns immediate app/phone visibility recovery. Keep
        // that harmless lifecycle work running while policy is pending; the
        // phone panel disables its raycaster and transfer augmentation until ready.
        PhoneBackpackUI.Update(gameplayPolicyReady);
        if (!gameplayPolicyReady)
        {
            LegacyConflictGuard.UpdatePrompt();
            return;
        }

        ResponsiveBackpackUI.Update();
        ContextualBackpackPanel.Update();
        StorageSortButton.Update();
        StorageHotkeys.Update();
        FilterConfigPanelEnhancements.Update();
    }

#if EXPERIMENTAL_UI_WORKSPACE
    public override void OnLateUpdate()
    {
        MovableStorageWindow.LateUpdate();
    }
#endif

    private static void ResetSessionUi(string reason)
    {
        GameplaySettingsSyncManager.OnMainSceneTransition(reason);
#if MULTIPLAYER_PERSISTENCE
        Patches.MultiplayerBackpackPersistence.ResetSession(reason);
#endif
#if SCHEDULE_I_047
        Patches.PlayerPatch.ResetPendingHydration(reason);
#endif
        ResponsiveBackpackUI.ResetSession(reason);
        ContextualBackpackPanel.ResetSession(reason);
        PhoneBackpackUI.ResetSession(reason);
        StorageSortButton.ResetSession();
#if EXPERIMENTAL_UI_WORKSPACE
        MovableStorageWindow.ResetSession("Main scene transition");
#endif
        FilterConfigPanelEnhancements.ResetSession();
        BackpackSearchBar.ResetSession();
        UserSlotLockOverlay.ResetSession();
        UserSlotLockRegistry.ResetSession(reason);
        OvercapTransferContext.End();
        MagazineConsolidator.ResetSession();
        ConfigSyncManager.ResetSession();
    }

    internal static void OnGameplayPolicyReadinessChanged(bool ready, string reason)
    {
        if (ready)
        {
            Logger.Info($"[GameplayPolicy] BAIQ inventory features are ready ({reason}).");
            return;
        }

        Logger.Warning($"[GameplayPolicy] BAIQ inventory features paused until host policy is verified ({reason}).");
        Patches.StorageMenuPatch.CloseBackpackForPolicy();
#if EXPERIMENTAL_UI_WORKSPACE
        MovableStorageWindow.ResetSession("host gameplay policy unavailable");
#endif
        ResponsiveBackpackUI.ResetSession("host gameplay policy unavailable");
        ContextualBackpackPanel.ResetSession("host gameplay policy unavailable");
        PhoneBackpackUI.SetPolicyReadiness(false, reason);
        StorageSortButton.ResetSession();
        FilterConfigPanelEnhancements.ResetSession();
        BackpackSearchBar.ResetSession();
        UserSlotLockOverlay.ResetSession();
        OvercapTransferContext.End();
        MagazineConsolidator.ResetSession();
    }

}
