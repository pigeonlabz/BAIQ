# Development Guide

This guide covers the supported local workflow for Inventory Expanded.
Read
[ARCHITECTURE.md](ARCHITECTURE.md) before changing persistence, routing, slot
filters, contextual UI, or stack capacity.

## Repository Layout

```text
InventoryExpanded.sln
src/InventoryExpanded/
  InventoryExpanded.csproj       IL2CPP/MONO project and output metadata
  BackpackMod.cs                 Melon metadata and process/session hooks
  PlayerBackpack.cs              Authoritative Backpack storage owner
  Config/                        MelonPreferences and config synchronization
  Cheats/                        Typed cheat settings and active-cheat state
  Patches/                       Narrow Harmony integration points
  UI/                            Standalone/contextual UI and native extensions
  Utility/                       Sort and oversized-stack policies
tests/                            Focused compatibility/polish test executables
docs/                             Active maintainer documentation
  audits/                        Point-in-time audits
  history/                       Preserved historical diagnostics/reference notes
releases/                         Intentional completed release artifacts/records
```

The canonical short name is **Inventory Expanded**. The current in-game Melon
display name uses the primary descriptive public title **Inventory Expanded -
Backpack & Inventory QoL** so MelonLoader can distinguish it from legacy 1.1.3,
which declares the same short name and author. This display name is not a
persistence identifier. The project, namespace, assembly, DLL, save subfile, preferences, and
several Unity object names retain `Backpack`, `InventoryExpanded`, or
`OGBackpack` identifiers for compatibility. Historical notes and release
archives may also use **Backpack and Inventory QoL**, **Backpack & Inventory
QoL**, or `BAIQ`. Do not broadly rename these internals or treat the historical
names as separate current mods.

Public releases use `MAJOR.MINOR.PATCH`; `1.3.0` is the current published public
version and `1.3.1` is the unreleased MAIN compatibility fix. Follow
[RELEASING.md](RELEASING.md) for version selection and do not
reconstruct or renumber early post-release history without authoritative evidence.

## Prerequisites

- A .NET SDK capable of building `net6.0` projects.
- Schedule I installed with MelonLoader initialized at least once.
- Generated references under `<Schedule I>\MelonLoader\Il2CppAssemblies`.
- Network access for the first NuGet restore of `Krafs.Publicizer` if it is not
  already cached.

For IL2CPP builds, `S1Dir` defaults to the preserved branch-specific reference
directory described below. Override it only with another copy of the matching
reference profile; the build checks its `Assembly-CSharp.dll` hash:

```powershell
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj `
  -c IL2CPP -p:S1Dir="D:\Refs\MAIN_047f12"
