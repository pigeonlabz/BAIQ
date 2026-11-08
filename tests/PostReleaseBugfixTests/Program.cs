using Backpack;
using Backpack.Config;

var failures = new List<string>();
var passed = 0;
var repository = FindRepositoryRoot(AppContext.BaseDirectory);
var shopSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "ShopInterfacePatch.cs"));
var interactionSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "InventoryInteractionPatch.cs"));
var hotkeySource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Utility", "StorageHotkeys.cs"));
var configurationSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "Configuration.cs"));
var configSyncSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "ConfigSyncManager.cs"));
var configSyncPayloadSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "ConfigSyncPayloadPolicy.cs"));
var storageMenuSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "StorageSortButton.cs"));
var sorterSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Utility", "InventorySorter.cs"));
var deliverySource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "DeliveryPatch.cs"));
var cheatSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "CheatPatch.cs"));
var playerSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlayerPatch.cs"));
var multiplayerPersistenceSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "MultiplayerBackpackPersistencePatch.cs"));
var persistenceDiagnosticsSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "BackpackPersistenceDiagnosticsPatch.cs"));
var playerManagerSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlayerManagerPatch.cs"));
var projectSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "InventoryExpanded.csproj"));
var modSource = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "BackpackMod.cs"));
var readmeSource = File.ReadAllText(Path.Combine(repository, "README.md"));
var releaseNotesSource = File.ReadAllText(Path.Combine(repository, "docs", "RELEASE_NOTES_1.3.0.md"));
var releaseScriptSource = File.ReadAllText(Path.Combine(repository, "tools", "prepare-release-candidate.ps1"));

Run("Quick move A. Native lock and machine restrictions exclude destinations", () =>
{
    var eligibility = ExtractMethod(interactionSource,
        "internal static bool IsNativeTransferDestinationBlocked", "private static void TryBeginPartialStackDrag");
    var quickMove = ExtractMethod(interactionSource,
        "internal static SlotList FilterValidQuickMoveDestinations", "internal static bool IsNativeTransferDestinationBlocked");
    var cash = ExtractMethod(interactionSource,
        "internal static bool IsValidCashDestination", "private static CashInstance AsCash");
    var reorder = ExtractMethod(File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Utility", "InventorySorter.cs")),
        "private static bool CanReorder", "private static bool PreservedSlotsMatch");

    Assert(eligibility.Contains("candidate.IsLocked") && eligibility.Contains("candidate.IsAddLocked") &&
           eligibility.Contains("candidate.IsRemovalLocked"),
        "custom routes must preserve Schedule I active reservations, add locks, and removal locks");
    Assert(quickMove.Contains("IsNativeTransferDestinationBlocked(candidate)"),
        "shift-click and contextual routes must reject native locked destinations before capacity checks");
    Assert(cash.Contains("IsNativeTransferDestinationBlocked(candidate)"),
        "cash quick-move routes must reject the same native slot restrictions");
    Assert(reorder.Contains("slot.IsLocked") && reorder.Contains("slot.IsAddLocked") && reorder.Contains("slot.IsRemovalLocked"),
        "Sort must leave employee-reserved and other native-locked slots untouched");
});

Run("Quick move priority A. Configured destination tier precedes native partial-stack consolidation", () =>
{
    var scenarios = new[]
    {
        (Name: "no matching stacks; both destinations empty", PreferredCapacity: 8, FallbackCapacity: 8, DeferFallback: true, ExpectedTier: "preferred"),
        (Name: "matching full stack in fallback", PreferredCapacity: 8, FallbackCapacity: 0, DeferFallback: true, ExpectedTier: "preferred"),
        (Name: "matching partial stack only in fallback", PreferredCapacity: 8, FallbackCapacity: 3, DeferFallback: true, ExpectedTier: "preferred"),
        (Name: "matching partial stacks in both destinations", PreferredCapacity: 2, FallbackCapacity: 5, DeferFallback: true, ExpectedTier: "preferred"),
        (Name: "preferred destination full; fallback partial", PreferredCapacity: 0, FallbackCapacity: 4, DeferFallback: false, ExpectedTier: "fallback"),
        (Name: "preferred destination filtered, add-locked, or removal-locked", PreferredCapacity: 0, FallbackCapacity: 4, DeferFallback: false, ExpectedTier: "fallback"),
        (Name: "preferred machine slots employee-reserved", PreferredCapacity: 0, FallbackCapacity: 4, DeferFallback: false, ExpectedTier: "fallback"),
        (Name: "modified stack limit provides preferred room", PreferredCapacity: 7, FallbackCapacity: 4, DeferFallback: true, ExpectedTier: "preferred"),
        (Name: "modified stack limit leaves no preferred room", PreferredCapacity: 0, FallbackCapacity: 4, DeferFallback: false, ExpectedTier: "fallback"),
        (Name: "no eligible destination", PreferredCapacity: 0, FallbackCapacity: 0, DeferFallback: false, ExpectedTier: "none"),
    };

    foreach (var backpackFirst in new[] { true, false })
    {
        Assert(QuickMoveDestinationPriorityPolicy.IsPreferredDestination(true, backpackFirst) == backpackFirst
               && QuickMoveDestinationPriorityPolicy.IsPreferredDestination(false, backpackFirst) != backpackFirst,
            $"BackpackFirst={backpackFirst} must classify Backpack and Inventory symmetrically");

        foreach (var scenario in scenarios)
        {
            var deferred = QuickMoveDestinationPriorityPolicy.ShouldDeferFallback(scenario.PreferredCapacity);
            var selectedTier = scenario.PreferredCapacity > 0
                ? "preferred"
                : scenario.FallbackCapacity > 0 ? "fallback" : "none";
            Assert(deferred == scenario.DeferFallback,
                $"BackpackFirst={backpackFirst}, {scenario.Name}: lower-priority destinations defer exactly while eligible preferred capacity remains");
            Assert(selectedTier == scenario.ExpectedTier,
                $"BackpackFirst={backpackFirst}, {scenario.Name}: a rejected preferred tier falls through only when fallback has eligible capacity");
        }
    }

    var priorityCapture = ExtractMethod(interactionSource, "private static void CaptureQuickMovePriorityDestinations", "private static int GetPreferredRemainingCapacity");
    var capacityCheck = ExtractMethod(interactionSource, "private static int GetPreferredRemainingCapacity", "internal static bool ShouldDeferFallbackCapacity");
    var fallbackGate = ExtractMethod(interactionSource, "internal static bool ShouldDeferFallbackCapacity", "private static void ClearQuickMovePriorityContext");
    var getQuickMove = ExtractMethod(interactionSource, "public static void GetQuickMoveSlots(ItemSlot __0, ref SlotList __result)", "internal static SlotList FilterValidQuickMoveDestinations");
    var capacityPatch = ExtractMethod(interactionSource, "public static void GetCapacityForItemPostfix", "[HarmonyPatch(typeof(ItemSlotUI))]");

    Assert(getQuickMove.IndexOf("FilterValidQuickMoveDestinations(__0, __result)", StringComparison.Ordinal)
               < getQuickMove.IndexOf("CaptureQuickMovePriorityDestinations(__0, __result)", StringComparison.Ordinal)
           && getQuickMove.Contains("IsExternalQuickMoveSource(__0)"),
        "priority tiers must be captured only from validated destinations for the active external-source quick move");
    Assert(priorityCapture.Contains("backpack.OwnsSlot(candidate)")
           && priorityCapture.Contains("QuickMoveDestinationPriorityPolicy.IsPreferredDestination"),
        "the current priority setting must split Backpack and regular-inventory candidates into preferred and fallback tiers");
    Assert(capacityCheck.Contains("IsNativeTransferDestinationBlocked(preferred)")
           && capacityCheck.Contains("preferred.GetCapacityForItem(item, true)"),
        "preferred capacity must honor native employee reservations, filters, add/removal locks, and the live effective stack limit");
    Assert(fallbackGate.Contains("ContainsSlot(_priorityFallbackDestinations, destination)")
           && fallbackGate.Contains("QuickMoveDestinationPriorityPolicy.ShouldDeferFallback")
           && capacityPatch.Contains("__result = 0"),
        "native capacity checks for lower-priority candidates must be deferred only while a preferred destination can accept the item");
    Assert(!capacityPatch.Contains("ChangeQuantity(") && !capacityPatch.Contains("SetQuantity(")
           && !capacityPatch.Contains("SetStoredItem(") && !capacityPatch.Contains("AddItem("),
        "the priority shim must not mutate source or destination quantities and must leave native commit/replication ownership intact");
});

