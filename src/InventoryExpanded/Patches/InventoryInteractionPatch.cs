using Backpack.UI;
using Backpack.Config;
using HarmonyLib;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#elif MONO
using ScheduleOne.Economy;
using ScheduleOne.Employees;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.ObjectScripts;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(ItemUIManager))]
public static class InventoryInteractionPatch
{
    private static SlotList _activeCashCompatibleExternalSlots;
    private static StorageEntity _activeCashCompatibleExternalStorage;
    private static bool _phoneQuickMoveTransition;
    private static bool _phoneQuickMoveActive;
    private static int _phoneQuickMoveManagerId;
    private static ItemSlot _priorityClickSource;
    private static ItemSlot _priorityRoutingSource;
    private static ItemInstance _priorityRoutingItem;
    private static SlotList _priorityPreferredDestinations;
    private static SlotList _priorityFallbackDestinations;
    private static bool _checkingPreferredCapacity;
    private static bool _priorityDeferralLogged;

    internal static bool PhoneQuickMoveActive => _phoneQuickMoveActive;

    [HarmonyPatch("SlotClicked", [typeof(ItemSlotUI)])]
    [HarmonyPrefix]
    public static void SlotClickedPrefix(ItemUIManager __instance, ItemSlotUI __0)
    {
        OvercapTransferContext.End();
        ClearQuickMovePriorityContext();
        _priorityClickSource = __0?.assignedSlot;
    }

    [HarmonyPatch("SlotClicked", [typeof(ItemSlotUI)])]
    [HarmonyPostfix]
    public static void SlotClickedPostfix(ItemUIManager __instance, ItemSlotUI __0)
    {
        OvercapTransferContext.End();
        ClearQuickMovePriorityContext();
    }

    [HarmonyPatch("SlotClicked", [typeof(ItemSlotUI)])]
    [HarmonyFinalizer]
    public static Exception SlotClickedFinalizer(Exception __exception)
    {
        OvercapTransferContext.End();
        ClearQuickMovePriorityContext();
        return __exception;
    }

    [HarmonyPatch("EndDrag")]
    [HarmonyPrefix]
    public static void EndDragPrefix(ItemUIManager __instance)
    {
        OvercapTransferContext.End();
        TryBeginPartialStackDrag(__instance);
    }

    [HarmonyPatch("EndDrag")]
    [HarmonyPostfix]
    public static void EndDragPostfix(ItemUIManager __instance)
    {
        OvercapTransferContext.End();
    }

    [HarmonyPatch("EndDrag")]
    [HarmonyFinalizer]
    public static Exception EndDragFinalizer(Exception __exception)
    {
        OvercapTransferContext.End();
        return __exception;
    }

    [HarmonyPatch("LateUpdate")]
    [HarmonyPostfix]
    public static void LateUpdate(ItemUIManager __instance)
    {
        ContextualBackpackPanel.UpdateNativeOverlayLayers(__instance);
        PhoneBackpackUI.UpdateNativeOverlayLayers(__instance);
    }

