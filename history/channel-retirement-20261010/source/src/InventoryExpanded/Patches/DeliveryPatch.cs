using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.Delivery;
#elif MONO
using ScheduleOne.Delivery;
#endif

namespace Backpack.Patches;

/// <summary>
/// Keeps Schedule I's delivery loader on one consistent stack-limit model.
/// DeliveryInstance generates payload chunks from ItemDefinition.StackLimit, so
/// nested vehicle-storage capacity checks must see that same native limit.
/// </summary>
internal static class DeliveryVehicleLoadContext
{
    [ThreadStatic]
    private static int _depth;

    public static bool IsActive => _depth > 0;

    public static void Enter()
    {
        _depth++;
    }

    public static void Exit()
    {
        if (_depth > 0)
            _depth--;
    }
}

[HarmonyPatch(typeof(DeliveryInstance), nameof(DeliveryInstance.AddItemsToDeliveryVehicle))]
public static class DeliveryInstancePatch
{
    [HarmonyPrefix]
    public static void AddItemsToDeliveryVehiclePrefix(DeliveryInstance __instance)
    {
        DeliveryVehicleLoadContext.Enter();

        if (Logger.DiagnosticLoggingEnabled)
            Logger.Diagnostic($"[DeliveryStack] Loading native-sized payload stacks for delivery {__instance?.DeliveryID ?? "<unknown>"}.");
    }

    [HarmonyFinalizer]
    public static Exception AddItemsToDeliveryVehicleFinalizer(Exception __exception)
    {
        DeliveryVehicleLoadContext.Exit();
        return __exception;
    }
}