Run("Quick move B. Take All verifies destination commit before source decrement", () =>
{
    var move = ExtractMethod(hotkeySource, "private static void MoveIntoDestinations", "private static int GetVerifiedAcceptedQuantity");
    var verify = ExtractMethod(hotkeySource, "private static int GetVerifiedAcceptedQuantity", "private static CashInstance AsCash");
    var add = move.IndexOf("destination.AddItem(copy)", StringComparison.Ordinal);
    var verifyCall = move.IndexOf("GetVerifiedAcceptedQuantity", StringComparison.Ordinal);
    var remove = move.IndexOf("source.ChangeQuantity(-accepted)", StringComparison.Ordinal);

    Assert(move.Contains("IsNativeTransferDestinationBlocked(destination)"),
        "manual Take All writes must recheck native destination locks at commit time");
    Assert(move.Contains("source.IsLocked || source.IsRemovalLocked"),
        "Take All must not remove from an active native lock or removal-locked source");
    Assert(add >= 0 && verifyCall > add && remove > verifyCall,
        "Take All must verify accepted destination quantity before reducing its source");
    Assert(verify.Contains("afterItem.CanStackWith(sourceItem, false)") &&
           verify.Contains("afterQuantity - priorQuantity") &&
           verify.Contains("accepted <= requested"),
        "verified transfer quantity must be compatible, positive, and bounded by the request");
});

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

Run("Take All B. MAIN uses the captured open owner context", () =>
{
    var ownerLookup = ExtractMethod(storageMenuSource,
        "public static bool TryGetOpenStorageOwner", "public static void Hide");
    Assert(ownerLookup.Contains("owner = _owner;") &&
           !ownerLookup.Contains("OpenedStorageEntity") &&
           !ownerLookup.Contains("SCHEDULE_I_BETA_047"),
        "the supported MAIN build must use one captured owner path for every StorageMenu.Open overload");

    var takeAll = ExtractMethod(hotkeySource, "private static void TryTakeAll()", "private static bool TryQuickMoveEntireStack");
    Assert(takeAll.Contains("TryGetOpenStorageOwner") && takeAll.Contains("var slots = owner.ItemSlots;"),
        "Take All must iterate the open owner's authoritative slots");
    Assert(takeAll.Contains("UserSlotLockRegistry.IsUserLocked(owner, source)"),
        "Take All must retain the owner-aware slot-lock guard");
});

Run("Take All C. Cash uses validated native slot balance and replication APIs", () =>
{
    var takeAll = ExtractMethod(hotkeySource,
        "private static bool TryQuickMoveEntireStack", "private static void MoveIntoDestinations");
    Assert(takeAll.Contains("MoveCashIntoDestinations") && takeAll.Contains("originalCashBalance"),
        "the unified MAIN target must preserve balance-aware cash Take All support");

    var cashMove = ExtractMethod(hotkeySource,
        "private static void MoveCashIntoDestinations", "private static void MoveIntoDestinations");
    Assert(cashMove.Contains("IsValidCashDestination") && cashMove.Contains("SetStoredItem(copy)") &&
           cashMove.Contains("ChangeBalance(-amount)") && cashMove.Contains("ReplicateStoredInstance()"),
        "cash movement must validate destinations and use native item-slot/balance writes");
});

Run("Cash. Manual routing is scoped to the active StorageEntity", () =>
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
        "the supported MAIN build must use one explicit positive storage-category policy");
    Assert(slotGuard.Contains("ContainsSlot(_activeCashCompatibleExternalSlots, candidate)") &&
           slotGuard.Contains("SameObject(owner, _activeCashCompatibleExternalStorage)"),
        "cash destinations must belong to the active, exactly captured storage owner");
    Assert(cashGuard.Contains("candidate.CanSlotAcceptCash()") && cashGuard.Contains("IsNativeTransferDestinationBlocked(candidate)") &&
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

Run("Cash B. Category allow/reject policy is explicit", () =>
{
    Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.GeneralPlaceableStorage),
        "generic storage closet/rack/safe containers should be accepted");
    Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.EmployeeLocker),
        "native employee-home lockers should be accepted");
    Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.DeadDrop),
        "the explicit dead-drop endpoint should remain accepted");
    Assert(CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.SupplierStash),
        "the explicit supplier-stash endpoint should remain accepted");
    Assert(!CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.SpecialPlaceableStorage),
        "special placeable subclasses must be rejected");
    Assert(!CashRoutingPolicy.AllowsExternalStorage(CashStorageKind.Unsupported),
        "unknown storage must fail closed");

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

Run("Compatibility A. MAIN selects the Schedule I 0.4.7 lifecycle", () =>
{
    Assert(playerSource.Contains("#if !SCHEDULE_I_047"),
        "the legacy Player.Load method must be excluded from both 0.4.7 targets");
    Assert(playerSource.Contains("[HarmonyPatch(\"Load\", typeof(PlayerData), typeof(string))]"),
        "the legacy Player.Load(PlayerData, string) path must remain available for older targets");
    Assert(playerSource.Contains("[HarmonyPatch(\"LoadInventory\")]") &&
           projectSource.Contains("<ReferenceGameVersion>0.4.7f12</ReferenceGameVersion>") &&
           projectSource.Contains("<ExpectedAssemblyCSharpSha256>F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D</ExpectedAssemblyCSharpSha256>"),
        "MAIN must compile the 0.4.7 LoadInventory lifecycle against the pinned f12 generated references");
    Assert(playerSource.Contains("__instance.Loader.TryLoadFile(containerPath, UserSlotLockRegistry.SaveFileName"),
        "the preserved older lifecycle must retain its slot-lock restore boundary");
    Assert(projectSource.Contains(";SCHEDULE_I_047</DefineConstants>") &&
           modSource.Contains("Schedule I 0.4.7 MAIN build"),
        "the stable MAIN target must select and identify the shared 0.4.7 lifecycle");
});

Run("Compatibility B. One MAIN IL2CPP artifact is configured", () =>
{
    Assert(projectSource.Contains("<Configurations>MONO;IL2CPP</Configurations>") &&
           !projectSource.Contains("IL2CPP_BETA_047"),
        "the active project must expose only the supported MAIN IL2CPP target and legacy MONO configuration");
    Assert(projectSource.Contains("<AssemblyName>BackpackInventoryQoL</AssemblyName>") &&
           projectSource.Contains("<DefineConstants>$(DefineConstants);IL2CPP;SCHEDULE_I_047</DefineConstants>"),
        "MAIN must compile the IL2CPP implementation into the established public DLL identity");
    Assert(modSource.Contains("Backpack.BuildMetadata.MelonName, \"1.3.3\", \"PigeonLabs\"") &&
           modSource.Contains("public const string MelonName = \"Inventory Expanded - Backpack & Inventory QoL\";") &&
           modSource.Contains("Schedule I 0.4.7 MAIN build") &&
           !modSource.Contains("1.3.0-beta.47"),
        "the unified artifact must have MAIN version metadata and the canonical settings-discovery name");
    Assert(releaseScriptSource.Contains("Name = 'MAIN'; Config = 'IL2CPP'; RefName = 'MAIN_047f12'") &&
           releaseScriptSource.Contains("Version = '1.3.3'") &&
           !releaseScriptSource.Contains("IL2CPP_BETA_047"),
        "candidate preparation must stage only MAIN against the f12 profile");
});

