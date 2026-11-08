using HarmonyLib;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.Vehicles;
#elif MONO
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI.Shop;
using ScheduleOne.Vehicles;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(ShopInterface))]
public static class ShopInterfacePatch
{
    [HarmonyPatch("HandoverItems")]
    [HarmonyPrefix]
    public static void HandoverItemsPrefix(ShopInterface __instance)
    {
        ShopFulfillmentTrace.Begin(__instance);
        LogShopState("HandoverItems begin", __instance);
    }

    [HarmonyPatch("HandoverItems")]
    [HarmonyPostfix]
    public static void HandoverItemsPostfix(bool __result)
    {
        Logger.Diagnostic($"[ShopOverflow] HandoverItems end: result={__result}.");
        ShopFulfillmentTrace.End();
    }

    [HarmonyPatch("HandoverItems")]
    [HarmonyFinalizer]
    public static Exception HandoverItemsFinalizer(Exception __exception)
    {
        ShopFulfillmentTrace.End();
        return __exception;
    }

    [HarmonyPatch("GetAvailableSlots")]
    [HarmonyPrefix]
    public static void GetAvailableSlotsPrefix(ShopInterface __instance)
    {
        if (ShopFulfillmentTrace.IsActive)
            LogShopState("GetAvailableSlots begin", __instance);
    }

    [HarmonyPatch("GetAvailableSlots")]
    [HarmonyPostfix]
#if IL2CPP
    public static void GetAvailableSlots(ShopInterface __instance, ref Il2CppSystem.Collections.Generic.List<ItemSlot> __result)
#elif MONO
    public static void GetAvailableSlots(ShopInterface __instance, ref List<ItemSlot> __result)
#endif
    {
        var nativeSlotCount = __result?.Count ?? 0;

        // GetAvailableSlots is also used by background shop/delivery systems. Only
        // alter the destination order for an actively open, player-facing shop;
        // scheduled deliveries must retain their native payload destinations.
        if (__instance == null || !__instance.IsOpen)
            return;

        if (PlayerBackpack.Instance == null || !PlayerBackpack.Instance.IsUnlocked)
        {
            if (ShopFulfillmentTrace.IsActive)
                Logger.Diagnostic($"[ShopOverflow] GetAvailableSlots end: nativeSlots={nativeSlotCount}, backpackUnlocked=false, appended=0, returned={nativeSlotCount}.");
            return;
        }

        var reordered = CreateSlotList();
        var loadingBayVehicle = __instance.GetLoadingBayVehicle();
        var loadVehicle = loadingBayVehicle != null && __instance.Cart.LoadVehicleToggle.isOn;

        if (loadVehicle)
            AddPlayerInventorySlots(reordered);

        AddNativeSlotsBeforeFallback(reordered, __result, loadingBayVehicle, __instance);

        var backpackSlots = PlayerBackpack.Instance.ItemSlots;
        var appendedSlots = AddUniqueSlots(reordered, backpackSlots);

        AddNativeFallbackSlots(reordered, __result, loadingBayVehicle, __instance);

        __result = reordered;

        if (ShopFulfillmentTrace.IsActive)
        {
            Logger.Diagnostic(
                $"[ShopOverflow] GetAvailableSlots end: nativeSlots={nativeSlotCount}, " +
                $"loadVehicle={loadVehicle}, backpackSlots={backpackSlots.Count}, appended={appendedSlots}, " +
                $"returned={__result.Count}, route={DescribeSlotSet(__result, null)}, " +
                $"backpackState={DescribeBackpackState()}.");
        }
    }

    private static void AddNativeSlotsBeforeFallback(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> destination,
        Il2CppSystem.Collections.Generic.List<ItemSlot> nativeSlots,
#elif MONO
        List<ItemSlot> destination,
        List<ItemSlot> nativeSlots,
#endif
        LandVehicle loadingBayVehicle,
        ShopInterface shop)
    {
        if (nativeSlots == null)
            return;

        for (var i = 0; i < nativeSlots.Count; i++)
        {
            var slot = GetSlot(nativeSlots, i);
            if (slot == null || IsNativeFallbackSlot(slot, loadingBayVehicle, shop))
                continue;

            AddUniqueSlot(destination, slot);
        }
    }

