# Architecture and Invariants

Inventory Expanded extends Schedule I's native inventory system. It does not own
a parallel inventory model. These boundaries are the minimum context required
before changing storage, UI, transfers, filters, Sort, cheats, or lifecycle code.

## Components

| Area | Primary implementation | Responsibility |
| --- | --- | --- |
| Entry/lifecycle | `src/InventoryExpanded/BackpackMod.cs` | Conflict preflight, explicit patch initialization, and save-session cleanup |
| Backpack owner | `src/InventoryExpanded/PlayerBackpack.cs` | Native storage entity, slots, capacity, and access |
| Preferences | `src/InventoryExpanded/Config/` | MelonPreferences, legacy migration, multiplayer sync |
| Standalone UI | `src/InventoryExpanded/UI/ResponsiveBackpackUI.cs` | Responsive grid, pages, Search, controls |
| Contextual UI | `src/InventoryExpanded/UI/ContextualBackpackPanel.cs` | Independent overlay beside native inventories |
| Phone UI | `src/InventoryExpanded/UI/PhoneBackpackUI.cs` | Adaptive projection beside supported Phone apps |
| Routing | `src/InventoryExpanded/Patches/InventoryInteractionPatch.cs` | Native quick-move observation and destination validation |
| Sort | `src/InventoryExpanded/Utility/InventorySorter.cs` | Shared, quantity-checked full-inventory sorting |
| Over-cap policy | `src/InventoryExpanded/Utility/OvercapInventoryPolicy.cs` | Lossless oversized-stack transfer rules |
| Cheats | `src/InventoryExpanded/Cheats/` and `Patches/CheatPatch.cs` | Stack multiplier and achievement suppression |

## Ownership and Persistence

`PlayerBackpack` attaches one native `StorageEntity` to the local player. That
entity's `ItemSlots` are authoritative and Schedule I's existing persistence
saves their item instances.

Invariants:

1. UI slots bind directly to authoritative Backpack slots.
2. Pagination and Search only change which authoritative indices are displayed.
3. Capacity changes use the established storage resize path and do not discard
   occupied slots silently.
4. Contextual and Phone access lease the same owner; neither creates a second
   Backpack.
5. Sort and transfers preserve exact item identity and aggregate quantity.
6. Existing save names and Backpack storage identity remain stable.

## Standalone UI

`ResponsiveBackpackUI` activates only for the native `StorageMenu` whose title is
`Backpack`. It hides the native Backpack layout temporarily, binds a responsive
set of native `ItemSlotUI` wrappers, and restores captured native state on close.

The title, grid, pagination, Search, and Sort are separate layout regions. Sort
always receives the complete Backpack slot list, not the current page or search
projection.

## Contextual UI

`InventoryInteractionPatch` observes both native `ItemUIManager.EnableQuickMove`
overloads. When an external owner is active, `ContextualBackpackPanel` opens as a
sibling overlay under the same canvas and registers its raycaster with the native
item UI manager.

The panel owns only its own header, Search, grid, footer, resize handles, canvas,
and raycaster. It must not reparent, resize, reorder, or anchor native storage or
machine controls. `StorageSortButton` likewise creates an independent,
layout-ignored control anchored to the live storage slot container and does not
mutate or derive its position from the native Done button.

Contextual layout is four predictable regions: compact header, Search, flexible
grid, and compact footer. Grid rows and columns derive from the inner area left
after those fixed regions and padding. Maximum panel size derives from the live
parent canvas minus a small safety margin; saved geometry is retained but clamped
back into the current canvas after resolution or UI-scale changes.

Drag and resize math uses the parent canvas `RectTransform` coordinate space:

```text
grab offset = local pointer - panel anchored position
panel position = local pointer - grab offset
```

Convert `Input.mousePosition` through
`ScreenPointToLocalPointInRectangle` before anchored-position math, then clamp.
Do not mix screen pixels, scaled canvas coordinates, world positions, or arbitrary
resolution offsets. Dragging is position-only; resize refreshes the projected slot
UI only when the usable grid crosses a row or column threshold. Preferences are
written when the geometry interaction ends, not for every pointer movement.

