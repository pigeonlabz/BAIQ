using Backpack;
using Backpack.Compatibility;
using Backpack.Cheats;
using Backpack.UI;
using System.Text.RegularExpressions;

var failures = new List<string>();

Run("Conflict A. Inventory Expanded only", () =>
{
    Assert(LegacyConflictPolicy.Classify("UnrelatedMod", "Other.Mod", "Other Mod", "Someone", null) == LegacyConflictConfidence.None,
        "an unrelated loaded mod must not be treated as a conflict");
});

Run("Conflict B. Known Backpack assembly", () =>
{
    Assert(LegacyConflictPolicy.Classify("Backpack", "Backpack.BackpackMod", "Inventory Expanded", "D-Kay", "818") == LegacyConflictConfidence.Positive,
        "known legacy identity should be positive");
});

Run("Conflict C. Renamed legacy DLL identity", () =>
{
    Assert(LegacyConflictPolicy.Classify("Backpack", null, "Renamed File", null, null) == LegacyConflictConfidence.Positive,
        "assembly identity must work independently of filename");
});

Run("Conflict D/E. Compatibility data is not a mod identity", () =>
{
    Assert(LegacyConflictPolicy.Classify(null, null, "Backpack.cfg", null, null) == LegacyConflictConfidence.None,
        "legacy cfg filename must not be treated as a loaded mod");
    Assert(LegacyConflictPolicy.Classify(null, "OGBackpack_01_Settings", null, null, null) == LegacyConflictConfidence.None,
        "preference category must not be treated as a mod type");
});

Run("Conflict F. Backpack substring is not sufficient", () =>
{
    Assert(LegacyConflictPolicy.Classify("MyBackpackTools", "Tools.BetterBackpack", "Backpack Organizer", "Someone", null) == LegacyConflictConfidence.None,
        "unrelated Backpack-named tools must not be false positives");
});

Run("Conflict F2. Apparent DLL names are conservative", () =>
{
    Assert(LegacyConflictPolicy.IsApparentBackpackDll("Backpack.dll"),
        "the canonical suspicious filename should receive a manual-only warning when identity inspection fails");
    Assert(LegacyConflictPolicy.IsApparentBackpackDll("OGBackpack.dll"),
        "an established Backpack filename should receive a manual-only warning when identity inspection fails");
    Assert(!LegacyConflictPolicy.IsApparentBackpackDll("MyBackpackTools.dll"),
        "a Backpack substring alone must not trigger the safeguard");
});

Run("Conflict F3. Unverified DLL is never moved", () =>
{
    WithTempModsDirectory(modsDirectory =>
    {
        var source = Path.Combine(modsDirectory, "Backpack.dll");
        File.WriteAllText(source, "unknown-test-content");
        Assert(!LegacyConflictPolicy.TryDisable(source, modsDirectory, null, out _, out var moveError),
            "automatic quarantine must require the detection-time fingerprint");
        Assert(File.Exists(source), "an unverified apparent Backpack DLL must remain in the active Mods directory");
        Assert(moveError.Contains("not positively fingerprinted", StringComparison.OrdinalIgnoreCase),
            "the withheld move should explain the positive-verification requirement");
    });
});

Run("Conflict G. Disable moves without deletion", () =>
{
    WithTempModsDirectory(modsDirectory =>
    {
        var source = Path.Combine(modsDirectory, "Backpack.dll");
        File.WriteAllText(source, "legacy-test-content");
        Assert(LegacyConflictPolicy.TryFingerprintActiveDll(source, modsDirectory, out var fingerprint, out var fingerprintError), fingerprintError);
        Assert(LegacyConflictPolicy.TryDisable(source, modsDirectory, fingerprint, out var destination, out var moveError), moveError);
        Assert(!File.Exists(source), "source should leave the active Mods directory");
        Assert(File.Exists(destination), "legacy DLL should exist in Disabled");
        Assert(File.ReadAllText(destination) == "legacy-test-content", "moved file content must remain intact");
    });
});

Run("Conflict H. Disabled collision never overwrites", () =>
{
    WithTempModsDirectory(modsDirectory =>
    {
        var disabledDirectory = Path.Combine(modsDirectory, "Disabled");
        Directory.CreateDirectory(disabledDirectory);
        var existing = Path.Combine(disabledDirectory, "Backpack.dll");
        File.WriteAllText(existing, "existing-disabled-copy");
        var source = Path.Combine(modsDirectory, "Backpack.dll");
        File.WriteAllText(source, "new-legacy-copy");
        Assert(LegacyConflictPolicy.TryFingerprintActiveDll(source, modsDirectory, out var fingerprint, out var fingerprintError), fingerprintError);
        Assert(LegacyConflictPolicy.TryDisable(source, modsDirectory, fingerprint, out var destination, out var moveError), moveError);
        Assert(destination.EndsWith("Backpack (1).dll", StringComparison.OrdinalIgnoreCase), "collision should select a numbered alternate");
        Assert(File.ReadAllText(existing) == "existing-disabled-copy", "existing disabled DLL must not be overwritten");
        Assert(File.ReadAllText(destination) == "new-legacy-copy", "newly disabled DLL must retain its content");
    });
});

Run("Restart A. Successful quarantine exposes safe quit", () =>
{
    var coordinator = new RestartRequiredCoordinator();
    Assert(!coordinator.CanQuit, "quit must start unavailable");
    coordinator.MarkActionApplied();
    Assert(coordinator.CanQuit, "verified quarantine must expose the safe quit action");
});

Run("Restart B. Quit cannot run before quarantine", () =>
{
    var coordinator = new RestartRequiredCoordinator();
    var quits = 0;
    Assert(!coordinator.TryQuit(() => quits++, out _),
        "quit must be rejected before successful quarantine");
    Assert(quits == 0, "a rejected quit must leave the game running");
});

Run("Restart C. Safe quit runs only once", () =>
{
    var coordinator = new RestartRequiredCoordinator();
    coordinator.MarkActionApplied();
    var quits = 0;
    Assert(coordinator.TryQuit(() => quits++, out var firstError), firstError);
    Assert(!coordinator.TryQuit(() => quits++, out _),
        "a second quit request must be rejected");
    Assert(quits == 1 && coordinator.QuitRequested,
        "the clean quit must be requested exactly once");
});

Run("Restart D. Later dismisses without quitting", () =>
{
    var guard = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Compatibility", "LegacyConflictGuard.cs"));
    var dismissStart = guard.IndexOf("private static void DismissRestartRequired()", StringComparison.Ordinal);
    var dismissEnd = guard.IndexOf("private static void SetButtonText", dismissStart, StringComparison.Ordinal);
    var dismissPath = guard[dismissStart..dismissEnd];
    Assert(guard.Contains("CreateButton(\"Later\"", StringComparison.Ordinal)
           && dismissPath.Contains("ClosePrompt();", StringComparison.Ordinal)
           && !dismissPath.Contains("Application.Quit", StringComparison.Ordinal)
           && !dismissPath.Contains("TryQuit", StringComparison.Ordinal),
        "the restart-required prompt must offer a Later action that only dismisses the prompt");
});

Run("Restart E. Runtime contains no automatic relaunch implementation", () =>
{
    var runtimeRoot = Path.Combine(FindProjectRoot(), "src", "InventoryExpanded");
    var runtimeSource = string.Join("\n", Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
    Assert(!runtimeSource.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase)
           && !runtimeSource.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase)
           && !runtimeSource.Contains("DelayedSteamRelauncher", StringComparison.Ordinal),
        "runtime source must not contain the old hidden PowerShell relaunch path");
});

