using Backpack.UI;
using HarmonyLib;
using Stopwatch = System.Diagnostics.Stopwatch;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppScheduleOne.DevUtilities;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(StorageMenu))]
public static class StorageMenuPatch
{
    private static Stopwatch _backpackOpenTimer;

    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake(StorageMenu __instance)
    {
        EnsureSlotCapacity(__instance);
    }

#if IL2CPP
    [HarmonyPatch("Open", [typeof(string), typeof(string), typeof(IItemSlotOwner), typeof(Il2CppSystem.Action)])]
#elif MONO
    [HarmonyPatch("Open", [typeof(string), typeof(string), typeof(IItemSlotOwner)])]
#endif
    [HarmonyPrefix]
    public static void PrepareOpen(StorageMenu __instance, string title)
    {
        if (title == PlayerBackpack.StorageName)
            _backpackOpenTimer = Stopwatch.StartNew();

        EnsureSlotCapacity(__instance);
        if (title == PlayerBackpack.StorageName)
            return;

        for (var i = 0; i < __instance.SlotsUIs.Length; i++)
        {
            var slotUi = __instance.SlotsUIs[i];
            if (slotUi == null)
                continue;

            slotUi.ClearSlot();
            slotUi.SetVisible(false);
        }
    }

#if IL2CPP
    [HarmonyPatch("Open", [typeof(string), typeof(string), typeof(IItemSlotOwner), typeof(Il2CppSystem.Action)])]
#elif MONO
    [HarmonyPatch("Open", [typeof(string), typeof(string), typeof(IItemSlotOwner)])]
#endif
    [HarmonyPostfix]
    public static void OpenByTitle(StorageMenu __instance, string title, IItemSlotOwner owner)
    {
        var nativeOpenMs = 0d;
        if (title == PlayerBackpack.StorageName && _backpackOpenTimer != null)
        {
            nativeOpenMs = _backpackOpenTimer.Elapsed.TotalMilliseconds;
            _backpackOpenTimer = null;
        }

        HandleOpen(__instance, title, owner, nativeOpenMs);
    }

#if IL2CPP
    [HarmonyPatch("Open", [typeof(IItemSlotOwner), typeof(string), typeof(string), typeof(Il2CppSystem.Action)])]
    [HarmonyPostfix]
    public static void OpenByOwner(StorageMenu __instance, IItemSlotOwner owner, string title)
    {
        HandleOpen(__instance, title, owner);
    }
#endif

#if IL2CPP
    [HarmonyPatch("Open", [typeof(StorageEntity), typeof(Il2CppSystem.Action)])]
#elif MONO
    [HarmonyPatch("Open", [typeof(StorageEntity)])]
#endif
    [HarmonyPostfix]
    public static void OpenByStorageEntity(StorageMenu __instance, StorageEntity entity)
    {
#if IL2CPP
        HandleOpen(__instance, __instance.TitleLabel.text, entity?.TryCast<IItemSlotOwner>());
#elif MONO
        HandleOpen(__instance, __instance.TitleLabel.text, entity);
#endif
    }

    private static void HandleOpen(StorageMenu instance, string title, IItemSlotOwner owner, double nativeOpenMs = 0d)
    {
        if (title == PlayerBackpack.StorageName)
        {
            StorageSortButton.Hide();
            ContextualBackpackPanel.Hide();
            ResponsiveBackpackUI.Open(instance, nativeOpenMs);
            return;
        }

        ResponsiveBackpackUI.Close(instance);
        StorageSortButton.Show(instance, owner);
    }

    [HarmonyPatch("CloseMenu")]
    [HarmonyPrefix]
    public static void CloseMenu(StorageMenu __instance)
    {
        HandleClose(__instance);
    }

    [HarmonyPatch("Close")]
    [HarmonyPrefix]
    public static void Close(StorageMenu __instance)
    {
        HandleClose(__instance);
    }

    private static void HandleClose(StorageMenu instance)
    {
        StorageSortButton.Hide();
        ResponsiveBackpackUI.Close(instance);
    }

    private static void EnsureSlotCapacity(StorageMenu menu)
    {
        if (menu.SlotsUIs == null || menu.SlotsUIs.Length == 0 || menu.SlotsUIs.Length >= PlayerBackpack.MaxStorageSlots)
            return;

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        var prefab = itemUiManager?.ItemSlotUIPrefab?.gameObject ?? menu.SlotsUIs[0]?.gameObject;
        if (prefab == null)
        {
            Logger.Error("No canonical ItemSlotUI prefab is available for StorageMenu capacity.");
            return;
        }

        var slots = new ItemSlotUI[PlayerBackpack.MaxStorageSlots];
        for (var i = 0; i < slots.Length; i++)
        {
            if (i < menu.SlotsUIs.Length)
            {
                slots[i] = menu.SlotsUIs[i];
                continue;
            }

            var slotObject = UnityEngine.Object.Instantiate(prefab, menu.SlotContainer);
            slotObject.name = "OGBackpack_NativeCapacitySlot_" + i;
            var slotUi = slotObject.GetComponent<ItemSlotUI>();
            slotUi.ClearSlot();
            slotUi.SetVisible(false);
            slots[i] = slotUi;
        }

        menu.SlotsUIs = slots;
    }
}
