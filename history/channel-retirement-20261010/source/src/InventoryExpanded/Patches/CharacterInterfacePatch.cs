using Backpack.UI;
using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.UI;
#elif MONO
using ScheduleOne.UI;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(CharacterInterface))]
public static class CharacterInterfacePatch
{
    [HarmonyPatch("Open")]
    [HarmonyPrefix]
    public static void Open()
    {
        try
        {
            PhoneBackpackUI.SetCharacterSuppressed(true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Phone Backpack Character screen transition skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPatch("Close")]
    [HarmonyPrefix]
    public static void Close()
    {
        try
        {
            PhoneBackpackUI.SetCharacterSuppressed(false);
            ContextualBackpackPanel.HidePhone();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack Character screen cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