Held-item and tooltip canvases remain above the contextual overlay. Their order
is reapplied because Schedule I can reset native canvas ordering when opening an
item-info panel.

## Phone UI

`PhoneBackpackUI` is another projection of the same authoritative Backpack. It
uses an explicit supported-app allowlist, with portrait presentation for Home,
Messages, and Dealers and landscape presentation for Products and Deliveries.
Unsupported and unknown apps reject the projection rather than risk covering
native UI.

Phone/app bounds and native HUD safety bounds remain authoritative. Interface
Scale compensation applies to the whole presentation so controls and slots retain
their proportions. The user-facing Phone Backpack UI Scale is then applied as a
single bounded multiplier to that automatically compensated presentation; panel
fitting still uses the final footprint and rejects unsafe regions. Phone scrolling
and Backpack paging are isolated. The Phone
projection intentionally has no Search input so gameplay letter hotkeys are not
captured; standalone and contextual Search remain unchanged.

## Search and Sort

`BackpackSearchBar` projects a case-insensitive display-name query to authoritative
slot indices. It preserves index order, includes all slots when empty, and never
feeds projected slots into persistence, Sort, or routing.

`InventorySorter` is shared by standalone Backpack, contextual Backpack, and
storage Sort controls. It:

1. selects eligible authoritative slots;
2. copies item instances before mutation;
3. groups packaged product instances first, unpackaged product instances second,
   and everything else third, then applies the existing ID/name/source ordering
   and consolidates only compatible stacks;
4. verifies item ID/quantity totals and required destination count;
5. writes through native replicated slot methods;
6. verifies totals again and refreshes the relevant UI.

Removal locks, add locks, hard filters, and non-default player filters are real
restrictions. `ItemSlot.IsLocked` is not an exclusion because it represents the
active accessor/network lease while the inventory is open.

## User Slot Locks

`UserSlotLockRegistry` owns automation locks independently of native
`ItemSlot.IsLocked`. Player and Backpack slots use their authoritative list index;
those indices are saved in `InventoryExpandedSlotLocks` beside the existing
player subfiles. Backpack capacity changes do not renumber retained slots, and a
lock beyond a temporarily reduced capacity remains attached to its original index.

Storage locks use authoritative owner plus slot index for the current save
session. They are cleared on `Main` unload and are not persisted because generic
world storage does not expose a proven stable identity across save reloads.
`UserSlotLockOverlay` is only a projection of registry state and may be rebuilt at
any time. Each supported slot UI owns at most one compact always-visible control;
there is no pointer-hover handler or per-frame binding scan. Native `CashSlot`
wrappers never expose the control. Manual drag/drop and explicit quick move do
not consult user locks.

Sort excludes user-locked slots before copying or planning. Locked occupied and
empty slots therefore never participate in ordering, consolidation, destination
capacity, oversized normalization, clearing, or eligible-subset total checks.

The current MelonPreferences category identifiers are ordered sections prefixed
with the exact MelonInfo name `Inventory Expanded - Backpack & Inventory QoL`:
`_01_Backpack`, `_02_Contextual`, `_03_Transfers`, and `_05_Cheats`. This lets
Mod Manager & Phone App 2.2.4 associate them with the Melon and renders Cheats
last. Phone Backpack preferences retain their keys while moving into Backpack
through a presence-aware migration. The 1.2.3 organized `InventoryExpanded_01_*`
through `InventoryExpanded_05_*` sections, former Phone category
(`InventoryExpanded_04_Phone`), consolidated
`InventoryExpanded_Settings`/`InventoryExpanded_Cheats` categories, and legacy
`OGBackpack_*` sections are migration inputs; entry identifiers remain stable
when values move into the organized categories.

