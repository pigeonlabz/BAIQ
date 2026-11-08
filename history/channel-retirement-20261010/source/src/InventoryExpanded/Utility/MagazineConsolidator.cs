#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
#elif MONO
using ScheduleOne;
using ScheduleOne.Equipping;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
#endif

namespace Backpack;

public static class MagazineConsolidator
{
    private sealed class MagazineEntry
    {
        public ItemSlot Slot { get; init; }
        public IntegerItemInstance Item { get; init; }
        public string ItemId { get; init; }
        public int Capacity { get; init; }
        public int OriginalValue { get; init; }
    }

    private sealed class MagazineDefinitionState
    {
        public int Capacity { get; set; }
        public bool Ambiguous { get; set; }
    }

    private static Dictionary<string, MagazineDefinitionState> _magazineDefinitions;

    public static void ResetSession()
    {
        _magazineDefinitions = null;
    }

    public static bool ConsolidateBackpack(PlayerBackpack backpack)
    {
        if (backpack == null || backpack.BackpackSlotCount < 2)
            return false;

        var slots = new List<ItemSlot>(backpack.BackpackSlotCount);
        for (var i = 0; i < backpack.BackpackSlotCount; i++)
            slots.Add(backpack.GetBackpackSlot(i));

        return ConsolidateSlots("Backpack", slots);
    }

    public static bool ConsolidatePlayerInventory()
    {
        var inventory = PlayerInventory.Instance;
        var hotbarSlots = inventory?.hotbarSlots;
        if (hotbarSlots == null || hotbarSlots.Count < 2)
            return false;

        var slots = new List<ItemSlot>(hotbarSlots.Count);
        for (var i = 0; i < hotbarSlots.Count; i++)
        {
#if IL2CPP
            slots.Add(hotbarSlots[new Index(i)]?.TryCast<ItemSlot>());
#elif MONO
            slots.Add(hotbarSlots[i]);
#endif
        }

        return ConsolidateSlots("Player inventory", slots);
    }

    public static bool ConsolidateSlots(string label, IReadOnlyList<ItemSlot> slots)
    {
        if (slots == null || slots.Count < 2)
            return false;

        EnsureMagazineDefinitions();
        if (_magazineDefinitions == null || _magazineDefinitions.Count == 0)
        {
            Logger.Warning("Magazine consolidation skipped; no ranged-weapon magazine definitions were available.");
            return false;
        }

        var entries = new List<MagazineEntry>();
        for (var i = 0; i < slots.Count; i++)
        {
            if (TryCreateEntry(slots[i], out var entry))
                entries.Add(entry);
        }

        if (entries.Count < 2)
            return false;

        var changed = false;
        foreach (var group in entries.GroupBy(entry => $"{entry.ItemId}\u001F{entry.Capacity}"))
        {
            var magazines = group.ToList();
            if (magazines.Count < 2)
                continue;

            var totalAmmo = magazines.Sum(entry => entry.OriginalValue);
            if (totalAmmo < 0 || totalAmmo > magazines.Count * magazines[0].Capacity)
            {
                Logger.Warning($"Magazine consolidation skipped {magazines[0].ItemId}; aggregate ammo was outside safe bounds.");
                continue;
            }

            var remaining = totalAmmo;
            var desiredValues = new int[magazines.Count];
            for (var i = 0; i < magazines.Count; i++)
            {
                desiredValues[i] = Math.Min(magazines[i].Capacity, remaining);
                remaining -= desiredValues[i];
            }

            if (remaining != 0 || desiredValues.Sum() != totalAmmo)
            {
                Logger.Warning($"Magazine consolidation plan failed for {magazines[0].ItemId}; no magazine state was changed.");
                continue;
            }

            for (var i = 0; i < magazines.Count; i++)
            {
                if (magazines[i].OriginalValue == desiredValues[i])
                    continue;

                magazines[i].Item.SetValue(desiredValues[i]);
                magazines[i].Slot.ReplicateStoredInstance();
                changed = true;
            }
        }

        if (changed)
            Logger.Diagnostic($"Consolidated compatible loaded magazines in {label}.");

        return changed;
    }

    private static bool TryCreateEntry(ItemSlot slot, out MagazineEntry entry)
    {
        entry = null;
        if (slot == null || slot.IsAddLocked || slot.IsRemovalLocked)
            return false;

        var item = AsInteger(slot.ItemInstance);
        if (item == null || item.Quantity != 1 || item.StackLimit != 1)
            return false;

        var definition = item.Definition;
        var itemId = definition?.ID;
        if (string.IsNullOrWhiteSpace(itemId) || !TryGetMagazineCapacity(itemId, out var capacity))
            return false;

        if (item.Value < 0 || item.Value > capacity)
        {
            Logger.Warning($"Magazine consolidation skipped {itemId}; loaded ammo value {item.Value} exceeds capacity {capacity}.");
            return false;
        }

        entry = new MagazineEntry
        {
            Slot = slot,
            Item = item,
            ItemId = itemId,
            Capacity = capacity,
            OriginalValue = item.Value
        };
        return true;
    }

    private static bool TryGetMagazineCapacity(string itemId, out int capacity)
    {
        capacity = 0;
        if (string.IsNullOrWhiteSpace(itemId) || _magazineDefinitions == null)
            return false;

        if (!_magazineDefinitions.TryGetValue(itemId, out var state) || state.Ambiguous || state.Capacity <= 0)
            return false;

        capacity = state.Capacity;
        return true;
    }

    private static void EnsureMagazineDefinitions()
    {
        if (_magazineDefinitions != null)
            return;

        _magazineDefinitions = new Dictionary<string, MagazineDefinitionState>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var registry = Registry.Instance;
            if (registry == null)
                return;

            var definitions = registry.GetAllItems();
            for (var i = 0; i < definitions.Count; i++)
            {
#if IL2CPP
                var definition = definitions[new Index(i)]?.TryCast<ItemDefinition>();
#elif MONO
                var definition = definitions[i];
#endif
                var rangedWeapon = AsRangedWeapon(definition?.Equippable);
                var magazine = rangedWeapon?.Magazine;
                if (magazine == null || rangedWeapon.MagazineSize <= 0 || string.IsNullOrWhiteSpace(magazine.ID))
                    continue;

                AddMagazineDefinition(magazine.ID, rangedWeapon.MagazineSize);
            }
        }
        catch (Exception exception)
        {
            _magazineDefinitions.Clear();
            Logger.Warning($"Magazine definition discovery failed safely: {exception.Message}");
        }
    }

    private static void AddMagazineDefinition(string itemId, int capacity)
    {
        if (!_magazineDefinitions.TryGetValue(itemId, out var state))
        {
            _magazineDefinitions[itemId] = new MagazineDefinitionState
            {
                Capacity = capacity
            };
            return;
        }

        if (state.Capacity != capacity)
            state.Ambiguous = true;
    }

    private static IntegerItemInstance AsInteger(ItemInstance item)
    {
        if (item == null)
            return null;

#if IL2CPP
        try
        {
            return item.TryCast<IntegerItemInstance>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return item as IntegerItemInstance;
#endif
    }

    private static Equippable_RangedWeapon AsRangedWeapon(Equippable equippable)
    {
        if (equippable == null)
            return null;

#if IL2CPP
        try
        {
            return equippable.TryCast<Equippable_RangedWeapon>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return equippable as Equippable_RangedWeapon;
#endif
    }
}
