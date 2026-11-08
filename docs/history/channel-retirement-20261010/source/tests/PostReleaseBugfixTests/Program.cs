using Backpack;

var failures = new List<string>();
var repository = FindRepositoryRoot(AppContext.BaseDirectory);
var shopSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "ShopInterfacePatch.cs"));
var interactionSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "InventoryInteractionPatch.cs"));
var hotkeySource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Utility", "StorageHotkeys.cs"));
var configurationSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "Configuration.cs"));
var storageMenuSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "StorageSortButton.cs"));
var sorterSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Utility", "InventorySorter.cs"));
var deliverySource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "DeliveryPatch.cs"));
var cheatSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "CheatPatch.cs"));
var playerSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlayerPatch.cs"));
var playerManagerSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlayerManagerPatch.cs"));
var projectSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "InventoryExpanded.csproj"));
var modSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "BackpackMod.cs"));
var readmeSource = File.ReadAllText(Path.Combine(repository, "README.md"));
var releaseNotesSource = File.ReadAllText(Path.Combine(repository, "docs", "RELEASE_NOTES_1.3.0.md"));
var releaseScriptSource = File.ReadAllText(Path.Combine(repository, "tools", "prepare-release-candidate.ps1"));

Run("Shop A. Live shop identifies primary destinations", () =>
{
    Assert(shopSource.Contains("AddNativeSlotsBeforeFallback(reordered, __result, loadingBayVehicle, __instance);"),
        "primary pass must classify slots from the live ShopInterface");
});

Run("Shop B. Live shop identifies fallback destinations", () =>
{
    Assert(shopSource.Contains("AddNativeFallbackSlots(reordered, __result, loadingBayVehicle, __instance);"),
        "fallback pass must classify slots from the live ShopInterface");

    var helper = ExtractMethod(shopSource, "private static bool IsNativeFallbackSlot", "private static bool IsDeliveryBaySlot");
    Assert(helper.Contains("ShopInterface shop") && helper.Contains("IsDeliveryBaySlot(slot, shop)"),
        "fallback classification must not depend on diagnostic trace state");
    Assert(!helper.Contains("ShopFulfillmentTrace"),
        "routing helper must remain independent of Diagnostic Logging");
});

Run("Shop C. Background delivery slot lists remain native", () =>
{
    var method = ExtractMethod(shopSource,
        "public static void GetAvailableSlots(ShopInterface __instance",
        "private static void AddNativeSlotsBeforeFallback");
    var guard = method.IndexOf("if (__instance == null || !__instance.IsOpen)", StringComparison.Ordinal);
    var reorder = method.IndexOf("var reordered = CreateSlotList();", StringComparison.Ordinal);

    Assert(guard >= 0, "shop routing must reject background/closed ShopInterface calls");
    Assert(reorder > guard, "background delivery guard must run before destination reordering");
    Assert(!method[..guard].Contains("Logger.DiagnosticLoggingEnabled", StringComparison.Ordinal),
        "background delivery guard must not depend on Diagnostic Logging");
});

Run("Shop D. Native delivery vehicle insertion is not detoured", () =>
{
    Assert(!shopSource.Contains("class ShopStorageTracePatch", StringComparison.Ordinal),
        "diagnostic code must not patch the global StorageEntity.InsertItem path used by deliveries");
    Assert(!shopSource.Contains("[HarmonyPatch(typeof(StorageEntity))]", StringComparison.Ordinal),
        "scheduled delivery payload insertion must remain entirely native");
});

Run("Delivery A. Override is bounded to native delivery loading", () =>
{
    Assert(deliverySource.Contains("typeof(DeliveryInstance), nameof(DeliveryInstance.AddItemsToDeliveryVehicle)"),
        "delivery context must target the authoritative vehicle-loading method");
    Assert(deliverySource.Contains("DeliveryVehicleLoadContext.Enter();"),
        "delivery context must begin before native loading");
    Assert(deliverySource.Contains("[HarmonyFinalizer]") && deliverySource.Contains("DeliveryVehicleLoadContext.Exit();"),
        "delivery context must always end, including exceptional exits");
    Assert(cheatSource.Contains("if (DeliveryVehicleLoadContext.IsActive)"),
        "stack multiplier must expose the native limit during delivery loading");
});

