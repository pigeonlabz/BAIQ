using Backpack.UI;
using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
#elif MONO
using ScheduleOne.UI;
using ScheduleOne.UI.Phone;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(Phone))]
public static class PhonePatch
{
    [HarmonyPatch("SetIsOpen", [typeof(bool)])]
    [HarmonyPrefix]
    public static void SetIsOpenPrefix(bool o)
    {
        try
        {
            PhoneBackpackUI.LogLifecycleState($"Phone.SetIsOpen.prefix requested={o}");
            if (!o)
                return;

            PhoneBackpackUI.Hide("phone reopening");
            ContextualBackpackPanel.Hide("Phone.SetIsOpen(true) transition");

            if (PlayerBackpack.Instance?.IsOpen == true)
                PlayerBackpack.Instance.Close();
            else
                ResponsiveBackpackUI.CloseActive();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Backpack UI phone transition cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPatch("SetIsOpen", [typeof(bool)])]
    [HarmonyPostfix]
    public static void SetIsOpenPostfix(Phone __instance, bool o)
    {
        try
        {
            if (o && __instance.IsOpen)
                PhoneBackpackUI.RequestShowAfterLayout(__instance);
            else
                PhoneBackpackUI.Hide("Phone.SetIsOpen(false)");

            PhoneBackpackUI.LogLifecycleState($"Phone.SetIsOpen.postfix requested={o}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Phone Backpack transition skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }
}

[HarmonyPatch(typeof(GameplayMenu))]
public static class GameplayMenuPatch
{
    [HarmonyPatch("SetScreen")]
    [HarmonyPrefix]
    public static void SetScreenPrefix(GameplayMenu __instance)
    {
        PhoneBackpackUI.LogLifecycleState($"GameplayMenu.SetScreen.prefix screen={__instance.CurrentScreen}");
    }

    [HarmonyPatch("SetScreen")]
    [HarmonyPostfix]
    public static void SetScreenPostfix(GameplayMenu __instance)
    {
        PhoneBackpackUI.OnGameplayScreenChanged(__instance);
    }
}