Migration version 7 resolves values per setting. An explicitly present current
target key wins, followed by the matching 1.2.3 organized category, a compatible
older Inventory Expanded source, OG Backpack source, legacy
`Backpack.cfg`/`BackpackMod.cfg` source, and the current default. Installations
marked version 6 or earlier receive one idempotent repair pass for missing target
keys. The marker defaults to the current version but absence on disk still
triggers migration; Reset therefore remains durable instead of reimporting a
retained legacy section on the next launch. Legacy sections and files are
retained, malformed values are ignored predictably, and complete current values
are never overwritten.

## Quick Move and Filters

Contextual routing extends the native destination list:

- external source -> Inventory and Backpack in the configured priority, with the
  previous Backpack-first order retained as the upgrade default;
- Backpack/player source -> external inventory first, then native destinations;
- unrelated source -> leave the native result unchanged.

Every ordinary destination must be distinct from the source, not add-locked, and
have positive `GetCapacityForItem(item, true)`. The `true` argument is essential:
it applies configured player filters. Native machine slot roles remain the source
of truth; do not hardcode item IDs or duplicate machine-specific rules.

Cash is a specialized balance, not an ordinary stack quantity. The active
cash-route policy is shared by MAIN and BETA. It permits the exact native
`PlaceableStorageEntity` type only when its `StorageEntity` reference matches
the active storage; `BedItem` and other special placeable subclasses fail
closed. It also preserves explicit native cash endpoints whose owning component
references the exact storage: `EmployeeHome.Storage`, `DeadDrop.Storage`, and
`SupplierStash.Storage`. Unknown `StorageEntity` owners, machine inventories,
delivery storage, and arbitrary `IItemSlotOwner` targets are not eligible.

Cash destinations must be the captured active container's own slots and pass
`CanSlotAcceptCash`, hard/player filters, add-lock and user-lock checks, cash
item compatibility, and the native per-slot balance limit. The cash quick-move
path leaves balance mutation to the existing native quick-move handling; the
existing BETA Take All helper remains a separate explicit balance-aware path.
Sort treats any slot containing cash, and any `CashSlot`, as protected: it
leaves that exact instance in place and never copies, consolidates, normalizes,
clears, or writes it as an ordinary stack.

## Shop, Reload, and Magazines

Shop purchase fulfilment uses `ShopInterface.GetAvailableSlots()` before handing
items to available inventory slots and then to the native delivery-bay/overflow
path. Backpack extends that slot list after the native player slots and before
overflow; delivery-bay and loading-vehicle fallback identity comes from the live
`ShopInterface`, never diagnostic trace state. It does not intercept spawned
overflow items.

Ranged weapon reload uses `Equippable_RangedWeapon.GetMagazine(out
StorableItemInstance)` to locate a compatible magazine source. Backpack only
fills that `out` result when the native lookup fails and the Backpack contains a
loaded `IntegerItemInstance` whose definition matches the weapon's native
`Magazine` definition.

Loaded magazines are stateful `IntegerItemInstance` objects. Their physical item
quantity remains one, while loaded ammo is stored in `Value` and serialized by
the native integer item data path. They must not be made normally stackable.
Consolidation may only transfer `Value` between known compatible magazine
instances discovered from ranged weapon `Magazine`/`MagazineSize` definitions,
with the same number of magazine instances before and after.
`MagazineConsolidator` is currently an unexposed internal helper, not a public
1.2.0 control or ordinary release-acceptance feature.

Filter quick actions extend the native `FilterConfigPanel`. Copy and Paste call
native panel methods. Use Current Item clones the open slot filter, sets a
one-item whitelist while preserving allowed qualities, and commits through
native `ItemSlot.SetPlayerFilter` replication. The advanced submenu remains owned
by the game.

## Oversized Stacks and Cheats

The stack multiplier scales ordinary stackable item limits while leaving native
limit-1/stateful items and specialized cash unchanged. A non-vanilla value marks
the cheat registry active and the central achievement patch suppresses unlocks.

After lowering a multiplier, an existing oversized stack temporarily reports its
current quantity as its limit. This gives it zero spare capacity while allowing
lossless decrements and a full manual move to an empty valid slot.

