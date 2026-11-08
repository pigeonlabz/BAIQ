# 1.3.0 Increase Production: system investigation

Status: Delivery Capacity investigation complete; no setting added. Packaging
Stations is deferred from the 1.3.0 public feature set. Its prior Mk1/Mk2
implementation, diagnostics, tests and safety research remain preserved in
source but the package Harmony adapters are excluded from the public DLLs.
Drying racks, mixing stations, and Plant Yield are unchanged and have been
live-tested successfully on MAIN and BETA per user report. BETA build references
remain f6 while the observed live runtime is f9.

Final 2026-10-04 completeness pass: Mushroom Bed and the five requested
Production Workstations were investigated and left native. A candidate
Mushroom Bed boundary was found in `ShroomColony.OnSpawnServer`, but its
FishNet spawn/load ordering and no-regeneration behavior need copied-save
runtime proof before it is safe to patch. The workstation systems have no
verified safe quantity boundary. See
[`FINAL_COMPLETENESS_AUDIT.md`](audits/1.3.0-production-completeness/FINAL_COMPLETENESS_AUDIT.md)
for exact paths, the additional Recycler inventory finding, profile comparison,
and feature-freeze recommendation.

MAIN builds against preserved 0.4.6f13 references under
`dev-bootstrap/refs/MAIN_046f13`; `Assembly-CSharp.dll` SHA-256 is
`19EEF6A62529F140CF749A72B85D76A57EFF9A534B0EF84068BC33195657A472`. BETA
builds against its independent 0.4.7f6 reference profile under
`dev-bootstrap/refs/BETA_047`; its `Assembly-CSharp.dll` SHA-256 is
`43DF688224AAC7FA9C8AF99234AA5E646334366A3FE5B6DCEA1548DE6B099E31`. Current
BETA runtime is 0.4.7f9. MAIN and BETA compile against matching named members in
their preserved interops; the BETA ISIL trace is generated from f6 and is
static evidence, not proof of f9 native body identity.

Evidence comes from `cpp2il_beta_047_path_trace/IsilDump/Assembly-CSharp/`,
`cpp2il_shop_trace/DiffableCs/Assembly-CSharp/`, the preserved reference
profiles, the current Harmony adapters, and the managed tests. No live save was
opened or changed for this investigation.

## Native transaction comparison

