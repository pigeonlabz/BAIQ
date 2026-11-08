namespace Backpack.Cheats;

internal static class PlantYieldPolicy
{
    public static int GetEffectiveYield(int nativeYield, int multiplier, bool enabled)
    {
        if (nativeYield <= 0 || !enabled || multiplier is not (2 or 4 or 8))
            return nativeYield;

        return (int)Math.Min((long)nativeYield * multiplier, int.MaxValue);
    }
}
