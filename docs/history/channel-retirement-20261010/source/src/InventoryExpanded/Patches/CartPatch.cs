using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
#elif MONO
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI.Shop;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(Cart))]
public static class CartPatch
{
    [HarmonyPatch("GetWarning")]
    [HarmonyPostfix]
    public static void GetWarning(Cart __instance, ref bool __result, ref string warning)
    {
        if (!PlayerBackpack.Instance.IsUnlocked)
            return;

        if (warning.StartsWith("Vehicle") || !__result)
            return;

        var items = PlayerBackpack.Instance.ItemSlots;
#if IL2CPP
        items.InsertRange(0, PlayerInventory.Instance.hotbarSlots.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ItemSlot>>());
#elif MONO
        items.InsertRange(0, PlayerInventory.Instance.hotbarSlots);
#endif
        if (!__instance.Shop.WillCartFit(items))
            return;

        warning = "Inventory won't fit everything. Some items will be placed in your backpack.";
    }
}