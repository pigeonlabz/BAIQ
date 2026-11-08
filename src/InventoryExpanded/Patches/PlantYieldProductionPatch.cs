#if IL2CPP
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.PlayerTasks;

namespace Backpack.Patches;

/// <summary>
/// The native player harvest task checks whether ProductQuantity fits before it
/// calls PlantHarvestable.Harvest. Keep the temporary quantity override active
/// across that whole synchronous Update so the native preflight and product
/// creation use the same value. Never persistently edit plant or bud state.
/// </summary>
internal static class PlantYieldOverrideCoordinator
{
    [ThreadStatic]
    private static List<Frame> _frames;

    [ThreadStatic]
    private static TemporaryCapacityOverrideScopes<IntPtr> _quantityScopes;

    private static List<Frame> Frames => _frames ??= new List<Frame>(1);

    private static TemporaryCapacityOverrideScopes<IntPtr> QuantityScopes =>
        _quantityScopes ??= new TemporaryCapacityOverrideScopes<IntPtr>();

    internal struct Frame
    {
        public IntPtr TaskPointer;
        public int Multiplier;
        public IntPtr HarvestablePointer;
        public PlantHarvestable Harvestable;
        public TemporaryCapacityOverrideScopes<IntPtr>.Lease Lease;
    }

    public static int Enter(HarvestPlant task)
    {
        if (!ProductionCheats.PlantYieldEnabled)
            return -1;

        var multiplier = ProductionCheats.GetMultiplier(ProductionCheats.PlantYieldEnabled);
        if (multiplier <= 1)
            return -1;

        var frames = Frames;
        var index = frames.Count;
        frames.Add(new Frame { TaskPointer = task.Pointer, Multiplier = multiplier });
        return index;
    }

    public static void Observe(HarvestPlant task, PlantHarvestable harvestable)
    {
        if (harvestable == null || _frames == null || _frames.Count == 0)
            return;

        var index = _frames.Count - 1;
        var frame = _frames[index];
        if (frame.TaskPointer != task.Pointer)
            return;

        var pointer = harvestable.Pointer;
        if (frame.HarvestablePointer == pointer && frame.Lease != null)
            return;

        Restore(frame);
        frame = new Frame { TaskPointer = frame.TaskPointer, Multiplier = frame.Multiplier };

        var nativeQuantity = harvestable.ProductQuantity;
        var lease = QuantityScopes.Enter(pointer, nativeQuantity, frame.Multiplier, enabled: true);
        var effectiveYield = PlantYieldPolicy.GetEffectiveYield(
            lease.Frame.OriginalCapacity,
            frame.Multiplier,
            enabled: true);
        frame.HarvestablePointer = pointer;
        frame.Harvestable = harvestable;
        frame.Lease = lease;
        _frames[index] = frame;

        if (lease.ShouldApply && lease.EffectiveCapacity == effectiveYield)
            harvestable.ProductQuantity = effectiveYield;
    }

    public static void Exit(int frameIndex)
    {
        if (frameIndex < 0 || _frames == null || _frames.Count <= frameIndex)
            return;

        while (_frames.Count > frameIndex)
        {
            var lastIndex = _frames.Count - 1;
            var frame = _frames[lastIndex];
            Restore(frame);
            _frames.RemoveAt(lastIndex);
        }
    }

    private static void Restore(Frame frame)
    {
        if (frame.Lease == null)
            return;

        if (!QuantityScopes.Exit(frame.Lease, out var nativeQuantity))
            return;

        try
        {
            if (frame.Harvestable != null
                && frame.Harvestable.Pointer == frame.HarvestablePointer
                && frame.Harvestable.ProductQuantity == frame.Lease.EffectiveCapacity)
            {
                frame.Harvestable.ProductQuantity = nativeQuantity;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"[PlantYield] Temporary quantity restoration skipped for an unavailable harvestable ({exception.GetType().Name}).");
        }
    }
}

[HarmonyPatch(typeof(HarvestPlant), nameof(HarvestPlant.Update))]
internal static class PlantHarvestTaskYieldPatch
{
    [HarmonyPrefix]
    private static void Prefix(HarvestPlant __instance, ref int __state)
    {
        __state = -1;
        __state = PlantYieldOverrideCoordinator.Enter(__instance);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(HarvestPlant __instance, int __state, Exception __exception)
    {
        PlantYieldOverrideCoordinator.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(HarvestPlant), "GetHoveredHarvestable")]
internal static class PlantHarvestableYieldPreflightPatch
{
    [HarmonyPostfix]
    private static void Postfix(HarvestPlant __instance, PlantHarvestable __result)
    {
        PlantYieldOverrideCoordinator.Observe(__instance, __result);
    }
}
#endif
