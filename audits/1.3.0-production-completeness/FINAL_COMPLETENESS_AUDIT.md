# 1.3.0 production completeness audit

Date: 2026-10-04  
Scope: Mushroom Bed and the five requested Production Workstations. Static
review only; no game save was opened, changed, or loaded for this audit.

## Decision

No BAIQ gameplay adapter was added. The five requested workstation systems
remain native for 1.3.0 because none has a verified, bounded quantity
transaction that safely scales its full input/output transformation. Mushroom
Bed has a promising native initial-yield boundary (`BaseShroomYield` in
`ShroomColony.OnSpawnServer`), but saved-colony hydration follows colony
creation in the available static trace. The exact FishNet spawn-versus-load
ordering and duplicate-initialization behavior have not been proven, so the
candidate is not safe to patch yet. These are deliberate RED/deferred results
under the project's no-loss/no-duplication rule, not evidence that the native
systems themselves are broken.

The five workstations are not the complete inventory of item transformation
systems. `ObjectScripts.Recycler` is an additional world machine that processes
trash and ejects cash. It was not investigated as a production feature and is
outside this pass's requested setting. `LaunderingStation` /
`LaunderingOperation` convert business cash over time, and `VendingMachine`
dispenses purchased goods; neither is an item production workstation. Sewer
mushrooms are a separate world-pickup spawner, not a cultivated Mushroom Bed.
These boundaries should be carried into any later broad system inventory.

## Evidence and reference profiles

The metadata-only `ProductionSurfaceAudit` tool reads PE metadata; it does not
load or execute Schedule I assemblies. Its reproducible outputs are
`MAIN_046f13.json`, `BETA_047f6.json`, and `BETA_047f9_observed.json` beside
this report. They record the input assembly path and SHA-256, focused type
inheritance, fields, properties, and method signatures. The f9 snapshot is for
API comparison only.

| Profile | Assembly-CSharp SHA-256 | Matching selected API shapes |
| --- | --- | --- |
| MAIN 0.4.6f13 | `19EEF6A62529F140CF749A72B85D76A57EFF9A534B0EF84068BC33195657A472` | 72 selected classes compared to f6; no gameplay member differences. One compiler-generated mushroom-station coroutine helper has a renamed private method. |
| BETA 0.4.7f6 build reference | `43DF688224AAC7FA9C8AF99234AA5E646334366A3FE5B6DCEA1548DE6B099E31` | Preserved build target and source of the available BETA ILIS traces. |
| BETA 0.4.7f9 observed | hash recorded in its JSON | Same 72 selected class shapes and signatures as f6, including operation/data types. This does not establish native f9 method-body parity. |

Native-body findings below use the preserved BETA f6 ISIL/path traces and
diffable type/data declarations. MAIN and f9 type/member signatures were
compared independently; their method bodies were not proven equivalent by
that comparison. Multiplayer behavior was not run live.

## System matrix

