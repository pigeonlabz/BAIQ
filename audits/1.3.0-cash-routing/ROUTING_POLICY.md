# 1.3.0 cash routing policy audit

Date: 2026-10-04  
Runtime baseline supplied by user: MAIN 0.4.6f13 and BETA 0.4.7f9.  
Build references: MAIN 0.4.6f13 and preserved BETA 0.4.7f6. The observed f9
snapshot is metadata-only and does not replace the f6 build profile.

## Finding

The branch difference was an unconditional BETA exception in
`CaptureCashCompatibleExternalEndpoint`: once an active quick-move slot owner
could be cast to `StorageEntity`, BETA marked the endpoint cash-compatible.
MAIN instead called `IsCashCompatibleExternalStorage`, which recognized exact
`EmployeeHome.Storage`, `DeadDrop.Storage`, `SupplierStash.Storage`, and only a
placeable whose item definition ID was `safe`. This made BETA accept ordinary
closets/racks while MAIN rejected them.

Both branches receive the external candidates from native
`ItemUIManager.EnableQuickMove` and later filter the result of
`GetQuickMoveSlots`. For cash, the contextual destination is restricted to the
captured active external slot list and the exact captured `StorageEntity` owner.
When cash originates in external storage, the destination remains the player's
dedicated `CashSlot`; there is no ordinary-slot fallback. The actual shift-click
continues through the game's quick-move path. The BETA-only Take All cash helper
is separate and unchanged.

## Positive classification

Allow the candidate external storage only when one of these conditions is
true:

1. It is exactly `PlaceableStorageEntity` (not a subclass), and that object's
   `StorageEntity` property is the same native storage object. This covers the
   generic placeable-container category, including safes, closets and racks,
   without item-name matching.
2. Its storage is exactly the reference owned by `EmployeeHome.Storage`,
   `DeadDrop.Storage`, or `SupplierStash.Storage`. A registered Dead Drop may
   also establish the same exact storage identity where the component hierarchy
   cannot be resolved from the slot owner.

The exact placeable type check is deliberate. Metadata inspection found
`BedItem : PlaceableStorageEntity`; a bed's storage is a special gameplay
endpoint and is rejected. The inspected MAIN f13, BETA f6 and observed BETA f9
surfaces show the same relevant type/property/method shapes. Machine classes
such as `PackagingStation`, `DryingRack`, `MixingStation`, `ChemistryStation`,
`LabOven`, `Cauldron`, `BrickPress` and `MushroomSpawnStation` are their own
`GridItem`/`IItemSlotOwner` implementations; they are not the generic placeable
storage class. Unknown owners, other placeable subclasses, and delivery
storage have no positive category and fail closed.

## Type/policy matrix

| Destination | Decision | Reason |
| --- | --- | --- |
| Employee Locker | Allow | Exact active `EmployeeHome.Storage` reference; existing native cash endpoint. |
| Safe | Allow | Exact generic `PlaceableStorageEntity` with matching `StorageEntity`. |
| Storage closet | Allow | Exact generic `PlaceableStorageEntity` with matching `StorageEntity`. |
| Storage rack | Allow | Exact generic `PlaceableStorageEntity` with matching `StorageEntity`. |
| Other generic placeable container | Allow | Same exact class and storage-owner check. |
| Dead Drop | Allow | Exact `DeadDrop.Storage`/registered storage identity; existing native endpoint. |
| Supplier Stash | Allow | Exact `SupplierStash.Storage` identity; existing native endpoint. |
| `BedItem` or other special placeable subclass | Reject | Exact-type guard treats subclasses as special. |
| Packaging Station | Reject | Machine-owned slots; not eligible placeable storage. |
| Drying Rack | Reject | Machine-owned slots; not eligible placeable storage. |
| Mixing Station V1/V2 | Reject | Machine-owned slots; not eligible placeable storage. |
| Chemistry Station | Reject | Machine-owned slots; not eligible placeable storage. |
| Lab Oven | Reject | Machine-owned slots; not eligible placeable storage. |
| Cauldron | Reject | Machine-owned slots; not eligible placeable storage. |
| Brick Press | Reject | Machine-owned slots; not eligible placeable storage. |
| Mushroom Spawn Station | Reject | Machine-owned slots; not eligible placeable storage. |
| Delivery vehicle | Reject | No positive general-storage owner category; delivery loading remains untouched. |
| Unknown `StorageEntity` / `IItemSlotOwner` | Reject | Broad storage interfaces alone do not grant cash eligibility. |

## Slot safety and conservation

The shared destination validation requires:

- `CanSlotAcceptCash()`;
- no native add lock and no BAIQ user lock;
- matching hard and player filters;
- an empty slot or an existing `CashInstance` (never overwrite another item);
- remaining cash balance capacity (`CashInstance.MAX_BALANCE` for the
  dedicated `CashSlot`, otherwise native `CashSlot.MAX_CASH_PER_SLOT`).

`CashRoutingPolicy.GetTransferAmount` fails closed for disallowed destinations,
non-finite balances, zero source balance and full/invalid capacity. The native
quick-move path remains responsible for the shift-click mutation. The managed
policy tests verify the exact accepted amount and conservation arithmetic;
they do not replace an in-game transfer test.

Per transfer, source balance decreases by the same amount destination balance
increases:

```text
source_before + destination_before
= source_after + destination_after
```

Sort remains unchanged and continues to protect cash instances and `CashSlot`s.

## Static API evidence

`MAIN_046f13.json`, `BETA_047f6.json`, and `BETA_047f9_observed.json` beside
this report were produced by the metadata-only `ProductionSurfaceAudit` tool.
They record assembly hashes and focused class relationships/signatures. The
f9 snapshot has the same selected storage, cash-slot, and special-owner API
shapes as f6; it is not f9 native-body or live routing proof. `CashSlot` and
`ItemSlot` expose `CanSlotAcceptCash()` in all three selected profiles.

## Runtime coverage still required

The supplied pre-change live runs prove MAIN safes and BETA general storage
were usable in their then-current builds. The new shared classifier itself has
not yet been deployed or live-tested. On copied saves, test MAIN 0.4.6f13 and
BETA 0.4.7f9 for inventory-to-container and container-to-inventory shift-clicks
on lockers, safes, closets, racks and another generic placeable; test partial,
exact and full balance room, filters and both lock types; then verify the
machine rejection matrix, Dead Drop, Supplier Stash, cash totals and unchanged
Sort behavior. BETA builds still use the preserved f6 profile.