| System | Native input and start path | Active work, completion, and conservation | Persistence, authority, timing, and visuals | Decision |
| --- | --- | --- | --- | --- |
| Drying racks | `DryingRack.ItemCapacity` gates `CanStartOperation` and `StartOperation`; native start removes the accepted input amount. | `DryingOperation.Quantity` is the active amount and completion yields that quantity. Existing BETA evidence already proves one 140-item operation produced 140 output. | `DryingRackData` stores slots and operations; native loader restores them. The patch temporarily scales only the capacity field around native eligibility/start calls and restores it in a finalizer. Duration, quality timing, storage, output, and visuals stay native. | **GREEN / implemented.** MAIN and BETA use the same scoped adapter. Exact 160 and save/reload still need live checks. |
| Mixing stations / Mk2 | `MixingStation.GetMixQuantity()` returns the minimum of ProductSlot quantity, MixerSlot quantity, and `MaxMixQuantity`. Both UI and physical task paths use that result and reduce both ingredient slots by the same quantity before sending `MixOperation`. | `MixOperation` stores product ID, product quality, ingredient ID, and an integer quantity. Server-side `TryCreateOutputItems` makes the operation quantity through the native output-space check; recipe ingredients are consumed 1:1. | `MixingStationData` saves both input slots, output slot, operation, and `CurrentMixTime`; loader restores that state without normalizing it to the current cap. Native duration is `Quantity * MixTimePerItem`. Mk2's `OnTimePass` calls the base mixer path. The adapter scopes `MaxMixQuantity` only during the native quantity getter; for a saved over-cap operation it advances persisted elapsed progress by `Quantity / native cap`. It does not alter world time, update frequency, operation quantity, output RPC, or quality. | **GREEN / implemented.** Standard and Mk2 share this base boundary on MAIN and BETA. Native-capacity duration, output-full behavior, and save/reload need live tests. |
| Mushroom Bed | `MushroomBed` owns a `ShroomColony` with `BaseShroomYield`, quality, growth, spawn ID, and active alignment indices. Player harvest preflights one `ShroomInstance`, then `GrowingMushroom.Harvest()` inserts one item and removes one server-authoritative alignment. `OnSpawnServer` uses `BaseShroomYield` to ask the native alignment/prefab path to spawn the initial harvestables. | A candidate is to scope `BaseShroomYield` only during fresh server initialization, capped at anchor count. This changes the number of independent native harvestables per new colony, not each harvest transaction. Employee harvest uses a different capacity/yield path. | `ShroomColonyData` persists active alignment indices, growth, quality, and spawn ID. `MushroomBed.Load` creates/assigns a colony before calling `ShroomColony.Load`; precise FishNet spawn-versus-hydration order is unresolved. | **RED / native pending lifecycle proof.** Do not clone harvestable objects or patch a general item factory. |
| Chemistry station | `ChemistryStation` uses `StationRecipe` ingredient alternatives/quantities and product definition/quantity. Player task consumes recipe inputs; NPC uses a separate beaker/start routine. `ChemistryCookOperation` has no batch count. | Server `FinalizeOperation` generates a quality-specific recipe product and commits it; the operation records recipe ID, quality and elapsed time only. | `ChemistryStationData` saves contents, recipe, quality, liquid state, and time. Player/NPC cancellation and partial capacity behavior are not proven. | **RED / deferred.** No persisted transaction count scales every recipe input with its output. |
| Cauldron | Four solid slots plus liquid; native recipe consumes 20 coca leaves and one liquid unit and creates 10 cocaine-base output at input quality. | `HasOutputSpace` tests a 10-unit candidate; `RemoveIngredients` consumes liquid/solids, and server completion commits fixed 10 output. No batch count exists. | Save stores slots, quality, and remaining cook time. Player task and employee coroutine are separate paths. Output room is checked before cooking, not reserved for completion. | **RED / deferred.** No safe common solid/liquid/output boundary. |
| Lab oven | Solid and liquid player tasks plus separate NPC start/finish behaviors create `OvenCookOperation`. Its ingredient quantity and progress are persisted. | Completion outputs operation quantity multiplied by the cookable output ratio. `CanOutputSpaceFitCurrentOperation` checks a one-unit output candidate; output quantity can exceed that preflight. | `LabOvenData` saves operation IDs, quantity, quality and progress. Cook duration is ingredient cookable-module time; trays/shards are part of gameplay/task presentation. | **RED / deferred.** Quantity field is insufficient while capacity preflight and completion disagree. |
| Brick press | `BrickPress` and `UseBrickPress` use product slots, a mould and `FunctionalProduct` objects. Native primary input boundary is 20. | `CompletePress` creates a packaged copy, requests output insertion, then consumes up to 20 matching input units without gating removal on acknowledged commit success. | `BrickPressData` saves slots, not an active multiplied task; press interaction and mould objects are stateful. NPC uses a separate coroutine. | **RED / deferred.** Stateful work and unchecked output commit prevent safe scaling. |
| Mushroom spawn station | Interface preflights one grain bag + syringe then starts `InocculateGrainBagTask`; task stages objects and consumes one of each input. | Success creates one output bag; cancellation cleans staged objects and restores native inputs. No batch count is persisted; employee uses a separate coroutine. | `SpawnStationData` persists slot contents, not active task staging. Concurrent output-room changes and cancellation/commit deltas are untested. | **RED / deferred.** Manual single-unit transaction needs its own all-input/output/cancel boundary. |
| Packaging station / Mk2 | Preserved research: Mk1 `PackagingStation.PackSingleInstance()` copies the loose `ProductItemInstance`, applies its `PackagingDefinition`, increments output by one package, and consumes one packaging item plus `PackagingDefinition.Quantity` loose items. `GetState(EMode.Package)` is the native readiness/capacity/compatibility check. Mk2 starts `PackageProductTaskMk2`; its constructor computes a package plan, then `PackagingTool` stages physical objects. Its finalization coroutine calls inherited `PackSingleInstance()` once per physical package. | A prior candidate adapter verified exact per-unit deltas before repeating Mk1 calls. Its source is retained but excluded from the 1.3.0 DLL. Mk2 remains unscaled; its physical plan, per-object finalization and cancellation were not multiplied. | `PackagingStationData` saves station slots, including packaged `ProductItemInstance` data. Active Mk2 tool/task staging is not in normal station save data. Those persistence findings are static and not live reload proof. | **DEFERRED / no Packaging Stations gameplay adapter in 1.3.0.** Candidate behavior, conservation and save/reload remain unproven. |
| Deliveries | `DeliveryShop.CanOrder()` calls `WillCartFitInVehicle()`. That method counts native-sized chunks per cart item using `ItemDefinition.StackLimit` and compares the total with destination vehicle `StorageEntity.ItemCount` (slot count). The order quantity remains the cart quantity copied into `DeliveryInstance.Items`; no quantity multiplier is involved. | `DeliveryInstance.AddItemsToDeliveryVehicle()` resolves each definition, makes native-limit chunks, decrements its local remainder, then calls void `StorageEntity.InsertItem`. `InsertItem` preflights with `HowManyCanFit`; on insufficient room it warns and returns without a remainder result. The loader does not restore its decremented remainder. The current `DeliveryVehicleLoadContext` keeps `BaseItemInstance.StackLimit` native during that loader to avoid the mismatched merge path. | Delivery records, vehicle data, delivery history and displayed history are separate members of `DeliveriesData`. `DeliveryManager.Load()` restores records and history; active delivery vehicles use normal `VehicleData` storage persistence. Server `OnTimePass` loads items and updates delivery state; player collection uses the vehicle's ordinary storage slots. Runtime save/reload and multiplayer collection parity remain untested here. | **RED / no setting in 1.3.0.** A genuine native capacity gate exists, but scaling only `WillCartFitInVehicle()` would admit paid orders that still produce vanilla chunks and may exceed actual slots; scaling chunks alone re-enters the historical loss path. A safe feature requires one capacity/commit/remainder design across order validation, chunk generation and vehicle storage, so a checkbox would be misleading and unsafe today. Keep the existing loss correction unconditional. |
| Player plant harvest | `HarvestPlant.Update` resolves the hovered harvestable, creates a candidate item and calls `PlayerInventory.CanItemFitInInventory(candidate, ProductQuantity)` before invoking `PlantHarvestable.Harvest()`. The adapter scales only that hovered field for the duration of this same synchronous task frame. | The native preflight and `Harvest()` use the same multiplied quantity. The native path creates, inserts and deactivates the bud once; failed capacity uses the no-harvest path. There is no custom partial/drop path. | `PlantData` stores seed ID, growth progress and active-bud indexes; output quantity is ordinary inventory item data. Employee/NPC harvesting remains vanilla. | **GREEN / user-reported live-tested on MAIN and BETA.** This packaging task does not change Plant Yield. |