Run("Compatibility C. MAIN binds the exact player-data overload", () =>
{
    var currentPath = ExtractMethod(playerManagerSource, "#if IL2CPP && SCHEDULE_I_047", "#else");
    Assert(currentPath.Contains("nameof(PlayerManager.TryGetPlayerData)") &&
           currentPath.Contains("[typeof(string), typeof(bool), typeof(FullPlayerData)]") &&
           currentPath.Contains("[ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out]"),
        "the supported MAIN build must target the synchronous out-data overload explicitly");
    Assert(currentPath.Contains("bool __1") && currentPath.Contains("ref FullPlayerData __2"),
        "the MAIN patch must capture isHost and the out FullPlayerData argument positionally");
});

Run("Compatibility D. The 0.4.7 lifecycle restores slot locks only for the host request", () =>
{
    var currentPathStart = playerManagerSource.IndexOf("#if IL2CPP && SCHEDULE_I_047", StringComparison.Ordinal);
    var currentPathEnd = playerManagerSource.IndexOf("#else\n    [HarmonyPatch(nameof(PlayerManager.TryGetPlayerData))]", currentPathStart, StringComparison.Ordinal);
    var currentPath = currentPathStart >= 0 && currentPathEnd > currentPathStart
        ? playerManagerSource[currentPathStart..currentPathEnd]
        : string.Empty;
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
    Assert(playerSource.Contains("if (isOwner)") &&
           playerSource.Contains("TryGetAuthoritativeBackpackStorage(__instance, out var ownerStorage"),
        "local writes must continue through the existing owner-bound storage validation");
    Assert(playerSource.Contains("BackpackPersistencePolicy.CanWriteBackpack(player.IsOwner, true, storageBoundToPlayer)"),
        "the owner path must retain the player-bound storage safeguard");
});

Run("Persistence A2. Guest writes require a changed host-authoritative replica", () =>
{
    Assert(BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, true, true, true, true, true),
        "a host may persist a guest's directly bound, server-initialized Backpack after observing a native server mutation");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(false, false, true, true, true, true, true, true),
        "a client-only process must not write a guest path");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, true, true, true, true, true, true, true),
        "the replicated path must not replace local-owner persistence");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, false, true, true, true, true, true),
        "a snapshot without the target guest identity must fail closed");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, false, true, true, true, true),
        "a StorageEntity not bound to the target Player must fail closed");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, true, false, true, true, true),
        "a non-Backpack StorageEntity must fail closed");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, true, true, false, true, true),
        "an uninitialized server replica must fail closed");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, true, true, true, false, true),
        "an uninitialized slot list must fail closed");
    Assert(!BackpackPersistencePolicy.CanWriteReplicatedBackpack(true, false, true, true, true, true, true, false),
        "a stale or unobserved empty replica must not overwrite an existing sidecar");

    Assert(multiplayerPersistenceSource.Contains("RpcLogic___SetStoredInstance") == false,
        "the fix must not invoke generated RPC logic directly");
    Assert(multiplayerPersistenceSource.Contains("SetStoredInstance_Internal") &&
           multiplayerPersistenceSource.Contains("SetItemSlotQuantity_Internal") &&
           !multiplayerPersistenceSource.Contains("SetSlotFilter_Internal"),
        "freshness must follow native item/quantity mutations, not a filter-only change");
    Assert(multiplayerPersistenceSource.Contains("TryResolveCandidateBackpackPath") &&
           multiplayerPersistenceSource.Contains("Player.json") &&
           multiplayerPersistenceSource.Contains("LoadedGameFolderPath") &&
           playerSource.Contains("AtomicTextFileWriter.TryWrite(candidateBackpackPath, contents") &&
           playerSource.IndexOf("AtomicTextFileWriter.TryWrite", StringComparison.Ordinal)
               < playerSource.IndexOf("MarkPersisted(", StringComparison.Ordinal),
        "guest state must be committed atomically only to the active save's exact PlayerCode-bound sidecar");
    Assert(multiplayerPersistenceSource.Contains("SnapshotWriteLock") &&
           multiplayerPersistenceSource.Contains("CanAcknowledgeSnapshot") &&
           multiplayerPersistenceSource.Contains("_resetGeneration"),
        "writes, mutations and session reset must coordinate so stale callbacks cannot acknowledge newer state");
    Assert(persistenceDiagnosticsSource.Contains("DescribeFileState(candidatePath)") &&
           persistenceDiagnosticsSource.Contains("candidateBackpackPath='{candidatePath"),
        "write diagnostics must inspect the per-player sidecar, not the native writer's relative filename");
    Assert(playerSource.Contains("string.Equals(reason, \"player is not the local owner\", StringComparison.Ordinal)") &&
           playerSource.Contains("0.4.7 remote-player hydration was not applied"),
        "expected host-side non-owner hydration skips must remain guarded but avoid warning-level noise");
    Assert(multiplayerPersistenceSource.Contains("host-side guest storage inspection failed safely") &&
           multiplayerPersistenceSource.Contains("host-side guest StorageEntity validation failed safely"),
        "IL2CPP storage inspection failures must reject the guest snapshot instead of escaping the persistence hook");
    var ownerBranchStart = playerSource.IndexOf("if (isOwner)", StringComparison.Ordinal);
    var ownerBranchReturn = playerSource.IndexOf("return;", ownerBranchStart, StringComparison.Ordinal);
    var lockWrite = playerSource.IndexOf("UserSlotLockRegistry.SerializePersistent()", StringComparison.Ordinal);
    Assert(ownerBranchStart >= 0 && lockWrite > ownerBranchStart && lockWrite < ownerBranchReturn,
        "the host must never write its own slot-lock registry under a guest Player path");
});

Run("Persistence A3. Dirty versions reject stale acknowledgements", () =>
{
    Assert(BackpackPersistencePolicy.CanAcknowledgeSnapshot(1, 0, 1),
        "the current dirty version must be acknowledgeable after verified readback");
    Assert(!BackpackPersistencePolicy.CanAcknowledgeSnapshot(2, 0, 3),
        "a future version must not be acknowledged");
    Assert(!BackpackPersistencePolicy.CanAcknowledgeSnapshot(2, 1, 1),
        "an already persisted or stale version must not move persistence state backwards");
    Assert(BackpackPersistencePolicy.IsCurrentDirtySnapshot(2, 1, 2) &&
           !BackpackPersistencePolicy.IsCurrentDirtySnapshot(2, 1, 1),
        "only the current dirty version is eligible to start a write");
});