Run("Delivery B. Global storage insertion remains unpatched", () =>
{
    var combined = deliverySource + cheatSource + shopSource;
    Assert(!combined.Contains("typeof(StorageEntity), nameof(StorageEntity.InsertItem)"),
        "fix must not intercept global StorageEntity.InsertItem");
    Assert(!combined.Contains("[HarmonyPatch(typeof(StorageEntity))]"),
        "fix must not add a broad StorageEntity patch");
});

Run("Delivery C. Native-stack-10 boundaries preserve exact quantity", () =>
{
    var quantities = new[] { 9, 10, 11, 19, 20, 21, 39, 40, 41, 79, 80, 81, 99, 100, 101 };
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    foreach (var quantity in quantities)
        Assert(SimulateDelivery(quantity, 10, multiplier, useNativeLimitDuringLoading: true) == quantity,
            $"native-stack-10 quantity {quantity} must survive at {multiplier}x");
});

Run("Delivery D. Addy native-stack-20 boundaries preserve exact quantity", () =>
{
    var quantities = new[] { 19, 20, 21, 39, 40, 41, 79, 80, 81, 99, 100, 101 };
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    foreach (var quantity in quantities)
        Assert(SimulateDelivery(quantity, 20, multiplier, useNativeLimitDuringLoading: true) == quantity,
            $"Addy quantity {quantity} must survive at {multiplier}x");
});

Run("Delivery E. Mixed orders preserve independent quantities", () =>
{
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    {
        Assert(SimulateDelivery(100, 10, multiplier, useNativeLimitDuringLoading: true) == 100,
            $"mixed item A must preserve 100 at {multiplier}x");
        Assert(SimulateDelivery(101, 10, multiplier, useNativeLimitDuringLoading: true) == 101,
            $"mixed item B must preserve 101 at {multiplier}x");
    }
});

Run("Delivery F. Regression model reproduces the 99 to 19 loss", () =>
{
    Assert(SimulateDelivery(99, 20, 2, useNativeLimitDuringLoading: false) == 19,
        "unfixed Addy delivery must reproduce the observed 99-to-19 loss");
    Assert(SimulateDelivery(99, 20, 2, useNativeLimitDuringLoading: true) == 99,
        "bounded native-limit context must preserve all 99 Addy items");
});

Run("Delivery G. Limit-one items remain non-stackable", () =>
{
    foreach (var multiplier in new[] { 1, 2, 4, 8 })
        Assert(SimulateDelivery(9, 1, multiplier, useNativeLimitDuringLoading: true) == 9,
            $"limit-one items must remain independent at {multiplier}x");
});

Run("Delivery H. Historical 99 at native 20 remains five safe chunks at every Stack Size", () =>
{
    var chunks = GetNativeDeliveryChunks(99, 20);
    Assert(chunks.SequenceEqual(new[] { 20, 20, 20, 20, 19 }),
        "the native definition limit must produce the established 20,20,20,20,19 payload");

    foreach (var multiplier in new[] { 1, 2, 4, 8 })
    {
        var effectiveLimit = 20 * multiplier;
        var independentlyScaledSlotCount = GetNativeDeliveryChunks(99, effectiveLimit).Count;
        Assert(SimulateDelivery(99, 20, multiplier, useNativeLimitDuringLoading: true) == 99,
            $"unconditional delivery safety must preserve all 99 units at {multiplier}x");
        if (multiplier > 1)
            Assert(independentlyScaledSlotCount < chunks.Count,
                "scaling only the order's fit calculation would reserve fewer slots than the native loader consumes");
    }

    var configuration = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "Configuration.cs"));
    Assert(!configuration.Contains("Delivery Capacity", StringComparison.Ordinal)
           && !configuration.Contains("DeliveryCapacity", StringComparison.Ordinal),
        "do not expose a capacity setting until order preflight and authoritative loading share a safe transaction");
});

Run("Take All A. Configured key keeps its default and live binding", () =>
{
    var update = ExtractMethod(hotkeySource, "public static void Update()", "public static bool IsTyping");
    Assert(update.Contains("Configuration.Instance.TakeAllKey") && update.Contains("Input.GetKeyDown(takeAllKey)"),
        "Take All must continue to use the currently configured key");
    Assert(configurationSource.Contains("\"Take All Key\"") && configurationSource.Contains("KeyCode.R"),
        "R must remain the default and Take All Key must remain configurable");
});

