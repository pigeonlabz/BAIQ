# Inventory Lifecycle and Quick-Move Diagnostics

This report records the evidence used for OG Backpack 1.17.6 on Schedule I
`0.4.6f13`. Temporary instrumentation observed menu lifecycle state, destination
lists, filter/capacity probes, and runtime weapon item state. It did not detour
inventory mutation methods.

## B lifecycle

### Open path

```text
PlayerBackpack.Update (B key)
  -> PlayerBackpack.Open
  -> StorageMenu.Open(IItemSlotOwner, title, subtitle, callback)
```

After open, the trace showed `StorageMenu.IsOpen=true`, quick move enabled,
cursor visible/unlocked, and the responsive backpack active.

### Broken close path

```text
PlayerBackpack.Close
  -> StorageMenu.CloseMenu
```

`CloseMenu` hid the visual menu and set `IsOpen=false`, but after ten frames the
trace still showed quick move enabled, cursor visible, and cursor unlocked. The
outer lifecycle cleanup had been skipped.

### Native close paths

Escape used:

```text
StorageMenu.Close
  -> StorageMenu.OnClose
  -> StorageMenu.CloseMenu
```

Done used:

```text
StorageMenu.OnClose
  -> StorageMenu.CloseMenu
```

Both disabled quick move and restored the locked, hidden gameplay cursor. The B
fix therefore calls `StorageMenu.Close()` and continues releasing the backpack
accessor afterward.

## Drag/drop validation path

The observed drag behavior and native slot API agree on these destination gates:

1. The target must permit additions (`IsAddLocked=false`).
2. Hard item filters must match.
3. The configured player SlotFilter must match item ID and quality.
4. An occupied slot must be stack-compatible.
5. The target must have remaining capacity.

`ItemSlot.GetCapacityForItem(item, true)` represents gates 2-5. Machine role is
separate: a Chemistry Station output slot reported free filtered capacity for
Acid while also reporting `IsAddLocked=true`, and native drag correctly rejected
it.

## Quick-move validation path

```text
ItemUIManager.SlotClicked
  -> ItemUIManager.GetQuickMoveSlots(source)
  -> OG Backpack contextual route expansion
  -> ItemSlot.TryInsertItemIntoSet(...)
```

Native insertion repeatedly called:

```text
candidate.GetCapacityForItem(item, false)
```

The `false` argument is `checkPlayerFilters`. Consequently, native quick move
treated slots blocked by whitelist/blacklist and quality settings as eligible.
It also accepted add-locked output slots because machine role is not part of the
capacity result.

The reproduced Acid case showed:

- an Acid input slot with only two spaces accepted two items;
- whitelist-mismatched input slots had `capacityFiltered=0`;
- the output slot had `IsAddLocked=true` but positive capacity;
- native quick move moved the remainder to that output slot.

## Final validation rule

OG Backpack keeps native and contextual destination ordering, then filters the
final candidate list centrally:

```csharp
candidate != source
&& !candidate.IsAddLocked
&& candidate.GetCapacityForItem(item, true) > 0
```

Native insertion still performs the transfer. If all valid candidates become
full, the untransferred quantity remains in the source slot. No machine or item
types are hardcoded.

The same final pass applies to:

- Player Inventory -> active interaction;
- Backpack -> active interaction;
- external interaction -> Backpack/native player destinations.

## Cash and dedicated player slot

Schedule I represents physical money as `CashInstance`. Its `Quantity` remains
1; the dollar value is `Balance`. The player's dedicated money destination is a
separate `CashSlot`, appended by `PlayerInventory.GetAllInventorySlots()` after
the ordinary hotbar slots.

`ItemUIManager.GetQuickMoveSlots` deliberately retains cash candidates even when
generic `GetCapacityForItem` returns zero. `StartDragCash` then applies the
special semantics:

1. Empty destination: create a cash instance and call `SetBalance`.
2. Occupied cash destination: calculate remaining balance capacity and call
   `ChangeBalance`, then `ItemSlot.ReplicateStoredInstance`.
3. Dedicated `CashSlot`: use `CashInstance.MAX_BALANCE`; ordinary physical cash
   slots use `CashSlot.MAX_CASH_PER_SLOT`.
4. Source: subtract only the amount accepted, replicate it, or clear it when
   fully consumed.

The 1.17.6 generic validation pass accidentally removed this native exception,
which is why `$800 + $200` could do nothing. Version 1.18.1 retains filtered,
unlocked cash-compatible candidates and selects an existing native `CashSlot`
before ordinary return destinations. A partial dedicated-slot fill is isolated
to that destination so native source-remainder handling preserves overflow. The
mod does not mutate balances itself.

The apparent contextual Backpack freeze was secondary to diagnostics:
`DiagnosticTrace.DescribeObject` read `UnityEngine.Object.name` from a stale or
destroyed IL2CPP wrapper in the `SlotClicked` prefix. That exception escaped
before native `SlotClicked` ran. Object inspection and the complete diagnostic
prefix are now independently guarded.

Dead drops use ordinary `WorldStorageEntity` inventory slots. They do not share
the player's dedicated `CashSlot` type or its larger balance-capacity branch.
The mod exposes those slots to native `StartDragCash` only for an explicit quick
move while that dead drop is active; employee lockers use the same narrow rule.

## SlotFilter representation

`SlotFilter` contains:

- `EType`: None, Whitelist, or Blacklist;
- `ItemIDs`: the restricted item identifiers;
- `AllowedQualities`: accepted `EQuality` values.

`SlotFilter.DoesItemMatchFilter` evaluates the configured filter, and
`ItemSlot.DoesItemMatchPlayerFilters`/`GetCapacityForItem(item, true)` expose that
decision at the slot level. Hard machine restrictions are represented by
`ItemFilter` entries and `DoesItemMatchHardFilters`.

## Filter QoL path

`FilterConfigPanel` exposes native Copy, Paste, Apply to siblings, and Clear
buttons/methods. Version 1.18.0 keeps that advanced UI intact and adds prominent
shortcuts that invoke the native Copy and Paste methods.

**Use Current Item** follows this isolated path:

1. Read `FilterConfigPanel.OpenSlot.ItemInstance`.
2. Clone the current SlotFilter or create a new filter.
3. Change its mode to Whitelist and replace only its item-ID list with the
   occupied item's ID, preserving its allowed-quality list.
4. Commit through the slot's replicated `SetPlayerFilter(filter, false)` path.
5. Refresh the native filter display.

No filter serialization, ownership, sibling propagation, or persistence logic is
reimplemented by OG Backpack.

## Magazine representation

Runtime inspection found M1911 magazines as `IntegerItemInstance` values:

```text
quantity=1, stackLimit=1, ammo values=7,5,7,7
```

The ammo count is per-instance state. Ordinary stacking would merge non-identical
state and risk losing ammunition data. A future safe QoL feature should transfer
ammo between compatible magazine instances, for example `1/7 + 6/7 -> 7/7 +
free slot`, using replicated IntegerItemInstance value changes. No magazine
behavior changed in this release.