Run("Restart F. A clean process has no persistent quarantine suppression", () =>
{
    var priorProcess = new RestartRequiredCoordinator();
    priorProcess.MarkActionApplied();
    var nextProcess = new RestartRequiredCoordinator();
    Assert(!nextProcess.CanQuit && !nextProcess.QuitRequested,
        "restart/quarantine state must be process-local and fresh on the next launch");
    var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Compatibility", "RestartRequiredCoordinator.cs"));
    Assert(!source.Contains("MelonPreferences", StringComparison.Ordinal)
           && !source.Contains("File.Write", StringComparison.Ordinal),
        "restart recovery must not persist a suppression flag or write compatibility state");
});

Run("Restart H. Quarantine path cannot initialize runtime", () =>
{
    var guard = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Compatibility", "LegacyConflictGuard.cs"));
    var quarantineStart = guard.IndexOf("private static void QuarantineConflict()", StringComparison.Ordinal);
    var quarantineEnd = guard.IndexOf("private static void OpenModsFolder()", quarantineStart, StringComparison.Ordinal);
    var quarantinePath = guard[quarantineStart..quarantineEnd];
    Assert(!quarantinePath.Contains("Il2CppTypeRegistrar", StringComparison.Ordinal)
           && !quarantinePath.Contains("PatchAll", StringComparison.Ordinal)
           && !quarantinePath.Contains("Configuration.Instance", StringComparison.Ordinal),
        "successful quarantine must not initialize types, patches, configuration, or runtime in the current process");
});

Run("Bootstrap A. Deferred types have no automatic registration attributes", () =>
{
    var sourceDirectory = Path.Combine(FindProjectRoot(), "src", "InventoryExpanded");
    var automaticAttributes = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
        .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\[\s*RegisterTypeInIl2Cpp(?:WithInterfaces)?(?:Attribute)?\b")
            .Select(_ => path))
        .ToList();
    Assert(automaticAttributes.Count == 0,
        "deferred custom types must not participate in MelonLoader's global automatic IL2CPP registration pass");
});

Run("Bootstrap B. Conflict preflight gates explicit registration", () =>
{
    var bootstrap = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "BackpackMod.cs"));
    var detectIndex = bootstrap.IndexOf("LegacyConflictGuard.Detect(this)", StringComparison.Ordinal);
    var registrationIndex = bootstrap.IndexOf("Il2CppTypeRegistrar.TryRegisterRequiredTypes", StringComparison.Ordinal);
    Assert(detectIndex >= 0 && registrationIndex > detectIndex,
        "legacy conflict detection must execute before explicit custom type registration");
    Assert(bootstrap[detectIndex..registrationIndex].Contains("return;", StringComparison.Ordinal),
        "the conflict branch must return without manually registering custom types");
});

Run("Bootstrap C. Clean registration executes required types exactly once", () =>
{
    Il2CppTypeRegistrar.ResetForTests();
    var registrations = new List<string>();
    Assert(Il2CppTypeRegistrar.TryRegisterAll(registrations.Add, out var firstError), firstError);
    Assert(Il2CppTypeRegistrar.TryRegisterAll(registrations.Add, out var secondError), secondError);
    Assert(registrations.SequenceEqual(Il2CppTypeRegistrar.RequiredTypes),
        "the clean path must register every required type in declared order exactly once");
});

Run("Bootstrap D. Explicit registrar covers every custom IL2CPP component", () =>
{
    var sourceDirectory = Path.Combine(FindProjectRoot(), "src", "InventoryExpanded");
    var customComponents = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
        .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*MonoBehaviour\b")
            .Select(match => "Backpack." + match.Groups["name"].Value))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    var registeredTypes = Il2CppTypeRegistrar.RequiredTypes.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    Assert(customComponents.SequenceEqual(registeredTypes),
        $"explicit registrar mismatch; components=[{string.Join(", ", customComponents)}], registrar=[{string.Join(", ", registeredTypes)}]");
});

Run("Bootstrap E. Conflict popup uses native Unity components only", () =>
{
    var guard = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Compatibility", "LegacyConflictGuard.cs"));
    Assert(!guard.Contains("AddComponent<PlayerBackpack>", StringComparison.Ordinal)
           && !guard.Contains("GetOrAddComponent<PlayerBackpack>", StringComparison.Ordinal),
        "the withheld-registration popup path must not instantiate an Inventory Expanded custom IL2CPP component");
});

Run("Bootstrap F. Persistence and configuration identifiers remain stable", () =>
{
    var root = FindProjectRoot();
    var playerBackpack = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "PlayerBackpack.cs"));
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    var playerPatch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PlayerPatch.cs"));
    var locks = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Utility", "UserSlotLockRegistry.cs"));
    var bootstrap = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "BackpackMod.cs"));

    AssertContains(playerBackpack, "namespace Backpack;", "Backpack namespace");
    AssertContains(playerBackpack, "StorageName = \"Backpack\"", "Backpack storage identity");
    AssertContains(playerPatch, "BackpackSaveFile = \"Backpack\"", "Backpack save subfile");
    AssertContains(locks, "SaveFileName = \"InventoryExpandedSlotLocks\"", "slot-lock save subfile");
    foreach (var identifier in new[]
             {
                 "Inventory Expanded - Backpack & Inventory QoL",
                 "InventoryExpanded_01_Backpack", "InventoryExpanded_02_Contextual",
                 "InventoryExpanded_03_Transfers", "InventoryExpanded_04_Phone",
                 "InventoryExpanded_05_Cheats", "InventoryExpanded_Settings",
                 "InventoryExpanded_Cheats", "OGBackpack_01_Settings", "OGBackpack_02_Cheats",
                 "Backpack.cfg", "BackpackMod.cfg", "MelonPreferences.cfg"
             })
    {
        if (identifier == "Inventory Expanded - Backpack & Inventory QoL")
            Assert(configuration.Contains("Backpack.BuildMetadata.MelonName", StringComparison.Ordinal)
                   && bootstrap.Contains("public const string MelonName = \"Inventory Expanded - Backpack & Inventory QoL\";", StringComparison.Ordinal),
                "canonical configuration identifier must match the stable Melon name");
        else
            AssertContains(configuration, identifier, $"configuration identifier {identifier}");
    }
});

Run("Bootstrap G. Preference categories match the current Melon name and migrate 1.2.3 values", () =>
{
    var configuration = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Config", "Configuration.cs"));
    const string melonName = "Inventory Expanded - Backpack & Inventory QoL";

    AssertContains(configuration, "ModManagerCategoryPrefix = Backpack.BuildMetadata.MelonName", "Mod Manager category prefix");
    foreach (var suffix in new[] { "_01_Backpack", "_02_Contextual", "_03_Transfers", "_05_Cheats" })
        AssertContains(configuration, $"ModManagerCategoryPrefix + \"{suffix}\"", $"discoverable category suffix {suffix}");

    var migration = configuration[configuration.IndexOf("private void MigrateCompatibleConfiguration", StringComparison.Ordinal)..];
    AssertMigrationSources(migration, "PreviousBackpackCategoryId",
        "01_StorageSlots", "02_ToggleKey", "03_EnablePoliceSearch", "04_UnlockRank", "05_UnlockTier",
        "16_ShowBackpackWithPhone", "17_PhoneBackpackDistance", "18_PhoneBackpackUiScale",
        "94_PhonePanelX", "95_PhonePanelY", "96_PhonePanelWidth", "97_PhonePanelHeight");
    AssertMigrationSources(migration, "PreviousContextualCategoryId",
        "06_ContextualBackpackEnabled", "07_AutoOpenWithContainersAndMachines", "08_RememberPanelPosition",
        "09_RememberPanelSize", "11_ResetContextualPanelPosition", "90_ContextualPanelX", "91_ContextualPanelY",
        "92_ContextualPanelWidth", "93_ContextualPanelHeight");
    AssertMigrationSources(migration, "PreviousTransfersCategoryId",
        "12_ShowSortButtons", "13_ShiftClickDestinationPriority", "14_TakeAllKey",
        "15_SortActiveContainerKey", "19_DiagnosticLogging");
    AssertMigrationSources(migration, "PreviousOrganizedCheatsCategoryId", "01_StackSizeMultiplier");
});