    [HarmonyPatch("EnableQuickMove", [typeof(SlotList)])]
    [HarmonyPostfix]
    public static void EnableQuickMove(ItemUIManager __instance, SlotList secondarySlots)
    {
        if (_phoneQuickMoveTransition || !GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
            return;

        RelinquishPhoneQuickMoveOwnership(__instance);
        CaptureCashCompatibleExternalEndpoint(secondarySlots);
        OpenContext(__instance, secondarySlots);
        UserSlotLockOverlay.RefreshAll();
    }

    [HarmonyPatch("EnableQuickMove", [typeof(SlotList), typeof(SlotList)])]
    [HarmonyPostfix]
    public static void EnableQuickMove(ItemUIManager __instance, SlotList primarySlots, SlotList secondarySlots)
    {
        if (_phoneQuickMoveTransition || !GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
            return;

        RelinquishPhoneQuickMoveOwnership(__instance);
        var externalSlots = SelectExternalSlots(primarySlots, secondarySlots);
        CaptureCashCompatibleExternalEndpoint(externalSlots);
        OpenContext(__instance, externalSlots);
        UserSlotLockOverlay.RefreshAll();
    }

    [HarmonyPatch("DisableQuickMove")]
    [HarmonyPrefix]
    public static void DisableQuickMove(ItemUIManager __instance)
    {
        _activeCashCompatibleExternalSlots = null;
        _activeCashCompatibleExternalStorage = null;
        if (_phoneQuickMoveTransition)
            return;

        ContextualBackpackPanel.Hide(__instance);
    }

    [HarmonyPatch("DisableQuickMove")]
    [HarmonyPostfix]
    public static void DisableQuickMoveComplete(ItemUIManager __instance)
    {
        UserSlotLockOverlay.RefreshAll();
    }

    internal static void BeginPhoneQuickMove(ItemUIManager manager)
    {
        if (_phoneQuickMoveActive || !GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures
            || manager == null || PlayerBackpack.Instance == null)
            return;

        var inventorySlots = PlayerInventory.Instance?.GetAllInventorySlots();
        var backpackSlots = PlayerBackpack.Instance.GetBackpackSlots();
        if (inventorySlots == null || inventorySlots.Count == 0 || backpackSlots == null || backpackSlots.Count == 0)
        {
            Logger.Diagnostic("[PhoneBackpack] Quick move unavailable: Inventory or Backpack slots are not ready.");
            return;
        }

        try
        {
            _phoneQuickMoveTransition = true;
            manager.EnableQuickMove(inventorySlots, backpackSlots);
            _phoneQuickMoveActive = true;
            _phoneQuickMoveManagerId = manager.GetInstanceID();
            UserSlotLockOverlay.RefreshAll();
            Logger.Diagnostic(
                $"[PhoneBackpack] Native quick move enabled inventory={inventorySlots.Count} backpack={backpackSlots.Count}.");
        }
        catch (Exception exception)
        {
            _phoneQuickMoveActive = false;
            _phoneQuickMoveManagerId = 0;
            Logger.Warning($"[PhoneBackpack] Native quick move setup skipped safely: {exception.Message}");
        }
        finally
        {
            _phoneQuickMoveTransition = false;
        }
    }

    internal static void EndPhoneQuickMove(ItemUIManager manager)
    {
        if (!_phoneQuickMoveActive)
            return;

        try
        {
            _phoneQuickMoveTransition = true;
            manager?.DisableQuickMove();
        }
        finally
        {
            _phoneQuickMoveActive = false;
            _phoneQuickMoveManagerId = 0;
            _phoneQuickMoveTransition = false;
            _activeCashCompatibleExternalSlots = null;
            _activeCashCompatibleExternalStorage = null;
            UserSlotLockOverlay.RefreshAll();
        }
    }

    private static void RelinquishPhoneQuickMoveOwnership(ItemUIManager manager)
    {
        if (!_phoneQuickMoveActive || manager == null || _phoneQuickMoveManagerId == 0
            || manager.GetInstanceID() != _phoneQuickMoveManagerId)
            return;

        // Native gameplay UI has just enabled its own quick-move endpoint on the
        // shared ItemUIManager. Keep that state alive and let the Phone presenter
        // stop claiming ownership; disabling it during Phone suspension would
        // invalidate the newly opened Character contextual panel.
        _phoneQuickMoveActive = false;
        _phoneQuickMoveManagerId = 0;
        _activeCashCompatibleExternalSlots = null;
        _activeCashCompatibleExternalStorage = null;
        Logger.Diagnostic(
            $"[PhoneBackpack] Native quick move ownership transferred to gameplay context on ItemUIManager#{manager.GetInstanceID()}; enabled state retained.");
        UserSlotLockOverlay.RefreshAll();
    }

    [HarmonyPatch("GetQuickMoveSlots", [typeof(ItemSlot)])]
    [HarmonyPostfix]
    public static void GetQuickMoveSlots(ItemSlot __0, ref SlotList __result)
    {
        if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
            return;

        OvercapTransferContext.Begin(__0);

        var resolvedContextualDestination = ContextualBackpackPanel.TryResolveQuickMoveDestination(__0, __result, out var destinationSlots);
        if (resolvedContextualDestination)
            __result = destinationSlots;

        __result = FilterValidQuickMoveDestinations(__0, __result);

        if (resolvedContextualDestination
            && PlayerBackpack.SlotsMatch(__0, _priorityClickSource)
            && ContextualBackpackPanel.IsExternalQuickMoveSource(__0))
            CaptureQuickMovePriorityDestinations(__0, __result);
    }

    private static void CaptureQuickMovePriorityDestinations(ItemSlot sourceSlot, SlotList destinations)
    {
        ClearCapturedPriorityDestinations();
        if (sourceSlot?.ItemInstance == null || AsCash(sourceSlot.ItemInstance) != null || destinations == null)
            return;

        var backpackFirst = Configuration.Instance.ShiftClickDestinationPriority != ShiftClickDestinationPriorityOption.InventoryFirst;
        var preferred = new SlotList();
        var fallback = new SlotList();
        var backpack = PlayerBackpack.Instance;
        if (backpack == null)
            return;

        try
        {
            for (var i = 0; i < destinations.Count; i++)
            {
                var candidate = GetSlot(destinations, i);
                if (candidate == null)
                    continue;

                var isBackpackDestination = backpack.OwnsSlot(candidate);
                if (QuickMoveDestinationPriorityPolicy.IsPreferredDestination(isBackpackDestination, backpackFirst))
                    preferred.Add(candidate);
                else
                    fallback.Add(candidate);
            }
        }
        catch (Exception exception)
        {
            ClearCapturedPriorityDestinations();
            Logger.Diagnostic($"[QuickMovePriority] destination tier capture skipped safely: {exception.GetType().Name}.");
            return;
        }

        _priorityRoutingSource = sourceSlot;
        _priorityRoutingItem = sourceSlot.ItemInstance;
        _priorityPreferredDestinations = preferred;
        _priorityFallbackDestinations = fallback;
        Logger.Diagnostic(
            $"[QuickMovePriority] source=external priority={(backpackFirst ? "BackpackFirst" : "InventoryFirst")} " +
            $"preferred={preferred.Count} fallback={fallback.Count}; fallback capacity is deferred while preferred capacity remains.");
    }

    private static int GetPreferredRemainingCapacity(ItemSlot sourceSlot, ItemInstance item)
    {
        if (_priorityPreferredDestinations == null || sourceSlot == null || item == null)
            return 0;

        var maximumCapacity = 0;

        for (var i = 0; i < _priorityPreferredDestinations.Count; i++)
        {
            var preferred = GetSlot(_priorityPreferredDestinations, i);
            try
            {
                if (preferred == null
                    || PlayerBackpack.SlotsMatch(preferred, sourceSlot)
                    || IsNativeTransferDestinationBlocked(preferred))
                    continue;

                maximumCapacity = Math.Max(maximumCapacity, Math.Max(0, preferred.GetCapacityForItem(item, true)));
            }
            catch (Exception exception)
            {
                Logger.Diagnostic($"[QuickMovePriority] preferred capacity check skipped safely: {exception.GetType().Name}.");
            }
        }

        return maximumCapacity;
    }

    internal static bool ShouldDeferFallbackCapacity(ItemSlot destination, ItemInstance item)
    {
        if (_checkingPreferredCapacity)
            return false;

        var enteredCapacityCheck = false;
        try
        {
            if (_priorityRoutingSource == null
                || _priorityRoutingItem == null
                || item == null
                || !SameItemInstance(_priorityRoutingItem, item)
                || !ContainsSlot(_priorityFallbackDestinations, destination))
                return false;

            _checkingPreferredCapacity = true;
            enteredCapacityCheck = true;
            var preferredCapacity = GetPreferredRemainingCapacity(_priorityRoutingSource, item);
            var deferFallback = QuickMoveDestinationPriorityPolicy.ShouldDeferFallback(preferredCapacity);
            if (deferFallback && !_priorityDeferralLogged)
            {
                _priorityDeferralLogged = true;
                Logger.Diagnostic($"[QuickMovePriority] deferred a fallback destination while preferred capacity={preferredCapacity} remained.");
            }

            return deferFallback;
        }
        catch (Exception exception)
        {
            Logger.Diagnostic($"[QuickMovePriority] fallback capacity check skipped safely: {exception.GetType().Name}.");
            return false;
        }
        finally
        {
            if (enteredCapacityCheck)
                _checkingPreferredCapacity = false;
        }
    }

    private static void ClearQuickMovePriorityContext()
    {
        _priorityClickSource = null;
        ClearCapturedPriorityDestinations();
    }

    private static void ClearCapturedPriorityDestinations()
    {
        _priorityRoutingSource = null;
        _priorityRoutingItem = null;
        _priorityPreferredDestinations = null;
        _priorityFallbackDestinations = null;
        _checkingPreferredCapacity = false;
        _priorityDeferralLogged = false;
    }

    internal static SlotList FilterValidQuickMoveDestinations(ItemSlot sourceSlot, SlotList candidates)
    {
        var valid = new SlotList();
        var item = sourceSlot?.ItemInstance;
        if (item == null || candidates == null)
            return valid;

        if (AsCash(item) != null)
        {
            var sourceIsActiveExternal = ContainsSlot(_activeCashCompatibleExternalSlots, sourceSlot);
            if (_activeCashCompatibleExternalSlots != null && !sourceIsActiveExternal)
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = GetSlot(candidates, i);
                if (!IsActiveCashStorageSlot(candidate)
                        || !IsValidCashDestination(sourceSlot, candidate, item, true))
                        continue;

                    valid.Add(candidate);
                }

                Logger.Diagnostic(
                    $"[CashRoute] Quick move to active cash-compatible storage: candidates={candidates.Count}, accepted={valid.Count}.");
                return valid;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = GetSlot(candidates, i);
                if (AsCashSlot(candidate) == null || !IsValidCashDestination(sourceSlot, candidate, item, false))
                    continue;

                valid.Add(candidate);
                break;
            }

            Logger.Diagnostic(
                $"[CashRoute] Dedicated cash-slot route: candidates={candidates.Count}, accepted={valid.Count}, " +
                $"activeExternal={(_activeCashCompatibleExternalSlots != null)}, sourceExternal={sourceIsActiveExternal}.");
            return valid;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = GetSlot(candidates, i);
            if (IsNativeTransferDestinationBlocked(candidate)
                || PlayerBackpack.SlotsMatch(candidate, sourceSlot))
                continue;

            try
            {
                if (candidate.GetCapacityForItem(item, true) > 0)
                    valid.Add(candidate);
            }
            catch (Exception exception)
            {
                Logger.Warning($"Skipping quick-move destination that could not be validated: {exception.Message}");
            }
        }

        return valid;
    }