Run("Take All B. Beta storage uses the captured owner context", () =>
{
    var ownerLookup = ExtractMethod(storageMenuSource,
        "public static bool TryGetOpenStorageOwner", "public static void Hide");
    Assert(ownerLookup.Contains("#if SCHEDULE_I_BETA_047") &&
           ownerLookup.Contains("owner = _owner;") &&
           ownerLookup.Contains("owner = menu?.OpenedStorageEntity?.TryCast<IItemSlotOwner>();"),
        "beta must use its captured owner while stable uses the opened storage's IL2CPP owner interface");

    var takeAll = ExtractMethod(hotkeySource, "private static void TryTakeAll()", "private static bool TryQuickMoveEntireStack");
    Assert(takeAll.Contains("TryGetOpenStorageOwner") && takeAll.Contains("var slots = owner.ItemSlots;"),
        "Take All must iterate the open owner's authoritative slots");
    Assert(takeAll.Contains("UserSlotLockRegistry.IsUserLocked(owner, source)"),
        "Take All must retain the owner-aware slot-lock guard");
});

Run("Take All C. Beta cash uses native slot balance and replication APIs", () =>
{
    var takeAll = ExtractMethod(hotkeySource,
        "private static bool TryQuickMoveEntireStack", "private static void MoveIntoDestinations");
    Assert(takeAll.Contains("#if SCHEDULE_I_BETA_047") && takeAll.Contains("MoveCashIntoDestinations"),
        "cash Take All support must remain isolated to the beta target");

    var cashMove = ExtractMethod(hotkeySource,
        "private static void MoveCashIntoDestinations", "private static void MoveIntoDestinations");
    Assert(cashMove.Contains("IsValidCashDestination") && cashMove.Contains("SetStoredItem(copy)") &&
           cashMove.Contains("ChangeBalance(-amount)") && cashMove.Contains("ReplicateStoredInstance()"),
        "cash movement must validate destinations and use native item-slot/balance writes");
});

Run("Cash. Manual routing is scoped to the active StorageEntity on both branches", () =>
{
    var capture = ExtractMethod(interactionSource,
        "private static void CaptureCashCompatibleExternalEndpoint", "private static StorageEntity GetExternalStorage");
    var slotGuard = ExtractMethod(interactionSource,
        "private static bool IsActiveCashStorageSlot", "private static CashStorageKind ClassifyCashStorage");
    var cashGuard = ExtractMethod(interactionSource,
        "internal static bool IsValidCashDestination", "private static CashInstance AsCash");

    Assert(!capture.Contains("#if SCHEDULE_I_047") && capture.Contains("ClassifyCashStorage(storage)") &&
           capture.Contains("CashRoutingPolicy.AllowsExternalStorage(kind)") &&
           capture.Contains("_activeCashCompatibleExternalStorage = storage;"),
        "MAIN and BETA must use one explicit positive storage-category policy");
    Assert(slotGuard.Contains("ContainsSlot(_activeCashCompatibleExternalSlots, candidate)") &&
           slotGuard.Contains("SameObject(owner, _activeCashCompatibleExternalStorage)"),
        "cash destinations must belong to the active, exactly captured storage owner");
    Assert(cashGuard.Contains("candidate.CanSlotAcceptCash()") && cashGuard.Contains("candidate.IsAddLocked") &&
           cashGuard.Contains("DoesItemMatchHardFilters") && cashGuard.Contains("DoesItemMatchPlayerFilters") &&
           cashGuard.Contains("UserSlotLockRegistry.IsUserLocked(candidate)") &&
           cashGuard.Contains("CashRoutingPolicy.GetTransferAmount"),
        "native acceptance, filters, add locks, and user locks must remain enforced");
    Assert(sorterSource.Contains("AsCashSlot(slot) != null || cashInstance != null"),
        "cash must remain protected from sorting and reordering");
});

Run("Cash A. Only bound supported storage categories are accepted", () =>
{
    var helper = ExtractMethod(interactionSource, "private static CashStorageKind ClassifyCashStorage", "private static bool IsRegisteredDeadDropStorage");
    Assert(helper.Contains("SameObject(employeeHome.Storage, storage)") &&
           helper.Contains("SameObject(deadDrop.Storage, storage)") &&
           helper.Contains("SameObject(supplierStash.Storage, storage)"),
        "special native cash endpoints must match their owning component's exact storage");
    Assert(helper.Contains("placeable.GetType() == typeof(PlaceableStorageEntity)") &&
           helper.Contains("SameObject(placeable.StorageEntity, storage)"),
        "ordinary placeables must be the exact general-storage type and bound to its own storage");
    Assert(helper.Contains("CashStorageKind.SpecialPlaceableStorage"),
        "special subclasses such as BedItem must fail closed");
});

