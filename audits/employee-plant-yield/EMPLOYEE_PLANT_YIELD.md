# Employee Plant Yield investigation

Date: 2026-10-04  
Decision: **RED — employee harvesting remains native for 1.3.0.**  
Scope: cultivated weed and coca harvested by Botanist employees. Mushroom Beds,
Packaging, Delivery Capacity, and player harvesting are outside this change.

## Finding

The current player Plant Yield adapter does not affect Botanists. Its Harmony
patch scopes `PlantHarvestable.ProductQuantity` around
`HarvestPlant.Update`, covering the player's capacity preflight and native
harvest call. Botanists use `HarvestPotBehaviour`, select active buds from a
`Pot`, and create a product through `Plant.GetHarvestedProduct(quantity)`.
That path does not read `PlantHarvestable.ProductQuantity`.

The static transaction path is understood, but it is not a safe place to
multiply output yet. `HarvestPotBehaviour.OnActionSuccess` deactivates selected
buds before it starts an asynchronous `MoveItemBehaviour` transfer. The
transfer rechecks destination capacity and attempts to return the item if the
destination no longer fits it. Its native fallback warns that the item will be
lost if the source cannot accept the return. There is no demonstrated
authoritative remainder or rollback contract for a scaled transfer. No
employee live scenario or save/reload test was run in this investigation.

The user-reported live observation is that employee yield remains native (about
12 in the reported test) on MAIN and BETA, while player weed/coca yield remains
multiplied. This observation was supplied by the user; it was not captured by
Pineapple during this investigation.

## Native path

For cultivated pots, the inspected path is:

```text
Botanist.UpdateBehaviour
  -> Botanist.GetPotsForHarvest
  -> HarvestPotBehaviour checks the pot and its assigned destination
  -> GetQuantityToHarvest
  -> GrowContainerBehaviour success flow
  -> HarvestPotBehaviour.OnActionSuccess
  -> Plant.GetHarvestedProduct(quantity)
  -> Pot.SetHarvestableActive_Server(index, false) for selected buds
  -> TransitRoute(Pot, PotConfiguration.Destination)
  -> MoveItemBehaviour.Initialize(..., maxMoveAmount: -1, skipPickup: true)
  -> network-enabled asynchronous transfer
  -> destination insertion, or attempted source return
```

`Botanist.UpdateBehaviour` has separate branches for `GetPotsForHarvest()` and
`GetMushroomBedsForHarvest()`. The cultivated-pot branch uses
`HarvestPotBehaviour`; the separate mushroom behavior is excluded here. The
inspected employee type responsible for the cultivated-pot path is
`Botanist`. The preserved Employee source set contains Botanist, Chemist,
Cleaner, and Packager; only Botanist exposes `GetPotsForHarvest()` and a
`HarvestPotBehaviour` field. No `Handler` type or alternate cultivated-pot
harvest path was found in the inspected profiles.

`GetQuantityToHarvest()` creates a one-unit candidate with
`Plant.GetHarvestedProduct(1)` and returns the minimum of the visible active
harvestable count and capacity checks for the candidate:

- `NPCInventory.GetCapacityForItem(candidate)`;
- `GetDestinationCapacityForItem(pot, candidate)`, which resolves the
  `PotConfiguration.Destination` and calls its
  `ITransitEntity.GetInputCapacityForItem(candidate, botanist, true)`;
- `pot.ITransitEntity.GetOutputCapacityForItem(candidate, null)`.

`DoesPotHaveValidDestination()` only requires a positive destination capacity.
It does not reserve capacity for a later transfer. `GetQuantityToHarvest()` is
also used to determine employee action duration, so changing this method's
result could change employee cadence as well as quantity.

The native quantity is bounded by all four values before action success. If
that minimum is below the number of active buds, `OnActionSuccess()` processes
that many buds and leaves the remaining active indices available for later
work. This is a native partial-bud remainder, not a guarantee that a scaled
product stack can be committed: the asynchronous transfer still rechecks
capacity after those selected buds have been deactivated. A zero destination
capacity is rejected by `DoesPotHaveValidDestination()`; partial, exact-fit,
and post-preflight capacity-change cases have not been exercised live here.

On success, `OnActionSuccess()` recalculates the quantity and calls the plant's
native product factory. Both `WeedPlant.GetHarvestedProduct(int)` and
`CocaPlant.GetHarvestedProduct(int)` request an item instance from the plant's
product definition with that quantity, then set `QualityItemInstance.Quality`
from the plant quality level. This is the native item-identity and quality
path and should be preserved by any future adapter.

Before the item reaches its assigned destination, the method iterates the
selected active harvestable indices and invokes
`Pot.SetHarvestableActive_Server(index, false)`. The source state is therefore
already consumed when the asynchronous movement starts. `MoveItemBehaviour`'s
placement routine rechecks destination input capacity. If the full amount will
not fit, it attempts to return the item to its source; if the source capacity
check also fails, native code logs `Source does not have enough capacity for
item! Item will be lost.` This is a static native failure branch, not an
observed loss in a live run.

## Conservation and implementation decision

The desired invariant would be:

```text
one legitimate Botanist harvest action
  -> source buds deactivated once
  -> exactly the full multiplied product quantity committed once
  -> original product ID and quality preserved
  -> no duplicate harvest/progression callback
```

The present preflight can bound the native quantity, but a multiplier must be
included consistently in NPC carry capacity, destination capacity, the pot's
transit capacity, the candidate instance, and the final movement. Those values
are checked before the asynchronous transfer, while the buds are deactivated
before destination insertion. Capacity can change between those points. Merely
scaling `GetQuantityToHarvest()` would also change `GetActionDuration()` and
does not establish an all-or-nothing output commit. Scaling only the item
factory would make the candidate disagree with its earlier capacity checks.