    internal static bool IsNativeTransferDestinationBlocked(ItemSlot candidate)
    {
        // Match Schedule I's native quick-move eligibility checks. IsLocked is
        // the active native ItemSlotLock (including employee reservations); it
        // is separate from BAIQ's persisted user-configurable slot locks.
        return candidate == null || candidate.IsLocked || candidate.IsAddLocked || candidate.IsRemovalLocked;
    }

    private static void TryBeginPartialStackDrag(ItemUIManager manager)
    {
        try
        {
            if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
                return;

            var source = manager?.draggedSlot?.assignedSlot;
            var destination = manager?.HoveredSlot?.assignedSlot;
            if (source == null || destination?.ItemInstance == null || PlayerBackpack.SlotsMatch(source, destination))
                return;

            OvercapTransferContext.Begin(source);
        }
        catch
        {
            OvercapTransferContext.End();
        }
    }

    internal static bool IsValidCashDestination(
        ItemSlot sourceSlot,
        ItemSlot candidate,
        ItemInstance cashItem,
        bool allowOrdinaryCashSlot)
    {
        var dedicatedCashSlot = AsCashSlot(candidate);
        if (IsNativeTransferDestinationBlocked(candidate)
            || (dedicatedCashSlot == null && !allowOrdinaryCashSlot)
            || PlayerBackpack.SlotsMatch(candidate, sourceSlot))
            return false;

        try
        {
            if (!candidate.CanSlotAcceptCash() || UserSlotLockRegistry.IsUserLocked(candidate))
                return false;
            if (!candidate.DoesItemMatchHardFilters(cashItem) || !candidate.DoesItemMatchPlayerFilters(cashItem))
                return false;

            var existingCash = AsCash(candidate.ItemInstance);
            if (candidate.ItemInstance != null && existingCash == null)
                return false;

            var balanceLimit = dedicatedCashSlot != null
                ? CashInstance.MAX_BALANCE
                : CashSlot.MAX_CASH_PER_SLOT;
            var sourceCash = AsCash(cashItem);
            return sourceCash != null && CashRoutingPolicy.GetTransferAmount(
                sourceCash.Balance,
                existingCash?.Balance ?? 0f,
                balanceLimit,
                destinationAllowed: true) > 0f;
        }
        catch (Exception exception)
        {
            Logger.Warning($"Skipping cash destination that could not be validated: {exception.Message}");
            return false;
        }
    }

