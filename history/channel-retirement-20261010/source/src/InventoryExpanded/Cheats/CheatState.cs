namespace Backpack.Cheats;

public enum CheatFeature
{
    StackSizeMultiplier
}

public static class CheatState
{
    private static readonly HashSet<CheatFeature> ActiveFeatures = [];

    public static bool IsAnyCheatActive => ActiveFeatures.Count > 0;

    public static string ActiveCheatSummary => ActiveFeatures.Count == 0
        ? "none"
        : string.Join(", ", ActiveFeatures.Select(Describe));

    internal static void SetActive(CheatFeature feature, bool active)
    {
        if (active)
            ActiveFeatures.Add(feature);
        else
            ActiveFeatures.Remove(feature);
    }

    private static string Describe(CheatFeature feature)
    {
        return feature switch
        {
            CheatFeature.StackSizeMultiplier => "Stack Size Multiplier",
            _ => feature.ToString()
        };
    }
}