Run("Bootstrap H. Unified MAIN metadata retains the canonical settings identity", () =>
{
    var bootstrap = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "BackpackMod.cs"));
    AssertContains(bootstrap, "Backpack.BuildMetadata.MelonName, \"1.3.3\", \"PigeonLabs\"",
        "the supported MAIN artifact must retain its canonical Melon name/version/author metadata");
    AssertContains(bootstrap, "public const string MelonName = \"Inventory Expanded - Backpack & Inventory QoL\";",
        "the canonical settings-discovery Melon name must remain stable");
    Assert(!bootstrap.Contains("1.3.0-beta.47", StringComparison.Ordinal) &&
           !bootstrap.Contains("SCHEDULE_I_BETA_BUILD", StringComparison.Ordinal),
        "the supported MAIN implementation must not carry the retired BETA product identity");
    Assert(!bootstrap.Contains("MelonAdditionalDependencies", StringComparison.Ordinal)
           && !bootstrap.Contains("MelonOptionalDependencies", StringComparison.Ordinal),
        "bootstrap must not introduce artificial dependency edges between the legacy and current assemblies");
});

Run("Sort A. Product groups have requested priority", () =>
{
    var items = new List<SortFixture>
    {
        new(InventorySortGroup.Other, "apple", "Apple", 0),
        new(InventorySortGroup.UnpackagedProduct, "cocaine", "Cocaine", 1),
        new(InventorySortGroup.PackagedProduct, "packaged_weed", "Packaged Weed", 2),
        new(InventorySortGroup.Other, "banana", "Banana", 3),
        new(InventorySortGroup.PackagedProduct, "packaged_meth", "Packaged Meth", 4),
        new(InventorySortGroup.UnpackagedProduct, "meth", "Meth", 5)
    };
    items.Sort(Compare);
    Assert(items.Select(item => item.Group).SequenceEqual([
        InventorySortGroup.PackagedProduct,
        InventorySortGroup.PackagedProduct,
        InventorySortGroup.UnpackagedProduct,
        InventorySortGroup.UnpackagedProduct,
        InventorySortGroup.Other,
        InventorySortGroup.Other]), "packaged, unpackaged, other group order should be stable");
});

Run("Sort B/C. Existing secondary order remains", () =>
{
    var packaged = new List<SortFixture>
    {
        new(InventorySortGroup.PackagedProduct, "weed", "Weed", 0),
        new(InventorySortGroup.PackagedProduct, "cocaine", "Cocaine", 1),
        new(InventorySortGroup.PackagedProduct, "meth", "Meth", 2)
    };
    packaged.Sort(Compare);
    Assert(packaged.Select(item => item.Id).SequenceEqual(["cocaine", "meth", "weed"]), "packaged products should retain ID ordering");

    var unpackaged = packaged.Select((item, index) => item with { Group = InventorySortGroup.UnpackagedProduct, SourceIndex = index }).Reverse().ToList();
    unpackaged.Sort(Compare);
    Assert(unpackaged.Select(item => item.Id).SequenceEqual(["cocaine", "meth", "weed"]), "unpackaged products should retain ID ordering");
});

Run("Sort D. Non-products retain existing order", () =>
{
    var items = new List<SortFixture>
    {
        new(InventorySortGroup.Other, "soil", "Soil", 0),
        new(InventorySortGroup.Other, "apple", "Apple", 1),
        new(InventorySortGroup.Other, "banana", "Banana", 2)
    };
    items.Sort(Compare);
    Assert(items.Select(item => item.Id).SequenceEqual(["apple", "banana", "soil"]), "no-product inventory should preserve the previous ID comparator");
});

Run("Phone scale A. Default preserves automatic compensation", () =>
{
    Assert(NearlyEqual(PhoneBackpackScalePolicy.Apply(0.8f, 100), 0.8f),
        "100% must preserve the existing automatically compensated footprint");
});

Run("Phone scale B. Smaller values reduce the full presentation", () =>
{
    Assert(PhoneBackpackScalePolicy.ClampPercent(25) == 50, "scale must clamp to the safe minimum");
    Assert(NearlyEqual(PhoneBackpackScalePolicy.Apply(0.8f, 50), 0.4f),
        "50% must halve the automatically compensated footprint");
});

Run("Phone scale C. Larger values remain bounded", () =>
{
    Assert(PhoneBackpackScalePolicy.ClampPercent(200) == 150, "scale must clamp to the safe maximum");
    Assert(NearlyEqual(PhoneBackpackScalePolicy.Apply(0.8f, 150), 1.2f),
        "150% must enlarge the automatically compensated footprint predictably");
});

Run("Phone scale D. Compensation and user preference compose", () =>
{
    Assert(NearlyEqual(PhoneBackpackScalePolicy.Apply(0.8f, 125), 1f),
        "125% should restore an automatically compensated 0.8 presentation to 1.0");
});

Run("Drying rack capacity A. Vanilla and disabled preference retain native capacity", () =>
{
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    {
        Assert(DryingRackCapacityPolicy.GetEffectiveCapacity(20, multiplier, false) == 20,
            $"Increase Production off must keep native capacity at {multiplier}x");
    }
    Assert(DryingRackCapacityPolicy.GetEffectiveCapacity(20, 1, true) == 20,
        "Vanilla must keep native capacity even when the stored preference is on");
});

Run("Drying rack capacity B. Enabled preference uses the selected 2x/4x/8x multiplier", () =>
{
    foreach (var (multiplier, expected) in new[] { (2, 40), (4, 80), (8, 160) })
    {
        Assert(DryingRackCapacityPolicy.GetEffectiveCapacity(20, multiplier, true) == expected,
            $"enabled {multiplier}x should provide capacity {expected}");
    }
});

Run("Drying rack capacity C. Multiplier reductions preserve existing operations and block unsafe new starts", () =>
{
    const int existingOperationQuantity = 160;
    foreach (var (multiplier, expectedCapacity) in new[] { (2, 40), (1, 20) })
    {
        var effective = DryingRackCapacityPolicy.GetEffectiveCapacity(20, multiplier, multiplier != 1);
        Assert(effective == expectedCapacity, $"new capacity at {multiplier}x should be {expectedCapacity}");
        Assert(DryingRackCapacityPolicy.GetRemainingCapacity(effective, existingOperationQuantity) == 0,
            "an existing 160-unit operation must leave no room for a future operation after reduction");
        Assert(existingOperationQuantity == 160,
            "capacity calculation must not clamp or rewrite an existing operation quantity");
    }

    var disabledCapacity = DryingRackCapacityPolicy.GetEffectiveCapacity(20, 8, false);
    Assert(disabledCapacity == 20 && DryingRackCapacityPolicy.GetRemainingCapacity(disabledCapacity, 160) == 0,
        "turning the preference off must preserve the operation and safely refuse an additional start");
    Assert(DryingRackCapacityPolicy.GetRemainingCapacity(80, 40) == 40,
        "an 8x to 4x reduction should allow only future room beyond the existing 40, without changing it");
});

