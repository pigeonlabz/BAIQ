using HarmonyLib;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
#elif MONO
using ScheduleOne.Equipping;
using ScheduleOne.ItemFramework;
using ScheduleOne.Storage;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(Equippable_RangedWeapon))]
public static class ReloadPatch
{
    [HarmonyPatch("GetMagazine")]
    [HarmonyPostfix]
    public static void GetMagazine(Equippable_RangedWeapon __instance, ref bool __result, ref StorableItemInstance __0)
    {
        if (__result || __instance == null || PlayerBackpack.Instance == null || !PlayerBackpack.Instance.IsUnlocked)
            return;

        try
        {
            if (!TryGetBackpackMagazine(__instance, out var magazine))
                return;

            __0 = magazine;
            __result = true;
        }
        catch (Exception exception)
        {
            Logger.Warning($"Backpack reload magazine lookup failed safely: {exception.Message}");
        }
    }

    private static bool TryGetBackpackMagazine(Equippable_RangedWeapon weapon, out StorableItemInstance magazine)
    {
        magazine = null;
        var requiredMagazine = weapon.Magazine;
        if (requiredMagazine == null || weapon.MagazineSize <= 0)
            return false;

        var slots = PlayerBackpack.Instance.GetBackpackSlots();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = GetSlot(slots, i);
            if (slot == null || slot.IsRemovalLocked)
                continue;

            var candidate = AsStorable(slot.ItemInstance);
            if (candidate == null || !DefinitionsMatch(candidate.Definition, requiredMagazine))
                continue;

            var ammoState = AsInteger(candidate);
            if (ammoState == null ||
                ammoState.Quantity != 1 ||
                ammoState.StackLimit != 1 ||
                ammoState.Value <= 0 ||
                ammoState.Value > weapon.MagazineSize)
                continue;

            magazine = candidate;
            return true;
        }

        return false;
    }

    private static bool DefinitionsMatch(ItemDefinition candidate, StorableItemDefinition required)
    {
        if (candidate == null || required == null)
            return false;

#if IL2CPP
        if (candidate.Pointer != IntPtr.Zero && candidate.Pointer == required.Pointer)
            return true;
#elif MONO
        if (ReferenceEquals(candidate, required))
            return true;
#endif

        return string.Equals(candidate.ID, required.ID, StringComparison.OrdinalIgnoreCase);
    }

    private static StorableItemInstance AsStorable(ItemInstance item)
    {
        if (item == null)
            return null;

#if IL2CPP
        try
        {
            return item.TryCast<StorableItemInstance>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return item as StorableItemInstance;
#endif
    }

    private static IntegerItemInstance AsInteger(StorableItemInstance item)
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

#if IL2CPP
    private static ItemSlot GetSlot(Il2CppSystem.Collections.Generic.List<ItemSlot> slots, int index)
    {
        return slots[new Index(index)]?.TryCast<ItemSlot>();
    }
#elif MONO
    private static ItemSlot GetSlot(List<ItemSlot> slots, int index)
    {
        return slots[index];
    }
#endif
}