| System | MAIN f13 / BETA f6 / observed f9 API | Operation and persistence model | Player / NPC path | Timing and candidate boundary | Result |
| --- | --- | --- | --- | --- | --- |
| Mushroom Bed | `MushroomBed : GrowContainer`; `ShroomColony : NetworkBehaviour`; `GrowingMushroom : MonoBehaviour`; `HarvestMushroomBedTask`. Shapes match across the three profiles. | Colony has `BaseShroomYield`, growth, normalized quality, spawn ID, and active alignment indices. `ShroomColonyData` persists spawn ID, growth, quality, and active indices. Each physical mushroom is removed from the colony on harvest. | Player task preflights one `ShroomInstance` and runs `GrowingMushroom.Harvest`. Employee `HarvestMushroomBedBehaviour` is a separate path with its own destination-capacity and quantity calculation. | Candidate boundary: scale `BaseShroomYield` only during fresh authoritative `ShroomColony.OnSpawnServer`, bounded by alignment count. It scales the number of native one-item harvestables in a colony, not item quantity per harvest. `MushroomBed.Load` creates/assigns a colony and then calls `ShroomColony.Load`; FishNet spawn/load callback ordering is unresolved. | **RED / native pending lifecycle proof.** |
| Cauldron | `Cauldron : GridItem`; `CauldronData`. Shapes match. | Four ingredient slots; native recipe uses 20 coca leaves and one liquid unit. `RemoveIngredients` removes the liquid and solids and returns selected quality. The active operation stores remaining cook time/quality, not a batch count. Completion creates 10 cocaine-base units and adds them to OutputSlot. Save data stores slots, quality, and remaining time. | Player `CauldronTask` and employee `StartCauldronBehaviour` are different orchestration paths into server start/finish methods. | Fixed native cook time. `HasOutputSpace` checks capacity for 10 output units before starting, but the operation does not reserve output room while it cooks. No single quantity field scales leaves, liquid, and output proportionally. | **RED / native.** |
| Chemistry Station | `ChemistryStation : GridItem`; `ChemistryCookOperation`; `StationRecipe`; `ChemistryStationData`. Shapes match. | Recipes hold explicit alternative ingredient quantities and product definition/quantity. The active operation stores recipe ID, quality, liquid presentation and elapsed time, not a transaction multiplier/batch count. Server finalization builds a product from the recipe, inserts it, clears operation and resets liquid/beaker state. Save data stores inputs/outputs, recipe, quality, liquid color/level, and time. | Player `UseChemistryStationTask` consumes recipe ingredients through its manual task path; employee `StartChemistryStationBehaviour` builds/fills its own beaker and starts separately. Finish is a separate player task/server finalize path. | Recipe cook time stays native. Changing a shared recipe or only the final product quantity would unbalance recipe inputs; a multiplier must cover every recipe ingredient and output together, and its partial/cancel path is not proven. | **RED / native.** |
| Lab Oven | `LabOven : GridItem`; `OvenCookOperation`; `LabOvenData`. Shapes match. | Active operation persists ingredient ID, quality, ingredient quantity, product ID, and cook progress. `GetProductItem(quantity)` can create quantity-bearing output and retains operation quality. `LabOvenData` saves input/output plus those operation fields. | Player has separate `StartLabOvenTask` (liquid) and `LabOvenSolidTask`; NPC uses `StartLabOvenBehaviour` and `FinishLabOvenBehaviour`. Harvest/finalization uses `FinalizeLabOven`. | Cook duration comes from the ingredient's cookable module, not a global tick change. Critical mismatch: `CanOutputSpaceFitCurrentOperation` asks for a one-unit product candidate, while the shatter/completion path requests output for operation quantity × the cookable output ratio and then clears the operation. Inputs/trays/shards also have distinct paths. | **RED / native.** |
| Brick Press | `BrickPress : GridItem`; `BrickPressData`; player `UseBrickPress`; employee `BrickPressBehaviour`. Shapes match. | `HasSufficientProduct` requires 20 primary product units. `CompletePress` copies a stateful `ProductItemInstance`, applies `BrickPackaging`, requests output insertion, then removes up to 20 matching units across input slots. The void/unchecked output commit result is not a safe transaction guarantee. Save data stores slot contents, not an active scaled press plan. Moulds and `FunctionalProduct` objects participate in task state. | Player task and employee coroutine are separate workflows. | Manual press/animation cadence. The native press preflight understands product/quality compatibility, but completion does not establish an acknowledged all-or-nothing output commit before consuming 20. No scaling or object cloning is safe here. | **RED / native.** |
| Mushroom Spawn Station | `MushroomSpawnStation : GridItem`; `SpawnStationData`; `InocculateGrainBagTask`; `MushroomSpawnStationInterface`. Shapes match. | Interface checks required inputs/output room then begins the inoculation task. Task setup stages native syringe/bag objects and consumes one syringe plus one grain bag. Success creates/inserts one output bag and records one completion. Cancellation destroys staged objects and has a native path to restore the consumed slot quantities. Station data saves slot contents; the active manual task/staging is transient. | Player uses `InocculateGrainBagTask`; NPC `UseSpawnStationBehaviour` has a separate coroutine. Applying the resulting spawn to a bed is a later `ApplyShroomSpawnTask`, a different transaction. | Interactive/manual; no station batch quantity exists. Preflight is for native one-unit work; multi-unit resources, output capacity and cancellation restore/commit would all need to scale as one transaction. | **RED / native.** |