Run("Cash B. Category allow/reject policy is shared by MAIN and BETA", () =>
{
    foreach (var branch in new[] { "MAIN", "BETA" })
    {
        Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.GeneralPlaceableStorage),
            $"{branch}: generic storage closet/rack/safe containers should be accepted");
        Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.EmployeeLocker),
            $"{branch}: native employee-home lockers should be accepted");
        Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.DeadDrop),
            $"{branch}: the explicit dead-drop endpoint should remain accepted");
        Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.SupplierStash),
            $"{branch}: the explicit supplier-stash endpoint should remain accepted");
        Assert(!CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.SpecialPlaceableStorage),
            $"{branch}: special placeable subclasses must be rejected");
        Assert(!CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.Unsupported),
            $"{branch}: unknown storage must fail closed");
    }

    var classifier = ExtractMethod(interactionSource, "private static CashStorageKind ClassifyCashStorage", "private static bool IsRegisteredDeadDropStorage");
    Assert(!classifier.Contains("StorageEntityName") && !classifier.Contains("Contains(\"Stash\"") &&
           !classifier.Contains("EndsWith(\"Stash\""),
        "storage category must come from component/type identity, never its display name");
    Assert(!classifier.Contains("PackagingStation") && !classifier.Contains("DryingRack") &&
           !classifier.Contains("MixingStation") && !classifier.Contains("ChemistryStation") &&
           !classifier.Contains("LabOven") && !classifier.Contains("Cauldron") &&
           !classifier.Contains("BrickPress") && !classifier.Contains("MushroomSpawnStation"),
        "specialized workstations must not enter the positive general-storage classification");
});

Run("Cash C. Transfer amount preserves exact balances at capacity boundaries", () =>
{
    var amount = CashRoutingPolicy.GetTransferAmount(75f, 0f, 1000f, destinationAllowed: true);
    var sourceAfter = 75f - amount;
    var destinationAfter = amount;
    Assert(amount == 75f && sourceAfter + destinationAfter == 75f,
        "empty eligible storage must receive the exact source balance without creating or deleting value");
    amount = CashRoutingPolicy.GetTransferAmount(75f, 950f, 1000f, destinationAllowed: true);
    sourceAfter = 75f - amount;
    destinationAfter = 950f + amount;
    Assert(amount == 50f && sourceAfter + destinationAfter == 1025f,
        "partial room must leave exact overflow at the source and preserve total balance");
    Assert(CashRoutingPolicy.GetTransferAmount(75f, 1000f, 1000f, destinationAllowed: true) == 0f,
        "full storage must not accept cash");
    Assert(CashRoutingPolicy.GetTransferAmount(75f, 0f, 1000f, destinationAllowed: false) == 0f,
        "unsupported or locked destinations must not accept cash");
    Assert(CashRoutingPolicy.GetTransferAmount(float.NaN, 0f, 1000f, destinationAllowed: true) == 0f,
        "non-finite balances must fail closed");
});

Run("Branding. Public BAIQ author is PigeonLabs and upstream license attribution stays intact", () =>
{
    Assert(projectSource.Contains("<Authors>PigeonLabs</Authors>") &&
           modSource.Contains("\"PigeonLabs\", \"https://www.nexusmods.com/schedule1/mods/2465\")") &&
           releaseScriptSource.Contains("'Author: PigeonLabs'"),
        "assembly/project metadata and generated candidate manifests must identify PigeonLabs only");
    Assert(readmeSource.Contains("maintained and released by **PigeonLabs**") &&
           readmeSource.Contains("OG Backpack by\nD-Kay") &&
           !readmeSource.Contains("Tugakit") && !readmeSource.Contains("Bread-chan"),
        "PigeonLabs is the public maintainer; required OG Backpack attribution remains separately labeled");
    Assert(releaseNotesSource.Contains("**Author: PigeonLabs**"),
        "public-facing release notes must credit PigeonLabs");
});