Quick move temporarily exposes the current configured limit and lets the native
loop fill compatible partial stacks before valid empty slots. Any untransferred
quantity remains in the source. Sort can normalize oversized stacks only after
exact-copy, slot-count, and aggregate-total preflight checks. An insufficient
plan leaves the original stack untouched.

Never clamp, delete, or world-drop overflow. JSON persistence stores quantity as
an integer without applying `StackLimit`; network quantity is unsigned 16-bit, so
effective capacities are capped at `65535`.

## Startup and Conflict Preflight

Known legacy Backpack mod conflicts are checked before configuration-driven
runtime setup. Inventory Expanded custom IL2CPP types do not use MelonLoader's
automatic registration attributes: `Il2CppTypeRegistrar` registers the complete
parent-before-child type list exactly once only after preflight passes. Harmony
patches are installed explicitly after type registration, and later callbacks
are gated by successful initialization. When a conflict is present, Inventory
Expanded registers no custom types, installs no patches, warns, and remains
fail-closed. The conflict popup is built only from native Unity/Schedule I UI
types once the main-menu canvas is ready, so it does not depend on withheld types.

The current Melon's display name uses **Inventory Expanded - Backpack & Inventory
QoL** because MelonLoader 0.7.3 keys dependency ordering by Melon name and rejects
a second registered Melon with the same name and author. The legacy 1.1.3 DLL
already declares **Inventory Expanded** by the same author. This display-name
separation is bootstrap-only and does not alter assembly, namespace, storage,
save, preference, or migration identifiers.

For a positively identified DLL, the user-approved disable action moves the file
to `Mods\Disabled` without deletion or overwrite. It never mutates saves,
preferences, or legacy configuration, and does not initialize runtime behavior in
the same process; a restart is required. After the move is verified on disk, the
modal offers a clean **Quit Game** action through Unity's normal application quit
path and a **Later** action that only dismisses the reminder. The mod never starts
a shell or helper process to relaunch the game. The verified filesystem move is
already durable when the prompt appears, and restart state is in memory only, so
the next user-launched process performs ordinary filesystem-based preflight.

## Logging Policy

`Logger.Info`, `Warning`, and `Error` are reserved for concise startup state,
actual migration/recovery, conflicts, safety failures, and actionable problems.
Routine successful lifecycle, UI geometry, performance, Sort, transfer,
cash-routing, and shop-overflow evidence uses `Logger.Diagnostic` and is hidden
by default. The user-facing **Diagnostic Logging** preference updates a cached
process-level gate live; hot paths must not read preferences per frame. Guard
expensive state descriptions before constructing them where practical.

MelonLoader supplies the public `[Inventory_Expanded]` source prefix. Inventory
Expanded does not add a second custom prefix.

## Save-Session Lifecycle

Only preferences and cheat settings are process scoped. Players, Backpack owners,
native managers, UI roots, wrappers, raycasters, contextual endpoints, searches,
and transfer context are save-session scoped.

`BackpackMod.OnSceneWasUnloaded` for `Main` is the authoritative shutdown path.
It destroys custom UI and clears bindings without mutating authoritative slots or
the saved Backpack subfile. Loading `Main` resets UI defensively before the new
player spawns. New renderers are keyed to current native manager instance IDs and
must rebuild if wrappers are destroyed or no longer match the hierarchy.

## IL2CPP Safety

- Prefer Harmony positional arguments (`__0`, `__1`) because generated parameter
  names change between game builds.
- Do not detour high-frequency mutation methods for diagnostics; native-managed
  trampoline nesting previously caused save-load access violations.
- Avoid stripped APIs such as
  `RectTransformUtility.CalculateRelativeRectTransformBounds`.
- Use pointer-aware slot identity helpers rather than managed reference equality.
- A successful build does not validate native signatures, wrapper lifetime,
  canvas behavior, or interaction paths; manual in-game testing remains required.

Detailed historical investigation remains under `docs/history/` and should not
be treated as the public entry point.

## Increase Production: Drying Racks, Mixing Stations, Plant Yield, and Deferred Packaging Research