## Delivery transaction trace and capacity decision

The paid-order path is:

```text
DeliveryShop cart quantity
  -> CanOrder: balance, active-delivery, destination/loading-dock checks
  -> WillCartFitInVehicle: count definition-sized chunks vs vehicle slot count
  -> SubmitOrder: copy exact cart IDs/quantities into DeliveryInstance.Items
  -> SendDelivery / server ReceiveDelivery; record receipt and debit cart cost + fee
  -> server OnTimePass arrival: AddItemsToDeliveryVehicle
  -> native-definition-sized ItemInstance chunks
  -> vehicle StorageEntity.InsertItem
  -> normal vehicle storage collection
```

`DeliveryShop.SubmitOrder` builds `StringIntPair` entries from each cart
quantity without applying Stack Size. It uses the cart total to calculate
delivery time, creates a delivery record, records a receipt and debits the
cart cost plus delivery fee. `CanOrder` prevents a second active shop delivery,
checks online balance and requires a valid destination/loading dock before the
vehicle-fit check.

For each cart row, `WillCartFitInVehicle` repeatedly subtracts that item's
`ItemDefinition.StackLimit` from its purchased quantity and counts one slot per
chunk. It compares the count to the vehicle storage's `ItemCount`. In the
known example, 99 with native limit 20 requires five vehicle slots. At 2x, an
effective instance limit of 40 would imply three slots only if the fit gate
were changed; the loader and insertion path still use native-sized chunks
under the unconditional safety context, so the actual load still requires five
slots.

The safety correction intentionally does not change the order cap or the
number of purchased items. It scopes `BaseItemInstance.StackLimit` back to its
native value only during `AddItemsToDeliveryVehicle()`, aligning
`StorageEntity.HowManyCanFit/InsertItem` with the `ItemDefinition.StackLimit`
chunks and avoiding the historical partial-stack merge-loss path. It is
unconditional: it does not consult a delivery setting, Stack Size value, or
whether a cheat is enabled.