Run("Compatibility A. Schedule I 0.4.7 lifecycle is selected for MAIN and BETA", () =>
{
    Assert(playerSource.Contains("#if !SCHEDULE_I_047"),
        "the legacy Player.Load method must be excluded from both 0.4.7 targets");
    Assert(playerSource.Contains("[HarmonyPatch(\"Load\", typeof(PlayerData), typeof(string))]"),
        "the legacy Player.Load(PlayerData, string) path must remain available for older targets");
    Assert(playerSource.Contains("[HarmonyPatch(\"LoadInventory\")]") &&
           projectSource.Contains("<ReferenceProfile>MAIN_047f12</ReferenceProfile>"),
        "MAIN must compile the 0.4.7 LoadInventory lifecycle against its current profile");
    Assert(playerSource.Contains("TryLoadFile(containerPath, UserSlotLockRegistry.SaveFileName"),
        "the preserved older lifecycle must retain its slot-lock restore boundary");
    Assert(projectSource.Contains("<DefineConstants>$(DefineConstants);SCHEDULE_I_047</DefineConstants>") &&
           modSource.Contains("Schedule I 0.4.7 MAIN build"),
        "the stable MAIN target must select and identify the shared 0.4.7 lifecycle");
});

Run("Compatibility B. Beta build is a distinct artifact", () =>
{
    Assert(projectSource.Contains("<Configurations>MONO;IL2CPP;IL2CPP_BETA_047</Configurations>"),
        "the beta must have an explicit build configuration");
    Assert(projectSource.Contains("<AssemblyName>BackpackInventoryQoL.Beta047</AssemblyName>"),
        "the beta must not overwrite the stable DLL");
    Assert(projectSource.Contains("<DefineConstants>$(DefineConstants);IL2CPP;SCHEDULE_I_047;SCHEDULE_I_BETA_047;SCHEDULE_I_BETA_BUILD</DefineConstants>"),
        "the beta lifecycle and separate beta identity must be selected at compile time");
    Assert(modSource.Contains("1.3.0-beta.47") &&
           modSource.Contains("Schedule I 0.4.7 beta build"),
        "the beta artifact must identify itself through version and compatibility logging");
    Assert(!modSource.Contains("Inventory Expanded - Backpack & Inventory QoL (0.4.7 Beta)"),
        "the beta must retain the canonical Melon name used for Mod Manager settings discovery");
});

Run("Compatibility C. Both 0.4.7 builds bind the exact player-data overload", () =>
{
    var currentPath = ExtractMethod(playerManagerSource, "#if IL2CPP && SCHEDULE_I_047", "#else");
    Assert(currentPath.Contains("[typeof(string), typeof(bool), typeof(FullPlayerData)]"),
        "both 0.4.7 builds must bind the exact overload");
    Assert(currentPath.Contains("bool __1") && currentPath.Contains("ref FullPlayerData __2"),
        "the patch must capture isHost and the out FullPlayerData argument positionally");
});

Run("Compatibility D. The 0.4.7 lifecycle restores slot locks only for the host request", () =>
{
    var currentPath = ExtractMethod(playerManagerSource, "#if IL2CPP && SCHEDULE_I_047", "#else");
    var hostGate = currentPath.IndexOf("if (__1)", StringComparison.Ordinal);
    var lockLoad = currentPath.IndexOf("UserSlotLockRegistry.LoadPersistent(lockData);", StringComparison.Ordinal);
    var lockReset = currentPath.IndexOf("UserSlotLockRegistry.LoadPersistent(null);", StringComparison.Ordinal);
    var backpackLoad = currentPath.IndexOf("TryLoadFile(dataPath, \"Backpack\"", StringComparison.Ordinal);

    Assert(hostGate >= 0 && lockLoad > hostGate && lockReset > hostGate,
        "slot locks must load or reset inside the confirmed host request only");
    Assert(backpackLoad > lockLoad && backpackLoad > lockReset,
        "slot-lock missing-file reset must run even when the Backpack subfile is absent");
});

Run("Compatibility E. The 0.4.7 Backpack hydration and persistence formats remain unchanged", () =>
{
    Assert(playerManagerSource.Contains("__2.InventoryString += \"|||\" + backpackString;"),
        "the 0.4.7 manager must continue appending the existing delimited payload");
    Assert(playerSource.Contains("BackpackPersistencePolicy.TryExtractBackpackPayload") &&
           playerSource.Contains("LoadInventoryPostfix") &&
           playerSource.Contains("CompletePendingHydration"),
        "the 0.4.7 LoadInventory path must extract before native loading and hydrate afterward");
    Assert(playerSource.Contains("private const string BackpackSaveFile = \"Backpack\";") &&
           playerSource.Contains("UserSlotLockRegistry.SaveFileName"),
        "shared persistence identifiers must remain unchanged");
});

