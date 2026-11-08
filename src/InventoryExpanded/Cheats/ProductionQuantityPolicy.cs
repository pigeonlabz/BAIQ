namespace Backpack.Cheats;

internal static class ProductionQuantityPolicy
{
    public static int GetEffectiveMaximum(int nativeMaximum, int multiplier, bool enabled)
    {
        if (nativeMaximum <= 0 || !enabled || multiplier is not (2 or 4 or 8))
            return nativeMaximum;

        return (int)Math.Min((long)nativeMaximum * multiplier, int.MaxValue);
    }
}