```

## Build

From the repository root:

```powershell
dotnet restore .\InventoryExpanded.sln
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj -c IL2CPP
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj -c IL2CPP_BETA_047
```

Each IL2CPP configuration defaults to its preserved reference profile and
checks the `Assembly-CSharp.dll` SHA-256 before resolving references. A
`-p:S1Dir=...` override must match that target's profile. For 1.3.1 candidate
builds that leave existing outputs untouched, run
`tools/prepare-release-candidate.ps1` instead.

The current MAIN 0.4.7f12 reference snapshot is under
`../dev-bootstrap/refs/MAIN_047f12`; its `reference-hashes.csv` records every
copied `MelonLoader/net6` and `MelonLoader/Il2CppAssemblies` file. Build MAIN
independently with `-p:S1Dir="<workspace>/dev-bootstrap/refs/MAIN_047f12"`.
The older MAIN 0.4.6f13 snapshot remains preserved for the published 1.3.0
audit history. The separate BETA snapshot remains under
`../dev-bootstrap/refs/BETA_047`. Both 0.4.7 targets use the shared player-data
and Backpack hydration lifecycle; BETA-specific storage UI behavior and its DLL
identity remain separately compiled. Do not use one branch's generated
interops to build the other.

The supported public/stable configuration is `IL2CPP`; this project does not use
a conventional `Release` configuration. Output is written to:

```text
src\InventoryExpanded\bin\IL2CPP\net6.0\BackpackInventoryQoL.dll
```

Schedule I 0.4.7 beta testing uses the isolated `IL2CPP_BETA_047`
configuration. Its compile-time lifecycle hooks and clearly identified assembly
are written to:

```text
src\InventoryExpanded\bin\IL2CPP_BETA_047\net6.0\BackpackInventoryQoL.Beta047.dll
```

Do not publish that artifact as the stable DLL or install both artifacts at the
same time. The stable target intentionally retains
`Player.Load(PlayerData, string)`; the beta target uses the observed
`TryGetPlayerData` to `LoadInventory` lifecycle instead. The two targets share
save identifiers and formats, but not their version-specific Harmony boundary.

An older `MONO` configuration remains for source compatibility but is not the
validated public target. A successful compile proves only managed compatibility;
it does not prove that Harmony patches, IL2CPP trampolines, Unity wrappers, or
game UI paths work at runtime.

## Local Testing

1. Close Schedule I.
2. Build the IL2CPP project.
3. Remove or move an older active `Backpack.dll`, then copy
   `BackpackInventoryQoL.dll` to `<Schedule I>\Mods`. A clean single-mod setup is
   preferred even though 1.2.0's startup safeguard detects known legacy conflicts,
   withholds initialization, and offers to move a positively identified DLL to
   `Mods\Disabled`.
4. Start the game and inspect `<Schedule I>\MelonLoader\Latest.log`.
5. Run the focused checks in [TESTING.md](TESTING.md).

The project supports `-p:DeployToGame=true`, but its post-build target kills the
game process, copies the DLL, and restarts Schedule I. Use it only when that
behavior is intentional; release builds should remain explicit and non-deploying.

## Diagnostic-First Workflow

For game compatibility or inventory regressions:

For unclear runtime lifecycle or state issues, prefer a targeted Pineapple Haze
reproduction before adding temporary diagnostic instrumentation directly to
Backpack. Runtime evidence is under `<Schedule I>\UserData\.pineapple`; use
`pineapple mark <label>` to identify focused test phases. Keep detailed Pineapple
instructions in the workspace and Pineapple project documentation.

1. Trace the native/runtime behavior and reproduce the smallest failing path.
2. Add concise targeted diagnostics only where the trace is incomplete.
3. Compare working native behavior against the modded path.
4. Identify the exact owner, slot list, overload, or state divergence.
5. Apply the smallest evidence-backed fix and retain native authoritative writes.
6. Regression-test adjacent storage, machine, player, Backpack, and lifecycle
   behavior.

Temporary diagnostics should report stable IDs, quantities, route decisions, and
actionable failures. Remove high-frequency click/mutation dumps before release.
Keep startup, migration, conflict, recovery warnings, and errors visible without
configuration. Route detailed successful UI/lifecycle, performance, Sort,
transfer, cash-routing, and shop-overflow traces through `Logger.Diagnostic`.
Its cached process-level gate is updated live by **Diagnostic Logging**; do not
read MelonPreferences in hot paths, and guard expensive diagnostic construction
when practical. MelonLoader supplies the public `[Inventory_Expanded]` prefix.

### Preserved Schedule I API surface audits

`tools/ProductionSurfaceAudit` is a metadata-only .NET tool for comparing
preserved `Assembly-CSharp.dll` profiles without loading game code. The
1.3.0 production audit captures focused Schedule I type names, inheritance,
interfaces, fields, properties, methods and the input assembly hash. Build and run it from
the workspace root:

```powershell
dotnet build .\InventoryExpanded\tools\ProductionSurfaceAudit\ProductionSurfaceAudit.csproj
$tool = '.\InventoryExpanded\tools\ProductionSurfaceAudit\bin\Debug\net9.0\ProductionSurfaceAudit.dll'
$audit = '.\InventoryExpanded\docs\audits\1.3.0-production-completeness'
dotnet $tool '.\dev-bootstrap\refs\MAIN_046f13\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll' "$audit\MAIN_046f13.json"
dotnet $tool '.\dev-bootstrap\refs\BETA_047\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll' "$audit\BETA_047f6.json"
dotnet $tool '.\dev-bootstrap\refs\BETA_047f9_observed\Assembly-CSharp.dll' "$audit\BETA_047f9_observed.json"
```

The f9 snapshot is diagnostic/API comparison only; it does not change the BETA
build target from the preserved f6 profile. The detailed transaction audit and
classification are in
[`FINAL_COMPLETENESS_AUDIT.md`](audits/1.3.0-production-completeness/FINAL_COMPLETENESS_AUDIT.md).

The release-candidate script also runs
`tools/ProductionCompileListAudit` after each branch build. This metadata-only
check ensures active production policy/patch source files occur in the
explicit `InventoryExpanded.csproj` compile list and in the built DLL, while
the preserved Packaging Station runtime adapters remain excluded. Run it
directly with:

```powershell
dotnet run --project .\InventoryExpanded\tools\ProductionCompileListAudit\ProductionCompileListAudit.csproj -- `
  .\InventoryExpanded MAIN .\InventoryExpanded\.release-work\1.3.1\build\output\MAIN\IL2CPP\net6.0\BackpackInventoryQoL.dll
```

## Implementation Lessons

### Conflict-Safe Startup

`BackpackMod` performs legacy-mod conflict preflight before configuration and
runtime setup. Custom IL2CPP components must not use automatic
`RegisterTypeInIl2Cpp` attributes: list them in `Il2CppTypeRegistrar` in
parent-before-child order and register them explicitly only after preflight
passes. Harmony patches are installed only after registration, and later
lifecycle callbacks are gated by successful initialization. Keep conflict
handling fail-closed: a warning or safe DLL move must never touch saves or
configuration, and a restart remains required after quarantining a DLL. Automatic
relaunch is intentionally unsupported: after the move is verified, offer the
normal Unity quit path and a **Later** action that leaves the process running.
Clearly instruct the user to relaunch through Steam, and do not persist
conflict-dismissal or quarantine flags.

