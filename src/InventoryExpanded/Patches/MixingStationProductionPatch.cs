#if IL2CPP
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Backpack.Patches;

/// <summary>
/// Exposes a scaled MaxMixQuantity only while the native getter calculates a
/// candidate batch. Both native input slots use that same result, preserving
/// their 1:1 recipe ratio and the persisted MixOperation.Quantity.
/// </summary>
internal static class MixingStationQuantityOverrideCoordinator
{
    [ThreadStatic]
    private static TemporaryCapacityOverrideScopes<IntPtr> _scopes;

    private static TemporaryCapacityOverrideScopes<IntPtr> Scopes =>
        _scopes ??= new TemporaryCapacityOverrideScopes<IntPtr>();

    public static TemporaryCapacityOverrideScopes<IntPtr>.Lease Enter(MixingStation station)
    {
        return Scopes.Enter(
            station.Pointer,
            station.MaxMixQuantity,
            ProductionCheats.GetMultiplier(ProductionCheats.MixingStationsEnabled),
            ProductionCheats.MixingStationsEnabled);
    }

    public static bool Exit(
        TemporaryCapacityOverrideScopes<IntPtr>.Lease lease,
        out int nativeMaximum)
    {
        return Scopes.Exit(lease, out nativeMaximum);
    }
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.GetMixQuantity))]
internal static class MixingStationGetQuantityProductionPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        MixingStation __instance,
        ref TemporaryCapacityOverrideScopes<IntPtr>.Lease __state)
    {
        __state = MixingStationQuantityOverrideCoordinator.Enter(__instance);
        if (__state.ShouldApply)
            __instance.MaxMixQuantity = __state.EffectiveCapacity;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(
        MixingStation __instance,
        TemporaryCapacityOverrideScopes<IntPtr>.Lease __state,
        Exception __exception)
    {
        if (MixingStationQuantityOverrideCoordinator.Exit(__state, out var nativeMaximum))
            __instance.MaxMixQuantity = nativeMaximum;
        return __exception;
    }
}

/// <summary>
/// MixOperation.Quantity is also the native timer multiplier. For a persisted
/// operation above the native cap, add only the elapsed-time delta needed for
/// the scaled quantity to complete in the native-capacity timeframe. The
/// persisted CurrentMixTime remains sufficient to resume the same operation
/// after reload, and this rule remains active if the preference is later turned
/// off or reduced. World time and station update frequency are untouched.
/// </summary>
internal static class MixingStationProductionTimingPatch
{
    private sealed class ProgressFrame
    {
        public ProgressFrame(IntPtr operation, int quantity, int nativeMaximum)
        {
            Operation = operation;
            Quantity = quantity;
            NativeMaximum = nativeMaximum;
        }

        public IntPtr Operation { get; }
        public int Quantity { get; }
        public int NativeMaximum { get; }
        public long Remainder { get; set; }
    }

    internal sealed class Lease
    {
        public IntPtr Station { get; init; }
        public IntPtr Operation { get; init; }
        public int PreviousMixTime { get; init; }
        public int AddedMinutes { get; init; }
        public long PreviousRemainder { get; init; }
        public bool HasProgressFrame { get; init; }
        public bool GuardEntered { get; init; }
    }

    [ThreadStatic]
    private static Dictionary<IntPtr, ProgressFrame> _frames;

    [ThreadStatic]
    private static HashSet<IntPtr> _activeStations;

    private static Dictionary<IntPtr, ProgressFrame> Frames =>
        _frames ??= new Dictionary<IntPtr, ProgressFrame>();

    private static HashSet<IntPtr> ActiveStations =>
        _activeStations ??= new HashSet<IntPtr>();

    private static Lease Apply(MixingStation station, int elapsedMinutes)
    {
        var pointer = station.Pointer;
        if (!ActiveStations.Add(pointer))
            return new Lease { Station = pointer };

        var lease = new Lease { Station = pointer, GuardEntered = true };
        var operation = station.CurrentMixOperation;
        if (operation == null || elapsedMinutes <= 0)
        {
            Frames.Remove(pointer);
            return lease;
        }

        var quantity = operation.Quantity;
        var nativeMaximum = station.MaxMixQuantity;
        if (quantity <= nativeMaximum || nativeMaximum <= 0)
        {
            Frames.Remove(pointer);
            return lease;
        }

        var operationPointer = operation.Pointer;
        if (!Frames.TryGetValue(pointer, out var frame)
            || frame.Operation != operationPointer
            || frame.Quantity != quantity
            || frame.NativeMaximum != nativeMaximum)
        {
            frame = new ProgressFrame(operationPointer, quantity, nativeMaximum);
            Frames[pointer] = frame;
        }

        var previousMixTime = station.CurrentMixTime;
        var previousRemainder = frame.Remainder;
        var delta = MixingProductionPolicy.GetExtraElapsedMinutes(
            elapsedMinutes,
            quantity,
            nativeMaximum,
            frame.Remainder);
        frame.Remainder = delta.Remainder;
        if (delta.ExtraMinutes <= 0)
        {
            return new Lease
            {
                Station = pointer,
                Operation = operationPointer,
                PreviousMixTime = previousMixTime,
                PreviousRemainder = previousRemainder,
                HasProgressFrame = true,
                GuardEntered = true
            };
        }

        station.CurrentMixTime = (int)Math.Min((long)previousMixTime + delta.ExtraMinutes, int.MaxValue);
        return new Lease
        {
            Station = pointer,
            Operation = operationPointer,
            PreviousMixTime = previousMixTime,
            AddedMinutes = station.CurrentMixTime - previousMixTime,
            PreviousRemainder = previousRemainder,
            HasProgressFrame = true,
            GuardEntered = true
        };
    }

    private static void RollBack(MixingStation station, Lease lease)
    {
        if (lease == null || !lease.HasProgressFrame || station.Pointer != lease.Station)
            return;

        var operation = station.CurrentMixOperation;
        if (operation == null || operation.Pointer != lease.Operation)
            return;

        station.CurrentMixTime = Math.Max(0, station.CurrentMixTime - lease.AddedMinutes);
        if (Frames.TryGetValue(lease.Station, out var frame) && frame.Operation == lease.Operation)
            frame.Remainder = lease.PreviousRemainder;
    }

    private static void Forget(MixingStation station)
    {
        Frames.Remove(station.Pointer);
    }

    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.OnTimePass))]
    private static class MixingStationOnTimePassProductionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(MixingStation __instance, int __0, ref Lease __state)
        {
            __state = Apply(__instance, __0);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(MixingStation __instance, Lease __state, Exception __exception)
        {
            if (__exception != null)
                RollBack(__instance, __state);
            if (__state?.GuardEntered == true)
                ActiveStations.Remove(__state.Station);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.MixingDone))]
    private static class MixingStationDoneProductionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MixingStation __instance)
        {
            Forget(__instance);
        }
    }
}
#endif
