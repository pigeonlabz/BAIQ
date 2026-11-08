namespace Backpack.Cheats;

/// <summary>
/// Holds independent production settings. Each native adapter asks for its own
/// effective multiplier so enabling one system cannot activate another.
/// </summary>
public static class ProductionCheats
{
    public static bool DryingRacksEnabled { get; private set; }
    public static bool MixingStationsEnabled { get; private set; }
    public static bool PlantYieldEnabled { get; private set; }
    public static bool PackagingStationsEnabled { get; private set; }

    public static int GetMultiplier(bool enabled)
    {
        return enabled && CheatSettings.StackSizeMultiplier != StackSizeMultiplierOption.Vanilla
            ? CheatSettings.StackSizeMultiplierValue
            : 1;
    }

    internal static void Apply(
        bool dryingRacksEnabled,
        bool mixingStationsEnabled,
        bool plantYieldEnabled,
        bool packagingStationsEnabled)
    {
        DryingRacksEnabled = dryingRacksEnabled;
        MixingStationsEnabled = mixingStationsEnabled;
        PlantYieldEnabled = plantYieldEnabled;
        PackagingStationsEnabled = packagingStationsEnabled;
    }
}