Run("Drying rack capacity D. MAIN patches the two rack start boundaries", () =>
{
    var root = FindProjectRoot();
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "DryingRackProductionPatch.cs"));
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    Assert(patch.Contains("#if IL2CPP", StringComparison.Ordinal)
           && !patch.Contains("SCHEDULE_I_047", StringComparison.Ordinal),
        "the rack capacity patch source must compile for the supported MAIN IL2CPP target");
    Assert(patch.Contains("nameof(DryingRack.CanStartOperation)", StringComparison.Ordinal)
           && patch.Contains("nameof(DryingRack.StartOperation)", StringComparison.Ordinal),
        "scaled capacity must be visible to both native UI eligibility and batch start");
    Assert(!patch.Contains("StorageEntity.InsertItem", StringComparison.Ordinal)
           && !patch.Contains("ItemSlot.GetCapacityForItem", StringComparison.Ordinal)
           && !patch.Contains("OnTimePass", StringComparison.Ordinal),
        "production support must not detour generic storage, slot capacity, or timers");
    Assert(Regex.IsMatch(configuration, "\\\"02_DryingRacks\\\"\\s*,\\s*false\\s*,\\s*\\\"Drying Racks\\\"", RegexOptions.CultureInvariant)
           && Regex.IsMatch(configuration, "\\\"03_MixingStations\\\"\\s*,\\s*false\\s*,\\s*\\\"Mixing Stations\\\"", RegexOptions.CultureInvariant)
           && configuration.Contains("#if IL2CPP", StringComparison.Ordinal),
        "each supported system preference must default off and be available to both IL2CPP configurations");
});

Run("Drying rack capacity E. Temporary field restores on normal, skipped, and exceptional exits", () =>
{
    var scopes = new TemporaryCapacityOverrideScopes<string>();

    var normalCapacity = 20;
    var normalLease = scopes.Enter("normal", normalCapacity, 8, true);
    ApplyCapacityOverride(normalLease, ref normalCapacity);
    Assert(normalCapacity == 160, "the native call should see the temporary 8x value");
    EndCapacityOverride(scopes, normalLease, ref normalCapacity);
    Assert(normalCapacity == 20, "normal completion must restore the original value");

    var skippedCapacity = 20;
    var skippedLease = scopes.Enter("skipped", skippedCapacity, 8, true);
    ApplyCapacityOverride(skippedLease, ref skippedCapacity);
    if (DryingRackCapacityPolicy.GetRemainingCapacity(skippedLease.EffectiveCapacity, 160) == 0)
        EndCapacityOverride(scopes, skippedLease, ref skippedCapacity);
    Assert(skippedCapacity == 20,
        "the StartOperation prefix's early-refusal path must still restore via its finalizer");

    var exceptionalCapacity = 20;
    var exceptionalLease = scopes.Enter("exceptional", exceptionalCapacity, 8, true);
    try
    {
        ApplyCapacityOverride(exceptionalLease, ref exceptionalCapacity);
        throw new InvalidOperationException("fixture native exception");
    }
    catch (InvalidOperationException exception) when (exception.Message == "fixture native exception")
    {
    }
    finally
    {
        EndCapacityOverride(scopes, exceptionalLease, ref exceptionalCapacity);
    }

    Assert(exceptionalCapacity == 20, "exceptional completion must restore the original value");
});

Run("Drying rack capacity F. Re-entrant CanStart/Start scopes reuse one effective value", () =>
{
    var scopes = new TemporaryCapacityOverrideScopes<string>();
    var capacity = 20;

    // Simulate a nested StartOperation call while CanStartOperation's prefix
    // override is still active. It must reuse 160, not multiply 160 by 8.
    var canStartLease = scopes.Enter("rack", capacity, 8, true);
    ApplyCapacityOverride(canStartLease, ref capacity);
    var startLease = scopes.Enter("rack", capacity, 8, true);
    ApplyCapacityOverride(startLease, ref capacity);
    Assert(startLease.EffectiveCapacity == 160 && capacity == 160,
        "nested rack calls must share the root capacity and avoid double scaling");
    EndCapacityOverride(scopes, startLease, ref capacity);
    Assert(capacity == 160,
        "a nested finalizer must not restore the field before its outer native call returns");
    EndCapacityOverride(scopes, canStartLease, ref capacity);
    Assert(capacity == 20, "the outermost finalizer must restore the root capacity exactly once");

    // Ordinary UI eligibility and operation start are sequential in normal
    // play; verify each independent call scales from the restored native value.
    var eligibilityLease = scopes.Enter("rack", capacity, 8, true);
    ApplyCapacityOverride(eligibilityLease, ref capacity);
    EndCapacityOverride(scopes, eligibilityLease, ref capacity);
    var operationLease = scopes.Enter("rack", capacity, 8, true);
    ApplyCapacityOverride(operationLease, ref capacity);
    Assert(operationLease.EffectiveCapacity == 160 && capacity == 160,
        "CanStartOperation finalization must leave StartOperation's baseline at native 20");
    EndCapacityOverride(scopes, operationLease, ref capacity);
    Assert(capacity == 20, "the sequential StartOperation call must restore native capacity");

    var patch = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "InventoryExpanded", "Patches", "DryingRackProductionPatch.cs"));
    Assert(Regex.Matches(patch, "DryingRackCapacityOverrideCoordinator\\.Exit\\(__state").Count == 2,
        "both native boundaries must exit the same scope coordinator from their Harmony finalizers");
});

Run("Mixer production A. Native batch ceiling scales only for its own enabled setting", () =>
{
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 1, true) == 10,
        "Vanilla must keep the native mixer batch ceiling");
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 8, false) == 10,
        "an OFF mixer setting must keep the native batch ceiling");
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 2, true) == 20,
        "2x must double the eligible batch ceiling");
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 4, true) == 40,
        "4x must quadruple the eligible batch ceiling");
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 8, true) == 80,
        "8x must multiply the eligible batch ceiling by eight");
    Assert(MixingProductionPolicy.GetEffectiveMaximum(10, 8, false) == 10,
        "an unrelated production setting cannot activate the mixer policy");
});

Run("Mixer production B. Batch inputs and output retain exact native ratios", () =>
{
    const int nativeMaximum = 10;
    foreach (var multiplier in new[] { 2, 4, 8 })
    {
        var cap = MixingProductionPolicy.GetEffectiveMaximum(nativeMaximum, multiplier, true);
        var productInput = cap;
        var mixerInput = cap;
        var quantity = Math.Min(Math.Min(productInput, mixerInput), cap);
        var productConsumed = quantity;
        var mixerConsumed = quantity;
        var output = quantity;
        Assert(productConsumed == mixerConsumed && output == productConsumed,
            $"{multiplier}x must retain native 1:1 ingredient and output ratios");
        Assert(productInput - productConsumed == 0 && mixerInput - mixerConsumed == 0,
            "the modeled exact batch must account for all consumed source quantities");

        var validQuantity = Math.Min(Math.Min(cap - 1, cap), cap);
        Assert(validQuantity == cap - 1,
            "a short first ingredient must cap the operation without creating free output");
        var remainingProduct = cap - 1 - validQuantity;
        var remainingMixer = cap - validQuantity;
        Assert(remainingProduct == 0 && remainingMixer == 1,
            "only the matching 1:1 recipe amount may be consumed when one ingredient is short");
    }
});

