# Inventory Expanded Architecture and Regression Guide

This document records the invariants behind the working implementation.
Future Schedule I compatibility updates should preserve these boundaries rather
than replacing the backpack with a second inventory model.

## Ownership and persistence

`PlayerBackpack` attaches to the player's backpack `StorageEntity`. Its
`ItemSlots` are the authoritative inventory and existing game persistence remains
responsible for saving their contents.

Important rules:

1. UI slots must bind directly to `PlayerBackpack.GetBackpackSlot(index)`.
2. Pagination must only change bindings and visibility; it must never create page
   inventories or move items between pages.
3. Capacity changes go through `ApplyConfiguredStorageSlots`/`UpdateSize` and
   must not discard occupied slots.
4. Contextual access sends and releases the backpack accessor but does not create
   another storage owner.

## Process and save-session lifetime

Only MelonPreferences/configuration and cheat settings are process-scoped. The
local `PlayerBackpack`, its `StorageEntity`/slots, native menu managers, custom UI
roots and slot wrappers, contextual interaction endpoint, raycaster registration,
search instances, and delayed diagnostic snapshots are save-session scoped.

MelonLoader's `OnSceneWasUnloaded` for the `Main` world scene is the authoritative
session-shutdown hook. It clears all runtime references and destroys any surviving
custom UI roots, but never mutates the native Backpack `ItemSlots` or the saved
`Backpack` subfile. `OnSceneWasLoaded` resets UI state defensively before the new
player is spawned. `PlayerBackpack.OnDestroy` clears its own binding, and a newly
owned component replaces any stale singleton reference if native teardown ordering
causes the unload callback to be missed.

Standalone UI creation is keyed to the current `StorageMenu` instance and
contextual UI creation is keyed to the current `ItemUIManager`. Both renderers
also rebuild when tracked `ItemSlotUI` wrappers are destroyed or no longer match
their slot-container children. A static list count alone is never proof that its
Unity objects are still alive.

## Configuration boundary

`Config/Configuration.cs` owns all settings. The category identifier is
`OGBackpack_01_Settings` and the display name is `Inventory Expanded`. The
identifier remains unchanged for compatibility with existing preferences.

MelonPreferences value types are intentional:

- `int` for capacity, tier, and preferred columns;
- `KeyCode` for the open key;
- `bool` for behavior switches;
- `ERank` for rank selection;
- hidden `float` values for persisted panel geometry.

Migration runs once only when no native OG Backpack category is present. It reads
legacy files but never writes or deletes them. The migration marker is hidden and
versioned so later migrations can be added independently.

Gameplay cheats use the separate `OGBackpack_02_Cheats` category, displayed as
`Inventory Expanded - Cheats`. `CheatSettings`
owns typed cheat values, while `CheatState` is the shared active-cheat registry.
Achievement code consults only the registry, so future cheats do not require
stack-specific achievement logic.

## Stack capacity and achievement safety

The current game obtains ordinary stack capacity from the virtual
`BaseItemInstance.StackLimit`, backed by the item's native definition. The stack
multiplier postfix scales that result with integer arithmetic only when the
native limit is greater than one and the instance is not `CashInstance`.

An existing stack above a newly reduced limit temporarily reports its current
quantity as its limit. This creates zero remaining capacity while allowing native
decrements and splits to reduce it losslessly. No setting change mutates quantity,
redistributes slots, or rewrites persistence.

During quick move only, `OvercapTransferContext` temporarily exposes the true
configured limit for the source item. The native quick-move loop then fills
compatible occupied slots before valid empty slots. Its final source decrement is
redirected through `SetQuantity` because native `ChangeQuantity` rejects a result
that remains above the configured limit. Manual drops onto empty slots do not use
this context and therefore preserve the complete oversized instance unchanged.

Sort creates exact `GetCopy(chunkQuantity)` copies no larger than the configured
limit. It verifies every copy quantity, aggregate item totals, and required slot
count before the first authoritative write. If the normalized plan cannot fit,
the original oversized stack remains untouched.

Persistence does not clamp these values: JSON `ItemData` stores quantity as an
integer, item loaders pass it directly into constructors, and storage hydration
stores the loaded instance directly. FishNet writes quantity as an unsigned
16-bit integer, so multiplied capacities are capped at `65535`.

`AchievementManager.UnlockAchievement` is Schedule I's central unlock path and
contains the actual Steam `SetAchievement` and `StoreStats` calls. Its prefix
returns before that method while `CheatState.IsAnyCheatActive` is true. This is
session-level suppression derived from current preferences, not permanent save
tainting.

## Standalone UI boundary

`UI/ResponsiveBackpackUI.cs` activates only when the opened `StorageMenu` title
is exactly `Backpack`.

