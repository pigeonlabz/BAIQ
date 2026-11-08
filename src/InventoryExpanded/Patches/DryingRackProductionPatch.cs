#if IL2CPP
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Backpack.Patches;

/// <summary>
/// The native rack methods are synchronous on Unity's main thread. Share one
/// per-thread scope across both patches so re-entrant calls for the same native
/// object reuse, rather than multiply, the outer effective capacity.
/// </summary>
internal static class DryingRackCapacityOverrideCoordinator
{
    [ThreadStatic]
    private static TemporaryCapacityOverrideScopes<IntPtr> _scopes;

    private static TemporaryCapacityOverrideScopes<IntPtr> Scopes =>
        _scopes ??= new TemporaryCapacityOverrideScopes<IntPtr>();

    public static TemporaryCapacityOverrideScopes<IntPtr>.Lease Enter(DryingRack rack)
    {
        return Scopes.Enter(
            rack.Pointer,
            rack.ItemCapacity,
            ProductionCheats.GetMultiplier(ProductionCheats.DryingRacksEnabled),
            ProductionCheats.DryingRacksEnabled);
    }

    public static bool Exit(
        TemporaryCapacityOverrideScopes<IntPtr>.Lease lease,
        out int originalCapacity)
    {
        return Scopes.Exit(lease, out originalCapacity);
    }
}

/// <summary>
/// Historical MAIN 0.4.6f13 and BETA 0.4.7f7 references expose the same public
/// serialized DryingRack.ItemCapacity field and synchronous CanStartOperation/StartOperation
/// boundaries. The supported target is MAIN 0.4.7f12. Temporarily expose a scaled value only during those native calls;
/// never serialize or retain the multiplied field value.
/// </summary>
[HarmonyPatch(typeof(DryingRack), nameof(DryingRack.CanStartOperation))]
internal static class DryingRackCanStartProductionPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        DryingRack __instance,
        ref TemporaryCapacityOverrideScopes<IntPtr>.Lease __state)
    {
        __state = DryingRackCapacityOverrideCoordinator.Enter(__instance);
        if (__state.ShouldApply)
            __instance.ItemCapacity = __state.EffectiveCapacity;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(
        DryingRack __instance,
        TemporaryCapacityOverrideScopes<IntPtr>.Lease __state,
        Exception __exception)
    {
        if (DryingRackCapacityOverrideCoordinator.Exit(__state, out var originalCapacity))
            __instance.ItemCapacity = originalCapacity;
        return __exception;
    }
}

[HarmonyPatch(typeof(DryingRack), nameof(DryingRack.StartOperation))]
internal static class DryingRackStartProductionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        DryingRack __instance,
        ref TemporaryCapacityOverrideScopes<IntPtr>.Lease __state)
    {
        __state = DryingRackCapacityOverrideCoordinator.Enter(__instance);
        if (__state.ShouldApply)
            __instance.ItemCapacity = __state.EffectiveCapacity;

        var effective = __state.EffectiveCapacity;
        var existingQuantity = __instance.GetTotalDryingItems();

        // Native StartOperation subtracts current operation quantity from
        // ItemCapacity without clamping at zero. If settings were reduced while
        // a larger operation is running, allowing that arithmetic to continue
        // could create a negative operation and add quantity back to InputSlot.
        // Refuse only this new start whether the preference is now off, Vanilla,
        // or a lower multiplier; existing operations and input remain untouched.
        var remaining = DryingRackCapacityPolicy.GetRemainingCapacity(
            effective,
            existingQuantity);
        if (remaining <= 0)
        {
            if (existingQuantity > __instance.ItemCapacity || ProductionCheats.DryingRacksEnabled)
            {
                Logger.Diagnostic(
                    $"[DryingRack] Start refused: current operations fill or exceed the active capacity " +
                    $"({existingQuantity}/{effective}); existing rack state was left unchanged.");
            }
            return false;
        }

        return true;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(
        DryingRack __instance,
        TemporaryCapacityOverrideScopes<IntPtr>.Lease __state,
        Exception __exception)
    {
        if (DryingRackCapacityOverrideCoordinator.Exit(__state, out var originalCapacity))
            __instance.ItemCapacity = originalCapacity;
        return __exception;
    }
}
#endif