    private static void AddNativeFallbackSlots(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> destination,
        Il2CppSystem.Collections.Generic.List<ItemSlot> nativeSlots,
#elif MONO
        List<ItemSlot> destination,
        List<ItemSlot> nativeSlots,
#endif
        LandVehicle loadingBayVehicle,
        ShopInterface shop)
    {
        if (nativeSlots == null)
            return;

        for (var i = 0; i < nativeSlots.Count; i++)
        {
            var slot = GetSlot(nativeSlots, i);
            if (slot == null || !IsNativeFallbackSlot(slot, loadingBayVehicle, shop))
                continue;

            AddUniqueSlot(destination, slot);
        }
    }

    private static void AddPlayerInventorySlots(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> destination
#elif MONO
        List<ItemSlot> destination
#endif
    )
    {
        var playerSlots = PlayerInventory.Instance?.hotbarSlots;
        if (playerSlots == null)
            return;

        for (var i = 0; i < playerSlots.Count; i++)
        {
#if IL2CPP
            var slot = playerSlots[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var slot = playerSlots[i];
#endif
            AddUniqueSlot(destination, slot);
        }
    }

    [HarmonyPatch("PlaceItemInDeliveryBay")]
    [HarmonyPrefix]
    public static void PlaceItemInDeliveryBayPrefix(ItemInstance __0)
    {
        if (!TraceActive)
            return;

        Logger.Diagnostic(
            $"[ShopOverflow] PlaceItemInDeliveryBay: item={DescribeItem(__0)}, " +
            $"backpackCapacity={GetBackpackCapacity(__0)}, backpackState={DescribeBackpackState()}.");
    }

    private static void LogShopState(string stage, ShopInterface shop)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        try
        {
            var cart = shop?.Cart;
            var cartItems = cart?.GetItemSum() ?? -1;
            var slotRequirement = cart?.GetTotalSlotRequirement() ?? -1;
            var loadVehicle = cart?.LoadVehicleToggle != null && cart.LoadVehicleToggle.isOn;
            Logger.Diagnostic(
                $"[ShopOverflow] {stage}: cartItems={cartItems}, slotRequirement={slotRequirement}, " +
                $"loadVehicle={loadVehicle}, backpackState={DescribeBackpackState()}.");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ShopOverflow] {stage} diagnostics failed safely: {exception.Message}");
        }
    }

    private static int GetBackpackCapacity(ItemInstance item)
    {
        if (item == null || PlayerBackpack.Instance == null || !PlayerBackpack.Instance.IsUnlocked)
            return 0;

        var capacity = 0;
        var slots = PlayerBackpack.Instance.GetBackpackSlots();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = GetSlot(slots, i);
            if (slot == null || slot.IsAddLocked)
                continue;

            try
            {
                capacity += Math.Max(0, slot.GetCapacityForItem(item, true));
            }
            catch (Exception exception)
            {
                Logger.Warning($"[ShopOverflow] Capacity probe skipped slot {i}: {exception.Message}");
            }
        }

        return capacity;
    }

    private static string DescribeBackpackState()
    {
        if (PlayerBackpack.Instance == null)
            return "instance=none";

        if (!PlayerBackpack.Instance.IsUnlocked)
            return $"unlocked=false slots={PlayerBackpack.Instance.BackpackSlotCount}";

        var empty = 0;
        var occupied = 0;
        var slots = PlayerBackpack.Instance.GetBackpackSlots();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = GetSlot(slots, i);
            if (slot?.ItemInstance == null)
                empty++;
            else
                occupied++;
        }

        return $"unlocked=true slots={slots.Count} empty={empty} occupied={occupied}";
    }

    private static string DescribeItem(ItemInstance item)
    {
        if (item == null)
            return "none";

        return $"{item.ID ?? "<null-id>"} qty={item.Quantity} limit={item.StackLimit}";
    }

    internal static string DescribeShopItem(ItemInstance item)
    {
        return DescribeItem(item);
    }

    internal static string DescribeStorage(StorageEntity storage)
    {
        if (storage == null)
            return "none";

        var name = storage.StorageEntityName ?? storage.name ?? "<unnamed>";
        var slotCount = storage.ItemSlots?.Count ?? -1;
        var tags = new List<string>();

        if (PlayerBackpack.Instance?.Storage != null && StoragesMatch(storage, PlayerBackpack.Instance.Storage))
            tags.Add("Backpack");

        var vehicle = ShopFulfillmentTrace.LoadingBayVehicle;
        if (vehicle?.Storage != null && StoragesMatch(storage, vehicle.Storage))
            tags.Add("LoadingBayVehicle");

        var shop = ShopFulfillmentTrace.Shop;
        var bays = shop?.DeliveryBays;
        if (bays != null)
        {
            for (var i = 0; i < bays.Count; i++)
            {
                var bay = GetStorage(bays, i);
                if (bay != null && StoragesMatch(storage, bay))
                {
                    tags.Add($"DeliveryBay[{i}]");
                    break;
                }
            }
        }

        var tagText = tags.Count == 0 ? "unclassified" : string.Join(",", tags);
        return $"{name} slots={slotCount} tags={tagText}";
    }

    internal static string DescribeSlotSet(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> slots,
#elif MONO
        List<ItemSlot> slots,
#endif
        ItemInstance item)
    {
        if (slots == null)
            return "slots=none";

        var backpack = 0;
        var vehicle = 0;
        var deliveryBay = 0;
        var totalCapacity = 0;
        var validCapacity = 0;

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = GetSlot(slots, i);
            if (slot == null)
                continue;

            if (PlayerBackpack.Instance?.OwnsSlot(slot) == true)
                backpack++;
            if (ShopFulfillmentTrace.LoadingBayVehicle?.Storage != null && StorageOwnsSlot(ShopFulfillmentTrace.LoadingBayVehicle.Storage, slot))
                vehicle++;
            if (IsDeliveryBaySlot(slot, ShopFulfillmentTrace.Shop))
                deliveryBay++;

            if (item == null)
                continue;

            try
            {
                var capacity = Math.Max(0, slot.GetCapacityForItem(item, true));
                totalCapacity += capacity;
                if (!slot.IsAddLocked)
                    validCapacity += capacity;
            }
            catch
            {
                // This is diagnostic only; keep routing untouched if a native probe rejects.
            }
        }

        return $"slots={slots.Count} backpack={backpack} vehicle={vehicle} deliveryBay={deliveryBay} capacity={totalCapacity} unlockedCapacity={validCapacity}";
    }

    internal static bool TraceActive => ShopFulfillmentTrace.IsActive;