### Authoritative Storage

`PlayerBackpack` owns one native `StorageEntity`; its `ItemSlots` are the source
of truth. UI pages and Search are projections only. Never create page-specific
inventories or copy slots into a second persistence model.

### Save-Session Lifecycle

Players, storage owners, menu managers, UI roots, slot wrappers, raycasters, and
search helpers are save-session scoped. `BackpackMod` clears them when the `Main`
scene unloads and defensively before the next session. Preferences and cheat
settings are process scoped.

### Contextual UI

`ContextualBackpackPanel` is an independent sibling overlay under the native
`ItemUIManager` canvas. It must not reparent or resize native machine/storage UI.
Pointer movement must be converted into the parent canvas coordinate space before
using `anchoredPosition`; preserve the original title-bar grab offset.
Header, Search, grid, and footer are fixed layout regions, with only the grid
consuming remaining inner space. Both resize corners use the same active-corner,
opposite-corner calculation. Derive maximum size from the current canvas and
clamp restored geometry rather than persisting resolution-specific limits.
Ordinary dragging updates and clamps `anchoredPosition` only; persist geometry
when the interaction ends. During resize, rebuild slot projections only when the
computed row, column, or visible-slot count actually changes.

### Standalone UI Performance

Delegated native `StorageMenu.Open` overloads may report the same Backpack open
more than once. Once the standalone root is active, later reports must be no-ops.
Keep slot wrappers and authoritative bindings across ordinary close/reopen, skip
assigning an unchanged slot, and rewrite responsive geometry only when its inputs
change. Do setup while the cached root is inactive and activate it once at the end.

### Inventory Routing

`InventoryInteractionPatch` observes native `EnableQuickMove` overloads and adds
contextual Backpack routes. Keep native filters enabled with
`GetCapacityForItem(item, true)`, reject add-locked/output slots, and preserve
machine rules. Cash is a specialized balance transfer and must retain its
native cash acceptance, filters, locks, and per-slot balance checks. MAIN and
BETA use the same positive classification: exact `PlaceableStorageEntity`
instances with matching storage identity, plus the explicitly owned
`EmployeeHome.Storage`, `DeadDrop.Storage`, and `SupplierStash.Storage`
endpoints. `BedItem` and unknown placeable subclasses are rejected. Do not
broaden this to arbitrary `StorageEntity` or `IItemSlotOwner` instances; machine
and delivery inventories remain excluded. The cash movement itself continues
through the existing native quick-move route. The BETA-only Take All balance
helper is separate and should not be confused with shift-click routing.

### Sort and Oversized Stacks

All Sort buttons call `Utility/InventorySorter.cs` with complete authoritative
slot lists. Product instances are grouped as packaged products, unpackaged
products, then everything else before the existing ID/name/source ordering. Sort
must prove item/quantity preservation before writing. Existing
oversized stacks remain valid after a multiplier reduction, have no spare
capacity, and must never be clamped, deleted, or world-dropped.

User slot locks are mod-owned automation state. Query
`UserSlotLockRegistry.IsUserLocked(...)`; never reuse native
`ItemSlot.IsLocked`. Player inventory identity follows `GetAllInventorySlots()`
index order, Backpack identity follows its authoritative storage index, and
generic storage identity is owner plus index for the current save session only.
Only player and Backpack indices are written to the save-specific
`InventoryExpandedSlotLocks` subfile. UI wrappers and IL2CPP pointers are not
persistent identities.

### IL2CPP Boundaries

- Use Harmony positional arguments such as `__0` and `__1` for generated method
  parameters whose names can change between game builds.
- Avoid diagnostic detours on high-frequency native mutation methods; previous
  nested trampolines caused save-load access violations.
- Do not use stripped methods such as
  `RectTransformUtility.CalculateRelativeRectTransformBounds` in this build.
- Compare IL2CPP slot identity using the established pointer-aware helpers, not
  managed wrapper reference equality.
- Treat destroyed Unity/IL2CPP wrappers as invalid even when a managed list still
  contains them.

## Compatibility Boundaries

Preserve these unless a guarded migration is explicitly designed and tested:

- namespace and root namespace `Backpack`;
- IL2CPP assembly and output file `BackpackInventoryQoL.dll`;
- saved Backpack storage/subfile identity;
- MelonPreferences entry keys and legacy migration from `OGBackpack_01_Settings`
  and `OGBackpack_02_Cheats`;
- existing preference entry keys;
- `UserData\Backpack.cfg` and `UserData\BackpackMod.cfg` migration paths.

Do not delete or rewrite `releases`, `docs/history`, point-in-time audits, or other
archive and reference material as part of ordinary maintenance.