For these reasons there is no approved scaling boundary in this pass. No
employee preference, Harmony patch, rollback, or output multiplier was added.
Vanilla employee behavior remains active. Do not treat the static loss warning
as proof that loss occurs during ordinary harvesting; it is a credible
failure path that must be closed or ruled out with evidence before scaling.

`HarvestPotBehaviour.OnActionSuccess` has one success callback and a loop that
deactivates the selected bud indices with one server call per index. Its body
contains no explicit XP increment. Whether deactivation of the final bud
triggers a separate completion callback, and how employee work progress is
counted, requires runtime evidence. No callback or progression behavior was
multiplied or modified.

## Authority and persistence

Static metadata exposes `Pot.SetHarvestableActive_Server(int, bool)` and its
FishNet ServerRpc readers/writers. Botanist scheduling occurs through its
networked employee behavior, and `MoveItemBehaviour` is enabled through its
networked path. This points to server-owned plant-state changes and transfer,
but multiplayer execution was not tested. A future patch must scale once on
the authoritative side.

Persistence metadata shows:

- `PlantData.ActiveBuds` stores the plant's active-bud indices;
- `BotanistData.MoveItemData` stores movement state;
- `MoveItemData` has `GrabbedItemQuantity`, `TemplateItemJSON`, `SourceGUID`,
  and `DestinationGUID`;
- `Pot.LoadPlant` hydrates the saved plant data.

These fields show that both source state and transit data have save
representations. They do not prove save/reload safety in the interval after bud
deactivation and before destination commit. No save/load hooks or lifecycle
patches were added.

## Employee support matrix

| Employee | Plant | Native behavior | Destination | Quantity boundary | Scaling status |
| --- | --- | --- | --- | --- | --- |
| Botanist | Cultivated weed | `HarvestPotBehaviour` | Assigned `PotConfiguration.Destination` via `TransitRoute` | Minimum of active buds and candidate capacity checks | RED — leave native |
| Botanist | Cultivated coca | `HarvestPotBehaviour` | Assigned `PotConfiguration.Destination` via `TransitRoute` | Same behavior; `CocaPlant.GetHarvestedProduct(int)` preserves plant quality | RED — leave native |
| Botanist | Mushroom Bed | `HarvestMushroomBedBehaviour` | Separate path | Not traced for this task | Out of scope |
| Chemist, Cleaner, Packager / other employee | Cultivated weed/coca | No matching path found in inspected surfaces | — | — | No support claimed |

## Profile comparison and limits

The focused metadata audit output is under
`InventoryExpanded/.release-work/employee-plant-yield-api/`. It compared MAIN
0.4.6f13, preserved BETA 0.4.7f6, and observed BETA 0.4.7f9. The employee
harvest methods and data signatures match across the three profiles, including
`Botanist.GetPotsForHarvest` / `UpdateBehaviour`, `HarvestPotBehaviour`,
`MoveItemBehaviour`, `WeedPlant`, `CocaPlant`, `Pot`, `PlantData`, and
`MoveItemData`. The inspected MAIN and BETA f6 disassembly also shows the same
transaction shape. The f9 artifact is an observed interop metadata snapshot;
it does not prove f9 native method bodies or runtime behavior. The preserved
BETA build target remains f6. One unrelated Botanist API difference is present:
MAIN exposes its dialogue members as `DialogueContainer`, while BETA f6/f9
exposes them as `Conversation`; none is used by the harvesting path.

Reference assembly hashes:

- MAIN f13: `19EEF6A62529F140CF749A72B85D76A57EFF9A534B0EF84068BC33195657A472`
- BETA f6: `43DF688224AAC7FA9C8AF99234AA5E646334366A3FE5B6DCEA1548DE6B099E31`
- BETA f9 observed metadata: `2EA1E345E7BD32797AA802274051A39FBD3F424C2C3889372A1582FC79B9BD91`

No Pineapple probe, game build, BAIQ build, release candidate, DLL, or player
harvest code was changed for this investigation. The focused metadata scanner
was built successfully. Its output is API-shape evidence only.

Current Pineapple plant scenarios observe `PlantHarvestable.Harvest(bool)` and
the local player's inventory. Botanist harvesting uses
`Pot.SetHarvestableActive_Server` and `MoveItemBehaviour` instead, so those
existing observations do not capture the employee's full transfer or employee
inventory. No employee probe was added because the static failure path is
already sufficient to reject scaling; targeted employee instrumentation
should precede a future runtime safety test.

## Next safe investigation

First add bounded, scenario-only read probes for
`HarvestPotBehaviour.OnActionSuccess` and `MoveItemBehaviour` initialization,
placement, and completion. Do not observe every frame. Then use a copied save
with one Botanist, one cultivated weed or coca pot, and a known assigned
destination. Capture read-only state before selection, after `OnActionSuccess`,
during movement, and after destination insertion:

- plant GUID, product ID, quality, active-bud indices/count;
- native quantity candidate and each capacity result;
- Botanist identity and `MoveItemBehaviour` state/`GrabbedItemQuantity`;
- destination and source inventory totals across all stacks;
- one action/success callback and the exact source-bud delta.

Then test a copied-save capacity-change case to observe native return behavior,
followed by save/reload while the transfer is in progress and after completion.
Do not enable multiplied employee yield until the full candidate amount is
preflighted and an authoritative remainder/rollback is proven. Keep the
existing player Plant Yield path unchanged.