It temporarily hides the native backpack grid/title, builds a responsive view,
and temporarily places that backpack screen's native close control in its own
layout. `Close` restores the control's original parent, sibling index, anchors,
pivot, size, and position.

This behavior is standalone-only. It must not run for external storage or machine
screens.

## Contextual UI boundary

`Patches/InventoryInteractionPatch.cs` observes the game's native
`ItemUIManager.EnableQuickMove` overloads. It selects the external slot list,
opens `ContextualBackpackPanel`, and registers backpack slots as an additional
player source.

Routing rules in `TryResolveQuickMoveDestination` are:

- external source -> Inventory and Backpack in the configured priority, with
  Backpack first retained as the backwards-compatible default;
- backpack or player source -> external slots first, then native destinations;
- unrelated sources -> leave the native result untouched.

After routing, `InventoryInteractionPatch.FilterValidQuickMoveDestinations`
sanitizes the complete result. An ordinary-item destination is retained only
when it is not the source, is not `IsAddLocked`, and reports positive capacity
from `GetCapacityForItem(item, true)`. The boolean must remain `true`; native
quick move uses `false`, which bypasses configured player filters. Do not
duplicate machine-specific rules or item IDs here.

Cash is the narrow exception. `CashInstance.Quantity` is always 1 and dollar
capacity is stored in `CashInstance.Balance`, so generic quantity capacity can
report zero for a valid partial fill. Cash destinations instead retain the
native cash route after hard/player-filter checks. Native `CashSlot` is normally
the sole automated destination. Explicit quick moves from player inventory or
Backpack into the actively open `EmployeeHome.Storage` or `DeadDrop.Storage` are
the only ordinary-slot exception; native partial-fill handling leaves any
overflow in the source. Schedule I then
performs the transfer itself through `StartDragCash`, using
`SetBalance` for a new cash instance and `ChangeBalance` plus slot replication
for a partial fill. Never emulate this with stack-size changes.

Slot identity comparisons use IL2CPP pointers (`PlayerBackpack.SlotsMatch`), not
managed wrapper reference equality.

The contextual panel is a sibling overlay under the native `ItemUIManager`
canvas. It owns only its header, grid, footer, resize handle, canvas, and
raycaster. It must not reparent or resize native storage/machine controls.

## Drag and resize coordinates

All contextual geometry uses the parent canvas `RectTransform` coordinate space.
`ScreenPointToLocalPointInRectangle` converts `Input.mousePosition` before any
anchored-position math.

On drag start:

```text
grab offset = local pointer - panel anchoredPosition
```

During drag:

```text
panel anchoredPosition = local pointer - grab offset
```

Do not mix screen pixels, scaled canvas coordinates, world positions, and
`anchoredPosition`. Do not add resolution-specific compensation offsets. Clamp
only after calculating the new position.

## Canvas and input ordering

The relative ordering is:

1. Native ItemUI canvas.
2. Contextual backpack panel (`native + 1`).
3. Native item-info tooltip (`panel + 1`).
4. Native held-item icon (`max(panel + 2, tooltip + 1)`).

Schedule I can reset the item-info canvas when opening it. Therefore tooltip
ordering is reapplied both from the `ItemInfoPanel.Open` postfix and from
`ItemUIManager.LateUpdate`.

When contextual access closes, any added canvas is destroyed and any native
canvas has its original `overrideSorting`, sorting layer, and order restored.
The panel's `GraphicRaycaster` must remain registered with `ItemUIManager` so its
native `ItemSlotUI` children receive hover, click, drag, and drop processing.

## Native UI isolation

`UI/StorageSortButton.cs` creates its own layout-ignored button under the live
storage slot container, anchored below its bottom-right edge. It uses the native
Done control only as a visual size/style template and does not modify or derive
position from it. Keep the control excluded from the slot grid's layout so the
native grid never recalculates around it.

The contextual overlay must leave storage, Mixing Station, Packaging Station,
and other supported native interfaces unchanged apart from the additional panel
and storage Sort button.

## Search projection

`UI/BackpackSearchBar.cs` is shared by the standalone and contextual Backpack
renderers. It stores a text query and projects it to authoritative backpack slot
indices by case-insensitive item display-name matching.

The projection contract is:

1. Preserve authoritative slot index order.
2. Include all configured slots when the query is empty.
3. Include only occupied matching slots while search is active.
4. Page over projected indices, but bind each visible `ItemSlotUI` to the original
   authoritative `ItemSlot`.
5. Never pass projected slots into sorting, persistence, quick move, or storage
   mutation code.

This makes search a display concern only. Clearing the query restores normal
capacity-based pagination without changing inventory contents or order.

## Filter panel extension

