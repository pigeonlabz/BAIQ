using MelonLoader;
using MelonLoader.Preferences;
using System.Globalization;
using System.Text.RegularExpressions;
using Backpack.Cheats;
using Backpack.UI;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Levelling;
#elif MONO
using ScheduleOne.Levelling;
#endif

namespace Backpack.Config;

public enum ShiftClickDestinationPriorityOption
{
    InventoryFirst,
    BackpackFirst
}

public class Configuration
{
    private const int ConfigMigrationVersion = PreferenceMigrationResolver.CurrentVersion;
    private const float MinContextualPanelWidth = 380f;
    private const float MinContextualPanelHeight = 300f;
    private const float MaxSavedContextualPanelDimension = 8192f;
    private const string ModManagerCategoryPrefix = "Inventory Expanded - Backpack & Inventory QoL";
    private const string BackpackCategoryId = ModManagerCategoryPrefix + "_01_Backpack";
    private const string ContextualCategoryId = ModManagerCategoryPrefix + "_02_Contextual";
    private const string TransfersCategoryId = ModManagerCategoryPrefix + "_03_Transfers";
    private const string CheatsCategoryId = ModManagerCategoryPrefix + "_05_Cheats";
    private const string PreviousBackpackCategoryId = "InventoryExpanded_01_Backpack";
    private const string PreviousContextualCategoryId = "InventoryExpanded_02_Contextual";
    private const string PreviousTransfersCategoryId = "InventoryExpanded_03_Transfers";
    private const string PreviousPhoneCategoryId = "InventoryExpanded_04_Phone";
    private const string PreviousOrganizedCheatsCategoryId = "InventoryExpanded_05_Cheats";
    private const string PreviousSettingsCategoryId = "InventoryExpanded_Settings";
    private const string PreviousCheatsCategoryId = "InventoryExpanded_Cheats";
    private const string LegacySettingsCategoryId = "OGBackpack_01_Settings";
    private const string LegacyCheatsCategoryId = "OGBackpack_02_Cheats";

    private static Configuration _instance;
    public static Configuration Instance => _instance ??= new Configuration();

    private readonly string _legacyConfigFile = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Backpack.cfg");
    private readonly string _legacyBackpackModConfigFile = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BackpackMod.cfg");
    private readonly string _melonPreferencesFile = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");

    private readonly MelonPreferences_Category _backpackCategory;
    private readonly MelonPreferences_Category _contextualCategory;
    private readonly MelonPreferences_Category _transfersCategory;
    private readonly MelonPreferences_Category _cheatsCategory;
    private readonly MelonPreferences_Entry<KeyCode> _toggleKeyEntry;
    private readonly MelonPreferences_Entry<bool> _enableSearchEntry;
    private readonly MelonPreferences_Entry<ERank> _unlockRankEntry;
    private readonly MelonPreferences_Entry<int> _unlockTierEntry;
    private readonly MelonPreferences_Entry<int> _storageSlotsEntry;
    private readonly MelonPreferences_Entry<bool> _contextualBackpackEnabledEntry;
    private readonly MelonPreferences_Entry<bool> _autoOpenContextualBackpackEntry;
    private readonly MelonPreferences_Entry<bool> _rememberPanelPositionEntry;
    private readonly MelonPreferences_Entry<bool> _rememberPanelSizeEntry;
    private readonly MelonPreferences_Entry<float> _panelXEntry;
    private readonly MelonPreferences_Entry<float> _panelYEntry;
    private readonly MelonPreferences_Entry<float> _panelWidthEntry;
    private readonly MelonPreferences_Entry<float> _panelHeightEntry;
    private readonly MelonPreferences_Entry<bool> _resetPanelPositionEntry;
    private readonly MelonPreferences_Entry<bool> _showSortButtonsEntry;
    private readonly MelonPreferences_Entry<ShiftClickDestinationPriorityOption> _shiftClickDestinationPriorityEntry;
    private readonly MelonPreferences_Entry<KeyCode> _takeAllKeyEntry;
    private readonly MelonPreferences_Entry<KeyCode> _sortActiveContainerKeyEntry;
    private readonly MelonPreferences_Entry<bool> _diagnosticLoggingEntry;
    private readonly MelonPreferences_Entry<bool> _showBackpackWithPhoneEntry;
    private readonly MelonPreferences_Entry<int> _phoneBackpackDistanceEntry;
    private readonly MelonPreferences_Entry<int> _phoneBackpackUiScaleEntry;
    private readonly MelonPreferences_Entry<float> _phonePanelXEntry;
    private readonly MelonPreferences_Entry<float> _phonePanelYEntry;
    private readonly MelonPreferences_Entry<float> _phonePanelWidthEntry;
    private readonly MelonPreferences_Entry<float> _phonePanelHeightEntry;
    private readonly MelonPreferences_Entry<int> _migrationVersionEntry;
    private readonly MelonPreferences_Entry<StackSizeMultiplierOption> _stackSizeMultiplierEntry;
#if IL2CPP
    private readonly MelonPreferences_Entry<bool> _dryingRacksProductionEntry;
    private readonly MelonPreferences_Entry<bool> _mixingStationsProductionEntry;
    private readonly MelonPreferences_Entry<bool> _plantYieldProductionEntry;
    private readonly MelonPreferences_Entry<bool> _packagingStationsProductionEntry;
#endif