## Mushroom Bed harvest trace

The bed is initialized with a spawn definition/colony, then grows through
`ShroomColony` state. Growth progress and quality are colony fields. The colony
tracks each physical mushroom by alignment index; these objects are native
harvestables with `Harvest()` callbacks, not decoration.

For player harvest, `HarvestMushroomBedTask.Update()` resolves the hovered
`GrowingMushroom`, requests `GetHarvestedShroom(1)`, and checks
`PlayerInventory.CanItemFitInInventory(candidate, 1)`. On a successful
interaction it calls `GrowingMushroom.Harvest()` once. The harvest body creates
one quality-bearing `ShroomInstance` (the factory path is inlined here), calls
`PlayerInventory.AddItemToInventory`, removes/destroys the visual harvestable,
and calls `ShroomColony.RemoveShroom_Server(alignmentIndex)` once. That server
RPC updates replicated/serialized active alignment state. A failed capacity
preflight leaves that player's harvest path untouched; partial capacity for a
multiplied stack is untested and the native preflight currently asks for one.

This differs from weed/coca: there is no `ProductQuantity` field on a
`PlantHarvestable` to scope around the same preflight and harvest call. Patching
`ShroomColony.GetHarvestedShroom` alone would scale the player's preflight
candidate but miss the inlined item creation in `GrowingMushroom.Harvest`;
patching the general item factory would affect unrelated creation paths.

Static inspection found a separate candidate boundary. On its first
`ShroomColony.OnSpawnServer`, the server checks `_shroomsInitiallySpawned`, sets
it, then loops `BaseShroomYield` times, selecting an unused alignment and
calling the native `AddShroomAtPosition_Local`. `GetRandomAvailableAlignmentIndex`
returns `-1` after the `_shroomAlignments` positions are used. This gives a
possible bounded adapter that asks the native routine to create more genuine
harvestable objects; it must cap at available anchors and must not clone
objects. Its semantics are per newly initialized colony: one harvest remains
one item, one alignment removal, and one native progression callback. Existing
colony alignments would be preserved when settings later change.

The candidate is still unsafe to enable. `ShroomColony.Load` restores saved
active alignment indices and growth/quality, and sets
`_shroomsInitiallySpawned = true` after adding those indices. The
`MushroomBed.Load` trace calls colony creation/assignment before invoking
`ShroomColony.Load`; the nested colony creation path performs a FishNet network
spawn. The available f6 trace does not establish whether `OnSpawnServer` runs
before hydration, whether saved state is applied before that callback in the
runtime loader, or whether native load behavior already guards this sequence.
If initial spawning runs before hydration, scaled initialization could add
extra mushrooms that are then combined with saved alignment state. Do not patch
until this ordering is established on live BETA and checked independently on
MAIN. The employee path remains separate and native.

The employee behavior is separate. It computes harvest quantity using a
destination-capacity calculation, obtains a candidate through
`GetHarvestedShroom(quantity)`, and has its own success callback. It is not
covered by the player's one-item preflight or by the existing Plant Yield
adapter. Keep employee harvesting native.

The intended conservation baseline for one physical bed harvest is:

```text
one active alignment removed
one quality-bearing ShroomInstance added to inventory
one harvest/progression event
```