The CHEATS preferences contain independent, default-off **Drying Racks**,
**Mixing Stations**, and **Plant Yield** settings. Each uses the selected Stack
Size multiplier only when its own setting is on and the multiplier is
non-Vanilla. The previous single production preference migrates to Drying Racks
only, preserving its former behavior without opting users into new systems.
Packaging Stations is deferred from 1.3.0: its preference key is retained but
hidden and forced inactive, and the preserved Mk1/Mk2 Harmony adapters are not
compiled into the public DLLs.

The drying-rack adapter remains system-specific. Pineapple's
0.4.7f7 BETA live captures measured native `DryingRack.ItemCapacity = 20` and
output room 160 under Stack Size 8x. With Increase Production enabled, a 140
input started one 140-unit operation and completed into 140 output. This
confirms BETA can exceed the native 20-unit batch limit, but does not yet prove
the 160-unit ceiling, collection conservation or save/reload behavior.

The existing stable-era MAIN trace and preserved BETA 0.4.7f7 interop metadata
show matching rack lifecycle shapes:

| Area | MAIN 0.4.6f13 | BETA 0.4.7f7 |
| --- | --- | --- |
| Rack type | `DryingRack : GridItem` | `DryingRack : GridItem` |
| Capacity | Public serialized `Int32 ItemCapacity`; read by eligibility and start logic | Same field and reads |
| Start methods | `Boolean CanStartOperation()`; `Void StartOperation()` | Same names and signatures |
| Active work | `List<DryingOperation>`; quantity is `Int32` | Same representation |
| Output capacity | `GetOutputCapacityForOperation(DryingOperation, EQuality)` consults the output slot's item capacity | Same method and signature |
| Save model | `DryingRackData` contains separate input/output item sets and a `DryingOperation[]` | Same fields and types |
| Patch compatibility | Same two narrow capacity/start boundaries; current MAIN build verified | Same boundaries; BETA build verified |

The MAIN IL trace shows `CanStartOperation` compares current in-progress quantity
with `ItemCapacity`. `StartOperation` reads that same field, subtracts existing
operation quantity, and bounds the new operation by available input. The
field-based boundary supports the same temporary override design. The published
1.3.0 MAIN comparison baseline reported `0.4.6f13` in `MelonLoader/Latest.log`. Its freshly
generated `Assembly-CSharp.dll` has SHA-256
`19EEF6A62529F140CF749A72B85D76A57EFF9A534B0EF84068BC33195657A472`;
the complete build references are preserved under
`dev-bootstrap/refs/MAIN_046f13` with a per-file hash manifest. The older
`3FFC1E1D86DF284860E106FD43A282F16A6B19912366FE52083361A7A6569C40`
interop hash is historical and is not required for this game version. MAIN
`1.3.0` was built against that preserved release profile. The generated
`StorageEntity` wrapper does not implicitly convert to `IItemSlotOwner`, so
the active-storage owner lookup uses the same IL2CPP `TryCast<IItemSlotOwner>()`
as the storage-open path. Build and static compatibility do not prove live
MAIN rack, inventory, or save/reload safety.

The current public MAIN game is `0.4.7f12`. Its generated
`Assembly-CSharp.dll` SHA-256 is
`F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D`, preserved
with the full reference tree under `dev-bootstrap/refs/MAIN_047f12`. In this
profile `PlayerManager.TryGetPlayerData` has both the asynchronous
`void(string, bool, Action<FullPlayerData>, Action)` overload and the
synchronous `bool(string, bool, out FullPlayerData)` overload; Harmony's
name-only target in published MAIN 1.3.0 is therefore ambiguous. The profile
also removes `Player.Load(PlayerData, string)`, so MAIN must use the shared
0.4.7 `TryGetPlayerData` → `LoadInventory` persistence lifecycle. Candidate
1.3.1 binds the synchronous overload by argument types and retains the existing
identity-bound save resolution, host-only lock restore, and deferred Backpack
hydration. BETA keeps its separate DLL identity and BETA-only storage UI paths.
Compile success and the earlier BETA runtime check do not replace live MAIN
save/reload validation.

