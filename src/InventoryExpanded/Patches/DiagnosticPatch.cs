using HarmonyLib;
using UnityEngine;
using Backpack.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Items;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.UI.Items;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(ItemInfoPanel))]
public static class ItemInfoPanelOverlayPatch
{
    [HarmonyPatch("Open", [typeof(ItemInstance), typeof(RectTransform)])]
    [HarmonyPostfix]
    public static void OpenItem(ItemInfoPanel __instance, RectTransform __1)
    {
        BackpackItemInfoPlacement.KeepOnScreen(__instance, __1);
        ContextualBackpackPanel.UpdateNativeOverlayLayers(Singleton<ItemUIManager>.Instance);
    }

    [HarmonyPatch("Open", [typeof(ItemDefinition), typeof(RectTransform)])]
    [HarmonyPostfix]
    public static void OpenDefinition(ItemInfoPanel __instance, RectTransform __1)
    {
        BackpackItemInfoPlacement.KeepOnScreen(__instance, __1);
        ContextualBackpackPanel.UpdateNativeOverlayLayers(Singleton<ItemUIManager>.Instance);
    }
}