    public Configuration()
    {
        _backpackCategory = MelonPreferences.CreateCategory(BackpackCategoryId, "BACKPACK");
        _showBackpackWithPhoneEntry = _backpackCategory.CreateEntry(
            "16_ShowBackpackWithPhone",
            true,
            "Show Backpack with Phone",
            "Show an adaptive Backpack layout beside the normal phone without covering phone apps.");
        _phoneBackpackDistanceEntry = _backpackCategory.CreateEntry(
            "17_PhoneBackpackDistance",
            40,
            "Phone Backpack Distance",
            "Distance between the Phone and its portrait Backpack side panels. Use 0 for closest or 100 for region-centred spacing.",
            validator: new ValueRange<int>(0, 100));
        _phoneBackpackUiScaleEntry = _backpackCategory.CreateEntry(
            "18_PhoneBackpackUiScale",
            PhoneBackpackScalePolicy.DefaultPercent,
            "Phone Backpack UI Scale",
            "Scale the complete Phone Backpack presentation after automatic Interface Scale compensation.",
            validator: new ValueRange<int>(PhoneBackpackScalePolicy.MinimumPercent, PhoneBackpackScalePolicy.MaximumPercent));
        _storageSlotsEntry = _backpackCategory.CreateEntry(
            "01_StorageSlots",
            PlayerBackpack.MaxStorageSlots,
            "Backpack Capacity",
            "Number of inventory slots in the Backpack.",
            validator: new ValueRange<int>(1, PlayerBackpack.MaxStorageSlots));
        _toggleKeyEntry = _backpackCategory.CreateEntry(
            "02_ToggleKey",
            KeyCode.B,
            "Backpack Open Key",
            "Keyboard key used to open or close the Backpack.");
        _unlockRankEntry = _backpackCategory.CreateEntry(
            "04_UnlockRank",
            ERank.Street_Rat,
            "Required Rank",
            "Legacy Backpack unlock rank retained for compatibility.",
            is_hidden: true);
        _unlockTierEntry = _backpackCategory.CreateEntry(
            "05_UnlockTier",
            1,
            "Required Tier",
            "Legacy Backpack unlock tier retained for compatibility.",
            is_hidden: true,
            validator: new ValueRange<int>(1, 5));
        _enableSearchEntry = _backpackCategory.CreateEntry(
            "03_EnablePoliceSearch",
            false,
            "Include Backpack in Police Searches",
            "Allow police body searches to find items stored in the Backpack.");
        _contextualCategory = MelonPreferences.CreateCategory(ContextualCategoryId, "CONTEXTUAL BACKPACK");
        _contextualBackpackEnabledEntry = _contextualCategory.CreateEntry(
            "06_ContextualBackpackEnabled",
            true,
            "Show Contextual Backpack",
            "Show the Backpack alongside compatible containers and machines.");
        _autoOpenContextualBackpackEntry = _contextualCategory.CreateEntry(
            "07_AutoOpenWithContainersAndMachines",
            true,
            "Open Contextual Backpack Automatically",
            "Open the contextual Backpack whenever a compatible inventory is opened.");
        _rememberPanelPositionEntry = _contextualCategory.CreateEntry(
            "08_RememberPanelPosition",
            true,
            "Remember Panel Position",
            "Remember where the contextual Backpack was placed.");
        _rememberPanelSizeEntry = _contextualCategory.CreateEntry(
            "09_RememberPanelSize",
            true,
            "Remember Panel Size",
            "Remember the size of the contextual Backpack.");
        _resetPanelPositionEntry = _contextualCategory.CreateEntry(
            "11_ResetContextualPanelPosition",
            false,
            "Reset Panel Layout",
            "Reset the contextual Backpack to its default position and size. This turns itself off after applying.");
        _transfersCategory = MelonPreferences.CreateCategory(TransfersCategoryId, "INVENTORY & TRANSFERS");
        _showSortButtonsEntry = _transfersCategory.CreateEntry(
            "12_ShowSortButtons",
            true,
            "Show Sort Buttons",
            "Show the Sort buttons added by Inventory Expanded.");
        _shiftClickDestinationPriorityEntry = _transfersCategory.CreateEntry(
            "13_ShiftClickDestinationPriority",
            ShiftClickDestinationPriorityOption.BackpackFirst,
            "Shift-Click Destination Priority",
            "Choose whether Inventory or Backpack is tried first when both are valid shift-click destinations.");
        _takeAllKeyEntry = _transfersCategory.CreateEntry(
            "14_TakeAllKey",
            KeyCode.R,
            "Take All Key",
            "Transfer valid items from an open simple storage container. Set to None to unbind.");
        _sortActiveContainerKeyEntry = _transfersCategory.CreateEntry(
            "15_SortActiveContainerKey",
            KeyCode.Backslash,
            "Sort Active Container Key",
            "Sort the supported container currently open. Set to None to unbind.");
        _diagnosticLoggingEntry = _transfersCategory.CreateEntry(
            "19_DiagnosticLogging",
            false,
            "Diagnostic Logging",
            "Writes detailed Inventory Expanded diagnostics to the MelonLoader log. Enable when troubleshooting or reporting a bug.");
        _panelXEntry = _contextualCategory.CreateEntry("90_ContextualPanelX", 560f, "Contextual panel X", "Saved contextual backpack panel X position.", is_hidden: true);
        _panelYEntry = _contextualCategory.CreateEntry("91_ContextualPanelY", 0f, "Contextual panel Y", "Saved contextual backpack panel Y position.", is_hidden: true);
        _panelWidthEntry = _contextualCategory.CreateEntry("92_ContextualPanelWidth", 640f, "Contextual panel width", "Saved contextual backpack panel width.", is_hidden: true, validator: new ValueRange<float>(MinContextualPanelWidth, MaxSavedContextualPanelDimension));
        _panelHeightEntry = _contextualCategory.CreateEntry("93_ContextualPanelHeight", 520f, "Contextual panel height", "Saved contextual backpack panel height.", is_hidden: true, validator: new ValueRange<float>(MinContextualPanelHeight, MaxSavedContextualPanelDimension));
        _phonePanelXEntry = _backpackCategory.CreateEntry("94_PhonePanelX", 560f, "Phone Backpack panel X", "Saved phone-context Backpack X position.", is_hidden: true);
        _phonePanelYEntry = _backpackCategory.CreateEntry("95_PhonePanelY", 0f, "Phone Backpack panel Y", "Saved phone-context Backpack Y position.", is_hidden: true);
        _phonePanelWidthEntry = _backpackCategory.CreateEntry("96_PhonePanelWidth", 640f, "Phone Backpack panel width", "Saved phone-context Backpack width.", is_hidden: true, validator: new ValueRange<float>(MinContextualPanelWidth, MaxSavedContextualPanelDimension));
        _phonePanelHeightEntry = _backpackCategory.CreateEntry("97_PhonePanelHeight", 520f, "Phone Backpack panel height", "Saved phone-context Backpack height.", is_hidden: true, validator: new ValueRange<float>(MinContextualPanelHeight, MaxSavedContextualPanelDimension));
        _migrationVersionEntry = _backpackCategory.CreateEntry(
            "99_ConfigMigrationVersion",
            ConfigMigrationVersion,
            "Config migration version",
            "Internal marker used to avoid importing legacy config files more than once.",
            is_hidden: true);

        _cheatsCategory = MelonPreferences.CreateCategory(CheatsCategoryId, "CHEATS");
        _stackSizeMultiplierEntry = _cheatsCategory.CreateEntry(
            "01_StackSizeMultiplier",
            StackSizeMultiplierOption.Vanilla,
            "Stack Size Multiplier",
            "Multiplies native stack limits for already-stackable items. Non-vanilla values suppress achievements while active.");
#if IL2CPP
        _dryingRacksProductionEntry = _cheatsCategory.CreateEntry(
            "02_DryingRacks",
            false,
            "Drying Racks",
            "Use the selected Stack Size multiplier for new drying-rack operations. Requires a non-Vanilla multiplier; existing operations are preserved.");
        _mixingStationsProductionEntry = _cheatsCategory.CreateEntry(
            "03_MixingStations",
            false,
            "Mixing Stations",
            "Use the selected Stack Size multiplier for valid mixing batches. Ingredient ratios remain 1:1, and larger batches use the native-capacity processing timeframe.");
        _plantYieldProductionEntry = _cheatsCategory.CreateEntry(
            "04_PlantYield",
            false,
            "Plant Yield",
            "Uses the selected Stack Size multiplier for player-harvested plants. Harvests require enough inventory space for the full yield.");
        _packagingStationsProductionEntry = _cheatsCategory.CreateEntry(
            "05_PackagingStations",
            false,
            "Packaging Stations",
            "Deferred from 1.3.0. This retained preference is hidden and has no effect until Packaging Stations is explicitly released.",
            is_hidden: true);
#endif

        Logger.Info(
            "[Config] Registered Mod Manager-compatible MelonPreferences categories " +
            $"for '{ModManagerCategoryPrefix}'; InventoryExpanded_* categories remain migration inputs.");

        _toggleKeyEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Backpack Open Key changed"));
        _enableSearchEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Police Search changed"));
        _unlockRankEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Required Rank changed"));
        _unlockTierEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Required Tier changed"));
        _storageSlotsEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Backpack Capacity changed"));
        _contextualBackpackEnabledEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Contextual Backpack changed"));
        _autoOpenContextualBackpackEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Contextual auto-open changed"));
        _rememberPanelPositionEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Remember position changed"));
        _rememberPanelSizeEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Remember size changed"));
        _showSortButtonsEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Show Sort Buttons changed"));
        _shiftClickDestinationPriorityEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Shift-click priority changed"));
        _takeAllKeyEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Take All Key changed"));
        _sortActiveContainerKeyEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Sort Active Container Key changed"));
        _diagnosticLoggingEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Diagnostic Logging changed"));
        _showBackpackWithPhoneEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Phone Backpack preference changed"));
        _phoneBackpackDistanceEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Phone Backpack distance changed"));
        _phoneBackpackUiScaleEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Phone Backpack UI scale changed"));
        _stackSizeMultiplierEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Stack Size Multiplier changed"));
#if IL2CPP
        _dryingRacksProductionEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Drying Racks production setting changed"));
        _mixingStationsProductionEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Mixing Stations production setting changed"));
        _plantYieldProductionEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyLivePreferences("Plant Yield production setting changed"));
#endif
        _resetPanelPositionEntry.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (!value)
                return;