The remaining transaction gap blocks safe capacity expansion. Native
`InsertItem` is void and returns after warning if the full incoming instance
does not fit; `AddItemsToDeliveryVehicle` already advanced its local chunk
remainder and has no rollback/remainder record to persist. An order-preflight
multiplier without a matching safe storage commit would therefore risk losing
paid goods. A chunk-size multiplier without a redesigned delivery-scoped
insertion boundary would restore the historical mismatch. Mixed-item orders,
pre-occupied delivery storage, partial insertion and vehicle-slot edge cases
were not executed live; no separate setting is shipped until those paths have
a validated commit/remainder contract.

The current MAIN 0.4.6f13 and preserved BETA f6 APIs both expose
`DeliveryShop.CanOrder(string ByRef)`, `WillCartFitInVehicle()`,
`DeliveryInstance.AddItemsToDeliveryVehicle()`,
`StorageEntity.HowManyCanFit(ItemInstance)` and
`StorageEntity.InsertItem(ItemInstance, Boolean)`. Reflection of the observed
BETA f9 interop reports the same signatures. This confirms the inspected API
surface, not the f9 native method bodies; no f9 source trace or live delivery
scenario was run in this task.

## Implemented policy and safety limits

`Drying Racks`, `Mixing Stations`, and player `Plant Yield` are separate
default-off preferences. Each uses the Stack Size multiplier only when its own
setting is on and Stack Size is non-Vanilla. The old `Increase Production`
preference migrates only to `Drying Racks`, preserving the old behavior; new
systems remain off.

For mixers, the mod leaves `MixOperation.Quantity`, both input amounts, product
identity/quality, server output creation, and output-space checks on the native
path. The only start-limit change is a temporary `MaxMixQuantity` override
around `GetMixQuantity()`. The timer policy uses the already-saved
`CurrentMixTime`: for operation quantity `q` above native cap `c`, its
additional elapsed progress is accumulated as `minutes * (q-c) / c`, carrying
the remainder between time callbacks. Together with the native elapsed-minute
increment, a full `q = c * N` batch reaches `q * MixTimePerItem` in the same
real game time as a native `c` batch. The carry is transient; after reload its
loss can affect progress by less than one virtual game-minute. This needs live
save/reload verification.

An active over-cap mixer operation keeps this timing rule if its setting is
turned off or Stack Size is reduced. Its saved quantity and progress are never
clamped. Drying-rack starts retain the existing refusal when current work fills
the newly reduced capacity. Vanilla and OFF keep native start limits for new
work.

Plant Yield multiplies `PlantHarvestable.ProductQuantity` only inside the
player's `HarvestPlant.Update` frame. This keeps the game's
`CanItemFitInInventory(candidate, ProductQuantity)` preflight aligned with
`PlantHarvestable.Harvest` product creation. A complete multiplied yield must
fit; the feature does not insert a remainder into a world drop or implement a
custom transaction. The task finalizer restores the field, including on native
exceptions, while the native harvest/deactivation callback stays single-shot.
Quality, progression, growth, hold time and visuals remain native. Employee/NPC
harvesting uses another path and is not affected. Static plant persistence
stores active buds and normal inventory items, but reload has not been tested.
Pineapple now captures selected plants and active-bud state read-only. A
completed save/reload still requires a manually copied-save test; snapshots do
not invoke save or load lifecycle methods.

Employee Plant Yield was investigated separately on 2026-10-04 and remains
native for 1.3.0. Botanists harvest cultivated weed/coca through
`HarvestPotBehaviour`, which deactivates selected source buds before an
asynchronous `MoveItemBehaviour` transfer. The transfer has a native loss
fallback if the destination and source cannot accept the item. No multiplied
employee yield setting or patch was added. See
[`EMPLOYEE_PLANT_YIELD.md`](audits/employee-plant-yield/EMPLOYEE_PLANT_YIELD.md)
for the full path, profile comparison, and the evidence needed to revisit this
decision. The player Plant Yield path and its live-confirmed behavior were not
changed.

## Packaging Stations implementation boundary (preserved research; deferred)

Packaging Stations is not part of the 1.3.0 public feature set. Its preference
key remains hidden and forced inactive; the preserved Mk1/Mk2 Harmony adapters
are excluded from public DLLs. This section records prior investigation only,
not 1.3.0 support. Achievement suppression continues to follow the existing
Stack Size cheat state.

