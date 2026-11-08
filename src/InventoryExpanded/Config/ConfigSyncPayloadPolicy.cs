using System.Globalization;

namespace Backpack.Config;

internal readonly record struct ConfigSyncPayload(
    string ModVersion,
    string UnlockRank,
    int UnlockTier,
    bool EnableSearch);

internal static class ConfigSyncPayloadPolicy
{
    private const string Schema = "2";

    internal static string Create(string modVersion, string unlockRank, int unlockTier, bool enableSearch)
    {
        return string.Join("|", new[]
        {
            Schema,
            modVersion,
            unlockRank,
            unlockTier.ToString(CultureInfo.InvariantCulture),
            enableSearch.ToString(CultureInfo.InvariantCulture)
        });
    }

    internal static bool TryParse(string payload, string expectedModVersion, out ConfigSyncPayload parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(expectedModVersion))
            return false;

        var parts = payload.Split('|');
        if (parts.Length != 5
            || !string.Equals(parts[0], Schema, StringComparison.Ordinal)
            || !string.Equals(parts[1], expectedModVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(parts[2])
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var tier)
            || tier is < 1 or > 5
            || !bool.TryParse(parts[4], out var enableSearch))
            return false;

        parsed = new ConfigSyncPayload(parts[1], parts[2], tier, enableSearch);
        return true;
    }
}
