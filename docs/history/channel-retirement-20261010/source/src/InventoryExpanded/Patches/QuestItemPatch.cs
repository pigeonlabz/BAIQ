using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Tools;
#elif MONO
using ScheduleOne.Dialogue;
using ScheduleOne.Doors;
using ScheduleOne.Map;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Tools;
#endif

namespace Backpack.Patches;

public static class QuestItemContext
{
    private static int _recognitionDepth;
    private static string _removalItemId;

    public static void BeginRecognition() => _recognitionDepth++;
    public static void EndRecognition() => _recognitionDepth = Math.Max(0, _recognitionDepth - 1);
    public static bool IsRecognizing => _recognitionDepth > 0;

    public static bool BeginRemoval(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || _removalItemId != null)
            return false;

        _removalItemId = itemId;
        return true;
    }

    public static void EndRemoval(bool began)
    {
        if (began)
            _removalItemId = null;
    }

    public static bool IsRemovalFor(string itemId)
    {
        return _removalItemId != null && _removalItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(PlayerInventory))]
public static class QuestPlayerInventoryPatch
{
    [HarmonyPatch("GetAmountOfItem", [typeof(string)])]
    [HarmonyPostfix]
    public static void GetAmountOfItem(string __0, ref uint __result)
    {
        if (QuestItemContext.IsRecognizing && PlayerBackpack.Instance != null)
            __result += PlayerBackpack.Instance.GetQuantityOfItem(__0);
    }

    [HarmonyPatch("RemoveAmountOfItem", [typeof(string), typeof(uint)])]
    [HarmonyPrefix]
    public static void RemoveAmountOfItemPrefix(PlayerInventory __instance, string __0, out uint __state)
    {
        __state = QuestItemContext.IsRemovalFor(__0) ? __instance.GetAmountOfItem(__0) : uint.MaxValue;
    }

    [HarmonyPatch("RemoveAmountOfItem", [typeof(string), typeof(uint)])]
    [HarmonyPostfix]
    public static void RemoveAmountOfItemPostfix(string __0, uint __1, uint __state)
    {
        if (__state == uint.MaxValue || __state >= __1)
            return;

        var remainder = __1 - __state;
        if (PlayerBackpack.Instance?.TryRemoveQuantityOfItem(__0, remainder) != true)
            Logger.Error($"Quest removal could not safely remove {remainder}x {__0} from Backpack after native inventory removal.");
    }
}

[HarmonyPatch(typeof(SewerDoorController), "CanPlayerAccess")]
public static class SewerDoorRecognitionPatch
{
    [HarmonyPrefix]
    public static void Prefix() => QuestItemContext.BeginRecognition();

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        QuestItemContext.EndRecognition();
        return __exception;
    }
}

[HarmonyPatch(typeof(SewerDoorController), "ExteriorHandleInteracted")]
public static class SewerDoorRemovalPatch
{
    [HarmonyPrefix]
    public static void Prefix(out bool __state)
    {
        __state = QuestItemContext.BeginRemoval(SewerManager.Instance?.SewerKeyItem?.ID);
    }

    [HarmonyFinalizer]
    public static Exception Finalizer(bool __state, Exception __exception)
    {
        QuestItemContext.EndRemoval(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(BombPlantLocation), "CanPlantBomb")]
public static class BombPlantRecognitionPatch
{
    [HarmonyPrefix]
    public static void Prefix() => QuestItemContext.BeginRecognition();

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        QuestItemContext.EndRecognition();
        return __exception;
    }
}

[HarmonyPatch(typeof(BombPlantLocation), "PlantBomb")]
public static class BombPlantRemovalPatch
{
    [HarmonyPrefix]
    public static void Prefix(out bool __state) => __state = QuestItemContext.BeginRemoval("bomb");

    [HarmonyFinalizer]
    public static Exception Finalizer(bool __state, Exception __exception)
    {
        QuestItemContext.EndRemoval(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(DialogueController_ArmsDealer), "CheckChoice")]
public static class ArmsDealerRecognitionPatch
{
    [HarmonyPrefix]
    public static void Prefix() => QuestItemContext.BeginRecognition();

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        QuestItemContext.EndRecognition();
        return __exception;
    }
}

[HarmonyPatch(typeof(DialogueController_ArmsDealer), "ModifyDialogueText")]
public static class ArmsDealerDialogueRemovalPatch
{
    [HarmonyPrefix]
    public static void Prefix(DialogueController_ArmsDealer __instance, string __0, out bool __state)
    {
        __state = __0 == "BOMB_CREATED" && QuestItemContext.BeginRemoval(__instance.RDX?.ID);
    }

    [HarmonyFinalizer]
    public static Exception Finalizer(bool __state, Exception __exception)
    {
        QuestItemContext.EndRemoval(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(DialogueController_ArmsDealer), "TradeRDXForBomb")]
public static class ArmsDealerTradeRemovalPatch
{
    [HarmonyPrefix]
    public static void Prefix(DialogueController_ArmsDealer __instance, out bool __state)
    {
        __state = QuestItemContext.BeginRemoval(__instance.RDX?.ID);
    }

    [HarmonyFinalizer]
    public static Exception Finalizer(bool __state, Exception __exception)
    {
        QuestItemContext.EndRemoval(__state);
        return __exception;
    }
}