Run("Persistence A4. Guest sidecars use staged atomic commits", () =>
{
    var temporaryRoot = Path.Combine(repository, ".release-work", "atomic-write-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporaryRoot);
    try
    {
        var destination = Path.Combine(temporaryRoot, "Backpack.json");
        File.WriteAllText(destination, "old-valid-sidecar");
        Assert(AtomicTextFileWriter.TryWrite(destination, "new-complete-sidecar", out var replaceReason),
            "a validated replacement should commit successfully: " + replaceReason);
        Assert(File.ReadAllText(destination) == "new-complete-sidecar",
            "the committed file must exactly match the complete staged payload");

        var newDestination = Path.Combine(temporaryRoot, "first-save", "Backpack.json");
        Directory.CreateDirectory(Path.GetDirectoryName(newDestination)!);
        Assert(AtomicTextFileWriter.TryWrite(newDestination, "first-complete-sidecar", out var createReason),
            "a first sidecar should be created by an atomic same-directory rename: " + createReason);
        Assert(File.ReadAllText(newDestination) == "first-complete-sidecar",
            "a new target must read back as the complete payload");

        var protectedDestination = Path.Combine(temporaryRoot, "protected.json");
        File.WriteAllText(protectedDestination, "prior-valid-sidecar");
        bool blockedCommit;
        using (new FileStream(protectedDestination, FileMode.Open, FileAccess.Read, FileShare.None))
            blockedCommit = !AtomicTextFileWriter.TryWrite(protectedDestination, "replacement", out _);
        Assert(blockedCommit,
            "a target that cannot be atomically replaced must report failure");
        Assert(File.ReadAllText(protectedDestination) == "prior-valid-sidecar",
            "a failed commit must leave the previous valid sidecar untouched");

        Assert(!AtomicTextFileWriter.TryWrite(Path.Combine(temporaryRoot, "missing", "Backpack.json"), "bad", out _),
            "a missing target directory must fail closed");
        Assert(!Directory.GetFiles(temporaryRoot, ".Backpack.json.*.tmp", SearchOption.AllDirectories).Any(),
            "successful and failed writes must clean their temporary files");
        Assert(!Directory.GetFiles(temporaryRoot, ".Backpack.json.*.bak", SearchOption.AllDirectories).Any(),
            "successful writes must remove the backup after exact readback succeeds");
    }
    finally
    {
        Directory.Delete(temporaryRoot, recursive: true);
    }
});

Run("Config A. Lobby payload validation excludes capacity", () =>
{
    var valid = ConfigSyncPayloadPolicy.Create("1.3.2.0", "Street_Rat", 1, false);
    Assert(ConfigSyncPayloadPolicy.TryParse(valid, "1.3.2.0", out var parsed),
        "a complete matching host payload should parse");
    Assert(parsed.UnlockRank == "Street_Rat" && parsed.UnlockTier == 1 && !parsed.EnableSearch,
        "the parsed legacy values must retain their meaning");
    Assert(!ConfigSyncPayloadPolicy.TryParse(valid, "1.3.1.0", out _),
        "mismatched mod versions must fail closed");
    Assert(!ConfigSyncPayloadPolicy.TryParse("2|1.3.2.0|Street_Rat|1", "1.3.2.0", out _),
        "truncated payloads must fail closed");
    Assert(!ConfigSyncPayloadPolicy.TryParse("2|1.3.2.0|Street_Rat|9|false", "1.3.2.0", out _),
        "out-of-range rank tiers must fail closed");
    Assert(!ConfigSyncPayloadPolicy.TryParse("2|1.3.2.0|Street_Rat|1|maybe", "1.3.2.0", out _),
        "invalid boolean settings must fail closed");
    Assert(!configSyncPayloadSource.Contains("StorageSlots") &&
           !configSyncSource.Contains("Configuration.Instance.StorageSlots ="),
        "multiplayer lobby metadata must not force guest Backpack capacity to the host's value");
    Assert(configSyncSource.Contains("if (isHost)") &&
           configSyncSource.Contains("lobbyService.SetLobbyData(Prefix, payload)") &&
           configSyncSource.Contains("lobbyService.GetLobbyData(Prefix)") &&
           configSyncSource.Contains("Enum.IsDefined(typeof(ERank), rank)"),
        "only the current host publishes legacy settings through the captured active service and parsed enum values must be defined");
});

Run("Gameplay policy A. Steam-owner-bound host policy validates strictly", () =>
{
    const ulong sessionGeneration = 7;
    const ulong ownerId = 76543210987654321;
    var settings = new GameplaySettingsSnapshot(8, true, false, true, false);
    var nonce = Guid.NewGuid().ToString("N");
    var payload = GameplaySettingsPolicy.CreateEnvelope("1.3.2.0", ownerId, nonce, settings);
    Assert(!string.IsNullOrWhiteSpace(payload), "a supported host policy should serialize");
    Assert(!payload.Contains(ownerId.ToString(), StringComparison.Ordinal),
        "the Steam owner ID must not be exposed in the lobby payload");
    Assert(GameplaySettingsPolicy.TryParseEnvelope(payload, "1.3.2.0", ownerId, out var parsedNonce, out var parsed),
        "the active game's Steam owner identity must accept a valid host policy");
    Assert(parsedNonce == nonce && parsed == settings,
        "every supported setting must round-trip unchanged");
    Assert(!GameplaySettingsPolicy.TryParseEnvelope(payload, "1.3.1.0", ownerId, out _, out _),
        "mismatched mod versions must fail closed");
    Assert(!GameplaySettingsPolicy.TryParseEnvelope(payload, "1.3.2.0", ownerId + 1, out _, out _),
        "policy claiming a previous or non-host Steam owner must fail owner binding");
    Assert(!GameplaySettingsPolicy.TryParseEnvelope("1" + payload[1..], "1.3.2.0", ownerId, out _, out _),
        "unsupported policy schema must fail closed");
    Assert(!GameplaySettingsPolicy.TryParseEnvelope(
        string.Join("|", new[] { "2", "1.3.2.0", GameplaySettingsPolicy.CreateOwnerBinding(ownerId), nonce, "3", "1", "0", "1", "0" }),
        "1.3.2.0", ownerId, out _, out _),
        "unsupported stack multipliers must fail closed");
    Assert(!GameplaySettingsPolicy.TryParseEnvelope(
        string.Join("|", new[] { "2", "1.3.2.0", GameplaySettingsPolicy.CreateOwnerBinding(ownerId), nonce, "8", "yes", "0", "1", "0" }),
        "1.3.2.0", ownerId, out _, out _),
        "noncanonical setting values must fail closed");
    Assert(GameplaySettingsPolicy.CreateEnvelope("1.3.2.0", ownerId, "not-a-guid", settings) == null,
        "a missing session nonce must not produce a host policy");
    Assert(GameplaySettingsPolicy.BeginGuest(sessionGeneration, ownerId).SessionGeneration == sessionGeneration,
        "the native lobby-entry generation must isolate accepted state locally even though metadata is read from the active lobby service");
});

Run("Gameplay policy B. Guest waits safely, applies host values, and restores local values", () =>
{
    const ulong sessionGeneration = 9;
    const ulong nextSessionGeneration = 10;
    const ulong ownerId = 555;
    var local = new GameplaySettingsSnapshot(1, false, false, false, false);
    var host = new GameplaySettingsSnapshot(8, true, true, true, true);
    var nonce = Guid.NewGuid().ToString("N");
    var envelope = GameplaySettingsPolicy.CreateEnvelope("1.3.2.0", ownerId, nonce, host);
    var pending = GameplaySettingsPolicy.BeginGuest(sessionGeneration, ownerId);
    Assert(pending.Mode == GameplaySettingsMode.GuestPending && !pending.IsReady,
        "guest policy must remain not-ready while host metadata is missing");
    Assert(pending.EffectiveSettings == GameplaySettingsSnapshot.Vanilla,
        "the pending fallback must use Vanilla stack limits and production cheats off");
    var identityPending = GameplaySettingsPolicy.LobbyIdentityPending(sessionGeneration, 0);
    Assert(identityPending.Mode == GameplaySettingsMode.LobbyIdentityPending && !identityPending.IsReady
           && identityPending.EffectiveSettings == GameplaySettingsSnapshot.Vanilla,
        "missing Steam lobby identity must remain pending with the safe fallback instead of enabling local gameplay rules");
    var hostPending = GameplaySettingsPolicy.HostPending(sessionGeneration, ownerId);
    Assert(hostPending.Mode == GameplaySettingsMode.HostPending && !hostPending.IsReady
           && hostPending.EffectiveSettings == GameplaySettingsSnapshot.Vanilla,
        "the host must remain paused until its policy is owner-published and verified by exact readback");
    Assert(GameplaySettingsPolicy.TryAcceptGuestEnvelope(pending, envelope, "1.3.2.0", out var accepted),
        "a valid current-owner policy should activate for the guest");
    Assert(accepted.Mode == GameplaySettingsMode.GuestReady && accepted.IsReady && accepted.EffectiveSettings == host,
        "guest effective values must match the host's complete policy");
    Assert(GameplaySettingsPolicy.TryAcceptGuestEnvelope(accepted, envelope, "1.3.2.0", out var repeated)
           && repeated == accepted,
        "repeated reads of the unchanged lobby policy must be idempotent");

    var changedEnvelope = GameplaySettingsPolicy.CreateEnvelope(
        "1.3.2.0", ownerId, Guid.NewGuid().ToString("N"), new GameplaySettingsSnapshot(4, false, false, false, false));
    Assert(!GameplaySettingsPolicy.TryAcceptGuestEnvelope(accepted, changedEnvelope, "1.3.2.0", out _),
        "host settings must not hot-change after an active policy was accepted");

    var switchedLobby = GameplaySettingsPolicy.BeginGuest(nextSessionGeneration, ownerId + 1);
    Assert(!switchedLobby.IsReady && switchedLobby.EffectiveSettings == GameplaySettingsSnapshot.Vanilla,
        "reconnect or session switch must discard the old effective policy before accepting the new owner");
    var restored = GameplaySettingsPolicy.EndSession(local);
    Assert(restored.Mode == GameplaySettingsMode.Local && restored.IsReady && restored.EffectiveSettings == local,
        "disconnect must restore the guest's stored local values as effective values");
    Assert(local.StackSizeMultiplier == 1 && !local.DryingRacks && !local.MixingStations && !local.PlantYield && !local.PackagingStations,
        "applying the host snapshot must not mutate the separately held local preference snapshot");

    var hostState = GameplaySettingsPolicy.BeginHost(sessionGeneration, ownerId, host, envelope);
    Assert(hostState.Mode == GameplaySettingsMode.Host && hostState.EffectiveSettings == host,
        "the host must retain its frozen local snapshot for the lobby");
    Assert(GameplaySettingsPolicy.BeginHost(sessionGeneration, ownerId, new GameplaySettingsSnapshot(3, false, false, false, false), envelope).Mode
           == GameplaySettingsMode.Incompatible,
        "unsupported local stack values must never become a published host policy");
});

Run("Gameplay policy C. Runtime uses authenticated owner and gates unsafe BAIQ extras", () =>
{
    var manager = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "GameplaySettingsSyncManager.cs"));
    var policy = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "GameplaySettingsPolicy.cs"));
    var storageMenu = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "StorageMenuPatch.cs"));
    var managerUpdate = ExtractMethod(manager, "internal static void Update(bool force = false)", "internal static void ReapplyEffectivePolicy");
    Assert(managerUpdate.Contains("GetSessionConnectionIdentifier")
           && managerUpdate.Contains("SteamUser.GetSteamID")
           && managerUpdate.Contains("lobby.IsHost")
           && managerUpdate.Contains("InstanceFinder.IsServer")
           && managerUpdate.Contains("InstanceFinder.IsClient")
           && !managerUpdate.Contains("lobby.LobbyID"),
        "runtime must use the active game lobby service's authenticated owner ID and compare Schedule/FishNet roles without the stale LobbyID property");
    Assert(manager.Contains("lobbyService.SetLobbyData")
           && manager.Contains("lobbyService.GetLobbyData")
           && manager.Contains("BAIQ_GameplayPolicy_v2"),
        "the host publishes and reads back through the captured game service instance, which scopes metadata to the current Steam lobby");
    Assert(policy.Contains("CreateOwnerBinding") && policy.Contains("expectedModVersion")
           && policy.Contains("TryAcceptGuestEnvelope")
           && manager.Contains("host policy changed during this lobby"),
        "owner binding, version validation and immutable in-session updates must be enforced");
    Assert(!manager.Contains("[ServerRpc]", StringComparison.Ordinal)
           && !manager.Contains("[ObserversRpc]", StringComparison.Ordinal)
           && !manager.Contains("ChangeQuantity(", StringComparison.Ordinal)
           && !manager.Contains("SetQuantity(", StringComparison.Ordinal)
           && !manager.Contains("TryInsertItem", StringComparison.Ordinal),
        "the settings transport must not add inventory RPCs or mutate items/quantities");
    Assert(storageMenu.Contains("!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures")
           && storageMenu.Contains("return false;")
           && interactionSource.Contains("if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)")
           && hotkeySource.Contains("!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures"),
        "Backpack open, BAIQ quick-move augmentation, sorting and Take All must stay paused until policy readiness");
    var saveMethod = ExtractMethod(configurationSource, "public void Save()", "public void SetContextualPanelGeometry");
    Assert(!saveMethod.Contains("_stackSizeMultiplierEntry.Value = CheatSettings", StringComparison.Ordinal)
           && !saveMethod.Contains("_dryingRacksProductionEntry.Value = ProductionCheats", StringComparison.Ordinal),
        "saving effective session state must not overwrite local MelonPreferences entries");
    Assert(configurationSource.Contains("GameplaySettingsSyncManager.ReapplyEffectivePolicy(GetLocalGameplaySettings())")
           && configurationSource.Contains("Controlled by host")
           && configurationSource.Contains("saved local preference is unchanged"),
        "guest effective overrides and settings descriptions must preserve local preference values");
});

