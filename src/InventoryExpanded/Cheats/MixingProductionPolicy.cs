namespace Backpack.Cheats;

/// <summary>
/// Policies for the native mixing quantity and its persisted elapsed-time field.
/// A batch above the native cap stores virtual elapsed minutes at the same ratio
/// as its quantity, so it reaches the native-capacity duration without changing
/// world time or the station's update cadence.
/// </summary>
internal static class MixingProductionPolicy
{
    public static int GetEffectiveMaximum(int nativeMaximum, int multiplier, bool enabled)
    {
        return ProductionQuantityPolicy.GetEffectiveMaximum(nativeMaximum, multiplier, enabled);
    }

    public static ProgressDelta GetExtraElapsedMinutes(
        int elapsedMinutes,
        int operationQuantity,
        int nativeMaximum,
        long remainder)
    {
        if (elapsedMinutes <= 0 || operationQuantity <= nativeMaximum || nativeMaximum <= 0)
            return new ProgressDelta(0, 0);

        var boundedRemainder = Math.Clamp(remainder, 0, nativeMaximum - 1L);
        var numerator = (long)elapsedMinutes * (operationQuantity - (long)nativeMaximum) + boundedRemainder;
        return new ProgressDelta(
            (int)Math.Min(numerator / nativeMaximum, int.MaxValue),
            numerator % nativeMaximum);
    }

    internal readonly record struct ProgressDelta(int ExtraMinutes, long Remainder);
}