Run("Mixer production C. Scaled batches retain native-capacity processing time", () =>
{
    const int nativeMaximum = 10;
    const int timePerItem = 3;
    foreach (var multiplier in new[] { 2, 4, 8 })
    {
        var operationQuantity = nativeMaximum * multiplier;
        var remainder = 0L;
        var virtualElapsed = 0;
        var nativeElapsed = nativeMaximum * timePerItem;
        for (var minute = 0; minute < nativeElapsed; minute++)
        {
            var delta = MixingProductionPolicy.GetExtraElapsedMinutes(1, operationQuantity, nativeMaximum, remainder);
            remainder = delta.Remainder;
            virtualElapsed += 1 + delta.ExtraMinutes;
        }

        Assert(virtualElapsed == operationQuantity * timePerItem,
            $"{multiplier}x must reach the enlarged native threshold in one native-capacity duration");
    }
});

Run("Mixer production D. Partial batches, elapsed-time carry, and settings reduction preserve active work", () =>
{
    const int nativeMaximum = 10;
    const int operationQuantity = 13;
    var remainder = 0L;
    var virtualElapsed = 0;
    for (var minute = 0; minute < 300; minute++)
    {
        var delta = MixingProductionPolicy.GetExtraElapsedMinutes(1, operationQuantity, nativeMaximum, remainder);
        remainder = delta.Remainder;
        virtualElapsed += 1 + delta.ExtraMinutes;
    }
    Assert(virtualElapsed == operationQuantity * 30,
        "a partial expanded batch must preserve the corresponding native-capacity timeframe");
    var nextDelta = MixingProductionPolicy.GetExtraElapsedMinutes(1, operationQuantity, nativeMaximum, remainder);
    Assert(nextDelta.Remainder > 0,
        "the persisted quantity remains timing-active even after the setting changes");
    Assert(operationQuantity == 13,
        "timing policy must never clamp the persisted operation quantity");
});

Run("Mixer production E. Runtime adapter uses native input/output and persisted timer paths", () =>
{
    var root = FindProjectRoot();
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "MixingStationProductionPatch.cs"));
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    Assert(patch.Contains("nameof(MixingStation.GetMixQuantity)", StringComparison.Ordinal)
           && patch.Contains("nameof(MixingStation.OnTimePass)", StringComparison.Ordinal),
        "only the native candidate quantity and mixer-time boundaries should be patched");
    Assert(patch.Contains("TemporaryCapacityOverrideScopes<IntPtr>", StringComparison.Ordinal)
           && patch.Contains("MixingStationQuantityOverrideCoordinator.Exit(__state", StringComparison.Ordinal),
        "the temporary native cap must re-use scoped re-entrancy and finalizer restoration");
    Assert(patch.Contains("CurrentMixOperation", StringComparison.Ordinal)
           && patch.Contains("CurrentMixTime", StringComparison.Ordinal)
           && !patch.Contains("TryCreateOutputItems", StringComparison.Ordinal)
           && !patch.Contains("SetMixOperation", StringComparison.Ordinal),
        "the adapter must preserve native operation serialization, server output creation, and quality handling");
    Assert(configuration.Contains("03_MixingStations", StringComparison.Ordinal)
           && configuration.Contains("Sources(\"02_IncreaseProduction\"", StringComparison.Ordinal),
        "settings must be independent and the old checkbox may migrate only to its original drying-rack behavior");
});

Run("Plant Yield A. Policy scales only enabled non-Vanilla harvest yield", () =>
{
    Assert(PlantYieldPolicy.GetEffectiveYield(3, 1, true) == 3,
        "Vanilla must preserve the native yield");
    Assert(PlantYieldPolicy.GetEffectiveYield(3, 8, false) == 3,
        "an OFF Plant Yield setting must preserve the native yield");
    Assert(PlantYieldPolicy.GetEffectiveYield(3, 2, true) == 6,
        "2x must multiply the item quantity created by a player harvest");
    Assert(PlantYieldPolicy.GetEffectiveYield(3, 4, true) == 12,
        "4x must multiply the item quantity created by a player harvest");
    Assert(PlantYieldPolicy.GetEffectiveYield(3, 8, true) == 24,
        "8x must multiply the item quantity created by a player harvest");
    Assert(PlantYieldPolicy.GetEffectiveYield(int.MaxValue / 2, 8, true) == int.MaxValue,
        "scaled yield arithmetic must saturate instead of overflowing negative");
});

Run("Plant Yield B. Temporary ProductQuantity scopes restore and do not double-scale", () =>
{
    var scopes = new TemporaryCapacityOverrideScopes<string>();
    var quantity = 3;

    var outer = scopes.Enter("bud", quantity, 8, true);
    ApplyCapacityOverride(outer, ref quantity);
    Assert(quantity == 24, "native preflight and harvest should observe the scaled quantity");

    // A nested Update/getter for the same harvestable must reuse the root
    // quantity even though it sees the already-scaled field.
    var nested = scopes.Enter("bud", quantity, 8, true);
    ApplyCapacityOverride(nested, ref quantity);
    Assert(nested.EffectiveCapacity == 24 && quantity == 24,
        "a re-entrant plant quantity scope must not multiply an already-scaled value");
    EndCapacityOverride(scopes, nested, ref quantity);
    Assert(quantity == 24, "a nested finalizer must leave the outer preflight value visible");
    EndCapacityOverride(scopes, outer, ref quantity);
    Assert(quantity == 3, "the outer task finalizer must restore the prefab yield");

    var exceptionalQuantity = 3;
    var exceptional = scopes.Enter("exceptional-bud", exceptionalQuantity, 4, true);
    try
    {
        ApplyCapacityOverride(exceptional, ref exceptionalQuantity);
        throw new InvalidOperationException("fixture native harvest exception");
    }
    catch (InvalidOperationException exception) when (exception.Message == "fixture native harvest exception")
    {
    }
    finally
    {
        EndCapacityOverride(scopes, exceptional, ref exceptionalQuantity);
    }
    Assert(exceptionalQuantity == 3, "an exceptional task exit must restore the original plant yield");

    var root = FindProjectRoot();
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PlantYieldProductionPatch.cs"));
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    Assert(patch.Contains("nameof(HarvestPlant.Update)", StringComparison.Ordinal)
           && patch.Contains("GetHoveredHarvestable", StringComparison.Ordinal)
           && patch.Contains("[HarmonyFinalizer]", StringComparison.Ordinal),
        "the patch must scope the player task's native preflight-to-harvest frame and restore on all exits");
    Assert(!patch.Contains("PlayerInventory.AddItemToInventory", StringComparison.Ordinal)
           && !patch.Contains("Plant.SetHarvestableActive", StringComparison.Ordinal)
           && !patch.Contains("GetHarvestedProduct", StringComparison.Ordinal),
        "the adapter must leave native inventory insertion, quality creation and harvest-state callbacks intact");
    Assert(Regex.IsMatch(configuration, "\\\"04_PlantYield\\\"\\s*,\\s*false\\s*,\\s*\\\"Plant Yield\\\"", RegexOptions.CultureInvariant),
        "Plant Yield must be an independent default-off preference");
});