No Pineapple runtime mushroom observation or save/reload run was performed in
this pass. The serialized active-alignment model is static evidence only.

## Workstation transaction findings

### Cauldron

The cauldron's input ratio is fixed at one liquid unit plus 20 coca leaves; the
selected input quality is retained for output. `HasOutputSpace()` constructs a
10-unit cocaine-base candidate and checks the output slot against that
quantity. `RemoveIngredients()` removes one liquid unit then consumes up to 20
leaves from its ingredient slots and returns quality. Server start stores
remaining cook time and quality; server completion creates a 10-unit output,
commits it to the output slot, resets fillable contents and clears the running
state. The serialized operation has time and quality but no batch quantity.
Increasing only the output would be free output; increasing only solids or
liquid would break the recipe. A room change while cooking and every employee
failure path need live testing before any future adapter.

### Chemistry Station

`StationRecipe` is the authoritative ratio: each ingredient entry specifies an
allowed item set and quantity, and the product entry specifies its definition
and quantity. The player task validates/matches and removes ingredient
quantities while staging its interaction; the employee behavior uses a
different `FillBeaker`/`StartCook` path. `ChemistryCookOperation` does not
record a batch count. `FinalizeOperation` is server-gated, asks the recipe to
create a quality-specific product instance, commits it to OutputSlot, clears
the operation, resets the beaker liquid/recipe, and updates progression.
The task/cancellation rollback and whether every recipe ingredient can be
proportionally removed before the manual skillcheck are not sufficiently
proved to scale this flow.

### Lab Oven

There are separate liquid and solid player tasks and separate NPC start/finish
behaviors. Both feed an `OvenCookOperation`, whose integer ingredient quantity,
product ID, quality and cook progress are serialized. Completion computes a
product item using the operation quantity and cookable output quantity; the
native cook duration comes from the ingredient's cookable module. The output
preflight, however, asks `GetProductItem(1)` and compares that one-unit
candidate with OutputSlot capacity, while the completion path calls
`GetProductItem(operation quantity × output ratio)`. Completion then clears the
operation after issuing its output write. Until output capacity and commit
failure/remainder semantics are established for all start variants, changing
the persisted quantity creates a credible item-loss path. It is not patched.

### Brick Press

`GetState()` asks `HasSufficientProduct`, which checks for at least 20 primary
product units. It also considers existing output compatibility and quality.
`CompletePress(product)` makes a one-unit copy of the stateful
`ProductItemInstance`, applies the press's `BrickPackaging`, submits the result
to OutputSlot, and then removes 20 matching input units across its two product
slots. The insertion return/commit status is not used to gate that removal.
Mould contents and the `FunctionalProduct` list participate in the physical
task. Save data captures slot contents but no multiplier-aware active press
plan. Repeating press callbacks or cloning mould products is unsafe.

### Mushroom Spawn Station

The interface preflights station inputs and output room before starting
`InocculateGrainBagTask`. Task construction stages one grain bag and one spore
syringe visual and decrements one from each station slot. The success path
creates the resulting one-unit grain-bag instance and submits it to the output
slot; success increments the inoculation statistic. The cancellation path
destroys the staged objects and restores the consumed slot units when the task
has not succeeded. The return status of the output write and exact behavior
when output changes during the manual task are not confirmed. The employee
`UseSpawnStationBehaviour` is a separate task coroutine. There is no persisted
active batch count, and scaling resources/output while retaining one manual
task would need additional proof for cancellation, re-entry, and output room.

## Existing supported feature record

This audit did not change existing adapters. User-reported live validation on
MAIN and BETA remains recorded for drying racks, Mixing Station V1 and V2, and
Plant Yield for weed and coca. The V1 skillcheck ingredient display remains a
visual-only known issue; no gameplay change was made for it. Packaging and
Delivery Capacity remain deferred. The delivery chunking/item-loss safety fix
remains unconditional.

## Classification and feature freeze