For mixers, `MixingStation.GetMixQuantity()` is the common quantity boundary
used by the interface, player task, NPC task and Mk2 base path. It takes the
minimum of both compatible ingredient-slot quantities and `MaxMixQuantity`;
native code consumes that same quantity from both slots and persists it in
`MixOperation`. The patch temporarily scales `MaxMixQuantity` only while this
getter runs, then restores the native field in a finalizer. The server keeps
authority over output creation, product identity and quality, and the native
output-space path. Standard and Mk2 stations share this base-class adapter in
both build profiles.

Native mixer duration is `MixOperation.Quantity × MixTimePerItem`. For an
operation larger than the native cap, the `OnTimePass` adapter adds the extra
elapsed minutes needed for the multiplied quantity to complete in the
native-capacity timeframe. It writes the existing persisted `CurrentMixTime`,
carries integer division remainder between time callbacks, and leaves world
time, update cadence and operation quantity unchanged. On an exception it rolls
back the temporary progress addition. The remainder is transient, so reload can
lose less than one virtual game-minute of progress; this and output capacity
need live validation before release.

The shared IL2CPP rack Harmony boundary temporarily substitutes an effective
`ItemCapacity = native capacity × Stack Size multiplier` while native
`CanStartOperation` and `StartOperation` execute, then restores the original
field even when an exception occurs. A per-thread, per-rack scope shares the
root baseline across re-entrant calls and restores only after the outermost
finalizer; separate sequential UI-check and operation-start calls each begin
from the restored native value. The field is not permanently mutated or saved.
Vanilla Stack Size, a disabled preference, or unsupported multiplier uses the
native path. Native `OnTimePass`, quality progression and completion are not
patched.

Native `StartOperation` subtracts current in-progress quantity from capacity
without clamping. If a setting reduction makes existing operations fill or
exceed the new limit, the mod refuses a new start before native input mutation;
it does not truncate or rewrite existing operations. Native `OnTimePass` waits
for enough output room to commit an entire operation automatically. Therefore,
an operation larger than the newly reduced output stack capacity may remain at
final quality until the multiplier is restored or the player uses the native
partial-claim path. That reduced-multiplier completion behavior has not yet
been tested in game.

The checked-in native IL trace is from the 0.4.7f6 BETA reference and is
version-adjacent to the live 0.4.7f7 capture; the f7 interop references confirm
the patch target names/signatures and compile successfully. Static BETA
metadata represents `DryingOperation.Quantity` as a signed 32-bit integer and
`DryingRackData` holds an array of operations plus separate input/output item
sets. The local IL trace shows completion computes
output room before creating/committing output; zero room retains the operation,
and split completion updates its remaining quantity. This is static, version-
adjacent evidence, not runtime save/reload proof for multiplied quantities.
Do not treat the feature as persistence-verified until the manual test matrix in
`docs/TESTING.md` passes on a copied save.

For player plant harvesting, `HarvestPlant.Update` first resolves its hovered
`PlantHarvestable`, creates a product candidate and calls
`PlayerInventory.CanItemFitInInventory(candidate, ProductQuantity)`. A false
result stays on the native no-harvest path. On success, that same synchronous
task invokes `PlantHarvestable.Harvest`; that method calls
`Plant.GetHarvestedProduct(ProductQuantity)`, discovers the product, inserts it
through `PlayerInventory.AddItemToInventory`, then calls
`Pot.SetHarvestableActive_Server` once. The plant adapter temporarily changes
only that hovered harvestable's `ProductQuantity` between the getter and the
end of `HarvestPlant.Update`, so the native preflight and native item creation
see the same multiplied count. A finalizer restores the field on normal,
early-return and exceptional exits; nested calls use the existing per-thread
capacity-scope manager to avoid multiplying an already-scaled field. The
adapter does not change quality, product ID, inventory insertion, discovery,
bud activation, plant growth, or timing.