#if IL2CPP
    private static Il2CppSystem.Collections.Generic.List<ItemSlot> CreateSlotList()
    {
        return new Il2CppSystem.Collections.Generic.List<ItemSlot>();
    }

    private static ItemSlot GetSlot(Il2CppSystem.Collections.Generic.List<ItemSlot> slots, int index)
    {
        return slots[new Index(index)]?.TryCast<ItemSlot>();
    }

    private static StorageEntity GetStorage(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<StorageEntity> storages, int index)
    {
        return storages[index]?.TryCast<StorageEntity>();
    }
#elif MONO
    private static List<ItemSlot> CreateSlotList()
    {
        return new List<ItemSlot>();
    }

    private static ItemSlot GetSlot(List<ItemSlot> slots, int index)
    {
        return slots[index];
    }

    private static StorageEntity GetStorage(StorageEntity[] storages, int index)
    {
        return storages[index];
    }
#endif

    private static int AddUniqueSlots(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> destination,
        Il2CppSystem.Collections.Generic.List<ItemSlot> source
#elif MONO
        List<ItemSlot> destination,
        List<ItemSlot> source
#endif
    )
    {
        if (source == null)
            return 0;

        var added = 0;
        for (var i = 0; i < source.Count; i++)
        {
            if (AddUniqueSlot(destination, GetSlot(source, i)))
                added++;
        }

        return added;
    }

    private static bool AddUniqueSlot(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> destination,
#elif MONO
        List<ItemSlot> destination,
#endif
        ItemSlot slot)
    {
        if (destination == null || slot == null)
            return false;

        for (var i = 0; i < destination.Count; i++)
        {
            if (PlayerBackpack.SlotsMatch(GetSlot(destination, i), slot))
                return false;
        }

        destination.Add(slot);
        return true;
    }

    private static bool IsNativeFallbackSlot(ItemSlot slot, LandVehicle loadingBayVehicle, ShopInterface shop)
    {
        return StorageOwnsSlot(loadingBayVehicle?.Storage, slot) || IsDeliveryBaySlot(slot, shop);
    }

    private static bool IsDeliveryBaySlot(ItemSlot slot, ShopInterface shop)
    {
        var bays = shop?.DeliveryBays;
        if (bays == null)
            return false;

        for (var i = 0; i < bays.Count; i++)
        {
            var bay = GetStorage(bays, i);
            if (StorageOwnsSlot(bay, slot))
                return true;
        }

        return false;
    }

    private static bool StorageOwnsSlot(StorageEntity storage, ItemSlot slot)
    {
        if (storage?.ItemSlots == null || slot == null)
            return false;

        for (var i = 0; i < storage.ItemSlots.Count; i++)
        {
            if (PlayerBackpack.SlotsMatch(GetSlot(storage.ItemSlots, i), slot))
                return true;
        }

        return false;
    }

    private static bool StoragesMatch(StorageEntity left, StorageEntity right)
    {
        if (left == null || right == null)
            return false;

#if IL2CPP
        return left.Pointer != IntPtr.Zero && left.Pointer == right.Pointer;
#elif MONO
        return ReferenceEquals(left, right);
#endif
    }
}

