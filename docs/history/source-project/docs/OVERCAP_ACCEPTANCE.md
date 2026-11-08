# Over-Cap Stack Acceptance

## Native audit

- `BaseItemInstance.SetQuantity` clamps only an increase that exceeds both the
  reported limit and the current quantity. A decrease from 37 to 12 is retained.
- `BaseItemInstance.ChangeQuantity` rejects any resulting quantity still above
  the reported limit. The normalizing transfer context therefore redirects only
  its over-cap source decrease through `SetQuantity`.
- `ItemSlot.GetCapacityForItem` reads the incoming instance's `StackLimit` and
  subtracts destination quantity after hard filters, optional player filters,
  and stack compatibility.
- Native quick move visits occupied compatible destinations first, then empty
  destinations, creates exact `GetCopy(amount)` instances, and performs one
  final source decrement for the total moved.
- Native empty-slot drag/drop creates a copy for the selected amount and removes
  that exact amount from the source. The ordinary capacity floor therefore moves
  a complete oversized stack intact.
- Representative storable, quality, product, and integer `GetCopy` methods pass
  the requested quantity directly into constructors. Constructors assign it
  without a stack-limit check.
- JSON `ItemData` records quantity as an integer and storage hydration calls
  `SetStoredItem` without normalization. FishNet writes and reads quantity as an
  unsigned 16-bit value without a stack-limit check.

## Manual matrix

Use an ordinary item whose native stack limit is 10. Create quantity 37 under a
higher multiplier, then reduce the multiplier to Vanilla without moving it.

1. Confirm the source remains exactly 37 and accepts no additional items.
2. Save and reload with the 37 stack in the Backpack, player inventory, and a
   storage container. Confirm each reload remains exactly 37.
3. Drag the complete 37 stack to a valid empty slot. Confirm one intact 37 stack
   moves and the source becomes empty.
4. Shift-click 37 into four valid empty destinations. Confirm 10, 10, 10, and 7.
5. Prepare compatible partial destinations and shift-click 37. Confirm partial
   stacks fill first, followed by empty slots capped at 10.
6. Provide exactly 25 valid destination capacity. Confirm 25 moves and exactly
   12 remains in the original source.
7. Provide no valid capacity. Confirm the source remains exactly 37 and no item
   is dropped or created.
8. Repeat quick move Backpack -> storage, storage -> Backpack, player inventory
   -> storage, and storage -> player inventory.
9. Repeat with whitelist/blacklist filters, quality filters, add-locked machine
   outputs, Chemistry Station inputs, Mixing Station, and Packaging Station.
   Invalid destinations must remain unchanged.
10. Sort with at least four eligible slots. Confirm 37 normalizes losslessly and
    total quantity remains 37.
11. Sort with too few eligible slots. Confirm Sort aborts before writes and the
    original 37 stack remains unchanged.
12. Save and reload after partial quick move and after Sort. Confirm all item
    totals and per-slot quantities persist exactly.

Expected logging is quiet during successful transfers and Sort operations.
Failures that abort a Sort before writing should still produce an actionable
`[InventoryExpanded]` error without dumping item pointers or slot contents.
