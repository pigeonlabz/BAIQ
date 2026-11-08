namespace Backpack.UI;

internal static class PhoneBackpackScalePolicy
{
    public const int MinimumPercent = 50;
    public const int MaximumPercent = 150;
    public const int DefaultPercent = 100;

    public static int ClampPercent(int percent)
    {
        return Math.Clamp(percent, MinimumPercent, MaximumPercent);
    }

    public static float Apply(float automaticScale, int configuredPercent)
    {
        return automaticScale * ClampPercent(configuredPercent) / 100f;
    }
}