For Mk1, `PackagingDefinition.Quantity = Q` means one packaging material plus
Q loose product units create one packaged output unit. Therefore N successful
native calls consume N packaging items and Q×N loose items and add N output
packages; output slot quantity is N, not Q×N. `PackSingleInstance` delegates
copying, quality, packaging assignment, slot writes, statistics and network
callbacks to Schedule I. Before each extra call the adapter invokes the native
`GetState(EMode.Package)` preflight (both references encode Package as enum 0,
and `EState.CanBegin` as 0), then commits exactly one native unit. A fresh
preflight naturally stops at insufficient product, packaging or output room.
The synchronous adapter does not mutate station capacity/slot fields, process
timers, or item definitions; its thread-scoped owner guard prevents nested
calls from multiplying again. If the original unit or a later native call does
not show exactly -1 packaging, -Q product and +1 output, the adapter stops and
does not retry.

The diagnostic candidate resolves the exact zero-argument `PackSingleInstance`
target, reports whether its Prefix/Postfix/Finalizer registered, and logs a
bounded root/extra-call trace. The root Prefix captures the authoritative slot
baseline before the native body; only the root owns the expansion loop. Nested
same-station native calls see the owner's guard, execute their native body once,
and cannot start another batch. This instrumentation still needs a live log to
prove the player path enters this target.

The Mk1 player task starts visual/interaction work without consuming the slot
contents; `PackageProductTask.Success` performs one native package commit and
then succeeds the task. Cancellation before success does not consume station
items. The employee coroutine also calls `PackSingleInstance`; it receives the
same per-call multiplier while its native coroutine cadence remains unchanged.
The station's `PackagingStationData.Contents` saves slot item state, but a real
reload with multiplied packaged output has not been tested. Host-authority and
multiplayer behavior also remain unproven at runtime.

Mk2 is intentionally not scaled. `PackageProductTaskMk2` first uses the native
package readiness check, then plans `P = min(packaging slot quantity,
floor(product slot quantity / Q))`, where Q is the packaging definition's
loose-product recipe quantity. It passes P and `P × Q` to `PackagingTool`;
the task constructor does not consume those station slots. No independent
native maximum was found at this planning boundary, so multiplying P would
plan physical work unsupported by the actual resource slots.

The visible number is written by `PackagingTool.UpdateScreen()` from its
`ProductQuantity` field. `Initialize` seeds that field with `P × Q`, and
`DropProduct` decrements it as loose product objects are staged. It is therefore
the remaining loose-product count, not a package count (the two coincide only
when Q is 1). The tool creates/stages the physical product and packaging
objects through its native path; completed physical packages are finalized
through a coroutine which invokes inherited `PackagingStation.PackSingleInstance`
once per unit, then removes/destroys the staged package object. `StopTask`
deinitializes the tool and clears transient staged objects. The native stop
method does not expose whether the stop represents completion or cancellation.

The task plan does not reserve room for all P packages. The BETA 0.4.7f9 live
capture proved the destructive edge: with output 159/160, one native commit
created output 160 and consumed one package plus Q=1 product; eight subsequent
native commits each consumed those inputs without output (final 130 packaging,
100 loose product, 160 output; 9 calls, 1 valid, 8 mismatches, no exceptions).
This proves the loss on the tested BAIQ 8x setup. Whether the same edge occurs
at vanilla stack size is still unproven, though the f6 trace predicts the same
failure whenever a multi-package task starts with only one native output slot
unit free. Static BETA f6 `PackSingleInstance`
increments/adds output, then unconditionally decrements both input slots; its
body does not check the output mutation result. `ItemSlot.ChangeQuantity(+1)`
can refuse a full/locked destination, which explains the observed shape.

The BAIQ Mk2 safety adapter now clamps `PackagingTool.Initialize`'s native
package/product plan to complete recipes that fit the current output slot. For
capacity it copies the loose `ProductItemInstance`, applies the station's real
`PackagingDefinition`, and calls native `GetCapacityForItem(candidate, false)`;
it never inserts or mutates that probe. Existing output compatibility,
quality/product state, effective instance stack limit, filters and locks are
therefore evaluated against the actual packaged representation. The native
station stock visuals are locked during tool initialization, so the adapter
does not bypass that lock or rewrite those visuals; the task screen and staged
objects use the clamped arguments, while the regular task-stop path refreshes
stock visuals. Before every Mk2 `PackSingleInstance`, a second guard checks input
quantities, recipe ratio, removal locks, output room, and output locks. If that
preflight fails or is unknown, Harmony skips the destructive native body, so
the station's authoritative input slots remain unchanged. A bounded warning
identifies the reason. No Mk2 throughput scaling is implemented.