            ResetContextualPanelGeometry();
            _resetPanelPositionEntry.Value = false;
            ApplyLivePreferences("Panel layout reset");
        });
    }

    public KeyCode ToggleKey { get; set; }
    public bool EnableSearch { get; set; }
    public FullRank UnlockLevel { get; internal set; }
    public int StorageSlots { get; internal set; }
    public bool ContextualBackpackEnabled { get; private set; }
    public bool AutoOpenContextualBackpack { get; private set; }
    public bool RememberPanelPosition { get; private set; }
    public bool RememberPanelSize { get; private set; }
    public int PreferredColumns => 0;
    public bool ShowSortButtons { get; private set; }
    public ShiftClickDestinationPriorityOption ShiftClickDestinationPriority { get; private set; }
    public KeyCode TakeAllKey { get; private set; }
    public KeyCode SortActiveContainerKey { get; private set; }
    public bool DiagnosticLogging { get; private set; }
    public bool ShowBackpackWithPhone { get; private set; }
    public int PhoneBackpackDistance { get; private set; }
    public int PhoneBackpackUiScale { get; private set; }
    public Vector2 ContextualPanelPosition { get; private set; }
    public Vector2 ContextualPanelSize { get; private set; }
    public Vector2 PhonePanelPosition { get; private set; }
    public Vector2 PhonePanelSize { get; private set; }

    public void Load()
    {
        MelonPreferences.Load();

        if (PreferenceMigrationResolver.ShouldRun(CurrentMigrationMarkerExists(), _migrationVersionEntry.Value))
        {
            MigrateCompatibleConfiguration();
            _migrationVersionEntry.Value = ConfigMigrationVersion;
            MelonPreferences.Save();
        }

        Reset();
    }

    private bool CurrentMigrationMarkerExists()
    {
        var sections = ReadPreferenceSections(_melonPreferencesFile);
        return sections.TryGetValue(BackpackCategoryId, out var backpack)
               && TryGetValue(backpack, "99_ConfigMigrationVersion", out _);
    }

    private void MigrateCompatibleConfiguration()
    {
        var sections = ReadPreferenceSections(_melonPreferencesFile);
        var legacy = new LegacyConfigValues();
        legacy.Merge(ReadBackpackCfg(_legacyConfigFile));
        legacy.Merge(ReadBackpackModCfg(_legacyBackpackModConfigFile));

        var migrated = 0;
        var malformed = 0;

        List<PreferenceMigrationCandidate> Sources(string key, params string[] categoryIds)
        {
            var candidates = new List<PreferenceMigrationCandidate>();
            foreach (var categoryId in categoryIds)
            {
                if (sections.TryGetValue(categoryId, out var section) && TryGetValue(section, key, out var rawValue))
                    candidates.Add(new PreferenceMigrationCandidate($"{categoryId}.{key}", rawValue));
            }

            return candidates;
        }

        void AddLegacyCandidate<T>(List<PreferenceMigrationCandidate> candidates, string source, T? value) where T : struct
        {
            if (value.HasValue)
                candidates.Add(new PreferenceMigrationCandidate(source, Convert.ToString(value.Value, CultureInfo.InvariantCulture)));
        }

        void Migrate(
            string targetCategory,
            string key,
            List<PreferenceMigrationCandidate> candidates,
            Func<string, bool> tryApply)
        {
            var currentValuePresent = sections.TryGetValue(targetCategory, out var target)
                                      && TryGetValue(target, key, out _);
            var result = PreferenceMigrationResolver.Resolve(
                currentValuePresent,
                candidates,
                tryApply,
                source =>
                {
                    malformed++;
                    Logger.Warning($"[Config] Ignored malformed saved value from {source}; trying the next compatible source for {targetCategory}.{key}.");
                });

            if (result == PreferenceMigrationResult.Migrated)
                migrated++;
        }

        bool ParseBool(string raw, Action<bool> apply)
        {
            if (!bool.TryParse(Unquote(raw), out var value))
                return false;
            apply(value);
            return true;
        }

        bool ParseInt(string raw, Action<int> apply)
        {
            if (!int.TryParse(Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return false;
            apply(value);
            return true;
        }

        bool ParseFloat(string raw, Action<float> apply)
        {
            if (!float.TryParse(Unquote(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return false;
            apply(value);
            return true;
        }

        var storageSources = Sources("01_StorageSlots", PreviousBackpackCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId);
        AddLegacyCandidate(storageSources, "Backpack.cfg/BackpackMod.cfg.StorageSlots", legacy.StorageSlots);
        Migrate(BackpackCategoryId, "01_StorageSlots", storageSources,
            raw => ParseInt(raw, value => _storageSlotsEntry.Value = Math.Clamp(value, 1, PlayerBackpack.MaxStorageSlots)));

        var toggleSources = Sources("02_ToggleKey", PreviousBackpackCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId);
        AddLegacyCandidate(toggleSources, "Backpack.cfg/BackpackMod.cfg.ToggleKey", legacy.ToggleKey);
        Migrate(BackpackCategoryId, "02_ToggleKey", toggleSources, raw =>
        {
            if (!TryParseKeyCode(raw, out var value))
                return false;
            _toggleKeyEntry.Value = value;
            return true;
        });

        var policeSources = Sources("03_EnablePoliceSearch", PreviousBackpackCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId);
        AddLegacyCandidate(policeSources, "Backpack.cfg/BackpackMod.cfg.EnablePoliceSearch", legacy.EnableSearch);
        Migrate(BackpackCategoryId, "03_EnablePoliceSearch", policeSources,
            raw => ParseBool(raw, value => _enableSearchEntry.Value = value));

        var rankSources = Sources("04_UnlockRank", PreviousBackpackCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId);
        AddLegacyCandidate(rankSources, "Backpack.cfg/BackpackMod.cfg.UnlockRank", legacy.UnlockRank);
        Migrate(BackpackCategoryId, "04_UnlockRank", rankSources, raw =>
        {
            if (!Enum.TryParse(Unquote(raw), true, out ERank value))
                return false;
            _unlockRankEntry.Value = value;
            return true;
        });

        var tierSources = Sources("05_UnlockTier", PreviousBackpackCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId);
        AddLegacyCandidate(tierSources, "Backpack.cfg/BackpackMod.cfg.UnlockTier", legacy.UnlockTier);
        Migrate(BackpackCategoryId, "05_UnlockTier", tierSources,
            raw => ParseInt(raw, value => _unlockTierEntry.Value = Math.Clamp(value, 1, 5)));

        Migrate(ContextualCategoryId, "06_ContextualBackpackEnabled", Sources("06_ContextualBackpackEnabled", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseBool(raw, value => _contextualBackpackEnabledEntry.Value = value));
        Migrate(ContextualCategoryId, "07_AutoOpenWithContainersAndMachines", Sources("07_AutoOpenWithContainersAndMachines", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseBool(raw, value => _autoOpenContextualBackpackEntry.Value = value));
        Migrate(ContextualCategoryId, "08_RememberPanelPosition", Sources("08_RememberPanelPosition", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseBool(raw, value => _rememberPanelPositionEntry.Value = value));
        Migrate(ContextualCategoryId, "09_RememberPanelSize", Sources("09_RememberPanelSize", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseBool(raw, value => _rememberPanelSizeEntry.Value = value));
        Migrate(ContextualCategoryId, "11_ResetContextualPanelPosition", Sources("11_ResetContextualPanelPosition", PreviousContextualCategoryId, PreviousSettingsCategoryId),
            raw => ParseBool(raw, value => _resetPanelPositionEntry.Value = value));

        Migrate(TransfersCategoryId, "12_ShowSortButtons", Sources("12_ShowSortButtons", PreviousTransfersCategoryId, PreviousSettingsCategoryId),
            raw => ParseBool(raw, value => _showSortButtonsEntry.Value = value));
        Migrate(TransfersCategoryId, "13_ShiftClickDestinationPriority", Sources("13_ShiftClickDestinationPriority", PreviousTransfersCategoryId, PreviousSettingsCategoryId), raw =>
        {
            if (!Enum.TryParse(Unquote(raw), true, out ShiftClickDestinationPriorityOption value)
                || !Enum.IsDefined(typeof(ShiftClickDestinationPriorityOption), value))
            {
                return false;
            }

            _shiftClickDestinationPriorityEntry.Value = value;
            return true;
        });
        Migrate(TransfersCategoryId, "14_TakeAllKey", Sources("14_TakeAllKey", PreviousTransfersCategoryId, PreviousSettingsCategoryId), raw =>
        {
            if (!TryParseKeyCode(raw, out var value))
                return false;
            _takeAllKeyEntry.Value = value;
            return true;
        });
        Migrate(TransfersCategoryId, "15_SortActiveContainerKey", Sources("15_SortActiveContainerKey", PreviousTransfersCategoryId, PreviousSettingsCategoryId), raw =>
        {
            if (!TryParseKeyCode(raw, out var value))
                return false;
            _sortActiveContainerKeyEntry.Value = value;
            return true;
        });

        Migrate(TransfersCategoryId, "19_DiagnosticLogging", Sources("19_DiagnosticLogging", PreviousTransfersCategoryId, PreviousSettingsCategoryId),
            raw => ParseBool(raw, value => _diagnosticLoggingEntry.Value = value));

        Migrate(BackpackCategoryId, "16_ShowBackpackWithPhone", Sources("16_ShowBackpackWithPhone", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseBool(raw, value => _showBackpackWithPhoneEntry.Value = value));
        Migrate(BackpackCategoryId, "17_PhoneBackpackDistance", Sources("17_PhoneBackpackDistance", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseInt(raw, value => _phoneBackpackDistanceEntry.Value = Math.Clamp(value, 0, 100)));
        Migrate(BackpackCategoryId, "18_PhoneBackpackUiScale", Sources("18_PhoneBackpackUiScale", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseInt(raw, value => _phoneBackpackUiScaleEntry.Value = PhoneBackpackScalePolicy.ClampPercent(value)));

        Migrate(ContextualCategoryId, "90_ContextualPanelX", Sources("90_ContextualPanelX", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseFloat(raw, value => _panelXEntry.Value = value));
        Migrate(ContextualCategoryId, "91_ContextualPanelY", Sources("91_ContextualPanelY", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseFloat(raw, value => _panelYEntry.Value = value));
        Migrate(ContextualCategoryId, "92_ContextualPanelWidth", Sources("92_ContextualPanelWidth", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseFloat(raw, value => _panelWidthEntry.Value = Mathf.Clamp(value, MinContextualPanelWidth, MaxSavedContextualPanelDimension)));
        Migrate(ContextualCategoryId, "93_ContextualPanelHeight", Sources("93_ContextualPanelHeight", PreviousContextualCategoryId, PreviousSettingsCategoryId, LegacySettingsCategoryId),
            raw => ParseFloat(raw, value => _panelHeightEntry.Value = Mathf.Clamp(value, MinContextualPanelHeight, MaxSavedContextualPanelDimension)));

        Migrate(BackpackCategoryId, "94_PhonePanelX", Sources("94_PhonePanelX", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseFloat(raw, value => _phonePanelXEntry.Value = value));
        Migrate(BackpackCategoryId, "95_PhonePanelY", Sources("95_PhonePanelY", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseFloat(raw, value => _phonePanelYEntry.Value = value));
        Migrate(BackpackCategoryId, "96_PhonePanelWidth", Sources("96_PhonePanelWidth", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseFloat(raw, value => _phonePanelWidthEntry.Value = Mathf.Clamp(value, MinContextualPanelWidth, MaxSavedContextualPanelDimension)));
        Migrate(BackpackCategoryId, "97_PhonePanelHeight", Sources("97_PhonePanelHeight", PreviousBackpackCategoryId, PreviousPhoneCategoryId, PreviousSettingsCategoryId),
            raw => ParseFloat(raw, value => _phonePanelHeightEntry.Value = Mathf.Clamp(value, MinContextualPanelHeight, MaxSavedContextualPanelDimension)));

        Migrate(CheatsCategoryId, "01_StackSizeMultiplier", Sources("01_StackSizeMultiplier", PreviousOrganizedCheatsCategoryId, PreviousCheatsCategoryId, LegacyCheatsCategoryId), raw =>
        {
            if (!Enum.TryParse(Unquote(raw), true, out StackSizeMultiplierOption value)
                || !Enum.IsDefined(typeof(StackSizeMultiplierOption), value))
            {
                return false;
            }

            _stackSizeMultiplierEntry.Value = value;
            return true;
        });

#if IL2CPP
        Migrate(CheatsCategoryId, "02_DryingRacks", Sources("02_IncreaseProduction", CheatsCategoryId, PreviousOrganizedCheatsCategoryId, PreviousCheatsCategoryId, LegacyCheatsCategoryId),
            raw => ParseBool(raw, value => _dryingRacksProductionEntry.Value = value));
        Migrate(CheatsCategoryId, "03_MixingStations", Sources("03_MixingStations", PreviousOrganizedCheatsCategoryId, PreviousCheatsCategoryId),
            raw => ParseBool(raw, value => _mixingStationsProductionEntry.Value = value));
#endif

        if (migrated > 0)
            Logger.Info($"[Config] Recovered {migrated} missing saved setting(s) using per-setting compatibility precedence; current values were left untouched.");
        if (malformed > 0)
            Logger.Warning($"[Config] Ignored {malformed} malformed legacy preference value(s) during one-time compatibility migration.");
    }

    public void Reset()
    {
        ToggleKey = _toggleKeyEntry.Value;
        EnableSearch = _enableSearchEntry.Value;
        UnlockLevel = new FullRank(_unlockRankEntry.Value, Math.Clamp(_unlockTierEntry.Value, 1, 5));
        StorageSlots = Math.Clamp(_storageSlotsEntry.Value, 1, PlayerBackpack.MaxStorageSlots);
        ContextualBackpackEnabled = _contextualBackpackEnabledEntry.Value;
        AutoOpenContextualBackpack = _autoOpenContextualBackpackEntry.Value;
        RememberPanelPosition = _rememberPanelPositionEntry.Value;
        RememberPanelSize = _rememberPanelSizeEntry.Value;
        ShowSortButtons = _showSortButtonsEntry.Value;
        ShiftClickDestinationPriority = Enum.IsDefined(typeof(ShiftClickDestinationPriorityOption), _shiftClickDestinationPriorityEntry.Value)
            ? _shiftClickDestinationPriorityEntry.Value
            : ShiftClickDestinationPriorityOption.BackpackFirst;
        TakeAllKey = _takeAllKeyEntry.Value;
        SortActiveContainerKey = _sortActiveContainerKeyEntry.Value;
        DiagnosticLogging = _diagnosticLoggingEntry.Value;
        Logger.SetDiagnosticLogging(DiagnosticLogging);
        ShowBackpackWithPhone = _showBackpackWithPhoneEntry.Value;
        PhoneBackpackDistance = Math.Clamp(_phoneBackpackDistanceEntry.Value, 0, 100);
        PhoneBackpackUiScale = PhoneBackpackScalePolicy.ClampPercent(_phoneBackpackUiScaleEntry.Value);
        ContextualPanelPosition = new Vector2(_panelXEntry.Value, _panelYEntry.Value);
        ContextualPanelSize = new Vector2(
            Math.Clamp(_panelWidthEntry.Value, MinContextualPanelWidth, MaxSavedContextualPanelDimension),
            Math.Clamp(_panelHeightEntry.Value, MinContextualPanelHeight, MaxSavedContextualPanelDimension));
        PhonePanelPosition = new Vector2(_phonePanelXEntry.Value, _phonePanelYEntry.Value);
        PhonePanelSize = new Vector2(
            Math.Clamp(_phonePanelWidthEntry.Value, MinContextualPanelWidth, MaxSavedContextualPanelDimension),
            Math.Clamp(_phonePanelHeightEntry.Value, MinContextualPanelHeight, MaxSavedContextualPanelDimension));
#if IL2CPP
        CheatSettings.Apply(
            _stackSizeMultiplierEntry.Value,
            _dryingRacksProductionEntry.Value,
            _mixingStationsProductionEntry.Value,
            _plantYieldProductionEntry.Value,
            packagingStationsEnabled: false);
#else
        CheatSettings.Apply(_stackSizeMultiplierEntry.Value);
#endif
    }

    public void Save()
    {
        _toggleKeyEntry.Value = ToggleKey;
        _enableSearchEntry.Value = EnableSearch;
        _unlockRankEntry.Value = UnlockLevel.Rank;
        _unlockTierEntry.Value = Math.Clamp(UnlockLevel.Tier, 1, 5);
        _storageSlotsEntry.Value = Math.Clamp(StorageSlots, 1, PlayerBackpack.MaxStorageSlots);
        _contextualBackpackEnabledEntry.Value = ContextualBackpackEnabled;
        _autoOpenContextualBackpackEntry.Value = AutoOpenContextualBackpack;
        _rememberPanelPositionEntry.Value = RememberPanelPosition;
        _rememberPanelSizeEntry.Value = RememberPanelSize;
        _showSortButtonsEntry.Value = ShowSortButtons;
        _shiftClickDestinationPriorityEntry.Value = ShiftClickDestinationPriority;
        _takeAllKeyEntry.Value = TakeAllKey;
        _sortActiveContainerKeyEntry.Value = SortActiveContainerKey;
        _diagnosticLoggingEntry.Value = DiagnosticLogging;
        _showBackpackWithPhoneEntry.Value = ShowBackpackWithPhone;
        _phoneBackpackDistanceEntry.Value = Math.Clamp(PhoneBackpackDistance, 0, 100);
        _phoneBackpackUiScaleEntry.Value = PhoneBackpackScalePolicy.ClampPercent(PhoneBackpackUiScale);
        _stackSizeMultiplierEntry.Value = CheatSettings.StackSizeMultiplier;
#if IL2CPP
        _dryingRacksProductionEntry.Value = ProductionCheats.DryingRacksEnabled;
        _mixingStationsProductionEntry.Value = ProductionCheats.MixingStationsEnabled;
        _plantYieldProductionEntry.Value = ProductionCheats.PlantYieldEnabled;
#endif
        MelonPreferences.Save();
    }

    public void SetContextualPanelGeometry(Vector2 position, Vector2 size)
    {
        if (RememberPanelPosition)
        {
            _panelXEntry.Value = position.x;
            _panelYEntry.Value = position.y;
        }

        if (RememberPanelSize)
        {
            _panelWidthEntry.Value = Mathf.Clamp(size.x, MinContextualPanelWidth, MaxSavedContextualPanelDimension);
            _panelHeightEntry.Value = Mathf.Clamp(size.y, MinContextualPanelHeight, MaxSavedContextualPanelDimension);
        }

        Reset();
        MelonPreferences.Save();
    }

    public void ResetContextualPanelGeometry()
    {
        _panelXEntry.Value = 560f;
        _panelYEntry.Value = 0f;
        _panelWidthEntry.Value = 640f;
        _panelHeightEntry.Value = 520f;
        _phonePanelXEntry.Value = 560f;
        _phonePanelYEntry.Value = 0f;
        _phonePanelWidthEntry.Value = 640f;
        _phonePanelHeightEntry.Value = 520f;
        Reset();
        MelonPreferences.Save();
    }

    public void SetPhonePanelGeometry(Vector2 position, Vector2 size)
    {
        if (RememberPanelPosition)
        {
            _phonePanelXEntry.Value = position.x;
            _phonePanelYEntry.Value = position.y;
        }

        if (RememberPanelSize)
        {
            _phonePanelWidthEntry.Value = Mathf.Clamp(size.x, MinContextualPanelWidth, MaxSavedContextualPanelDimension);
            _phonePanelHeightEntry.Value = Mathf.Clamp(size.y, MinContextualPanelHeight, MaxSavedContextualPanelDimension);
        }

        Reset();
        MelonPreferences.Save();
    }

    public void SynchronizeLivePreferences(string reason)
    {
        ApplyLivePreferences(reason);
    }

    private void ApplyLivePreferences(string reason)
    {
        Reset();
        PlayerBackpack.Instance?.ApplyConfiguredStorageSlots();
        ResponsiveBackpackUI.InvalidateLayout();
        ContextualBackpackPanel.InvalidateLayout();
        PhoneBackpackUI.InvalidateLayout(reason);
        StorageSortButton.RefreshVisibility();
        Logger.Diagnostic(
            $"[Config] Applied ({reason}); key={ToggleKey}, capacity={StorageSlots}, " +
            $"contextual={ContextualBackpackEnabled}/{AutoOpenContextualBackpack}, " +
            $"showSort={ShowSortButtons}, phoneRequested={ShowBackpackWithPhone}, " +
            $"phoneDistance={PhoneBackpackDistance}, phoneScale={PhoneBackpackUiScale}%, " +
            $"diagnosticLogging={DiagnosticLogging}.");
    }

    private bool CurrentNativeConfigExists()
    {
        if (!File.Exists(_melonPreferencesFile))
            return false;

        var contents = File.ReadAllText(_melonPreferencesFile);
        return HasCategory(contents, BackpackCategoryId)
               || HasCategory(contents, ContextualCategoryId)
               || HasCategory(contents, TransfersCategoryId)
               || HasCategory(contents, PreviousPhoneCategoryId)
               || HasCategory(contents, CheatsCategoryId)
               || HasCategory(contents, PreviousSettingsCategoryId)
               || HasCategory(contents, PreviousCheatsCategoryId);
    }

    private bool LegacyMelonPreferencesExist()
    {
        if (!File.Exists(_melonPreferencesFile))
            return false;

        var contents = File.ReadAllText(_melonPreferencesFile);
        return HasCategory(contents, LegacySettingsCategoryId) || HasCategory(contents, LegacyCheatsCategoryId);
    }

    private static bool HasCategory(string contents, string categoryId)
    {
        return contents.Contains($"[{categoryId}]", StringComparison.OrdinalIgnoreCase)
               || contents.Contains($"[\"{categoryId}\"]", StringComparison.OrdinalIgnoreCase);
    }

    private void MigratePhonePreferencesToBackpack()
    {
        var sections = ReadPreferenceSections(_melonPreferencesFile);
        if (!sections.TryGetValue(PreviousPhoneCategoryId, out var phone))
            return;

        var migrated = 0;
        void Copy(string key, Func<string, bool> apply)
        {
            if (!TryGetValue(phone, key, out var rawValue) ||
                (sections.TryGetValue(BackpackCategoryId, out var backpack) && TryGetValue(backpack, key, out _)))
            {
                return;
            }

            if (apply(rawValue))
                migrated++;
        }

        Copy("16_ShowBackpackWithPhone", raw =>
        {
            if (!bool.TryParse(Unquote(raw), out var value))
                return false;
            _showBackpackWithPhoneEntry.Value = value;
            return true;
        });
        Copy("17_PhoneBackpackDistance", raw =>
        {
            if (!int.TryParse(Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return false;
            _phoneBackpackDistanceEntry.Value = Math.Clamp(value, 0, 100);
            return true;
        });
        Copy("94_PhonePanelX", raw => TryCopyFloat(raw, value => _phonePanelXEntry.Value = value));
        Copy("95_PhonePanelY", raw => TryCopyFloat(raw, value => _phonePanelYEntry.Value = value));
        Copy("96_PhonePanelWidth", raw => TryCopyFloat(raw, value => _phonePanelWidthEntry.Value = Mathf.Clamp(value, MinContextualPanelWidth, MaxSavedContextualPanelDimension)));
        Copy("97_PhonePanelHeight", raw => TryCopyFloat(raw, value => _phonePanelHeightEntry.Value = Mathf.Clamp(value, MinContextualPanelHeight, MaxSavedContextualPanelDimension)));

        if (migrated > 0)
            Logger.Info($"Migrated {migrated} saved Phone Backpack values into the Backpack settings section.");
    }

    private static bool TryCopyFloat(string raw, Action<float> apply)
    {
        if (!float.TryParse(Unquote(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        apply(value);
        return true;
    }

    private void MigrateOrganizedPreferenceCategories()
    {
        var sections = ReadPreferenceSections(_melonPreferencesFile);
        var migrated = 0;

        void Copy(
            Dictionary<string, string> source,
            string targetCategory,
            string key,
            Func<string, bool> apply)
        {
            if (source == null || !TryGetValue(source, key, out var rawValue) ||
                (sections.TryGetValue(targetCategory, out var target) && TryGetValue(target, key, out _)))
                return;

            if (apply(rawValue))
                migrated++;
        }

        bool ParseBool(string raw, Action<bool> apply)
        {
            if (!bool.TryParse(Unquote(raw), out var value))
                return false;
            apply(value);
            return true;
        }

        bool ParseInt(string raw, Action<int> apply)
        {
            if (!int.TryParse(Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return false;
            apply(value);
            return true;
        }

        bool ParseFloat(string raw, Action<float> apply)
        {
            if (!float.TryParse(Unquote(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return false;
            apply(value);
            return true;
        }

        if (sections.TryGetValue(PreviousSettingsCategoryId, out var settings))
        {
            Copy(settings, BackpackCategoryId, "01_StorageSlots", raw => ParseInt(raw, value => _storageSlotsEntry.Value = Math.Clamp(value, 1, PlayerBackpack.MaxStorageSlots)));
            Copy(settings, BackpackCategoryId, "02_ToggleKey", raw =>
            {
                if (!TryParseKeyCode(raw, out var value))
                    return false;
                _toggleKeyEntry.Value = value;
                return true;
            });
            Copy(settings, BackpackCategoryId, "03_EnablePoliceSearch", raw => ParseBool(raw, value => _enableSearchEntry.Value = value));
            Copy(settings, BackpackCategoryId, "04_UnlockRank", raw =>
            {
                if (!Enum.TryParse(Unquote(raw), true, out ERank value))
                    return false;
                _unlockRankEntry.Value = value;
                return true;
            });
            Copy(settings, BackpackCategoryId, "05_UnlockTier", raw => ParseInt(raw, value => _unlockTierEntry.Value = Math.Clamp(value, 1, 5)));

            Copy(settings, ContextualCategoryId, "06_ContextualBackpackEnabled", raw => ParseBool(raw, value => _contextualBackpackEnabledEntry.Value = value));
            Copy(settings, ContextualCategoryId, "07_AutoOpenWithContainersAndMachines", raw => ParseBool(raw, value => _autoOpenContextualBackpackEntry.Value = value));
            Copy(settings, ContextualCategoryId, "08_RememberPanelPosition", raw => ParseBool(raw, value => _rememberPanelPositionEntry.Value = value));
            Copy(settings, ContextualCategoryId, "09_RememberPanelSize", raw => ParseBool(raw, value => _rememberPanelSizeEntry.Value = value));
            Copy(settings, ContextualCategoryId, "11_ResetContextualPanelPosition", raw => ParseBool(raw, value => _resetPanelPositionEntry.Value = value));
            Copy(settings, ContextualCategoryId, "90_ContextualPanelX", raw => ParseFloat(raw, value => _panelXEntry.Value = value));
            Copy(settings, ContextualCategoryId, "91_ContextualPanelY", raw => ParseFloat(raw, value => _panelYEntry.Value = value));
            Copy(settings, ContextualCategoryId, "92_ContextualPanelWidth", raw => ParseFloat(raw, value => _panelWidthEntry.Value = Mathf.Clamp(value, MinContextualPanelWidth, MaxSavedContextualPanelDimension)));
            Copy(settings, ContextualCategoryId, "93_ContextualPanelHeight", raw => ParseFloat(raw, value => _panelHeightEntry.Value = Mathf.Clamp(value, MinContextualPanelHeight, MaxSavedContextualPanelDimension)));

            Copy(settings, TransfersCategoryId, "12_ShowSortButtons", raw => ParseBool(raw, value => _showSortButtonsEntry.Value = value));
            Copy(settings, TransfersCategoryId, "13_ShiftClickDestinationPriority", raw =>
            {
                if (!Enum.TryParse(Unquote(raw), true, out ShiftClickDestinationPriorityOption value))
                    return false;
                _shiftClickDestinationPriorityEntry.Value = value;
                return true;
            });
            Copy(settings, TransfersCategoryId, "14_TakeAllKey", raw =>
            {
                if (!TryParseKeyCode(raw, out var value))
                    return false;
                _takeAllKeyEntry.Value = value;
                return true;
            });
            Copy(settings, TransfersCategoryId, "15_SortActiveContainerKey", raw =>
            {
                if (!TryParseKeyCode(raw, out var value))
                    return false;
                _sortActiveContainerKeyEntry.Value = value;
                return true;
            });

            Copy(settings, BackpackCategoryId, "16_ShowBackpackWithPhone", raw => ParseBool(raw, value => _showBackpackWithPhoneEntry.Value = value));
            Copy(settings, BackpackCategoryId, "17_PhoneBackpackDistance", raw => ParseInt(raw, value => _phoneBackpackDistanceEntry.Value = Math.Clamp(value, 0, 100)));
            Copy(settings, BackpackCategoryId, "94_PhonePanelX", raw => ParseFloat(raw, value => _phonePanelXEntry.Value = value));
            Copy(settings, BackpackCategoryId, "95_PhonePanelY", raw => ParseFloat(raw, value => _phonePanelYEntry.Value = value));
            Copy(settings, BackpackCategoryId, "96_PhonePanelWidth", raw => ParseFloat(raw, value => _phonePanelWidthEntry.Value = Mathf.Clamp(value, MinContextualPanelWidth, MaxSavedContextualPanelDimension)));
            Copy(settings, BackpackCategoryId, "97_PhonePanelHeight", raw => ParseFloat(raw, value => _phonePanelHeightEntry.Value = Mathf.Clamp(value, MinContextualPanelHeight, MaxSavedContextualPanelDimension)));
        }

        if (sections.TryGetValue(PreviousCheatsCategoryId, out var cheats))
        {
            Copy(cheats, CheatsCategoryId, "01_StackSizeMultiplier", raw =>
            {
                if (!Enum.TryParse(Unquote(raw), true, out StackSizeMultiplierOption value))
                    return false;
                _stackSizeMultiplierEntry.Value = value;
                return true;
            });
        }

        if (migrated > 0)
            Logger.Info($"Migrated {migrated} saved Inventory Expanded values into the organized 1.2.0 settings sections.");
    }

    private void MigrateLegacyMelonPreferences()
    {
        var sections = ReadPreferenceSections(_melonPreferencesFile);
        Logger.Info("Migrating Inventory Expanded settings from legacy OGBackpack MelonPreferences categories. Old entries will be left untouched.");

        if (sections.TryGetValue(LegacySettingsCategoryId, out var settings))
            ApplyLegacyPreferenceSettings(settings);
        if (sections.TryGetValue(LegacyCheatsCategoryId, out var cheats))
            ApplyLegacyPreferenceCheats(cheats);
    }

    private void ApplyLegacyPreferenceSettings(Dictionary<string, string> settings)
    {
        if (TryGetValue(settings, "01_StorageSlots", out var storageSlots) && int.TryParse(Unquote(storageSlots), out var slots))
            _storageSlotsEntry.Value = Math.Clamp(slots, 1, PlayerBackpack.MaxStorageSlots);
        if (TryGetValue(settings, "02_ToggleKey", out var toggleKey) && TryParseKeyCode(toggleKey, out var keyCode))
            _toggleKeyEntry.Value = keyCode;
        if (TryGetValue(settings, "03_EnablePoliceSearch", out var policeSearch) && bool.TryParse(Unquote(policeSearch), out var enableSearch))
            _enableSearchEntry.Value = enableSearch;
        if (TryGetValue(settings, "04_UnlockRank", out var unlockRank) && Enum.TryParse(Unquote(unlockRank), true, out ERank rank))
            _unlockRankEntry.Value = rank;
        if (TryGetValue(settings, "05_UnlockTier", out var unlockTier) && int.TryParse(Unquote(unlockTier), out var tier))
            _unlockTierEntry.Value = Math.Clamp(tier, 1, 5);
        if (TryGetValue(settings, "06_ContextualBackpackEnabled", out var contextualEnabled) && bool.TryParse(Unquote(contextualEnabled), out var showContextual))
            _contextualBackpackEnabledEntry.Value = showContextual;
        if (TryGetValue(settings, "07_AutoOpenWithContainersAndMachines", out var autoOpen) && bool.TryParse(Unquote(autoOpen), out var autoOpenContextual))
            _autoOpenContextualBackpackEntry.Value = autoOpenContextual;
        if (TryGetValue(settings, "08_RememberPanelPosition", out var rememberPosition) && bool.TryParse(Unquote(rememberPosition), out var rememberPanelPosition))
            _rememberPanelPositionEntry.Value = rememberPanelPosition;
        if (TryGetValue(settings, "09_RememberPanelSize", out var rememberSize) && bool.TryParse(Unquote(rememberSize), out var rememberPanelSize))
            _rememberPanelSizeEntry.Value = rememberPanelSize;
        if (TryGetValue(settings, "90_ContextualPanelX", out var panelX) && float.TryParse(Unquote(panelX), out var x))
            _panelXEntry.Value = x;
        if (TryGetValue(settings, "91_ContextualPanelY", out var panelY) && float.TryParse(Unquote(panelY), out var y))
            _panelYEntry.Value = y;
        if (TryGetValue(settings, "92_ContextualPanelWidth", out var panelWidth) && float.TryParse(Unquote(panelWidth), out var width))
            _panelWidthEntry.Value = Mathf.Clamp(width, MinContextualPanelWidth, MaxSavedContextualPanelDimension);
        if (TryGetValue(settings, "93_ContextualPanelHeight", out var panelHeight) && float.TryParse(Unquote(panelHeight), out var height))
            _panelHeightEntry.Value = Mathf.Clamp(height, MinContextualPanelHeight, MaxSavedContextualPanelDimension);
    }

    private void ApplyLegacyPreferenceCheats(Dictionary<string, string> cheats)
    {
        if (TryGetValue(cheats, "01_StackSizeMultiplier", out var multiplier)
            && Enum.TryParse(Unquote(multiplier), true, out StackSizeMultiplierOption option))
        {
            _stackSizeMultiplierEntry.Value = option;
        }
    }

    private static bool TryGetValue(Dictionary<string, string> values, string key, out string value)
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadPreferenceSections(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return sections;

        Dictionary<string, string> currentSection = null;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var section = line.Trim('[', ']', '"');
                currentSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[section] = currentSection;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0 || currentSection == null)
                continue;

            currentSection[NormalizeKey(line[..separator])] = line[(separator + 1)..].Trim();
        }

        return sections;
    }

    private void MigrateLegacyConfig()
    {
        var migration = new LegacyConfigValues();
        migration.Merge(ReadBackpackCfg(_legacyConfigFile));
        migration.Merge(ReadBackpackModCfg(_legacyBackpackModConfigFile));

        if (!migration.HasValues)
            return;

        Logger.Info("Migrating OG Backpack settings to MelonPreferences.cfg. Legacy config files will be left untouched.");

        if (migration.ToggleKey.HasValue)
            _toggleKeyEntry.Value = migration.ToggleKey.Value;
        if (migration.EnableSearch.HasValue)
            _enableSearchEntry.Value = migration.EnableSearch.Value;
        if (migration.UnlockRank.HasValue)
            _unlockRankEntry.Value = migration.UnlockRank.Value;
        if (migration.UnlockTier.HasValue)
            _unlockTierEntry.Value = Math.Clamp(migration.UnlockTier.Value, 1, 5);
        if (migration.StorageSlots.HasValue)
            _storageSlotsEntry.Value = Math.Clamp(migration.StorageSlots.Value, 1, PlayerBackpack.MaxStorageSlots);
    }

    private static LegacyConfigValues ReadBackpackCfg(string path)
    {
        var values = new LegacyConfigValues();
        if (!File.Exists(path))
            return values;

        var section = string.Empty;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Trim('[', ']', '"');
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
                continue;

            var key = NormalizeKey(line[..separator]);
            var value = line[(separator + 1)..].Trim();

            if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("ToggleKey", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseKeyCode(value, out var keyCode))
                        values.ToggleKey = keyCode;
                    else
                        WarnMalformedLegacy(path, "General.ToggleKey");
                }
                else if (key.Equals("EnableSearch", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(Unquote(value), out var enableSearch))
                        values.EnableSearch = enableSearch;
                    else
                        WarnMalformedLegacy(path, "General.EnableSearch");
                }
            }
            else if (section.Equals("Backpack", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("StorageSlots", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(Unquote(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots))
                        values.MergeStorageSlots(slots);
                    else
                        WarnMalformedLegacy(path, "Backpack.StorageSlots");
                }
                else if (key.Equals("UnlockLevel", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFullRank(value, values))
                        WarnMalformedLegacy(path, "Backpack.UnlockLevel");
                }
            }
        }

        return values;
    }

    private static LegacyConfigValues ReadBackpackModCfg(string path)
    {
        var values = new LegacyConfigValues();
        if (!File.Exists(path))
            return values;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("["))
                continue;

            var separator = line.IndexOf('=');
            if (separator < 0)
                continue;

            var key = NormalizeKey(line[..separator]);
            var value = line[(separator + 1)..].Trim();

            if (key.Contains("ToggleKey", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseKeyCode(value, out var keyCode))
                    values.ToggleKey = keyCode;
                else
                    WarnMalformedLegacy(path, key);
            }
            else if (key.Contains("StorageSlots", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(Unquote(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots))
                    values.MergeStorageSlots(slots);
                else
                    WarnMalformedLegacy(path, key);
            }
            else if (key.Contains("PoliceSearch", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(Unquote(value), out var policeSearch))
                    values.EnableSearch = policeSearch;
                else
                    WarnMalformedLegacy(path, key);
            }
            else if (key.Contains("UnlockLevel", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseRankTierString(value, values))
                    WarnMalformedLegacy(path, key);
            }
        }

        return values;
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf('#');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static string NormalizeKey(string key)
    {
        return Unquote(key).Trim();
    }

    private static string Unquote(string value)
    {
        return value.Trim().Trim('"');
    }

    private static bool TryParseKeyCode(string value, out KeyCode keyCode)
    {
        return Enum.TryParse(Unquote(value), true, out keyCode);
    }

    private static void WarnMalformedLegacy(string path, string key)
    {
        Logger.Warning($"[Config] Ignored malformed saved value for {key} in {Path.GetFileName(path)}; an older compatible source or current default will be used.");
    }

    private static bool TryParseFullRank(string value, LegacyConfigValues values)
    {
        var parsed = false;
        var rankMatch = Regex.Match(value, @"Rank\s*=\s*""?(?<rank>[A-Za-z0-9_]+)""?", RegexOptions.IgnoreCase);
        var tierMatch = Regex.Match(value, @"Tier\s*=\s*(?<tier>\d+)", RegexOptions.IgnoreCase);
        if (rankMatch.Success && Enum.TryParse(rankMatch.Groups["rank"].Value, true, out ERank rank))
        {
            values.UnlockRank = rank;
            parsed = true;
        }
        if (tierMatch.Success && int.TryParse(tierMatch.Groups["tier"].Value, out var tier))
        {
            values.UnlockTier = tier;
            parsed = true;
        }

        return parsed;
    }

    private static bool TryParseRankTierString(string value, LegacyConfigValues values)
    {
        var parsed = false;
        var parts = Unquote(value).Split(["_Tier"], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && Enum.TryParse(parts[0], true, out ERank rank))
        {
            values.UnlockRank = rank;
            parsed = true;
        }
        if (parts.Length > 1 && int.TryParse(parts[1], out var tier))
        {
            values.UnlockTier = tier;
            parsed = true;
        }

        return parsed;
    }

    private sealed class LegacyConfigValues
    {
        public KeyCode? ToggleKey { get; set; }
        public bool? EnableSearch { get; set; }
        public ERank? UnlockRank { get; set; }
        public int? UnlockTier { get; set; }
        public int? StorageSlots { get; private set; }

        public bool HasValues => ToggleKey.HasValue || EnableSearch.HasValue || UnlockRank.HasValue || UnlockTier.HasValue || StorageSlots.HasValue;

        public void Merge(LegacyConfigValues other)
        {
            if (other.ToggleKey.HasValue)
                ToggleKey = other.ToggleKey;
            if (other.EnableSearch.HasValue)
                EnableSearch = other.EnableSearch;
            if (other.UnlockRank.HasValue)
                UnlockRank = other.UnlockRank;
            if (other.UnlockTier.HasValue)
                UnlockTier = other.UnlockTier;
            if (other.StorageSlots.HasValue)
                MergeStorageSlots(other.StorageSlots.Value);
        }

        public void MergeStorageSlots(int slots)
        {
            StorageSlots = PreferenceMigrationResolver.MergeLegacyCapacity(StorageSlots, slots);
        }
    }
}