| Feature | Classification |
| --- | --- |
| Drying Racks | GREEN; existing implementation, user live-confirmed on both branches. |
| Mixing V1/V2 | GREEN; existing implementation, user live-confirmed on both branches; V1 display issue remains visual only. |
| Weed/Coca Plant Yield | GREEN; existing implementation, user live-confirmed on both branches. |
| Mushroom Bed Yield | RED; a native initial-yield candidate exists, but colony spawn/load ordering and anchor bounds need runtime proof before it can be changed safely. Player/NPC harvesting remains native. |
| Cauldron | RED; no batch count; separate liquid/solid consumption and fixed quantity output. |
| Chemistry Station | RED; recipe transaction and manual/employee paths differ; operation has no batch count. |
| Lab Oven | RED; several input paths and mismatch between one-unit output preflight and quantity-bearing completion. |
| Brick Press | RED; stateful mould/product objects and unchecked output commit before fixed 20-unit input consumption. |
| Mushroom Spawn Station | RED; manual staged objects, one-unit input transaction, output commit and cancellation need a proven scaled boundary. |
| Packaging Stations | Deferred from 1.3.0; prior research/source preserved and package adapters excluded. |
| Delivery Capacity | Deferred from 1.3.0; unconditional delivery-loss protection retained. |

The requested systems are explicitly classified. A 1.3.0 feature freeze can
begin **only if Mushroom Bed Yield and Production Workstations are recorded as
native/deferred and omitted from feature claims**. The release must not imply
that Mushroom Beds or Production Workstations scale. If those systems remain a
release requirement, the freeze should wait for the Mushroom Bed
spawn/hydration proof and separately scoped workstation transaction
investigations, followed by copied-save live tests.

## Future live validation checklist

No live test is possible for scaled behavior until an adapter has a separately
approved safe transaction boundary. For such a future candidate, use a copied
save and capture authoritative Pineapple snapshots before interaction, after
starting, at completion, after collection, and after save/reload.

- **Mushroom Bed:** on a copied save capture `BaseShroomYield`, alignment count, `_shroomsInitiallySpawned`, taken/active indices, and active harvestable count at first planting; capture whether `OnSpawnServer` precedes `ShroomColony.Load`; save/reload and prove no extra alignments are generated. Then test one known active alignment, product ID/quality, inventory totals across all stacks, one harvest vs one removed alignment, exact/partial/insufficient/zero inventory room, failed harvest remains active, successful/failed state after reload, and employee path remains native. Only after proving loader ordering should a multiplied variant be tested.
- **Cauldron:** baseline ratio (20 coca leaves + one liquid -> ten output) at empty, exact, partial and full output; snapshot resources at start and finish; change output room while cooking; verify no time change and reload while cooking/completed.
- **Chemistry Station:** test every distinct recipe ingredient shape (including alternative items and multiple ingredient types); record recipe quantities, output quantity/quality, input deltas at start, cancellation, output-full behavior, finish, and reload. Test player and employee paths separately.
- **Lab Oven:** test every liquid and solid player start, every NPC start/finish path, products with different output ratios, exact/partial/full output room, shatter/collection, operation quantity/progress reload, and timing. Compare the amount checked by preflight with the exact amount committed.
- **Brick Press:** record both product slots, mould/FunctionalProduct identities, output identity/quality, compatibility, exact 20-unit boundary, one-short input, full/incompatible output, cancellation before/after compression, and reload. Do not use cloned press objects.
- **Mushroom Spawn Station:** one grain bag + one syringe -> one output bag; verify the output identity, cancellation restoration, zero/partial/full output room, staged-object cleanup, one completion statistic, employee path, and save/reload during staging.

For every success, compare exact per-type and quality-aware deltas at every
endpoint. For every refusal/cancellation, verify inputs are unchanged or
restored exactly, outputs are unchanged, and native task/visual state does not
replay after reload. Managed model tests cannot replace these live checks.
