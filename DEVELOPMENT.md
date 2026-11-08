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
version and `1.3.2` is the unreleased unified MAIN candidate. Follow
[RELEASING.md](RELEASING.md) for version selection and do not
reconstruct or renumber early post-release history without authoritative evidence.

## Prerequisites

- A .NET SDK capable of building `net6.0` projects.
- Schedule I installed with MelonLoader initialized at least once. For a
  standalone clone, set `SCHEDULE_I_DIR` to that game directory or pass
  `-p:S1Dir=...` to the build command.
- Generated references under `<Schedule I>\MelonLoader\Il2CppAssemblies`.
- Network access for the first NuGet restore of `Krafs.Publicizer` if it is not
  already cached.

For the supported MAIN IL2CPP build, `S1Dir` uses `SCHEDULE_I_DIR` when set;
the original workspace can still fall back to its preserved `MAIN_047f12`
reference directory. Use only matching generated references; the build checks
their `Assembly-CSharp.dll` hash:

```powershell
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj `
  -c IL2CPP -p:S1Dir="D:\Refs\MAIN_047f12"
```

## Build

From the repository root:

```powershell
dotnet restore .\InventoryExpanded.sln
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj -c IL2CPP
```

The supported IL2CPP configuration checks the `Assembly-CSharp.dll` SHA-256
before resolving references. A `-p:S1Dir=...` override must match the MAIN
0.4.7f12 profile. For 1.3.2 candidate builds that leave existing outputs
untouched, run
`tools/prepare-release-candidate.ps1` instead.

The supported MAIN 0.4.7f12 reference snapshot is under
`../dev-bootstrap/refs/MAIN_047f12`; its `reference-hashes.csv` records every
copied `MelonLoader/net6` and `MelonLoader/Il2CppAssemblies` file. The older
MAIN 0.4.6f13 and BETA 0.4.7 reference snapshots remain preserved for audit and
historical builds, but the active release workflow builds only MAIN against
f12. The candidate promotes the BETA-tested explicit player-data overload,
0.4.7 hydration lifecycle and captured storage-owner behavior into the
established MAIN assembly identity. Do not substitute historical beta interops
for the current MAIN reference profile.

The supported public/stable configuration is `IL2CPP`; this project does not use
a conventional `Release` configuration. Output is written to:

```text
src\InventoryExpanded\bin\IL2CPP\net6.0\BackpackInventoryQoL.dll
```

The active public artifact is `BackpackInventoryQoL.dll` from the single `IL2CPP`
configuration. The obsolete BETA assembly, source conditionals and build
configuration are preserved in
`docs/history/channel-retirement-20261010/` and the existing release-candidate
archives. A temporary beta compatibility target may be introduced for a future
major game update only with a separate matching reference profile, explicit
runtime evidence and a dated archive; it is not part of this 1.3.2 build.

An older `MONO` configuration remains for source compatibility but is not a
supported or validated release target. A successful compile proves only managed
compatibility; it does not prove that Harmony patches, IL2CPP trampolines, Unity
wrappers, or game UI paths work at runtime.

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

For the multiplayer Backpack persistence investigation, build the isolated
`BackpackInventoryQoL-MultiplayerPersistence-Diagnostic.dll` with
`tools/build-multiplayer-persistence-diagnostic.ps1`. The opt-in trace observes
ordinary save, read, hydration and cleanup methods without patching generated
RPC handlers or changing ownership checks, serialization, or synchronization.
The development assembly has a distinct filename and assembly identity; the
ordinary MAIN build remains unchanged when the diagnostic build property is
unset. It hashes PlayerCode for cross-peer correlation and records aggregate
counts rather than item contents. Follow the two-client procedure and
unobservable-boundary notes in [TESTING.md](TESTING.md).

MAIN 1.3.2 promotes the validated MPTEST1 guest-persistence behavior into the
normal IL2CPP build. Host persistence still observes the existing server-side
native `StorageEntity` bound to the remote Player network object; it adds no
inventory RPC and keeps the local-owner guard. A write requires host/lobby-server
role, a non-owner Player with a host-resolved PlayerCode, exact Player/Storage
network-object binding, Backpack identity, server initialization, populated
slots, and an observed native item-instance or quantity mutation in the current
save session.

The write path resolves `LoadManager.LoadedGameFolderPath\Players`, requires
the `Player.WriteData` parent to match that root, checks `SaveFolderName` is a
single child folder, and validates `Player.json` against that PlayerCode before
writing. The existing `ItemSet` JSON is staged in the same directory, verified,
and atomically committed to `Backpack.json`. The previous sidecar is retained
until target readback succeeds. Per-storage gates order mutation callbacks,
write acknowledgments, storage cleanup and session resets. Failure leaves the
dirty marker available for retry; no item contents or raw PlayerCode are logged.
The host's slot-lock registry is never written under a guest path. The guest's
separate slot-lock registry is not transferred into the host save.

The previously tested MPTEST1 artifact passed two-client reconnect, repeated
save, intentional-empty, and host save/reload checks. These results do not prove
the exact MAIN 1.3.2 build works at runtime. The f12 probe observed normal owner
matches but did not prove pre-reader rejection of deliberately unauthorized
native storage RPCs. This persistence change adds no RPC and is not an
exploit-proof claim. Keep that authorization boundary visible in release review.

The current 1.3.2 candidate adds a separate
`GameplaySettingsSyncManager` for Stack Size and the supported Increase
Production settings. It does not repurpose the legacy rank/police-search
payload. The host publishes through Steam's owner-only `SetLobbyData`; guests
verify `GetLobbyOwner`, the local `SteamUser` identity, Schedule's `Lobby.IsHost`
and FishNet client/server role, the lobby-owner binding, exact schema and exact
BAIQ assembly version. No general-purpose RPC or inventory mutation endpoint is
added. Steam metadata is an authenticated transport within that lobby, not an
anti-cheat system; it cannot prove all members have BAIQ installed.

Gameplay preferences remain local MelonPreferences. `CheatSettings` and
`ProductionCheats` are the effective session layer; saving effective values
never writes them back to preference entries. The host's settings freeze for
the lobby. A guest stays at Vanilla stack limits and production-off, with BAIQ
Backpack, quick-move augmentation, Sort and Take All paused, until a matching
host policy is accepted. Ordinary native game inventory remains available.
Leaving restores the guest's own preferences; a lost policy fails closed and a
changed active policy requires reconnect. The Mod Manager setting fields remain
editable because MelonPreferences provides no supported read-only control, but
effective-value overrides cannot be bypassed by editing them.

The lobby watcher treats missing Steam lobby identity as a retryable, fail-closed
state: it logs whether the lobby ID, owner, or local Steam ID is unavailable,
waits through a short startup window, then continues at a slower recovery rate.
The host becomes ready only after writing its frozen policy and reading back the
exact envelope; it does not wait for itself as a guest. A missing `Lobby`
wrapper suspends a tracked session rather than restoring local settings, while
an explicit `IsInLobby == false` begins session-end handling. Main-scene save
transitions re-verify the current lobby. Diagnostics use short hashed identity
tags and do not include full Steam IDs.

Phone presentation checks continue while policy is pending so transient geometry
and app changes can recover. The panel is read-only until policy readiness:
the panel raycaster, quick-move augmentation, contextual accessor and controls
stay off. Layout retry is bounded to 20 checks per second and only runs while a
phone presentation is requested/open; a valid read-only panel does not perform a
full geometry walk every frame while policy is pending. Unsupported app changes
hide the panel; returning to Home forces a fresh layout calculation. Check the RC3 focused
procedure in [TESTING.md](TESTING.md); no live RC3 acceptance is implied by the
static tests.

The candidate restores guarded Mk1 Packaging Station scaling. Mk2 throughput
and its separate experimental safety/diagnostic patches remain excluded. The
native packaging transaction repeats only after exact item/quantity deltas and
the native preflight are verified. Exact f12 host/guest production authority
and inventory conservation remain manual acceptance requirements. Guest
slot-lock synchronization is deferred. See the exact MAIN matrix in
[TESTING.md](TESTING.md). The separate `build-mptest1.ps1` and its historical
artifacts remain available for investigation; they are not the production build.

For that read-only f12 boundary check, build the separate
`tools/build-fishnet-auth-diagnostic.ps1` artifact. Its exact Harmony targets
are `StorageEntity.RpcReader___Server_SetStoredInstance_2652194801` and
`RpcReader___Server_SetItemSlotQuantity_1692629761`; each reader's final
`NetworkConnection` is recorded as the Fish-Net-supplied sender. The pinned f12
generated wrapper signatures expose a separate `NetworkConnection` argument on
`SetStoredInstance_Internal`, which the probe logs separately and compares with
the reader sender. The wrappers do not expose whether this argument came from
serialized RPC data or was otherwise supplied by the reader. The preserved
native reader dump is not pinned to the current f12 binary hash, so do not use it
to claim f12 wire or ownership behavior. The probe also records target Player
and StorageEntity network owners, plus whether the two native setters run and
change the observed slot's occupancy/quantity. It never reads `PooledReader`,
alters arguments or state, blocks RPCs, or includes the production guest-persistence path.
A missing reader marker
cannot identify a Fish-Net ownership rejection, because such a rejection may
occur before the game-specific managed reader is invoked. Setter entry and
return also do not alone establish that a mutation was authorized for the
target Player. The two-peer procedure and exact inference limits are in
[TESTING.md](TESTING.md).

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

The f9 snapshot is diagnostic/API comparison only and is not a supported build
profile. The detailed transaction audit and classification are in
[`FINAL_COMPLETENESS_AUDIT.md`](audits/1.3.0-production-completeness/FINAL_COMPLETENESS_AUDIT.md).

The release-candidate script also runs
`tools/ProductionCompileListAudit` after the MAIN build. This metadata-only
check ensures active production policy/patch source files occur in the
explicit `InventoryExpanded.csproj` compile list and in the built DLL, while
the guarded Mk1 Packaging adapter is present and experimental Mk2 diagnostic
and safety adapters remain excluded. Run it directly with:

```powershell
dotnet run --project .\InventoryExpanded\tools\ProductionCompileListAudit\ProductionCompileListAudit.csproj -- `
  .\InventoryExpanded MAIN .\InventoryExpanded\release-candidates\1.3.2-main-047f12-host-settings-rc2-20261011\MAIN\BackpackInventoryQoL.dll
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
native cash acceptance, filters, locks, and per-slot balance checks. The
supported MAIN build uses a positive classification: exact `PlaceableStorageEntity`
instances with matching storage identity, plus the explicitly owned
`EmployeeHome.Storage`, `DeadDrop.Storage`, and `SupplierStash.Storage`
endpoints. `BedItem` and unknown placeable subclasses are rejected. Do not
broaden this to arbitrary `StorageEntity` or `IItemSlotOwner` instances; machine
and delivery inventories remain excluded. The cash movement itself continues
through the existing native quick-move route. Take All's balance-aware helper is
separate from shift-click routing and requires its own conservation checks.

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
