namespace Backpack.Cheats;

public enum StackSizeMultiplierOption
{
    Vanilla = 1,
    x2 = 2,
    x4 = 4,
    x8 = 8
}

public static class CheatSettings
{
    private static bool _initialized;

    public static StackSizeMultiplierOption StackSizeMultiplier { get; private set; } = StackSizeMultiplierOption.Vanilla;
    public static int StackSizeMultiplierValue => (int)StackSizeMultiplier;

    internal static void Apply(
        StackSizeMultiplierOption stackSizeMultiplier,
        bool dryingRacksEnabled = false,
        bool mixingStationsEnabled = false,
        bool plantYieldEnabled = false,
        bool packagingStationsEnabled = false)
    {
        var normalized = Enum.IsDefined(typeof(StackSizeMultiplierOption), stackSizeMultiplier)
            ? stackSizeMultiplier
            : StackSizeMultiplierOption.Vanilla;
        var changed = !_initialized
            || StackSizeMultiplier != normalized
            || ProductionCheats.DryingRacksEnabled != dryingRacksEnabled
            || ProductionCheats.MixingStationsEnabled != mixingStationsEnabled
            || ProductionCheats.PlantYieldEnabled != plantYieldEnabled
            || ProductionCheats.PackagingStationsEnabled != packagingStationsEnabled;

        StackSizeMultiplier = normalized;
        ProductionCheats.Apply(dryingRacksEnabled, mixingStationsEnabled, plantYieldEnabled, packagingStationsEnabled);
        CheatState.SetActive(CheatFeature.StackSizeMultiplier, normalized != StackSizeMultiplierOption.Vanilla);

        if (!changed)
            return;

        _initialized = true;
        Logger.Info(
            $"[PackagingProduction] config key=05_PackagingStations, preference={packagingStationsEnabled}, " +
            $"stackMultiplier={(int)normalized}, effectiveMultiplier={ProductionCheats.GetMultiplier(packagingStationsEnabled)}, " +
            $"targetPackages={PackagingProductionPolicy.GetTargetPackages((int)normalized, packagingStationsEnabled)}.");
        Logger.Info(
            $"Stack Size Multiplier: {Format(normalized)}; Increase Production settings: " +
            $"drying racks {(ProductionCheats.DryingRacksEnabled ? "on" : "off")}, " +
            $"mixing stations {(ProductionCheats.MixingStationsEnabled ? "on" : "off")}, " +
            $"plant yield {(ProductionCheats.PlantYieldEnabled ? "on" : "off")}, " +
            $"packaging stations {(ProductionCheats.PackagingStationsEnabled ? "on" : "off")}. " +
            (CheatState.IsAnyCheatActive ? "Achievements are suppressed while cheats are active." : "Achievements are enabled."));
    }

    public static string Format(StackSizeMultiplierOption option)
    {
        return option == StackSizeMultiplierOption.Vanilla ? "Vanilla / 1x" : option.ToString();
    }
}
