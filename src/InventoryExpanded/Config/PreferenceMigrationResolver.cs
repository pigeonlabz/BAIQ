namespace Backpack.Config;

internal enum PreferenceMigrationResult
{
    CurrentValuePreserved,
    Migrated,
    DefaultRetained
}

internal readonly record struct PreferenceMigrationCandidate(string Source, string RawValue);

internal static class PreferenceMigrationResolver
{
    public const int CurrentVersion = 8;

    public static bool ShouldRun(int storedVersion)
    {
        return storedVersion < CurrentVersion;
    }

    public static bool ShouldRun(bool currentMarkerPresent, int storedVersion)
    {
        return !currentMarkerPresent || ShouldRun(storedVersion);
    }

    public static int? MergeLegacyCapacity(int? existing, int candidate)
    {
        if (candidate < 1)
            return existing;

        return existing.HasValue ? Math.Max(existing.Value, candidate) : candidate;
    }

    public static PreferenceMigrationResult Resolve(
        bool currentValuePresent,
        IEnumerable<PreferenceMigrationCandidate> candidates,
        Func<string, bool> tryApply,
        Action<string> onMalformed)
    {
        if (currentValuePresent)
            return PreferenceMigrationResult.CurrentValuePreserved;

        foreach (var candidate in candidates)
        {
            if (tryApply(candidate.RawValue))
                return PreferenceMigrationResult.Migrated;

            onMalformed?.Invoke(candidate.Source);
        }

        return PreferenceMigrationResult.DefaultRetained;
    }
}