internal static class ShopFulfillmentTrace
{
    public static bool IsActive { get; private set; }
    public static ShopInterface Shop { get; private set; }
    public static LandVehicle LoadingBayVehicle { get; private set; }
    public static bool LoadVehicle { get; private set; }

    public static void Begin(ShopInterface shop)
    {
        End();
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        Shop = shop;
        LoadVehicle = shop?.Cart?.LoadVehicleToggle != null && shop.Cart.LoadVehicleToggle.isOn;
        LoadingBayVehicle = null;
        try
        {
            LoadingBayVehicle = shop?.GetLoadingBayVehicle();
        }
        catch
        {
            LoadingBayVehicle = null;
        }

        IsActive = true;
        Logger.Diagnostic(
            $"[ShopOverflow] Trace begin: loadVehicle={LoadVehicle}, " +
            $"loadingBayVehicle={(LoadingBayVehicle == null ? "none" : LoadingBayVehicle.VehicleName ?? LoadingBayVehicle.name ?? "<unnamed>")}, " +
            $"vehicleStorage={ShopInterfacePatch.DescribeStorage(LoadingBayVehicle?.Storage)}.");
    }

    public static void End()
    {
        if (IsActive)
            Logger.Diagnostic("[ShopOverflow] Trace end.");

        IsActive = false;
        Shop = null;
        LoadingBayVehicle = null;
        LoadVehicle = false;
    }
}

[HarmonyPatch(typeof(ItemSlot))]
public static class ShopItemSlotTracePatch
{
    [HarmonyPatch("TryInsertItemIntoSet")]
    [HarmonyPrefix]
#if IL2CPP
    public static void TryInsertItemIntoSetPrefix(Il2CppSystem.Collections.Generic.List<ItemSlot> __0, ItemInstance __1)
#elif MONO
    public static void TryInsertItemIntoSetPrefix(List<ItemSlot> __0, ItemInstance __1)
#endif
    {
        if (!ShopInterfacePatch.TraceActive)
            return;

        Logger.Diagnostic(
            $"[ShopOverflow] TryInsertItemIntoSet begin: item={ShopInterfacePatch.DescribeShopItem(__1)}, " +
            $"{ShopInterfacePatch.DescribeSlotSet(__0, __1)}.");
    }

    [HarmonyPatch("TryInsertItemIntoSet")]
    [HarmonyPostfix]
    public static void TryInsertItemIntoSetPostfix(bool __result)
    {
        if (!ShopInterfacePatch.TraceActive)
            return;

        Logger.Diagnostic($"[ShopOverflow] TryInsertItemIntoSet end: result={__result}.");
    }
}
