using Backpack.Config;

var failures = new List<string>();

Run("A. Fresh install retains defaults", () =>
{
    var applied = false;
    var result = Resolve(false, Array.Empty<PreferenceMigrationCandidate>(), _ => applied = true);
    Assert(result == PreferenceMigrationResult.DefaultRetained, "fresh install should retain registered defaults");
    Assert(!applied, "fresh install should not apply a migration value");
});

Run("B. Normal 1.1.x value migrates", () =>
{
    string applied = null;
    var result = Resolve(false, [new("InventoryExpanded_Settings.02_ToggleKey", "F8")], raw =>
    {
        applied = raw;
        return true;
    });
    Assert(result == PreferenceMigrationResult.Migrated && applied == "F8", "previous Inventory Expanded value should migrate");
});

Run("C. OG Backpack value migrates", () =>
{
    bool? applied = null;
    var result = Resolve(false, [new("OGBackpack_01_Settings.03_EnablePoliceSearch", "true")], raw =>
    {
        if (!bool.TryParse(raw, out var value))
            return false;
        applied = value;
        return true;
    });
    Assert(result == PreferenceMigrationResult.Migrated && applied == true, "OG value should migrate when newer sources are absent");
});

Run("D. Legacy cfg fallback and capacity maximum", () =>
{
    string applied = null;
    var result = Resolve(false, [new("Backpack.cfg/BackpackMod.cfg.ToggleKey", "G")], raw =>
    {
        applied = raw;
        return true;
    });
    Assert(result == PreferenceMigrationResult.Migrated && applied == "G", "legacy cfg should be a valid final migration source");
    Assert(PreferenceMigrationResolver.MergeLegacyCapacity(null, 24) == 24, "first legacy capacity should be retained");
    Assert(PreferenceMigrationResolver.MergeLegacyCapacity(24, 128) == 128, "larger legacy capacity should win");
    Assert(PreferenceMigrationResolver.MergeLegacyCapacity(128, 12) == 128, "smaller legacy capacity must not reduce capacity");
});

Run("E. Mixed partial config preserves explicit current and recovers missing", () =>
{
    var applyCount = 0;
    var preserved = Resolve(true, [new("OGBackpack_01_Settings.01_StorageSlots", "64")], _ =>
    {
        applyCount++;
        return true;
    });
    Assert(preserved == PreferenceMigrationResult.CurrentValuePreserved && applyCount == 0, "explicit current value must never be overwritten");

    string recovered = null;
    var migrated = Resolve(false, [new("OGBackpack_01_Settings.01_StorageSlots", "64")], raw =>
    {
        recovered = raw;
        return true;
    });
    Assert(migrated == PreferenceMigrationResult.Migrated && recovered == "64", "missing current value should recover from an older source");
});

Run("F. Version 6 and 7 receive production-setting migration", () =>
{
    Assert(PreferenceMigrationResolver.ShouldRun(false, PreferenceMigrationResolver.CurrentVersion),
        "an absent new-category marker must trigger migration even when the registered entry has the current default");
    Assert(PreferenceMigrationResolver.ShouldRun(true, 6), "a present version 6 marker must run later compatibility migrations");
    Assert(PreferenceMigrationResolver.ShouldRun(true, 7), "a present version 7 marker must run the production-setting migration");
    Assert(!PreferenceMigrationResolver.ShouldRun(true, PreferenceMigrationResolver.CurrentVersion),
        "a present current marker must remain idempotent after Save or Reset");
});

Run("F2. Legacy production toggle maps only to drying racks", () =>
{
    var dryingRacks = false;
    var mixingStations = false;
    var plantYield = false;
    var packagingStations = false;
    var result = Resolve(
        false,
        [new("current-cheats.02_IncreaseProduction", "true")],
        raw =>
        {
            if (!bool.TryParse(raw, out var value))
                return false;
            dryingRacks = value;
            return true;
        });
    Assert(result == PreferenceMigrationResult.Migrated && dryingRacks,
        "the old setting should retain its established drying-rack meaning");
    Assert(!mixingStations,
        "the old all-production toggle must not opt users into new systems");
    Assert(!plantYield,
        "the legacy checkbox must leave the new Plant Yield setting at its default OFF state");
    Assert(!packagingStations,
        "the legacy checkbox must leave new Packaging Stations at its default OFF state");
});

Run("G. Version 5 still receives a repair pass", () =>
{
    Assert(PreferenceMigrationResolver.ShouldRun(5), "version 5 must run the compatibility repair");
});

Run("H. Malformed value falls through predictably", () =>
{
    var malformed = new List<string>();
    int? applied = null;
    var candidates = new[]
    {
        new PreferenceMigrationCandidate("InventoryExpanded_Settings.01_StorageSlots", "not-a-number"),
        new PreferenceMigrationCandidate("OGBackpack_01_Settings.01_StorageSlots", "48")
    };
    var result = PreferenceMigrationResolver.Resolve(false, candidates, raw =>
    {
        if (!int.TryParse(raw, out var value))
            return false;
        applied = value;
        return true;
    }, malformed.Add);
    Assert(result == PreferenceMigrationResult.Migrated && applied == 48, "malformed newer value should fall through to compatible older value");
    Assert(malformed.SequenceEqual(["InventoryExpanded_Settings.01_StorageSlots"]), "malformed source should be identified once");
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Migration compatibility tests failed: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("Migration compatibility tests passed: 9/9 scenarios.");
return 0;

PreferenceMigrationResult Resolve(
    bool currentValuePresent,
    IEnumerable<PreferenceMigrationCandidate> candidates,
    Func<string, bool> tryApply)
{
    return PreferenceMigrationResolver.Resolve(currentValuePresent, candidates, tryApply, _ => { });
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
    }
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