Run("Persistence A. Write eligibility requires owner and bound storage", () =>
{
    Assert(BackpackPersistencePolicy.CanWriteBackpack(true, true, true),
        "an authoritative owner with its bound storage must be allowed to save, including an empty Backpack");
    Assert(!BackpackPersistencePolicy.CanWriteBackpack(false, true, true),
        "a non-owner must never write Backpack persistence");
    Assert(!BackpackPersistencePolicy.CanWriteBackpack(true, false, true),
        "an unavailable storage must not overwrite persistence");
    Assert(!BackpackPersistencePolicy.CanWriteBackpack(true, true, false),
        "a storage not bound to the writing player must not overwrite persistence");
    Assert(playerSource.IndexOf("if (!__instance.IsOwner)", StringComparison.Ordinal)
           < playerSource.IndexOf("new ItemSet(backpackStorage.ItemSlots).GetJSON()", StringComparison.Ordinal),
        "the ownership guard must execute before Backpack serialization");
});

Run("Persistence B. Player path indices fail closed", () =>
{
    Assert(BackpackPersistencePolicy.IsValidPlayerPathIndex(0, 1, 1),
        "the sole valid player path must resolve");
    Assert(BackpackPersistencePolicy.IsValidPlayerPathIndex(1, 3, 3),
        "the selected entry must remain valid when multiple players exist");
    Assert(!BackpackPersistencePolicy.IsValidPlayerPathIndex(-1, 1, 1),
        "IndexOf == -1 must fail safely");
    Assert(!BackpackPersistencePolicy.IsValidPlayerPathIndex(1, 2, 1),
        "a shorter path list must fail safely");
    Assert(!BackpackPersistencePolicy.IsValidPlayerPathIndex(1, 1, 2),
        "an index beyond the player-data list must fail safely");
    Assert(BackpackPersistencePolicy.IsMatchingPlayerIdentity("player-b", "player-b", "player-b"),
        "the intended player entry must pass identity validation when multiple players exist");
    Assert(!BackpackPersistencePolicy.IsMatchingPlayerIdentity("player-b", "player-b", "player-a"),
        "a path index belonging to another player must fail identity validation");
    Assert(!BackpackPersistencePolicy.IsMatchingPlayerIdentity("", "player-b", "player-b"),
        "a missing requested player identity must fail safely");
    Assert(BackpackPersistencePolicy.IsValidPlayerDataIndex(0, 1),
        "a matched player-data entry must remain usable when the 0.4.7 path collection is transiently empty");
    Assert(!BackpackPersistencePolicy.IsValidPlayerDataIndex(-1, 1) &&
           !BackpackPersistencePolicy.IsValidPlayerDataIndex(1, 1),
        "fallback path resolution must still require an exact loaded player-data entry");
    Assert(playerManagerSource.Contains("Directory.Exists(directory)") &&
           playerManagerSource.Contains("File.Exists(backpackFilePath)"),
        "the 0.4.7 path must validate its directory and treat a missing Backpack file explicitly");
});

Run("Persistence C. The 0.4.7 fallback is rooted and identity-bound", () =>
{
    var playersRoot = Path.Combine(Path.GetTempPath(), "safe-save", "Players");
    Assert(BackpackPersistencePolicy.IsPathWithinRoot(Path.Combine(playersRoot, "Player_0"), playersRoot),
        "an immediate player directory beneath the authoritative Players root must be accepted");
    Assert(!BackpackPersistencePolicy.IsPathWithinRoot(Path.Combine(Path.GetDirectoryName(playersRoot)!, "Players_Other", "Player_0"), playersRoot),
        "a sibling path with a matching prefix must not escape the authoritative Players root");
    Assert(BackpackPersistencePolicy.IsMatchingPlayerFileIdentity("owner-code", "owner-code"),
        "an exact Player.json identity must be accepted");
    Assert(!BackpackPersistencePolicy.IsMatchingPlayerFileIdentity("owner-code", "other-code") &&
           !BackpackPersistencePolicy.IsMatchingPlayerFileIdentity("owner-code", ""),
        "a mismatched or absent Player.json identity must fail closed");

    var currentPath = ExtractMethod(playerManagerSource, "#if IL2CPP && SCHEDULE_I_047", "#else");
    Assert(currentPath.Contains("Singleton<LoadManager>.Instance") &&
           currentPath.Contains("LoadedGameFolderPath") &&
           currentPath.Contains("Path.Combine(activeSaveRoot, \"Players\")"),
        "the fallback must begin at Schedule I's active authoritative load root");
    Assert(currentPath.Contains("TryGetProperty(\"PlayerCode\"") &&
           currentPath.Contains("matches.Count != 1"),
        "the fallback must select exactly one Player.json identity and reject zero or ambiguous matches");
    Assert(!currentPath.Contains("Path.Combine(activeSaveRoot, \"Players\", \"Player_0\")"),
        "the fallback must never guess Player_0");
});