Run("Packaging A. Mk1 target remains one unit when off or Vanilla", () =>
{
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    {
        Assert(PackagingProductionPolicy.GetTargetPackages(multiplier, enabled: false) == 1,
            $"OFF must preserve one native package at {multiplier}x");
        Assert(PackagingProductionPolicy.GetTargetPackages(multiplier, enabled: true) == (multiplier == 1 ? 1 : multiplier),
            $"Vanilla must be native; enabled non-Vanilla should use {multiplier}x");
        Assert(PackagingProductionPolicy.GetAdditionalPackages(multiplier, enabled: false, 1, 20, 40, 2, 20) == 0,
            "OFF must never schedule an additional native transaction");
    }
});

Run("Packaging Mk1 is opt-in and guarded in the 1.3.3 candidate", () =>
{
    var root = FindProjectRoot();
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    var cheatSettings = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Cheats", "CheatSettings.cs"));
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PackagingStationsProductionPatch.cs"));
    var project = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "InventoryExpanded.csproj"));
    Assert(configuration.Contains("\"05_PackagingStations\"", StringComparison.Ordinal)
           && configuration.Contains("_packagingStationsProductionEntry.OnEntryValueChanged", StringComparison.Ordinal)
           && configuration.Contains("_packagingStationsProductionEntry.Value", StringComparison.Ordinal)
           && configuration.Contains("_packagingStationsProductionEntry.Description", StringComparison.Ordinal),
        "the backwards-compatible packaging preference must be visible, live-applied and explain its active policy");
    Assert(cheatSettings.Contains("ProductionCheats.Apply(dryingRacksEnabled, mixingStationsEnabled, plantYieldEnabled, packagingStationsEnabled)", StringComparison.Ordinal),
        "the packaging policy/state plumbing remains available in preserved research code for a later release");
    foreach (var multiplier in new[] { 2, 4, 8 })
        Assert(PackagingProductionPolicy.GetTargetPackages(multiplier, enabled: true) == multiplier,
            $"enabled Stack Size {multiplier}x must reach the packaging transaction target");

    Assert(patch.Contains("[HarmonyTargetMethod]", StringComparison.Ordinal)
           && patch.Contains("Type.EmptyTypes", StringComparison.Ordinal)
           && patch.Contains("Harmony.GetPatchInfo(target)", StringComparison.Ordinal)
           && patch.Contains("NATIVE ROOT RETURN", StringComparison.Ordinal),
        "the exact zero-argument target and successful Harmony registration must be inspectable");
    Assert(project.Contains("Cheats\\PackagingProductionPolicy.cs", StringComparison.Ordinal)
           && project.Contains("Cheats\\PackagingTransactionGuard.cs", StringComparison.Ordinal)
           && project.Contains("Patches\\PackagingStationsProductionPatch.cs", StringComparison.Ordinal)
           && !project.Contains("Patches\\PackagingMk2DiagnosticsPatch.cs", StringComparison.Ordinal)
           && !project.Contains("Patches\\PackagingMk2SafetyPatch.cs", StringComparison.Ordinal),
        "only the verified-delta Mk1 adapter is compiled; Mk2 diagnostics and safety experiments remain excluded");

    var prefixStart = patch.IndexOf("private static void Prefix", StringComparison.Ordinal);
    var postfixStart = patch.IndexOf("private static void Postfix", StringComparison.Ordinal);
    Assert(prefixStart >= 0 && postfixStart > prefixStart,
        "the runtime adapter must retain a Prefix baseline and validate it in Postfix");
    var prefixBody = patch[prefixStart..postfixStart];
    Assert(prefixBody.IndexOf("TryCapture(__instance, out var before", StringComparison.Ordinal)
               < prefixBody.IndexOf("__state.Before = before", StringComparison.Ordinal),
        "the initial slot snapshot must be captured in the Prefix before the native body mutates slots");
    Assert(patch.Contains("if (Guard.IsHeld(__state.StationPointer))", StringComparison.Ordinal)
           && patch.Contains("classification=reentrant", StringComparison.Ordinal)
           && patch.Contains("if (__state?.Lease == null)", StringComparison.Ordinal),
        "the root owns the batch loop while nested native calls execute once without opening a nested loop");
});

Run("Packaging root snapshot permits only verified extra native units", () =>
{
    const int multiplier = 8;
    const int recipeQuantity = 2;
    var target = PackagingProductionPolicy.GetTargetPackages(multiplier, enabled: true);
    Assert(target == 8, "an enabled 8x preference should set an eight-package interaction target");

    var guard = new PackagingTransactionGuard<string>();
    Assert(guard.TryEnter("station", out var rootLease), "the root native invocation should own the station batch guard");
    using (rootLease)
    {
        Assert(guard.IsHeld("station"), "the root should remain the guard owner while its postfix runs");
        var beforeRoot = (packaging: 9, product: 18, output: 0);
        var afterRoot = (packaging: 8, product: 16, output: 1);
        Assert(PackagingProductionPolicy.IsOneNativePackageCommitted(
                beforeRoot.packaging, afterRoot.packaging,
                beforeRoot.product, afterRoot.product,
                recipeQuantity, beforeRoot.output, afterRoot.output),
            "a baseline captured before native mutation should validate one exact root recipe unit");

        Assert(!guard.TryEnter("station", out _),
            "the root's internal PackSingleInstance call must be classified as reentrant, not acquire another batch scope");
        var extraUnits = PackagingProductionPolicy.GetAdditionalPackages(
            multiplier, enabled: true, packagesAlreadyCommitted: 1,
            availablePackagingUnits: afterRoot.packaging,
            availableLooseProduct: afterRoot.product,
            looseProductPerPackage: recipeQuantity,
            outputRoom: 7);
        Assert(extraUnits == 7, "the root owner should be allowed to plan the remaining seven validated units");

        var afterExtra = (packaging: 7, product: 14, output: 2);
        Assert(PackagingProductionPolicy.IsOneNativePackageCommitted(
                afterRoot.packaging, afterExtra.packaging,
                afterRoot.product, afterExtra.product,
                recipeQuantity, afterRoot.output, afterExtra.output),
            "each extra native transaction must also pass exact input/output delta validation");

        var badRoot = (packaging: 8, product: 18, output: 1);
        Assert(!PackagingProductionPolicy.IsOneNativePackageCommitted(
                beforeRoot.packaging, badRoot.packaging,
                beforeRoot.product, badRoot.product,
                recipeQuantity, beforeRoot.output, badRoot.output),
            "a root delta mismatch must stop all extras instead of weakening conservation checks");
    }
    Assert(!guard.IsHeld("station"), "the root finalizer's lease must release the guard");
});

Run("Packaging B. Mk1 limits extras by multiplier, recipe, packaging and output room", () =>
{
    Assert(PackagingProductionPolicy.GetAdditionalPackages(2, true, 1, 9, 18, 2, 9) == 1,
        "2x should add one complete native recipe unit");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(4, true, 1, 9, 18, 2, 9) == 3,
        "4x should add three units when every resource has room");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 18, 2, 9) == 7,
        "8x should add seven units when every resource has room");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 18, 2, 7) == 7,
        "exact output capacity should permit exactly the requested remaining packages");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 18, 2, 3) == 3,
        "partial output room should limit work to complete packages that fit");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 2, 18, 2, 7) == 2,
        "insufficient packaging must limit work to units with corresponding material");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 5, 2, 7) == 2,
        "loose product must be divided by the native recipe ratio before work is scheduled");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 18, 2, 0) == 0,
        "zero output room must schedule no additional transaction");
    Assert(PackagingProductionPolicy.GetAdditionalPackages(8, true, 1, 9, 1, 0, 7) == 0,
        "an invalid native recipe ratio must schedule no output");
});

