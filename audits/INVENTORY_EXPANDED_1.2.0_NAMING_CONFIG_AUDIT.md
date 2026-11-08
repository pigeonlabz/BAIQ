# Inventory Expanded 1.2.0 Naming and Configuration Audit

## Audit outcome

The normal upgrade path is sound: current saved values win, recognized legacy values import once, and defaults fill anything absent. Branding can safely move to **Inventory Expanded** without changing save/config identifiers.

There are a few edge-case migration risks, chiefly partially migrated or malformed preference files. This was a read-only audit: no migrations or builds were performed as part of the investigation.

## 1. Naming and identifier audit

| Current value | Location/purpose | Classification | Compatibility | Recommendation |
|---|---|---|---|---|
| `Inventory Expanded` | `ScheduleOne-Backpack/BackpackMod.cs`, Melon/Mod Manager name | User-facing metadata | Not used as a preference filename or key | Keep as canonical name |
| `Inventory Expanded` | `ScheduleOne-Backpack/ScheduleOne-Backpack.csproj`, product/title metadata | User-facing metadata | Low risk | Keep |
| `Backpack & Inventory QoL` | `README.md`, release documentation | User-facing | Not persisted | Rename primary title to `Inventory Expanded - Backpack & Inventory QoL` |
| `Backpack and Inventory QoL (OG Backpack Fork)` | Current Nexus title | User-facing storefront name | Nexus URL/mod ID must remain; title may change | Use `Inventory Expanded - Backpack & Inventory QoL` |
| `OG Backpack` | Credits, history, legacy identifiers | Mixed | Historical identifiers are sensitive; prose is not | Retain prominently in description/credits/history, not necessarily title |
| `Backpack` namespace | Project code | Internal | Changing it affects type names, Melon entry type and patches | Keep |
| `Backpack.BackpackMod` | Melon entry type | Internal | Directly referenced by `MelonInfo` | Keep |
| `BackpackInventoryQoL.dll` | IL2CPP assembly/output | Distribution/internal | Renaming risks users loading both old and new DLLs | Keep for 1.2.0 |
| `BackpackInventoryQoL_Mono.dll` | Mono assembly/output | Distribution/internal | Same duplicate-loading concern | Keep |
| `[InventoryExpanded]` | `ScheduleOne-Backpack/Utility/Logger.cs` | Diagnostic | Not persisted; useful for support continuity | Keep |
| `BACKPACK`, `CONTEXTUAL BACKPACK`, `INVENTORY & TRANSFERS`, `CHEATS` | Settings display headings | User-facing | Display names are separate from IDs | Safe to adjust, although current headings are already suitable |
| `Backpack` | Save subfile, storage/menu title and runtime comparisons | Internal/persisted | Critical | Never rename |
| `InventoryExpandedSlotLocks` | Slot-lock save subfile | Persisted | Critical | Never rename |
| `Backpack_Config` | Multiplayer configuration message prefix | Protocol identifier | Compatibility-sensitive | Keep |
| `OGBackpack_*` | Runtime GameObject names | Internal UI/debug names | Mostly not persisted, but may serve as lookup/diagnostic anchors | Leave unchanged; renaming has no user benefit |
| `InventoryExpanded_*` preference categories | MelonPreferences identifiers | Persisted | Critical | Never rename |
| README/architecture/release-policy headings | Documentation | User-facing | Safe in current authoritative docs | Standardize prospectively; do not rewrite historical changelog/archive material |

No explicit custom Harmony ID was found. Harmony patching is attached through the Melon/Harmony attributes, so there is no separate brand-related Harmony identifier to migrate.

A Melon display-name change would affect how the mod appears in logs and Mod Manager, but it would not automatically rename the explicit MelonPreferences categories or create a mod-specific configuration file. Preferences remain in the shared `UserData\MelonPreferences.cfg`.

## 2. Configuration and legacy compatibility

The current implementation is in `ScheduleOne-Backpack/Config/Configuration.cs`.

### Current categories

| ID | Visible heading |
|---|---|
| `InventoryExpanded_01_Backpack` | `BACKPACK` |
| `InventoryExpanded_02_Contextual` | `CONTEXTUAL BACKPACK` |
| `InventoryExpanded_03_Transfers` | `INVENTORY & TRANSFERS` |
| `InventoryExpanded_05_Cheats` | `CHEATS` |

### Current active keys