Run("Gameplay policy D. Effective settings feed stack, production and achievement consumers", () =>
{
    var cheat = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "CheatPatch.cs"));
    var production = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Cheats", "ProductionCheats.cs"));
    var drying = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "DryingRackProductionPatch.cs"));
    var mixing = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "MixingStationProductionPatch.cs"));
    var plants = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlantYieldProductionPatch.cs"));
    var packaging = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PackagingStationsProductionPatch.cs"));
    Assert(cheat.Contains("CheatSettings.StackSizeMultiplierValue")
           && cheat.Contains("CheatState.IsAnyCheatActive")
           && production.Contains("enabled && CheatSettings.StackSizeMultiplier != StackSizeMultiplierOption.Vanilla"),
        "stack capacity and achievement suppression must follow effective settings");
    Assert(drying.Contains("ProductionCheats.GetMultiplier")
           && mixing.Contains("ProductionCheats.GetMultiplier")
           && plants.Contains("ProductionCheats.GetMultiplier")
           && packaging.Contains("ProductionCheats.GetMultiplier"),
        "each enabled production path must read one effective multiplier, not reapply a synchronized multiplier");
});

Run("Gameplay policy E. Lobby identity recovers and publication gates both peers", () =>
{
    var manager = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "GameplaySettingsSyncManager.cs"));
    var lifecyclePatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "GameplaySettingsLobbyLifecyclePatch.cs"));
    var mod = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "BackpackMod.cs"));
    var legacySync = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Config", "ConfigSyncManager.cs"));
    var hostUpdate = ExtractMethod(manager, "private static void UpdateHostPolicy", "private static void UpdateGuestPolicy");
    var lobbyWrapperUnavailable = ExtractMethod(manager, "private static void HandleLobbyInstanceUnavailable()", "internal static void OnMainSceneTransition");
    var verifiedRecheck = hostUpdate.IndexOf("string.Equals(current, _frozenHostState.AcceptedEnvelope", StringComparison.Ordinal);
    var readyAfterFirstReadback = hostUpdate.IndexOf("SetState(_frozenHostState", StringComparison.Ordinal);
    var exactWriteReadback = hostUpdate.IndexOf("string.Equals(readback, _frozenHostState.AcceptedEnvelope", StringComparison.Ordinal);
    var readyAfterWriteReadback = hostUpdate.IndexOf("SetState(_frozenHostState", readyAfterFirstReadback + 1, StringComparison.Ordinal);
    Assert(manager.Contains("IdentityWarmupSeconds") && manager.Contains("RecoveryPollIntervalSeconds")
           && manager.Contains("HandleIdentityUnavailable(")
           && manager.Contains("ownerSourceDisagreement")
           && manager.Contains("Active lobby owner identity recovered"),
        "startup owner/service identity loss must report unavailable fields, retry, and recover without manual interaction");
    Assert(hostUpdate.IndexOf("SetState(GameplaySettingsPolicy.HostPending", StringComparison.Ordinal)
           < hostUpdate.IndexOf("lobbyService.SetLobbyData", StringComparison.Ordinal)
           && hostUpdate.Contains("if (_hostPolicyPublished)")
           && verifiedRecheck >= 0 && readyAfterFirstReadback > verifiedRecheck
           && exactWriteReadback >= 0 && readyAfterWriteReadback > exactWriteReadback,
        "the host must become ready only after active-service publication and exact policy readback");
    Assert(lifecyclePatch.Contains("typeof(SteamLobbyService)")
           && lifecyclePatch.Contains("\"Initialize\"")
           && lifecyclePatch.Contains("\"OnLobbyCreated\"")
           && lifecyclePatch.Contains("\"OnLobbyEntered\"")
           && lifecyclePatch.Contains("\"LeaveLobby\"")
           && lifecyclePatch.Contains("SteamLobbyService __instance")
           && manager.Contains("AdvanceSessionGeneration()"),
        "the genuine native SteamLobbyService instance must be captured at initialization/create/enter and invalidated at leave");
    Assert(manager.Contains("lobbyService.GetLobbyData(MetadataKey)")
           && manager.Contains("lobbyService.SetLobbyData(MetadataKey")
           && manager.Contains("GetSessionConnectionIdentifier")
           && manager.Contains("ownerSourceMatches")
           && manager.Contains("localSteamId == ownerId")
           && !manager.Contains("lobby.GetLobbyData"),
        "policy metadata must use the active public service API, cross-check the Lobby owner, and avoid a nonexistent Lobby.GetLobbyData API");
    Assert(mod.Contains("PhoneBackpackUI.Update(gameplayPolicyReady)")
           && mod.IndexOf("PhoneBackpackUI.Update(gameplayPolicyReady)", StringComparison.Ordinal)
              < mod.IndexOf("if (!gameplayPolicyReady)", StringComparison.Ordinal)
           && mod.Contains("GameplaySettingsSyncManager.OnMainSceneTransition(reason)"),
        "phone visibility must continue to update while gated, and Main scene transitions must re-verify instead of ending a live lobby");
    Assert(manager.Contains("if (lobby == null)")
           && manager.Contains("if (!lobby.IsInLobby)")
           && lobbyWrapperUnavailable.Contains("policy paused while membership is rechecked")
           && !lobbyWrapperUnavailable.Contains("EndSession("),
        "a temporarily missing Lobby wrapper must pause a tracked session, while only confirmed lobby exit may restore local settings");
    var identityLog = ExtractMethod(manager, "private static void LogIdentityUnavailable", "internal static void ReapplyEffectivePolicy");
    Assert(identityLog.Contains("membership=active")
           && identityLog.Contains("scheduleHost=")
           && identityLog.Contains("fishNetServer=")
           && identityLog.Contains("fishNetClient=")
           && identityLog.Contains("owner={(ownerId == 0 ? \"missing\" : \"available\")}")
           && !identityLog.Contains("SteamID", StringComparison.Ordinal),
        "transient identity diagnostics must distinguish lobby membership and host roles without logging raw Steam IDs");
    Assert(manager.Contains("if (identity == 0)")
           && manager.Contains("return \"missing\";")
           && manager.Contains("Guest policy metadata discovered")
           && manager.Contains("Guest policy metadata rejected by schema/version/owner/value checks"),
        "missing identities must not be fingerprinted as zero, while guest policy discovery and validation outcomes remain diagnosable");
    Assert(mod.Contains("PhoneBackpackUI.SetPolicyReadiness(false, reason)")
           && !mod.Contains("PhoneBackpackUI.ResetSession(\"host gameplay policy unavailable\")"),
        "a policy readiness drop must make the phone surface read-only without destroying its retryable presentation");
    Assert(legacySync.Contains("Attempting to write the supported legacy lobby settings")
           && legacySync.Contains("Verified the supported legacy lobby settings by exact metadata readback")
           && legacySync.Contains("var readback = lobbyService.GetLobbyData(Prefix)")
           && legacySync.Contains("var payload = lobbyService.GetLobbyData(Prefix)")
           && legacySync.Contains("slow lobby-scoped retries continue")
           && legacySync.Contains("if (lobby == null || lobbyService == null)")
           && legacySync.Contains("if (!lobbyService.IsInLobby)")
           && legacySync.Contains("if (!lobby.IsInLobby)")
           && !legacySync.Contains("Shared the supported legacy lobby settings"),
        "legacy metadata diagnostics must distinguish an attempted write from a verified write and retry transient wrapper loss");
});

