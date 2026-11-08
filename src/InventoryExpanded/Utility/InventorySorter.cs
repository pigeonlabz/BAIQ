#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#elif MONO
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Backpack;

public static class InventorySorter
{
    private sealed class PlannedItem
    {
        public ItemInstance Item { get; init; }
        public int SourceIndex { get; init; }
    }

    private sealed class PreservedSlotSnapshot
    {
        public ItemSlot Slot { get; init; }
        public ItemInstance Item { get; init; }
        public int Quantity { get; init; }
        public bool HasCashBalance { get; init; }
        public float CashBalance { get; init; }
    }

    public static bool SortBackpack(PlayerBackpack backpack)
    {
        if (backpack == null || backpack.BackpackSlotCount < 2)
            return false;

        var slots = new List<ItemSlot>(backpack.BackpackSlotCount);
        for (var i = 0; i < backpack.BackpackSlotCount; i++)
            slots.Add(backpack.GetBackpackSlot(i));

        return SortAuthoritativeSlots("Backpack", slots);
    }

    public static bool SortPlayerInventory(PlayerInventory inventory)
    {
        var playerSlots = inventory?.hotbarSlots;
        if (playerSlots == null || playerSlots.Count < 2)
            return false;

        var authoritativeSlots = new List<ItemSlot>(playerSlots.Count);
        for (var i = 0; i < playerSlots.Count; i++)
        {
#if IL2CPP
            authoritativeSlots.Add(playerSlots[new Index(i)]?.TryCast<ItemSlot>());
#elif MONO
            authoritativeSlots.Add(playerSlots[i]);
#endif
        }

        return SortAuthoritativeSlots("Player inventory", authoritativeSlots);
    }

    public static bool SortStorage(SlotList slots, IItemSlotOwner owner = null)
    {
        if (slots == null || slots.Count < 2)
            return false;

        var authoritativeSlots = new List<ItemSlot>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
            authoritativeSlots.Add(GetSlot(slots, i));

        return SortAuthoritativeSlots("Storage", authoritativeSlots, owner);
    }

    private static bool SortAuthoritativeSlots(
        string label,
        IReadOnlyList<ItemSlot> slots,
        IItemSlotOwner owner = null)
    {
        var sortableSlots = new List<ItemSlot>(slots.Count);
        var plannedItems = new List<PlannedItem>();
        var preservedSnapshots = new List<PreservedSlotSnapshot>();

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var isUserLocked = owner != null
                ? UserSlotLockRegistry.IsUserLocked(owner, slot)
                : UserSlotLockRegistry.IsUserLocked(slot);
            var cashInstance = AsCash(slot?.ItemInstance);
            var isCashProtected = AsCashSlot(slot) != null || cashInstance != null;
            if (isUserLocked || isCashProtected)
            {
                preservedSnapshots.Add(new PreservedSlotSnapshot
                {
                    Slot = slot,
                    Item = slot?.ItemInstance,
                    Quantity = slot?.Quantity ?? 0,
                    HasCashBalance = cashInstance != null,
                    CashBalance = cashInstance?.Balance ?? 0f
                });
                continue;
            }

            if (!CanReorder(slot))
                continue;

            sortableSlots.Add(slot);
            if (slot.ItemInstance == null)
                continue;

            var copy = slot.ItemInstance.GetCopy(slot.Quantity);
            if (copy == null)
            {
                Logger.Error($"Could not copy {slot.ItemInstance.ID} while planning inventory sort; no slots were changed.");
                return false;
            }

            plannedItems.Add(new PlannedItem
            {
                Item = copy,
                SourceIndex = i
            });
        }

        if (sortableSlots.Count < 2 || plannedItems.Count == 0)
            return false;

        if (!NormalizeOversizedStacks(label, plannedItems))
            return false;

        plannedItems.Sort(ComparePlannedItems);
        ConsolidateCompatibleStacks(plannedItems);

        var desiredItems = plannedItems.Select(item => item.Item).ToList();
        var sourceTotals = DiagnosticTrace.GetTotals(sortableSlots);
        var desiredTotals = DiagnosticTrace.GetTotals(desiredItems);
        if (!DiagnosticTrace.TotalsMatch(sourceTotals, desiredTotals))
        {
            Logger.Error($"Sort safety check failed for {label} before writing; no slots were changed.");
            return false;
        }

        if (plannedItems.Count > sortableSlots.Count)
        {
            Logger.Error("Inventory sort produced more stacks than the authoritative inventory can hold; no slots were changed.");
            return false;
        }