- Backpack:
  `16_ShowBackpackWithPhone`, `17_PhoneBackpackDistance`, `01_StorageSlots`, `02_ToggleKey`, `03_EnablePoliceSearch`
- Contextual:
  `06_ContextualBackpackEnabled`, `07_AutoOpenWithContainersAndMachines`, `08_RememberPanelPosition`, `09_RememberPanelSize`, `11_ResetContextualPanelPosition`
- Transfers:
  `12_ShowSortButtons`, `13_ShiftClickDestinationPriority`, `14_TakeAllKey`, `15_SortActiveContainerKey`
- Cheats:
  `01_StackSizeMultiplier`
- Hidden compatibility/state:
  `04_UnlockRank`, `05_UnlockTier`, `90`-`97` panel geometry keys, `99_ConfigMigrationVersion`

`10_PreferredColumns` is no longer registered or used; layout now uses automatic columns. Rank and tier values remain preserved but hidden and no longer gate access.

### Recognized legacy sources

- Previous categories:
  `InventoryExpanded_04_Phone`,
  `InventoryExpanded_Settings`,
  `InventoryExpanded_Cheats`
- OG categories:
  `OGBackpack_01_Settings`,
  `OGBackpack_02_Cheats`
- Legacy files:
  `UserData\Backpack.cfg`,
  `UserData\BackpackMod.cfg`
- Current storage:
  `UserData\MelonPreferences.cfg`

### Migration lifecycle

On load:

1. The raw preference file is inspected to identify current/former categories.
2. MelonPreferences loads registered current entries.
3. If migration version is below `5`, exactly one main source path is selected:
   - current/former Inventory Expanded categories;
   - otherwise OG MelonPreferences categories;
   - otherwise legacy `.cfg` files.
4. Former Phone-category values are imported into the Backpack category.
5. Migration marker `5` is saved.
6. Normal runtime values are populated.

Imports are presence-aware: an explicitly present current target key is not overwritten by an older source value. This implements the desired precedence:

```text
current explicitly saved value
-> compatible legacy value
-> fresh default
```

Migration is effectively idempotent because the version marker prevents subsequent runs. Legacy files and old preference sections are left intact rather than deleted.

For the two legacy `.cfg` files, `Backpack.cfg` is read first and `BackpackMod.cfg` second. The second file wins for most overlapping scalar values; capacity uses the maximum discovered value.

### Confirmed compatibility risks

1. **Partially mixed configurations:** the presence of any current/former Inventory Expanded category selects that migration path and skips OG categories and old `.cfg` files. If that current category is incomplete while the only valid copy of another setting exists in an older source, that missing value can fall back to its default and then be marked migrated.

2. **Premature/current migration marker:** if `99_ConfigMigrationVersion` is already `5` but target keys are missing, no recovery import occurs.

3. **Malformed legacy values:** parse failures fall back to defaults, while migration can still advance to version `5`.

4. **Retired settings:** old preferred-column values are intentionally no longer honored. Rank/tier values remain preserved but no longer affect runtime access.

A normal, intact 1.1.x configuration using `InventoryExpanded_Settings`/`InventoryExpanded_Cheats` should migrate correctly without losing its active saved values.

## 3. Easy-win settings, ranked

| Rank | Candidate | Proposed values/default | Existing path | Risk/complexity | 1.2.0 |
|---|---|---|---|---|---|
| 1 | Sort button scope | `Everywhere` default, `Backpack Only`, `Storage Only`, `Hidden` | Existing UIs already independently create/show their Sort controls | UI-only; small | **Maybe** |
| 2 | Take All destination | `Follow Shift-Click Priority` default, `Inventory Only`, `Backpack Only` | Take All already obtains destinations, applies priority, then passes them through existing validity filters | Transfer-sensitive but localized; small | **Maybe**, only with routing regression tests |
| 3 | Contextual Backpack interaction scope | `Storage & Machines` default, `Storage Only`, `Machines Only` | Existing contextual entry path | Current mode combines storage and machines; reliable classification work is needed | **Defer** |
| 4 | Phone Backpack supported apps | Individual toggles for currently supported apps, all current apps enabled by default | Explicit Home/Messages/Dealers/Products/Deliveries branches already exist | More settings and layout/support combinations; medium | **Defer** |
| 5 | Phone presentation size cap | `Automatic` default plus conservative choices | Existing UI-scale compensation and reference-scale logic | Recently stabilized geometry; regression-sensitive | **Defer** |
| 6 | Police search mode | `Ignore Backpack` / `Use Officer Search Threshold` | Existing boolean branch | Limited gain over current bool; consequence behavior needs runtime validation | **Defer** |