Run("Phone policy A. Transient geometry retries and pending policy is read-only", () =>
{
    var phone = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "PhoneBackpackUI.cs"));
    var phonePatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PhonePatch.cs"));
    var playerBackpack = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "PlayerBackpack.cs"));
    var interaction = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "InventoryInteractionPatch.cs"));
    var playerPatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PlayerPatch.cs"));
    var phoneUpdate = ExtractMethod(phone, "public static void Update(bool gameplayPolicyReady)", "public static void Refresh()");
    var deferredRequest = ExtractMethod(phone, "internal static void RequestShowAfterLayout", "private static IEnumerator ShowAfterCanvasLayout");
    var deferredShow = ExtractMethod(phone, "private static IEnumerator ShowAfterCanvasLayout", "public static void Hide(");
    var phoneReadiness = ExtractMethod(phone, "internal static void SetPolicyReadiness", "public static void ResetSession");
    var geometryHelper = ExtractMethod(phone, "private static void HideForInvalidGeometry()", "private static bool AreProjectedBoundsUsable");
    Assert(phoneUpdate.Contains("ResolveAppState()") && phoneUpdate.Contains("SuspendForApp")
           && phoneUpdate.Contains("Time.unscaledTime >= _nextLayoutRetryAt")
           && phoneUpdate.Contains("SetPanelInteractionEnabled(gameplayPolicyReady)")
           && phoneUpdate.Contains("if (!gameplayPolicyReady && layoutWasValid)"),
        "phone lifecycle must retry transient layout, detect unsupported app switches, and synchronize interactivity with policy readiness");
    Assert(geometryHelper.Contains("_layoutValid = false")
           && geometryHelper.Contains("LayoutRetryIntervalSeconds")
           && geometryHelper.Contains("SuspendForApp"),
        "invalid geometry must clear the stale validity flag, release the phone overlay, and schedule a bounded retry");
    Assert(phone.Contains("geometryValid={geometryValid} layoutCacheValid={layoutCacheValid}")
           && phone.Contains("_panelRaycaster.enabled = enabled")
           && phone.Contains("_sortButton.interactable = enabled"),
        "diagnostics must separate current presentation from cached layout and the panel must be read-only while policy is pending");
    Assert(phonePatch.Contains("PhoneBackpackUI.RequestShowAfterLayout(__instance)")
           && deferredRequest.Contains("MelonCoroutines.Start")
           && deferredShow.Contains("WaitForEndOfFrame")
           && deferredShow.Contains("Canvas.ForceUpdateCanvases")
           && deferredShow.Contains("requestGeneration != _showRequestGeneration")
           && deferredShow.Contains("Show(phone)"),
        "first phone presentation must measure after a Unity canvas pass and cancel stale open requests, leaving bounded retries active if geometry is still transient");
    var hide = ExtractMethod(phone, "public static void Hide(", "public static void Update(bool gameplayPolicyReady)");
    var reset = ExtractMethod(phone, "public static void ResetSession(", "private static void EnsureCreated()");
    Assert(hide.Contains("_showRequestGeneration++") && reset.Contains("_showRequestGeneration++"),
        "closing the phone or changing save sessions must cancel any deferred first-open coroutine");
    var sortContents = ExtractMethod(playerBackpack, "public bool SortContents()", "public bool ConsolidateMagazines()");
    Assert(sortContents.Contains("!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures")
           && phone.Contains("_sortButton.interactable = enabled"),
        "phone Sort remains unavailable before policy readiness, including direct calls to the shared mutation method");
    Assert(phoneReadiness.Contains("SetPanelInteractionEnabled(false)")
           && !phoneReadiness.Contains("ResetSession("),
        "policy loss must disable phone interactions without discarding the presentation needed for layout recovery");
    Assert(interaction.Contains("PhoneQuickMoveActive") && phoneUpdate.Contains("if (!gameplayPolicyReady)"),
        "phone quick-move augmentation and pointer input must remain disabled until policy readiness");
    Assert(playerPatch.Contains("saveable?.SaveFolderName")
           && playerPatch.Contains("Path.Combine(parentFolderPath, folderName)")
           && playerPatch.Contains("BackpackSaveFile + \".json\""),
        "the persistence diagnostic must report the native player subfolder used by WriteSubfile without changing the actual save destination");
});