        for (var i = 0; i < sortableSlots.Count; i++)
        {
            var slot = sortableSlots[i];
            if (i < plannedItems.Count)
            {
                slot.SetStoredItem(plannedItems[i].Item, true);
            }
            else if (slot.ItemInstance != null)
            {
                slot.ClearStoredInstance(true);
            }
        }

        var postWriteTotals = DiagnosticTrace.GetTotals(sortableSlots);
        if (!DiagnosticTrace.TotalsMatch(sourceTotals, postWriteTotals))
            Logger.Error($"Sort safety check failed for {label} after writing; item totals changed unexpectedly.");

        if (!PreservedSlotsMatch(preservedSnapshots))
            Logger.Error($"Sort safety check failed for {label}; a protected slot changed unexpectedly.");

        return true;
    }

    private static bool NormalizeOversizedStacks(string label, List<PlannedItem> items)
    {
        var normalized = new List<PlannedItem>();
        var changed = false;

        foreach (var planned in items)
        {
            if (!StackCapacityPolicy.IsOversized(planned.Item, out var configuredLimit))
            {
                normalized.Add(planned);
                continue;
            }

            changed = true;
            var remaining = planned.Item.Quantity;
            while (remaining > 0)
            {
                var chunkQuantity = Math.Min(configuredLimit, remaining);
                var chunk = planned.Item.GetCopy(chunkQuantity);
                if (chunk == null || chunk.Quantity != chunkQuantity)
                {
                    Logger.Error(
                        $"Could not create an exact {chunkQuantity}-item stack while sorting {label}; " +
                        "normalization copy; no slots were changed.");
                    return false;
                }

                normalized.Add(new PlannedItem
                {
                    Item = chunk,
                    SourceIndex = planned.SourceIndex
                });
                remaining -= chunkQuantity;
            }
        }

        if (!changed)
            return true;

        items.Clear();
        items.AddRange(normalized);
        return true;
    }

    private static int ComparePlannedItems(PlannedItem left, PlannedItem right)
    {
        return InventorySortOrder.Compare(
            GetSortGroup(left.Item),
            left.Item.ID,
            left.Item.Name,
            left.SourceIndex,
            GetSortGroup(right.Item),
            right.Item.ID,
            right.Item.Name,
            right.SourceIndex);
    }

    private static InventorySortGroup GetSortGroup(ItemInstance item)
    {
#if IL2CPP
        var product = item?.TryCast<ProductItemInstance>();
#elif MONO
        var product = item as ProductItemInstance;
#endif
        if (product == null)
            return InventorySortGroup.Other;

        return product.AppliedPackaging != null
            ? InventorySortGroup.PackagedProduct
            : InventorySortGroup.UnpackagedProduct;
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

    private static void ConsolidateCompatibleStacks(List<PlannedItem> items)
    {
        for (var sourceIndex = 0; sourceIndex < items.Count; sourceIndex++)
        {
            var source = items[sourceIndex].Item;
            for (var targetIndex = 0; targetIndex < sourceIndex && source.Quantity > 0; targetIndex++)
            {
                var target = items[targetIndex].Item;
                var available = target.StackLimit - target.Quantity;
                if (available <= 0 || !target.CanStackWith(source, false))
                    continue;

                var amount = Math.Min(available, source.Quantity);
                target.SetQuantity(target.Quantity + amount);
                source.SetQuantity(source.Quantity - amount);
            }
        }

        items.RemoveAll(item => item.Item.Quantity <= 0);
    }

    private static bool CanReorder(ItemSlot slot)
    {
        if (slot == null || slot.IsLocked || slot.IsRemovalLocked || slot.IsAddLocked)
            return false;

        if (slot.HardFilters != null && slot.HardFilters.Count > 0)
            return false;

        return slot.PlayerFilter == null || slot.PlayerFilter.IsDefault();
    }

    private static bool PreservedSlotsMatch(List<PreservedSlotSnapshot> snapshots)
    {
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (snapshot.Slot == null || snapshot.Slot.Quantity != snapshot.Quantity ||
                !ItemsMatch(snapshot.Slot.ItemInstance, snapshot.Item))
                return false;

            if (snapshot.HasCashBalance)
            {
                var cashInstance = AsCash(snapshot.Slot.ItemInstance);
                if (cashInstance == null || cashInstance.Balance != snapshot.CashBalance)
                    return false;
            }
        }

        return true;
    }

    private static bool ItemsMatch(ItemInstance left, ItemInstance right)
    {
        if (left == null || right == null)
            return left == null && right == null;

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
