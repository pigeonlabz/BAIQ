namespace Backpack.Cheats;

/// <summary>
/// Computes only the capacity available to future drying operations. It never
/// changes a rack's existing operations or their quantities.
/// </summary>
internal static class DryingRackCapacityPolicy
{
    public static int GetEffectiveCapacity(int nativeCapacity, int multiplier, bool increaseProductionActive)
    {
        if (nativeCapacity <= 0 || !increaseProductionActive || multiplier is not (2 or 4 or 8))
            return nativeCapacity;

        return (int)Math.Min((long)nativeCapacity * multiplier, int.MaxValue);
    }

    public static int GetRemainingCapacity(int effectiveCapacity, int existingOperationQuantity)
    {
        return (int)Math.Clamp((long)effectiveCapacity - Math.Max(0, existingOperationQuantity), 0, int.MaxValue);
    }
}
