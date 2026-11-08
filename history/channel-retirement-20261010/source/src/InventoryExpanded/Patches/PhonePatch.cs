using Backpack.UI;
using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.UI.Phone;
#elif MONO
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
            if (!o)
                return;

            PhoneBackpackUI.Hide("phone reopening");
            ContextualBackpackPanel.Hide();

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
                PhoneBackpackUI.Show(__instance);
            else
                PhoneBackpackUI.Hide("Phone.SetIsOpen(false)");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Phone Backpack transition skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