Run("Phone/Character A. Four-step gameplay-screen ownership transition", () =>
{
    var phonePatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PhonePatch.cs"));
    var characterPatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "CharacterInterfacePatch.cs"));
    var phoneUi = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "PhoneBackpackUI.cs"));
    var playerBackpack = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "PlayerBackpack.cs"));
    var closeHook = characterPatch.IndexOf("[HarmonyPatch(\"Close\")]", StringComparison.Ordinal);
    var closePostfix = characterPatch.IndexOf("[HarmonyPostfix]", closeHook, StringComparison.Ordinal);
    var closePrefix = characterPatch.IndexOf("[HarmonyPrefix]", closeHook, StringComparison.Ordinal);
    var characterClose = characterPatch[(closeHook >= 0 ? closeHook : 0)..];
    var closePostfixRelative = closePostfix - Math.Max(closeHook, 0);
    var characterState = ExtractMethod(phoneUi, "public static void SetCharacterSuppressed(bool suppressed)", "private static bool UpdateAppState");
    var phoneUpdate = ExtractMethod(phoneUi, "public static void Update(bool gameplayPolicyReady)", "public static void Refresh()");
    var resolveApp = ExtractMethod(phoneUi, "private static PhoneAppState ResolveAppState()", "private static T FindAppComponent");
    var screenChanged = ExtractMethod(phoneUi, "internal static void OnGameplayScreenChanged(GameplayMenu menu)", "public static void SetCharacterSuppressed(bool suppressed)");
    var phoneClose = ExtractMethod(phonePatch, "public static void SetIsOpenPostfix", "public static class GameplayMenuPatch");
    var hide = ExtractMethod(phoneUi, "public static void Hide(", "public static void Update(bool gameplayPolicyReady)");
    var lifecycleLog = ExtractMethod(phoneUi, "internal static void LogLifecycleState(string boundary)", "public static void SetCharacterSuppressed(bool suppressed)");
    var ensureCreated = ExtractMethod(phoneUi, "private static void EnsureCreated()", "private static void CreateControls");
    var backpackToggle = ExtractMethod(playerBackpack, "private void Update()", "public void SetBackpackEnabled(bool state)");

    Assert(closeHook >= 0 && closePrefix > closeHook && closePostfix > closePrefix
           && characterClose[closePostfixRelative..].Contains("PhoneBackpackUI.SetCharacterSuppressed(false)")
           && characterClose[closePostfixRelative..].Contains("ContextualBackpackPanel.HidePhone()"),
        "Character suppression must remain active until native CharacterInterface.Close has completed");
    Assert(characterClose.Contains("PhoneBackpackUI.LogLifecycleState(\"CharacterInterface.Close.prefix\")"),
        "the native Character close prefix must capture the pre-close presenter and UI owner state");
    Assert(characterState.Contains("if (_requested && _phone?.IsOpen == true)")
           && phoneUpdate.Contains("ResolveAppState()")
           && phoneUpdate.Contains("if (!_isOpen)")
           && phoneUpdate.Contains("Show(_phone, gameplayPolicyReady)"),
        "Phone → Character → Phone must resolve the restored native app and resume the existing panel");
    var gameplayOwner = resolveApp.IndexOf("if (gameplayMenu != null)", StringComparison.Ordinal);
    var characterScreenGate = resolveApp.IndexOf("selectedScreen, \"Character\"", StringComparison.Ordinal);
    var phoneScreenGate = resolveApp.IndexOf("selectedScreen, \"Phone\"", StringComparison.Ordinal);
    var characterFallback = resolveApp.IndexOf("characterInterface?.IsOpen == true || _characterSuppressed", StringComparison.Ordinal);
    var activeAppLookup = resolveApp.IndexOf("var activeApp = Phone.ActiveApp", StringComparison.Ordinal);
    Assert(gameplayOwner >= 0 && characterScreenGate > gameplayOwner && phoneScreenGate > characterScreenGate
           && characterFallback > phoneScreenGate && activeAppLookup > phoneScreenGate,
        "GameplayMenu selection must outrank stale CharacterInterface and suppression flags, with legacy fallback only when no gameplay owner exists");
    Assert(phonePatch.Contains("[HarmonyPatch(typeof(GameplayMenu))]")
           && phonePatch.Contains("GameplayMenu.SetScreen.prefix")
           && phonePatch.Contains("OnGameplayScreenChanged(__instance)"),
        "the four-step regression must trace the native gameplay-screen owner before and after SetScreen");
    Assert(screenChanged.Contains("selectedScreen, \"Character\"")
           && screenChanged.Contains("SuspendForApp(\"GameplayMenu.CurrentScreen=Character\")")
           && screenChanged.Contains("selectedScreen, \"Phone\"")
           && screenChanged.Contains("RequestShowAfterLayout(Phone.Instance)"),
        "Character ownership must force the existing panel hidden, and returning to Phone must re-request it even if Phone.IsOpen did not change");
    var reconcileSuppression = screenChanged.IndexOf("_characterSuppressed = false", StringComparison.Ordinal);
    var resolveReturnedPhone = screenChanged.IndexOf("var phoneState = ResolveAppState()", StringComparison.Ordinal);
    var requestReturnedPhone = screenChanged.IndexOf("RequestShowAfterLayout(Phone.Instance)", StringComparison.Ordinal);
    Assert(reconcileSuppression >= 0 && resolveReturnedPhone > reconcileSuppression && requestReturnedPhone > resolveReturnedPhone,
        "the reproduced mismatch CharacterInterface.IsOpen=false with characterSuppressed=true must clear suppression before Phone ownership is resolved and re-requested");
    Assert(phoneClose.Contains("PhoneBackpackUI.Hide(\"Phone.SetIsOpen(false)\")")
           && hide.Contains("if (!_isOpen)") && hide.Contains("_root.SetActive(false)")
           && phoneUpdate.Contains("if (!appState.Allowed)") && phoneUpdate.Contains("SuspendForApp"),
        "Character selection must hide the presenter, closing Phone must keep its root inactive, and return-to-Phone must use the normal reopen request");
    Assert(lifecycleLog.Contains("desiredVisible=") && lifecycleLog.Contains("rootActiveSelf=")
           && lifecycleLog.Contains("rootActiveInHierarchy=") && lifecycleLog.Contains("rootId=")
           && lifecycleLog.Contains("parentId=") && lifecycleLog.Contains("characterSuppressed="),
        "transition diagnostics must compare desired and actual visibility and capture the root's stable identity and parent");
    Assert(backpackToggle.Contains("Input.GetKeyDown(Configuration.Instance.ToggleKey)"),
        "the independent B-key Backpack toggle must remain wired outside Phone/Character presentation");
    Assert(phonePatch.Contains("PhoneBackpackUI.RequestShowAfterLayout(__instance)"),
        "closing and reopening Phone must continue through the normal deferred presentation lifecycle");
    Assert(ensureCreated.Contains("_root != null && _createdForItemUiManagerId == managerId"),
        "repeated Phone/Character transitions must reuse the existing Phone Backpack window for the current native UI manager");
});