This is a narrow prevention strategy, not a completed runtime proof. A race or
native insertion failure after a successful preflight remains possible. The
f6 finalization coroutine calls `PackSingleInstance`, then destroys/removes the
staged physical package and advances its finalized counter regardless of the
void method's result; thus a refused commit does not strand that individual
object in the normal path. Cancellation of the remaining staged task still
needs live validation. The native task plan clamp and commit guard have managed
policy coverage only.
The BETA live f9 evidence validates the exact previously observed hooks and
failure path; BETA builds still use f6 references. MAIN 0.4.6f13 and BETA f6
expose the same named slot/copy/packaging/commit APIs, but the preserved MAIN
diffable dump has no native method body, so vanilla failure parity is unknown.

`PackagingStationData` persists station slot contents, not the active
`PackageProductTaskMk2`, `PackagingTool`, staged physical-object lists, or
partial tool progress; the loader restores slots and refreshes visuals. This
suggests active Mk2 staging is transient, but actual save/reload behavior has
not been tested. Already committed packages use the ordinary station output
slot save representation. Output-full prevention, skipped-commit cleanup,
partial cancellation, restart and reload need copied-save/runtime validation
before Mk2 can be called safe.

MAIN 0.4.6f13 and preserved BETA 0.4.7f6 references expose matching Mk1
method/slot signatures. Observed BETA f9 interop exposes the same named APIs,
but this does not prove f9 method-body behavior. Packaging adapters are
excluded from 1.3.0 public DLLs; retained source and diagnostic work are for a
later release after separate review and copied-save validation.

The effective-stack delivery regression fix is unchanged and remains active
regardless of the delivery feature setting (no such setting is exposed).
Achievement
suppression remains driven by the existing non-Vanilla Stack Size cheat state;
an Increase Production preference at Vanilla has no effect.

## MAIN / BETA parity

| System | MAIN 0.4.6f13 / 1.3.0 | BETA 0.4.7f6 refs / 1.3.0-beta.47 | Difference |
| --- | --- | --- | --- |
| Drying racks | Supported by the shared scoped patch; builds against MAIN profile. | Supported by the shared scoped patch; prior live 140-item result exists. | No patch difference. |
| Mixing stations / Mk2 | Supported by the shared base-type quantity and time patches; build succeeds. | Supported by the same base-type patches; build succeeds against isolated BETA refs. | Named boundary matches; BETA f6 static bodies do not prove current f9 runtime behavior. |
| Chemistry, cauldron, lab oven, brick press, spawn station | Vanilla behavior; no new setting. | Vanilla behavior; no new setting. | None. |
| Packaging / Mk2 | Research and safety adapters are preserved in source but excluded from the 1.3.0 public DLL. | Same; BETA f9 loss evidence and f6 reference analysis remain preserved. | Deferred from the public release; no packaging gameplay change in this task. |
| Deliveries | Unconditional delivery-load stack-alignment safety fix remains; no Delivery Capacity setting. | Same; f9 interop signatures match f6, but f9 native method bodies and live delivery behavior were not tested. | No behavior change. Capacity expansion is deferred because native preflight and loader/storage commit are not one safe transaction. |
| Player hand-harvest Plant Yield | Existing implementation; user reports successful live test. | Existing implementation; user reports successful live test on 0.4.7f7. | No adapter change in this task. |
| Botanist employee weed/coca harvest | Native behavior; no employee scaling setting. | Native behavior; f6 trace plus f9 API metadata only; no live employee test. | Remains native because buds are deactivated before an asynchronous destination transfer. |

## Remaining release evidence

Managed tests cover 2x/4x/8x caps, mixer timing, active-work preservation,
preference migration, drying-rack restoration/re-entrancy, retained packaging
policy/source research, and the delivery 99-at-native-20 chunk regression.
They do not prove live delivery loading, pre-occupied vehicle behavior,
collection, save/reload or network safety. Packaging checklists remain in the
docs as deferred research, not a 1.3.0 release gate. Delivery Capacity and
other workstations remain outside the candidate.