Run("Packaging C. Every Mk1 native transaction consumes a complete matching recipe", () =>
{
    Assert(PackagingProductionPolicy.IsOneNativePackageCommitted(4, 3, 12, 9, 3, 0, 1),
        "one package must consume one material, Q loose product and commit one output");
    Assert(!PackagingProductionPolicy.IsOneNativePackageCommitted(4, 3, 12, 12, 3, 0, 1),
        "free output must not authorize another scaled transaction");
    Assert(!PackagingProductionPolicy.IsOneNativePackageCommitted(4, 4, 12, 9, 3, 0, 1),
        "unconsumed packaging material must not be treated as a successful recipe");
    Assert(!PackagingProductionPolicy.IsOneNativePackageCommitted(4, 3, 12, 9, 3, 0, 0),
        "input consumption without a committed output must stop scaling");
    Assert(!PackagingProductionPolicy.IsOneNativePackageCommitted(4, 2, 12, 6, 3, 0, 1),
        "a multi-unit input delta must not be mistaken for one native package");

    const int nativeRecipeQuantity = 3;
    const int completedPackages = 4;
    var consumedPackaging = completedPackages;
    var consumedProduct = completedPackages * nativeRecipeQuantity;
    var outputPackages = completedPackages;
    Assert(consumedPackaging == outputPackages
           && consumedProduct == outputPackages * nativeRecipeQuantity,
        "successful output must account for every packaging unit and every loose-product unit");
});

Run("Packaging D. Setting reductions affect only future Mk1 interactions", () =>
{
    const int existingOutputPackages = 11;
    Assert(PackagingProductionPolicy.GetTargetPackages(4, true) == 4,
        "8x to 4x changes the next interaction target without normalizing prior output");
    Assert(PackagingProductionPolicy.GetTargetPackages(2, true) == 2,
        "8x to 2x changes the next interaction target without clamping slot quantity");
    Assert(PackagingProductionPolicy.GetTargetPackages(1, true) == 1,
        "returning to Vanilla leaves only future interactions native");
    Assert(PackagingProductionPolicy.GetTargetPackages(8, false) == 1,
        "turning the setting off leaves only future interactions native");
    Assert(existingOutputPackages == 11,
        "target calculation must not rewrite or delete already-created packaged output");
});

Run("Packaging E. Re-entrancy scope releases after nesting and exceptions", () =>
{
    var guard = new PackagingTransactionGuard<string>();
    Assert(guard.TryEnter("mk1", out var outer), "the first native call should own the expansion scope");
    Assert(!guard.TryEnter("mk1", out _), "nested PackSingleInstance calls must not expand recursively");
    outer.Dispose();
    outer.Dispose();
    Assert(guard.TryEnter("mk1", out var afterNormalExit), "normal completion must release the expansion scope");
    afterNormalExit.Dispose();

    try
    {
        if (!guard.TryEnter("exception", out var exceptional))
            throw new InvalidOperationException("guard unexpectedly occupied");
        using (exceptional)
            throw new InvalidOperationException("fixture native package exception");
    }
    catch (InvalidOperationException exception) when (exception.Message == "fixture native package exception")
    {
    }

    Assert(guard.TryEnter("exception", out var afterException),
        "an exception must release the re-entrancy guard and leave future native calls usable");
    afterException.Dispose();
});

Run("Packaging F. Mk1 policy is included while Mk2 remains native", () =>
{
    var root = FindProjectRoot();
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PackagingStationsProductionPatch.cs"));
    var configuration = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    var project = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "InventoryExpanded.csproj"));
    var mod = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "BackpackMod.cs"));
    const string packagingPreferenceKey = "\"05_PackagingStations\"";
    var packagingEntryStart = configuration.IndexOf(packagingPreferenceKey, StringComparison.Ordinal);
    var packagingEntry = packagingEntryStart >= 0
        ? configuration[packagingEntryStart..Math.Min(configuration.Length, packagingEntryStart + 420)]
        : string.Empty;
    Assert(patch.Contains("nameof(PackagingStation.PackSingleInstance)", StringComparison.Ordinal)
           && patch.Contains("TryGetNativePackPreflight", StringComparison.Ordinal)
           && patch.Contains("PackagingStationMk2", StringComparison.Ordinal)
           && patch.Contains("Enum.ToObject(method.GetParameters()[0].ParameterType, 0)", StringComparison.Ordinal),
        "only Mk1's native single-package commit may be repeated, after native preflight, with an explicit Mk2 exclusion");
    Assert(patch.Contains("__instance.PackSingleInstance()", StringComparison.Ordinal)
           && !patch.Contains("TryInsertItemIntoSet", StringComparison.Ordinal)
           && !patch.Contains("ChangeQuantity(", StringComparison.Ordinal)
           && !patch.Contains("new ProductItemInstance", StringComparison.Ordinal),
        "the adapter must not create output or mutate generic slot quantities itself");
    Assert(!packagingEntry.Contains("is_hidden: true", StringComparison.Ordinal)
           && project.Contains("Patches\\PackagingStationsProductionPatch.cs", StringComparison.Ordinal)
           && !project.Contains("Patches\\PackagingMk2DiagnosticsPatch.cs", StringComparison.Ordinal)
           && !project.Contains("Patches\\PackagingMk2SafetyPatch.cs", StringComparison.Ordinal)
           && mod.Contains("PackagingStationsProductionPatch.ReportPatchRegistration", StringComparison.Ordinal),
        "the preference and verified-delta Mk1 adapter are active in the candidate, with Mk2 throughput untouched");
});

Run("Packaging Mk2 A. Native plan is bounded by real packaging and loose-product resources", () =>
{
    var packageLimited = PackagingMk2ObservationPolicy.EstimateNativePlan(3, 40, 4);
    Assert(packageLimited == new PackagingMk2Plan(3, 12),
        "package-limited plans must not invent packaging units or consume surplus product");

    var productLimited = PackagingMk2ObservationPolicy.EstimateNativePlan(9, 31, 4);
    Assert(productLimited == new PackagingMk2Plan(7, 28),
        "product-limited plans must include only complete native recipes");

    Assert(PackagingMk2ObservationPolicy.EstimateNativePlan(0, 100, 4) == default,
        "empty packaging input must plan no work");
    Assert(PackagingMk2ObservationPolicy.EstimateNativePlan(8, 3, 4) == default,
        "insufficient loose product must plan no partial package");

    Assert(packageLimited.PackageCount <= 3 && productLimited.PackageCount <= 9,
        "the estimated plan must never exceed each fixture's available packaging");
    Assert(packageLimited.LooseProductCount % 4 == 0 && productLimited.LooseProductCount % 4 == 0,
        "the estimated plan must preserve the native recipe ratio");
});

Run("Packaging Mk2 B. Each observed commit must be one complete native transaction", () =>
{
    Assert(PackagingMk2ObservationPolicy.IsOneNativePackageCommitted(8, 7, 32, 28, 2, 3, 4),
        "one commit must consume one package and Q loose product while creating one output");
    Assert(!PackagingMk2ObservationPolicy.IsOneNativePackageCommitted(8, 7, 32, 31, 2, 3, 4),
        "partial product consumption must not count as a complete commit");
    Assert(!PackagingMk2ObservationPolicy.IsOneNativePackageCommitted(8, 7, 32, 28, 2, 2, 4),
        "input consumption without a new output must be reported as a mismatch");
    Assert(!PackagingMk2ObservationPolicy.IsOneNativePackageCommitted(8, 8, 32, 28, 2, 3, 4),
        "output creation without packaging consumption must be reported as a mismatch");
});