`UI/FilterConfigPanelEnhancements.cs` adds a separate quick-action group to the
native `FilterConfigPanel`. It does not modify or replace the advanced submenu.
Copy and Paste call the panel's native methods. Use Current Item clones the open
slot's `SlotFilter`, preserves allowed qualities, creates a one-item whitelist,
and commits through native `ItemSlot.SetPlayerFilter(filter, false)` replication.

## Sorting contract

`Utility/InventorySorter.cs` is the only sorting implementation. All Sort buttons
call it with authoritative slots.

The sorter:

1. Selects eligible authoritative slots.
2. Copies occupied item instances before changing any slot.
3. Sorts by ID, name, and original index.
4. Consolidates only when `CanStackWith` allows it and respects stack limits.
5. Verifies item ID/quantity totals before writes.
6. Writes copies with replication enabled and clears unused eligible slots.
7. Verifies totals immediately after writes and refreshes the relevant UI.

Cash is excluded before the copy/normalize/consolidate plan. Any slot containing
`CashInstance`, and any `CashSlot`, is preserved by exact instance identity and
quantity. Existing misplaced cash remains recoverable, but Sort must never
transform it into a quantity-backed stack or clear a zero-dollar remnant.

`ItemSlot.IsLocked` is not an exclusion. In the current build it represents the
accessor/network lease held while the player has the inventory open. Excluding it
makes every visible inventory Sort button a no-op.

Actual restrictions remain exclusions:

- `IsRemovalLocked`;
- `IsAddLocked`;
- one or more hard filters;
- a non-default player filter.

Sorting always receives the complete backpack or external owner list. Never pass
only visible page slots.

## IL2CPP compatibility notes

- Prefer Harmony positional arguments (`__0`, `__1`) for generated game method
  parameters. Names such as `item` and `itemSlot` changed to `instance` and `ui`
  in Schedule I `0.4.6f13` and caused patch compilation failures.
- Do not detour high-frequency mutation methods such as `ItemSlot.SetStoredItem`
  and `ItemSlot.ItemDataChanged` for diagnostics. Nested native-to-managed
  trampolines caused an `AccessViolationException` while loading saved backpack
  contents.
- `RectTransformUtility.CalculateRelativeRectTransformBounds` is stripped in the
  current IL2CPP build and throws `NotSupportedException: Method unstripping
  failed`. Position independent UI controls using existing RectTransform anchors,
  dimensions, and local positions instead.
- Use structured Unity APIs for hierarchy and coordinate work. Avoid arbitrary
  screen offsets that only fit one resolution or CanvasScaler value.

## Operational logging

User-facing log messages use the `[InventoryExpanded]` prefix. Keep concise
startup and save-session lifecycle messages, stale UI recovery warnings,
configuration migration notices, achievement suppression notices, and actionable
errors. Ordinary slot clicks, Sort operations, tooltip hierarchy, cash routing,
and over-cap transfers must not emit diagnostic dumps.

`Utility/DiagnosticTrace.cs` now contains only the quantity-total comparison used
by Sort's losslessness checks; despite its historical filename, it does not log.
Do not restore mutation-level `ItemSlot` or `StorageEntity` Harmony diagnostics.
Destroyed IL2CPP wrappers can throw from seemingly harmless object inspection,
and previous mutation-level traces interrupted native inventory interaction.

## Regression checklist

Run this checklist after a Schedule I update or inventory/UI change:

1. Load an existing save containing items beyond slot 12 and confirm all 128-slot
   contents remain present.
2. Open the standalone backpack with the configured key.
3. Confirm the title is above the grid and paging controls do not overlap slots.
4. Sort while viewing a middle page; confirm the full inventory is sorted and the
   current page refreshes.
5. Change capacity in Mod Manager & Phone App, save, restart, and confirm the
   value persists.
6. Open a Huge Storage Closet using its door interaction.
7. Drag and resize the contextual panel at the current resolution/UI scale; the
   original header grab point must remain under the cursor.
8. Hover a contextual item and confirm its tooltip renders above the panel.
9. Drag an item over the contextual panel and confirm the held icon remains
   visible above it.
10. Move an item backpack -> storage and storage -> backpack using native drag
    and quick move.
11. Run contextual backpack Sort and confirm all backpack pages are included.
12. Run storage Sort and confirm the native Done button remains in place.
13. Repeat contextual access, transfers, and layout checks on Mixing Station and
    Packaging Station.
14. Exercise any other interaction that calls a materially different
    `EnableQuickMove` overload.
15. Close and reload the save; confirm backpack and external inventory contents
   persisted and MelonLoader contains no Harmony or IL2CPP errors.
16. Without restarting the process, test Save A -> menu -> Save A, Save A -> menu
    -> Save B, and Save B -> menu -> Save A. Confirm standalone and contextual
    grids rebuild, each save shows only its own contents, and lifecycle logs show
    different player/storage/manager IDs with no accumulating roots or raycasters.