Run("Phone/Character B. Character contextual Backpack retains native quick-move ownership", () =>
{
    var interaction = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "InventoryInteractionPatch.cs"));
    var contextual = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "ContextualBackpackPanel.cs"));
    var phoneUi = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "UI", "PhoneBackpackUI.cs"));
    var phonePatch = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "Patches", "PhonePatch.cs"));
    var mod = File.ReadAllText(Path.Combine(repository, "src", "InventoryExpanded", "BackpackMod.cs"));
    var firstEnable = ExtractMethod(interaction,
        "public static void EnableQuickMove(ItemUIManager __instance, SlotList secondarySlots)",
        "[HarmonyPatch(\"EnableQuickMove\", [typeof(SlotList), typeof(SlotList)])]");
    var secondEnable = ExtractMethod(interaction,
        "public static void EnableQuickMove(ItemUIManager __instance, SlotList primarySlots, SlotList secondarySlots)",
        "[HarmonyPatch(\"DisableQuickMove\")]");
    var relinquish = ExtractMethod(interaction, "private static void RelinquishPhoneQuickMoveOwnership", "[HarmonyPatch(\"GetQuickMoveSlots\"");
    var endPhoneQuickMove = ExtractMethod(interaction, "internal static void EndPhoneQuickMove", "private static void RelinquishPhoneQuickMoveOwnership");
    var characterContext = ExtractMethod(contextual, "internal static bool IsActiveCharacterContext", "public static bool Show(object context");
    var contextualUpdate = ExtractMethod(contextual, "public static void Update()", "public static void UpdateNativeOverlayLayers");
    var contextualHide = ExtractMethod(contextual, "public static void Hide(object context)", "public static void ResetSession");
    var updateLoop = ExtractMethod(mod, "public override void OnUpdate()", "public override void OnLateUpdate()");
    var setPhoneAccessor = ExtractMethod(phoneUi, "private static void SetPhoneContextualAccessor", "private static void SuspendForApp");
    var suspend = ExtractMethod(phoneUi, "private static void SuspendForApp", "private static bool CanOpen");
    var nativeDisableStart = interaction.IndexOf("public static void DisableQuickMove(ItemUIManager __instance)", StringComparison.Ordinal);
    var nativeDisableEnd = interaction.IndexOf("public static void DisableQuickMoveComplete", nativeDisableStart, StringComparison.Ordinal);
    var nativeDisable = interaction[nativeDisableStart..nativeDisableEnd];

    Assert(firstEnable.Contains("RelinquishPhoneQuickMoveOwnership(__instance)")
           && secondEnable.Contains("RelinquishPhoneQuickMoveOwnership(__instance)")
           && firstEnable.IndexOf("RelinquishPhoneQuickMoveOwnership", StringComparison.Ordinal)
              < firstEnable.IndexOf("OpenContext", StringComparison.Ordinal)
           && secondEnable.IndexOf("RelinquishPhoneQuickMoveOwnership", StringComparison.Ordinal)
              < secondEnable.IndexOf("OpenContext", StringComparison.Ordinal),
        "native Character quick-move activation must take ownership before opening its contextual Backpack in either EnableQuickMove overload");
    Assert(relinquish.Contains("_phoneQuickMoveActive = false")
           && relinquish.Contains("_phoneQuickMoveManagerId = 0")
           && relinquish.Contains("enabled state retained")
           && !relinquish.Contains("manager?.DisableQuickMove"),
        "Phone suspension must relinquish its quick-move bookkeeping without disabling the native Character context that replaced it");
    Assert(interaction.Contains("_phoneQuickMoveManagerId = manager.GetInstanceID()")
           && relinquish.Contains("manager.GetInstanceID() != _phoneQuickMoveManagerId")
           && endPhoneQuickMove.Contains("manager?.DisableQuickMove()"),
        "ownership handoff must match the active ItemUIManager while ordinary Phone close still disables Phone-owned quick move");
    Assert(characterContext.Contains("_isOpen")
           && characterContext.Contains("ContextMode.StorageOrMachine")
           && characterContext.Contains("_activeInteraction.ContextId != GetObjectId(itemUiManager)")
           && characterContext.Contains("gameplayMenu?.IsOpen == true")
           && characterContext.Contains("gameplayMenu.CurrentScreen.ToString(), \"Character\""),
        "the shared Backpack accessor must transfer only to an open Character contextual panel owned by the same native UI manager");
    Assert(setPhoneAccessor.Contains("ContextualBackpackPanel.IsActiveCharacterContext(_registeredItemUiManager)")
           && setPhoneAccessor.Contains("_phoneAccessorActive = false")
           && suspend.Contains("SetPhoneContextualAccessor(false)"),
        "Phone presenter suspension and close must preserve the Character panel's accessor lease while Character owns the selected gameplay screen");
    Assert(contextualUpdate.Contains("!IsQuickMoveActive(itemUiManager)")
           && contextualUpdate.Contains("ContextualBackpackPanel.Update: ItemUIManager.QuickMoveEnabled=false")
           && nativeDisable.Contains("_phoneQuickMoveTransition")
           && nativeDisable.IndexOf("_phoneQuickMoveTransition", StringComparison.Ordinal)
              < nativeDisable.IndexOf("ContextualBackpackPanel.Hide(__instance)", StringComparison.Ordinal)
           && contextualHide.Contains("ItemUIManager.DisableQuickMove contextId=")
           && phonePatch.Contains("OnGameplayScreenChanged(__instance)")
           && updateLoop.IndexOf("PhoneBackpackUI.Update(gameplayPolicyReady)", StringComparison.Ordinal)
              < updateLoop.IndexOf("ContextualBackpackPanel.Update()", StringComparison.Ordinal),
        "after Phone teardown suppresses its DisableQuickMove hide hook, ContextualBackpackPanel.Update was the next-frame close caller; genuine native DisableQuickMove and GameplayMenu transitions must remain observable");
    Assert(contextual.Contains("_root != null && _rect != null && _slotContainer != null && _createdForItemUiManagerId == itemUiManagerId")
           && relinquish.Contains("UserSlotLockOverlay.RefreshAll()")
           && !relinquish.Contains("SetStoredInstance")
           && !relinquish.Contains("SetItemSlotQuantity"),
        "repeated Character close/reopen and Phone→Character→Phone→Character transitions must reuse one contextual root without changing inventory state");
    Assert(contextual.Contains("[ContextualLifecycle] opened mode={mode}")
           && contextual.Contains("[ContextualLifecycle] closing reason={reason}")
           && contextual.Contains("rootId=")
           && contextual.Contains("parentId="),
        "lifecycle diagnostics must identify the actual contextual close reason, owner, panel, and parent for manual confirmation");
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

    var currentPathStart = playerManagerSource.IndexOf("#if IL2CPP && SCHEDULE_I_047", StringComparison.Ordinal);
    var currentPathEnd = playerManagerSource.IndexOf("#else\n    [HarmonyPatch(nameof(PlayerManager.TryGetPlayerData))]", currentPathStart, StringComparison.Ordinal);
    var currentPath = currentPathStart >= 0 && currentPathEnd > currentPathStart
        ? playerManagerSource[currentPathStart..currentPathEnd]
        : string.Empty;
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

Console.WriteLine($"Post-release bugfix tests passed: {passed}/{passed} scenarios.");
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
        passed++;
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