The inventory preflight accounts for compatible existing-stack room and empty
slots for the requested quantity. Native `AddItemToInventory` can partially
mutate slots if it encounters a capacity mismatch after the preflight; the mod
relies on the same synchronous task and quantity for both calls. Runtime tests
still need to confirm exact, partial and full-space behavior. `PlantData` stores
seed ID, growth progress and active-bud indexes; multiplied product quantity is
saved through ordinary inventory item data, and the native server callback
deactivates the harvested bud. There is no new plant save field. This is static
save-model evidence, not save/reload proof.

Employee/NPC harvesting is a separate `HarvestPotBehaviour.OnActionSuccess`
path that creates an item through `Plant.GetHarvestedProduct` and routes it via
transit. It is deliberately left at vanilla yield; this setting applies only
to player hand-harvests until the NPC path has its own safe capacity and
conservation model. The task does not alter harvestable visuals or clone buds.

### Packaging Stations (deferred research; not in 1.3.0 public builds)

The following transaction trace documents preserved investigation/source only.
The Packaging Stations preference is hidden and forced off in 1.3.0, and its
production, diagnostic and safety Harmony patches are excluded from both public
DLLs until a later release explicitly includes the feature.

`PackagingStation.PackSingleInstance()` is the narrow native unit boundary for
Mk1. `PackagingDefinition.Quantity = Q` means one packaging item and Q loose
product items become one `ProductItemInstance` in the output slot. The native
method copies the original product item, applies its packaging definition,
updates output, then consumes the one packaging item and Q loose items. The
native `GetState(EMode.Package)` readiness path checks current inputs, existing
output compatibility, and output capacity; MAIN and BETA references encode
Package and CanBegin as enum value 0.

The adapter runs after a native one-package call. It adds nothing unless
Packaging Stations is on and Stack Size is 2x/4x/8x, first verifies that the
native call consumed exactly one packaging item and Q loose items while
increasing output by one, then repeats only the same native method. Before
every extra package it calls the native Package readiness check and snapshots
the slot quantities; it verifies the exact one-unit deltas before attempting
another. This outputs N packaged instances for N packaging items and Q×N loose
items, preserving native copy, quality, package identity, slot callbacks,
statistics and networking. Partial input/output capacity yields only complete
native package units. The adapter does not alter task cadence, timers, or
generic insertion/creation methods. A per-thread owner guard prevents nested
calls from recursively multiplying, and a Harmony finalizer releases the
guard if the native call throws.

The player Mk1 task does not reserve inputs before success; cancellation before
`PackageProductTask.Success` leaves slot quantities native. Employee packaging
calls the same native unit method in a coroutine, so each worker step packages
up to the selected multiplier with the existing coroutine cadence. The station
save data contains its slot contents, but multiplied-output save/reload,
collection conservation and multiplayer authority still need live testing.

Mk2 is excluded explicitly. `PackageProductTaskMk2` computes a planned count
from station quantities, updates the corresponding visuals, and passes the
plan to `PackagingTool`. The tool stages physical objects; its finalization
coroutine calls inherited `PackagingStation.PackSingleInstance()` once per
finalized physical package. The constructor itself does not directly consume
slot quantities. Cancelling deinitializes transient tool objects, while units
already finalized have used the native one-package commit. Active tool/task
state is not part of the station's normal saved `Contents`. Scaling the Mk2
plan needs live validation of physical staging, per-unit commits, cancellation
and reload, so this candidate leaves Mk2 native.

The user reports live gameplay validation for MAIN and BETA drying racks,
mixing stations and Plant Yield. Mixing Station V1 retains its known
visual-only skillcheck presentation issue; this feature does not change it.
Packaging Mk1 is static-trace-backed and managed-tested only; no live package,
collection, save/reload or host-authority evidence is claimed here.

Mod Manager exposes production preferences as normal checkboxes. The
MelonPreferences entry API used by this project provides no enabled/greyed
state, so at Vanilla each checkbox remains visible/editable but has no effect;
its stored value is preserved for a later non-Vanilla selection. Do not add a
third-party Mod Manager UI detour merely to gray them out without a separately
reviewed stable UI boundary.