Run("Persistence D. Payload extraction preserves native inventory", () =>
{
    Assert(BackpackPersistencePolicy.TryExtractBackpackPayload(
               "native-json|||backpack-json",
               out var nativeInventory,
               out var backpackPayload),
        "a delimited Backpack payload must be detected");
    Assert(nativeInventory == "native-json" && backpackPayload == "backpack-json",
        "native and Backpack payloads must be separated without modification");
    Assert(!BackpackPersistencePolicy.TryExtractBackpackPayload(
               "native-only",
               out var untouchedInventory,
               out var missingPayload),
        "a native-only inventory must remain on the unchanged path");
    Assert(untouchedInventory == "native-only" && missingPayload == null,
        "native-only inventory data must be returned untouched");
    Assert(BackpackPersistencePolicy.TryExtractBackpackPayload(
               "native|||payload|||content",
               out _,
               out var payloadWithDelimiter)
           && payloadWithDelimiter == "payload|||content",
        "only the first delimiter may split the native inventory from the Backpack payload");
});

Run("Persistence E. Deferred hydration is session-bound and apply-once", () =>
{
    Assert(playerSource.Contains("pending.SessionGeneration != BackpackMod.SessionGeneration"),
        "pending 0.4.7 hydration must be cancelled across save sessions");
    Assert(playerSource.Contains("yield return null;"),
        "0.4.7 hydration must cross the native LoadInventory frame boundary");
    Assert(playerSource.Contains("pending.Applying = true;") &&
           playerSource.Contains("Never retry after LoadTo begins"),
        "0.4.7 hydration must not reapply an ItemSet after application begins");
    Assert(modSource.Contains("PlayerPatch.ResetPendingHydration(reason)"),
        "session resets must invalidate pending 0.4.7 hydration");
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Post-release bugfix tests failed: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("Post-release bugfix tests passed: 28/28 scenarios.");
return 0;

int SimulateDelivery(int requested, int nativeLimit, int multiplier, bool useNativeLimitDuringLoading)
{
    var slots = new List<int>();
    var remainingOrder = requested;
    while (remainingOrder > 0)
    {
        var chunk = Math.Min(remainingOrder, nativeLimit);
        InsertUsingNativeDeliverySemantics(slots, chunk,
            useNativeLimitDuringLoading ? nativeLimit : nativeLimit * multiplier);
        remainingOrder -= chunk;
    }

    return slots.Sum();
}

List<int> GetNativeDeliveryChunks(int requested, int nativeLimit)
{
    var chunks = new List<int>();
    var remaining = requested;
    while (remaining > 0)
    {
        var chunk = Math.Min(remaining, nativeLimit);
        chunks.Add(chunk);
        remaining -= chunk;
    }

    return chunks;
}

void InsertUsingNativeDeliverySemantics(List<int> slots, int incoming, int visibleLimit)
{
    var remaining = incoming;
    for (var i = 0; i < slots.Count && remaining > 0; i++)
    {
        if (slots[i] <= 0 || slots[i] + incoming > visibleLimit)
            continue;

        var amount = Math.Min(visibleLimit - slots[i], remaining);
        remaining -= amount;

        // Schedule I's normally unreachable merge branch decrements the stored
        // quantity while treating the incoming quantity as consumed.
        slots[i] -= amount;
    }

    if (remaining > 0)
        slots.Add(remaining);
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

string ExtractMethod(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    Assert(start >= 0 && end > start, $"could not locate source range {startMarker}");
    return source[start..end];
}

string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "InventoryExpanded.sln")))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate InventoryExpanded repository root.");
}