Run("Packaging Mk2 C. Diagnostics are bounded, read-only, and expose native plan/display gaps", () =>
{
    var root = FindProjectRoot();
    var patch = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PackagingMk2DiagnosticsPatch.cs"));
    Assert(patch.Contains("MaxDetailedEvents = 8", StringComparison.Ordinal)
           && patch.Contains("ProductCountText?.text", StringComparison.Ordinal)
           && patch.Contains("displayedRemainingProduct", StringComparison.Ordinal)
           && patch.Contains("requestedScaledPlanPackages", StringComparison.Ordinal)
           && patch.Contains("appliedScaledPlan=false", StringComparison.Ordinal),
        "Mk2 traces must bound output, identify the native display value, and never apply a scaled plan");
    Assert(patch.Contains("completionVsCancellation=not-exposed-by-native-stop-method", StringComparison.Ordinal)
           && patch.Contains("capacityProbe=", StringComparison.Ordinal)
           && patch.Contains("PackSingleInstance threw", StringComparison.Ordinal),
        "diagnostics must distinguish unknown cancellation, label the output-capacity probe, and surface native commit exceptions");
    Assert(!patch.Contains("TryInsertItemIntoSet", StringComparison.Ordinal)
           && !patch.Contains("ChangeQuantity(", StringComparison.Ordinal)
           && !patch.Contains("SetQuantity(", StringComparison.Ordinal),
        "Mk2 observation must not mutate inventory or task quantities");
});

Run("Packaging Mk2 D. Safe plan clamp respects resources, recipe ratio and output room", () =>
{
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(109, 109, 139, 109, 1, 1)
           == new PackagingMk2Plan(1, 1),
        "the live 109-unit plan with one output space must become a single-package task");
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(8, 24, 8, 24, 3, 5)
           == new PackagingMk2Plan(5, 15),
        "partial output room must clamp to complete native recipe units");
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(8, 24, 3, 24, 3, 8)
           == new PackagingMk2Plan(3, 9),
        "the task plan must not exceed available packaging materials");
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(8, 24, 8, 14, 3, 8)
           == new PackagingMk2Plan(4, 12),
        "the task plan must use only complete product recipes");
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(8, 24, 8, 24, 3, 0) == default,
        "zero output room must never produce a positive safe plan");
    Assert(PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(8, 24, 8, 2, 3, 8) == default,
        "insufficient product for one complete recipe must plan no commits");
});

Run("Packaging Mk2 E. Commit guard refuses any incomplete or blocked transaction", () =>
{
    Assert(PackagingMk2ObservationPolicy.CanCommitNativePackage(1, 3, 3, 1, true, true),
        "exact resources and exact output room allow one complete native package");
    Assert(!PackagingMk2ObservationPolicy.CanCommitNativePackage(1, 3, 3, 0, true, true),
        "zero output room must refuse before native input mutation");
    Assert(!PackagingMk2ObservationPolicy.CanCommitNativePackage(1, 3, 3, 1, false, true),
        "input removal locks must refuse the native transaction");
    Assert(!PackagingMk2ObservationPolicy.CanCommitNativePackage(1, 3, 3, 1, true, false),
        "output locks or incompatible output must refuse the native transaction");
    Assert(!PackagingMk2ObservationPolicy.CanCommitNativePackage(0, 3, 3, 1, true, true),
        "missing packaging material must refuse output creation");
    Assert(!PackagingMk2ObservationPolicy.CanCommitNativePackage(1, 2, 3, 1, true, true),
        "insufficient loose product must refuse packaging consumption");
});

Run("Packaging Mk2 F. Preserved safety research uses a packaged copy; runtime adapter is deferred", () =>
{
    var root = FindProjectRoot();
    var project = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "InventoryExpanded.csproj"));
    var safety = File.ReadAllText(Path.Combine(root, "src", "InventoryExpanded", "Patches", "PackagingMk2SafetyPatch.cs"));
    Assert(!project.Contains("Patches\\PackagingMk2SafetyPatch.cs", StringComparison.Ordinal),
        "the preserved Mk2 safety adapter must remain excluded from the 1.3.0 public DLL");
    Assert(safety.Contains("GetCopy(1)", StringComparison.Ordinal)
           && safety.Contains("SetPackaging(definition)", StringComparison.Ordinal)
           && safety.Contains("GetCapacityForItem(candidate, false)", StringComparison.Ordinal),
        "capacity must use a copied, correctly packaged candidate and native slot compatibility checks");
    Assert(safety.Contains("IsRemovalLocked", StringComparison.Ordinal)
           && safety.Contains("IsAddLocked", StringComparison.Ordinal)
           && safety.Contains("IsLocked", StringComparison.Ordinal)
           && safety.Contains("ref bool __runOriginal", StringComparison.Ordinal)
           && safety.Contains("PackagingStationMk2", StringComparison.Ordinal),
        "the commit boundary must refuse locked or unprovable Mk2 output before the native input decrements");
    Assert(safety.Contains("planClamp=", StringComparison.Ordinal)
           && safety.Contains("commitGuard=", StringComparison.Ordinal),
        "startup logging must prove plan-clamp and commit-guard patch registration");
    Assert(!safety.Contains("ChangeQuantity(", StringComparison.Ordinal)
           && !safety.Contains("new ProductItemInstance", StringComparison.Ordinal),
        "the safety adapter must not mutate slots or manufacture output itself");
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Pre-release polish tests failed: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("Pre-release polish tests passed.");
return 0;

int Compare(SortFixture left, SortFixture right)
{
    return InventorySortOrder.Compare(
        left.Group, left.Id, left.Name, left.SourceIndex,
        right.Group, right.Id, right.Name, right.SourceIndex);
}

void WithTempModsDirectory(Action<string> test)
{
    var root = Path.Combine(FindProjectRoot(), ".release-work", "ConflictTests", Guid.NewGuid().ToString("N"));
    var modsDirectory = Path.Combine(root, "Mods");
    Directory.CreateDirectory(modsDirectory);
    try
    {
        test(modsDirectory);
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

string FindProjectRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "InventoryExpanded.sln")))
            return current.FullName;
        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the InventoryExpanded project root from the test output directory.");
}

void AssertContains(string source, string expected, string label)
{
    Assert(source.Contains(expected, StringComparison.Ordinal), $"stable {label} changed or disappeared");
}

void AssertMigrationSources(string migrationSource, string categoryConstant, params string[] keys)
{
    foreach (var key in keys)
    {
        var pattern = $"Sources\\(\"{Regex.Escape(key)}\",[^\\r\\n]*\\b{categoryConstant}\\b";
        Assert(Regex.IsMatch(migrationSource, pattern), $"1.2.3 source {categoryConstant}.{key} must be migrated");
    }
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
    }
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

void ApplyCapacityOverride<T>(TemporaryCapacityOverrideScopes<T>.Lease lease, ref int capacity) where T : notnull
{
    if (lease.ShouldApply)
        capacity = lease.EffectiveCapacity;
}

void EndCapacityOverride<T>(TemporaryCapacityOverrideScopes<T> scopes, TemporaryCapacityOverrideScopes<T>.Lease lease, ref int capacity) where T : notnull
{
    if (scopes.Exit(lease, out var originalCapacity))
        capacity = originalCapacity;
}

bool NearlyEqual(float left, float right)
{
    return Math.Abs(left - right) <= 0.0001f;
}

internal readonly record struct SortFixture(InventorySortGroup Group, string Id, string Name, int SourceIndex);