One presentation issue was confirmed: `Show Contextual Backpack` and `Open Contextual Backpack Automatically` currently both have to be enabled for the contextual panel to open. There is no separate manual contextual-open path, so disabling “Automatically” currently suppresses contextual access entirely rather than merely disabling automatic opening. That should be clarified or consolidated in a later settings pass rather than changed immediately before release.

## 4. Police-search findings

The integration is in `ScheduleOne-Backpack/Patches/BodySearchBehaviourPatch.cs`.

When **Include Backpack in Police Searches** is disabled, the prefix returns control to vanilla unchanged.

When enabled, the mod:

1. Intercepts the vanilla `SearchClean` result.
2. Prevents the search screen from immediately reopening.
3. Presents the officer's Backpack-check dialogue.
4. Checks Backpack contents against the officer's existing `MaxStealthLevel`.
5. Calls vanilla `ConcludeSearch` with the resulting clean/fail status.

The item test in `ScheduleOne-Backpack/PlayerBackpack.cs` treats:

- non-product items as detectable when their legal status is not Legal;
- products as detectable when unpackaged, or when packaging stealth is less than or equal to the officer's threshold;
- sufficiently concealed packaged products as undetected.

There is no meaningful existing numeric “police multiplier.” `MaxStealthLevel` is a discrete stealth threshold, while vanilla timing constants affect search duration rather than detection probability.

The mod does not itself confiscate or remove Backpack items. It passes a search success/failure result back to vanilla `ConcludeSearch`; downstream consequences remain vanilla-controlled and should not be described more specifically without runtime/decompiled confirmation.

The current boolean belongs under normal Backpack/QoL settings. Any mode that weakens detection beyond ignoring the Backpack entirely would be gameplay-altering and better placed under Cheats.

## 5. Recommended naming strategy

Use:

- **Canonical Melon/Mod Manager name:** `Inventory Expanded`
- **Nexus and primary documentation title:** `Inventory Expanded - Backpack & Inventory QoL`
- **Short descriptive text:** “Expanded Backpack storage and inventory quality-of-life tools for Schedule I.”
- **Credits/history:** clearly retain “Originally based on OG Backpack by D-Kay...” and other existing contributor attribution.

This gives the new brand prominence without obscuring what the mod does or breaking search recognition for the existing title.

## 6. Identifiers that must not be renamed

For 1.2.0, retain exactly:

- `Backpack` save subfile
- `InventoryExpandedSlotLocks`
- `Backpack_Config`
- `InventoryExpanded_01_Backpack`
- `InventoryExpanded_02_Contextual`
- `InventoryExpanded_03_Transfers`
- `InventoryExpanded_04_Phone`
- `InventoryExpanded_05_Cheats`
- `InventoryExpanded_Settings`
- `InventoryExpanded_Cheats`
- `OGBackpack_01_Settings`
- `OGBackpack_02_Cheats`
- Every existing preference key listed above
- `Backpack.cfg`
- `BackpackMod.cfg`
- `Backpack` namespace and `Backpack.BackpackMod`
- `BackpackInventoryQoL` and `BackpackInventoryQoL_Mono`
- `PlayerBackpack.StorageName = "Backpack"`
- Legacy `|||` inventory-load delimiter support
- Existing Nexus mod ID/URL

The `OGBackpack_*` GameObject names are not proven persisted, but should also remain unchanged because renaming provides no release benefit.

## 7. Suggested final polish sequence

1. Freeze all persistence, preference, save, protocol and DLL identifiers.
2. Standardize only active public-facing metadata and current documentation.
3. Retain OG Backpack attribution in description, credits and history.
4. Optionally implement scoped Sort visibility as the safest additional setting.
5. Avoid other new options unless clearly worth extending the regression matrix.
6. Add migration tests for partial/mixed configurations before attempting to improve recovery behavior.
7. Run fresh-install, 1.1.x-upgrade, OG-category and legacy-`.cfg` test matrices.
8. Only then perform the release build and release-specific verification.

## Audit scope confirmation

The audit itself did not modify source, configuration, project, documentation, or installed-game files. No build or migration was run during the audit. This Markdown file was subsequently added at the user's request to preserve the report.