    private static CashInstance AsCash(ItemInstance item)
    {
        if (item == null)
            return null;

#if IL2CPP
        try
        {
            return item.TryCast<CashInstance>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return item as CashInstance;
#endif
    }

    private static CashSlot AsCashSlot(ItemSlot slot)
    {
        if (slot == null)
            return null;

#if IL2CPP
        try
        {
            return slot.TryCast<CashSlot>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return slot as CashSlot;
#endif
    }

    private static void OpenContext(ItemUIManager manager, SlotList externalSlots)
    {
        if (externalSlots == null || externalSlots.Count == 0)
            return;

        if (!ContextualBackpackPanel.Show(manager, externalSlots))
            return;

        RegisterBackpackAsPlayerSource(manager);
    }

    private static void CaptureCashCompatibleExternalEndpoint(SlotList externalSlots)
    {
        _activeCashCompatibleExternalSlots = null;
        _activeCashCompatibleExternalStorage = null;
        var storage = GetExternalStorage(externalSlots);
        if (storage == null)
            return;

        var kind = ClassifyCashStorage(storage);
        var compatible = CashRoutingPolicy.AllowsExternalStorage(kind);
        Logger.Diagnostic(() =>
            $"[CashRoute] External storage='{storage.StorageEntityName}', type={storage.GetType().Name}, " +
            $"kind={kind}, slots={externalSlots?.Count ?? 0}, cashCompatible={compatible}.");
        if (!compatible)
            return;

        _activeCashCompatibleExternalSlots = externalSlots;
        _activeCashCompatibleExternalStorage = storage;
    }

    private static StorageEntity GetExternalStorage(SlotList externalSlots)
    {
        if (externalSlots == null)
            return null;

        for (var i = 0; i < externalSlots.Count; i++)
        {
            var owner = GetSlot(externalSlots, i)?.SlotOwner;
#if IL2CPP
            var storage = owner?.TryCast<StorageEntity>();
#elif MONO
            var storage = owner as StorageEntity;
#endif
            if (storage != null)
                return storage;
        }

        return null;
    }

    private static bool IsActiveCashStorageSlot(ItemSlot candidate)
    {
        if (candidate == null || !ContainsSlot(_activeCashCompatibleExternalSlots, candidate)
            || _activeCashCompatibleExternalStorage == null)
            return false;

#if IL2CPP
        var owner = candidate.SlotOwner?.TryCast<StorageEntity>();
#elif MONO
        var owner = candidate.SlotOwner as StorageEntity;
#endif
        return SameObject(owner, _activeCashCompatibleExternalStorage);
    }

    private static CashStorageKind ClassifyCashStorage(StorageEntity storage)
    {
        if (storage == null)
            return CashStorageKind.Unsupported;

        try
        {
            var employeeHome = storage.GetComponentInParent<EmployeeHome>();
            if (employeeHome != null && SameObject(employeeHome.Storage, storage))
                return CashStorageKind.EmployeeLocker;

            var deadDrop = storage.GetComponentInParent<DeadDrop>();
            if (deadDrop != null && SameObject(deadDrop.Storage, storage))
                return CashStorageKind.DeadDrop;

            var supplierStash = storage.GetComponentInParent<SupplierStash>();
            if (supplierStash != null && SameObject(supplierStash.Storage, storage))
                return CashStorageKind.SupplierStash;

            var placeable = storage.GetComponentInParent<PlaceableStorageEntity>();
            if (placeable != null && SameObject(placeable.StorageEntity, storage))
            {
                // BedItem inherits PlaceableStorageEntity but its storage has a special
                // gameplay role. Only the exact native general-container type is allowed.
                return placeable.GetType() == typeof(PlaceableStorageEntity)
                    ? CashStorageKind.GeneralPlaceableStorage
                    : CashStorageKind.SpecialPlaceableStorage;
            }

            return IsRegisteredDeadDropStorage(storage)
                ? CashStorageKind.DeadDrop
                : CashStorageKind.Unsupported;
        }
        catch (Exception exception)
        {
            Logger.Warning($"Cash-compatible external storage could not be identified: {exception.Message}");
            return CashStorageKind.Unsupported;
        }
    }

    private static bool IsRegisteredDeadDropStorage(StorageEntity storage)
    {
        var deadDrops = DeadDrop.DeadDrops;
        if (deadDrops == null)
            return false;

        for (var i = 0; i < deadDrops.Count; i++)
        {
#if IL2CPP
            var deadDrop = deadDrops[new Index(i)]?.TryCast<DeadDrop>();
#elif MONO
            var deadDrop = deadDrops[i];
#endif
            if (deadDrop != null && SameObject(deadDrop.Storage, storage))
                return true;
        }

        return false;
    }
    private static bool SameObject(UnityEngine.Object left, UnityEngine.Object right)
    {
        return left != null && right != null && left.GetInstanceID() == right.GetInstanceID();
    }

    private static void RegisterBackpackAsPlayerSource(ItemUIManager manager)
    {
        if (PlayerBackpack.Instance == null)
            return;

        var combinedPlayerSources = new SlotList();
        AddUniqueSlots(combinedPlayerSources, manager.PrimarySlots);
        AddUniqueSlots(combinedPlayerSources, PlayerBackpack.Instance.GetBackpackSlots());
        manager.PrimarySlots = combinedPlayerSources;
    }

    private static SlotList SelectExternalSlots(SlotList primarySlots, SlotList secondarySlots)
    {
        var primaryIsPlayer = IsPlayerInventory(primarySlots);
        var secondaryIsPlayer = IsPlayerInventory(secondarySlots);
        if (primaryIsPlayer && !secondaryIsPlayer)
            return secondarySlots;
        if (secondaryIsPlayer && !primaryIsPlayer)
            return primarySlots;
        if (primarySlots == null || primarySlots.Count == 0)
            return secondarySlots;
        if (secondarySlots == null || secondarySlots.Count == 0)
            return primarySlots;

        var combined = new SlotList();
        AddUniqueSlots(combined, primarySlots);
        AddUniqueSlots(combined, secondarySlots);
        return combined;
    }

    private static bool IsPlayerInventory(SlotList slots)
    {
        var playerInventory = PlayerInventory.Instance;
        var playerSlots = playerInventory?.hotbarSlots;
        if (slots == null || playerInventory == null || playerSlots == null || slots.Count == 0)
            return false;

        for (var i = 0; i < slots.Count; i++)
        {
            var candidate = GetSlot(slots, i);
            if (PlayerBackpack.SlotsMatch(candidate, playerInventory.cashSlot))
                return true;

            for (var j = 0; j < playerSlots.Count; j++)
            {
#if IL2CPP
                var playerSlot = playerSlots[new Index(j)]?.TryCast<ItemSlot>();
#elif MONO
                var playerSlot = playerSlots[j];
#endif
                if (PlayerBackpack.SlotsMatch(candidate, playerSlot))
                    return true;
            }
        }

        return false;
    }

    private static void AddUniqueSlots(SlotList destination, SlotList source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var candidate = GetSlot(source, i);
            var exists = false;
            for (var j = 0; j < destination.Count; j++)
            {
                if (!PlayerBackpack.SlotsMatch(candidate, GetSlot(destination, j)))
                    continue;

                exists = true;
                break;
            }

            if (!exists && candidate != null)
                destination.Add(candidate);
        }
    }

    private static bool ContainsSlot(SlotList slots, ItemSlot candidate)
    {
        if (slots == null || candidate == null)
            return false;

        for (var i = 0; i < slots.Count; i++)
        {
            if (PlayerBackpack.SlotsMatch(GetSlot(slots, i), candidate))
                return true;
        }

        return false;
    }

    private static bool SameItemInstance(ItemInstance left, ItemInstance right)
    {
        if (left == null || right == null)
            return false;

#if IL2CPP
        return left.Pointer == right.Pointer;
#elif MONO
        return ReferenceEquals(left, right);
#endif
    }

    private static ItemSlot GetSlot(SlotList slots, int index)
    {
#if IL2CPP
        return slots[new Index(index)]?.TryCast<ItemSlot>();
#elif MONO
        return slots[index];
#endif
    }

}

[HarmonyPatch(typeof(ItemSlot))]
public static class QuickMoveDestinationPriorityPatch
{
    // Native SlotClicked scans all compatible partial stacks before it tries
    // empty slots. Defer only the lower-priority tier during that native path;
    // insertion, source updates and network replication remain native-owned.
    [HarmonyPatch("GetCapacityForItem", [typeof(ItemInstance), typeof(bool)])]
    [HarmonyPostfix]
    public static void GetCapacityForItemPostfix(ItemSlot __instance, ItemInstance __0, ref int __result)
    {
        if (InventoryInteractionPatch.ShouldDeferFallbackCapacity(__instance, __0))
            __result = 0;
    }
}

[HarmonyPatch(typeof(ItemSlotUI))]
public static class UserSlotLockUiPatch
{
    [HarmonyPatch("AssignSlot", [typeof(ItemSlot)])]
    [HarmonyPostfix]
    public static void AssignSlot(ItemSlotUI __instance)
    {
        UserSlotLockOverlay.RefreshSafely(__instance);
    }

    [HarmonyPatch("ClearSlot")]
    [HarmonyPostfix]
    public static void ClearSlot(ItemSlotUI __instance)
    {
        UserSlotLockOverlay.RefreshSafely(__instance);
    }

    [HarmonyPatch("UpdateUI")]
    [HarmonyPostfix]
    public static void UpdateUI(ItemSlotUI __instance)
    {
        UserSlotLockOverlay.RefreshSafely(__instance);
    }

    [HarmonyPatch("OnDestroy")]
    [HarmonyPrefix]
    public static void OnDestroy(ItemSlotUI __instance)
    {
        UserSlotLockOverlay.Forget(__instance);
    }
}
