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
            PhoneBackpackUI.LogLifecycleState("CharacterInterface.Open.prefix");
            PhoneBackpackUI.SetCharacterSuppressed(true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Phone Backpack Character screen transition skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPatch("Open")]
    [HarmonyPostfix]
    public static void OpenPostfix()
    {
        PhoneBackpackUI.LogLifecycleState("CharacterInterface.Open.postfix");
    }

    [HarmonyPatch("Close")]
    [HarmonyPrefix]
    public static void ClosePrefix()
    {
        PhoneBackpackUI.LogLifecycleState("CharacterInterface.Close.prefix");
    }

    [HarmonyPatch("Close")]
    [HarmonyPostfix]
    public static void ClosePostfix()
    {
        try
        {
            PhoneBackpackUI.SetCharacterSuppressed(false);
            ContextualBackpackPanel.HidePhone();
            PhoneBackpackUI.LogLifecycleState("CharacterInterface.Close.postfix");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack Character screen cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
