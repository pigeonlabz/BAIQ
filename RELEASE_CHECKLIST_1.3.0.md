# 1.3.0 release candidate: live validation

Use separate copied test saves for MAIN `0.4.6f13` and current BETA `0.4.7f9`.
Install only the matching DLL from
`release-candidates/1.3.0-cash-general-storage-20261004/` and confirm its
SHA-256 matches that directory's `manifest.txt`. Packaging
adapters are excluded from this 1.3.0 candidate; packaging checklists below are
preserved for a later release and are not release gates. Do not publish until
the required live checks for supported features pass. The prior 140-item BETA
drying result does not need repeating.

## Cash-routing baseline and candidate status

The following results were reported for the pre-change builds; they are
baseline evidence, not validation of the new staged candidate:

- MAIN: cash shift-click passed between inventory and employee locker/safe;
  Packaging Station and unrelated workstation rejected it; storage closet did
  not accept cash. Delivery order 320 yielded exactly 320.
- BETA: cash shift-click passed between inventory and locker/safe and also
  worked both ways with ordinary storage; Sort left cash alone. Delivery of two
  item types at 320 each yielded exactly 320 of each.
- Both branches: settings were present, and no obvious lifecycle errors were
  reported. The staged cash policy still requires the copied-save checks below.

## MAIN — required

- [ ] Copy a test save and launch MAIN with `BackpackInventoryQoL.dll` version `1.3.0`.
- [ ] Set Stack Size to 8x and turn **Drying Racks** ON.
- [ ] Put exactly 160 input items in one drying rack; start **one** 160-item operation.
- [ ] Save while it is processing; reload the copied save and confirm the active operation still contains 160.
- [ ] Complete it to exactly 160 output; collect all 160 and account for source, rack, and destination with no unexplained loss or duplication.
- [ ] Enable Mixing Stations with Stack Size 8x. Load matching product and mixer inputs and confirm one operation consumes the same quantity from both slots, up to 8x the native mixer cap.
- [ ] Confirm output quantity, product identity and quality match the native mix result for every consumed unit; verify insufficient input does not create free output.
- [ ] Time a full scaled mixer batch against the same station's native-capacity batch. Confirm the larger quantity completes in the native-capacity timeframe.
- [ ] Save and reload an active scaled mixer operation. Confirm its full quantity and elapsed progress resume, then verify exact output with no loss or duplication.
- [ ] Shift-click cash both ways with an employee locker, safe, closet, rack,
      and another generic placeable. Confirm identical category decisions and
      exact source-plus-destination balance totals.
- [ ] Confirm filtered, locked, full and unknown storage is skipped and that
      every listed production machine and delivery vehicle rejects cash.
- [ ] Confirm unsupported storage rejects cash and non-money quick-move remains intact.
- [ ] Check `MelonLoader/Latest.log` for new patch failures or exceptions.

## BETA — required

- [ ] Launch BETA with only `BackpackInventoryQoL.Beta047.dll` version `1.3.0-beta.47` on a separate copied test save.
- [ ] At Stack Size 8x with **Drying Racks** ON, start exactly one 160-item drying operation.
- [ ] Save while processing; reload and confirm the operation still contains 160.
- [ ] Complete to exactly 160 output; collect it and account for all 160 items without unexplained loss or duplication.
- [ ] Enable Mixing Stations at 8x and repeat the exact ingredient-ratio, quantity, timing, save/reload and output checks above.
- [ ] Repeat the same cash allow/reject matrix used for MAIN; compare source and
      destination balance deltas and verify Sort does nothing to cash.
- [ ] Check `MelonLoader/Latest.log` for new patch failures or exceptions.

## Plant Yield — live-tested on MAIN and BETA

The user reports successful live tests on both branches. The longer checklist
below remains an optional regression matrix; leave an individual edge case
unchecked unless that case was actually exercised.

Use a separate copied test save per branch. Pineapple's plant scenario only
observes the selected pot, native harvest calls, and local player inventory; it
does not harvest, save, reload, or alter plant state. `result.json` remains
INCONCLUSIVE unless a future verified assertion runner is added.

### Capture setup

- [ ] Launch matching MAIN 0.4.6f13 or BETA 0.4.7f7 and confirm the Pineapple
      session `events.jsonl` contains `Probe.PlantHarvestObservation` with
      `installed=true` and the exact `Harvest(bool)` target.
- [ ] Run `.\pineapple.ps1 plants`, identify the test pot by persistent GUID,
      then assign the listed runtime ID to `$PlantId` (for example,
      `[int]$PlantId = 12345`) and set `$Branch` to `main` or `beta`.
- [ ] Start `.\pineapple.ps1 scenario-start -Label "plant-yield-$Branch" -PlantIds $PlantId`.
      Confirm `before.json` contains the selected plant, its save projection,
      buds, inventory slots/totals, and `playerInventoryComplete=true`.
- [ ] Immediately before a harvest attempt run
      `.\pineapple.ps1 scenario-snapshot -Label before-harvest`; after the
      attempt run `.\pineapple.ps1 scenario-snapshot -Label after-harvest`.
- [ ] Save and reload the copied save manually, then run
      `.\pineapple.ps1 scenario-snapshot -Label after-reload` if the same
      Pineapple scenario remains active. If the game/Pineapple process
      restarted, list plants again, select the same pot GUID, start a new
      scenario, and compare by pot GUID plus bud index.
- [ ] At the end run `.\pineapple.ps1 scenario-stop`. Keep `events.ref.json`
      with the referenced session `events.jsonl`.

### Vanilla and multiplier cases

- [ ] Stack Size Vanilla, Plant Yield OFF: harvest once and confirm native
      `ProductQuantity`, item ID/quality, and one bud transition.
- [ ] Enable Plant Yield at 2x, then return Stack Size to Vanilla without
      changing the stored checkbox: harvest once and confirm the setting is
      inactive and quantity remains native.
- [ ] 2x with enough room: confirm multiplied yield and unchanged product ID
      and quality.
- [ ] 2x exact capacity: confirm the whole yield fits once.
- [ ] 2x insufficient capacity and zero capacity: compare before/after
      inventory totals and bud active state; each failed attempt should leave
      inventory unchanged and the bud active. Save/reload and recheck both.
- [ ] 4x with enough room: confirm one native harvest invocation and exact
      multiplied inventory delta.
- [ ] 8x with enough room: confirm one native harvest invocation and exact
      multiplied inventory delta.
- [ ] For an 8x save/reload case, snapshot before harvest and save the copied
      save; harvest, snapshot, save again, reload, and snapshot once more.
      Confirm the same pot GUID
      and bud index remain inactive, inventory quantity/quality persists, and
      no second `Plant.HarvestInvocation` or regenerated yield appears.
- [ ] Verify growth duration/stages, visuals, and employee/NPC harvesting stay
      vanilla. Record multiplayer/host-authority behavior if tested; it remains
      unproven by the offline test suite.

### Required evidence for one successful and one failed-capacity case

- [ ] Record native bud `ProductQuantity`, selected multiplier, expected yield,
      actual inventory delta across all relevant stacks, item ID, item quality,
      bud active state before/after/reload, and the matching
      `Plant.HarvestInvocation` count/caller path.
- [ ] On the failed-capacity case record unchanged inventory totals, active bud
      before/after/reload, and absence of a successful harvest invocation.

## Deliveries — unconditional loss-prevention regression, MAIN and BETA

Delivery Capacity is not exposed in 1.3.0. The existing delivery load stack
alignment remains unconditional at every Stack Size setting. These copied-save
checks validate the safety fix and do not imply expanded order capacity.

- [ ] Order 99 units of one stackable item with native limit 20. Confirm the
      order, payment and `DeliveryInstance.Items` each remain exactly 99.
- [ ] Repeat with Stack Size Vanilla, 2x, 4x and 8x. At arrival, inspect the
      authoritative vehicle storage and confirm safe native chunks are
      20, 20, 20, 20, 19 (or equivalent slot totals summing to 99), with no
      item loss or duplication.
- [ ] Collect all 99 and compare ordered, paid, delivery-record, loaded and
      collected totals. Leave no remainder behind; verify collected is 99.
- [ ] Repeat with more than one item type and with a non-stackable/stateful
      item where available. Confirm identity/state is retained and no generic
      insertion behavior changes.
- [ ] If the game allows a copied-save delivery vehicle with pre-existing
      contents, test matching partial stacks, incompatible stacks, exact room
      and insufficient room. Record native refusal/remainder behavior; do not
      infer safety from the empty-vehicle case.
- [ ] Save/reload with a pending delivery, a loaded vehicle and partially
      collected goods. Compare delivery ID, item quantities, vehicle storage
      and history; confirm no delivery replay or regenerated quantity.
- [ ] Repeat on MAIN and current BETA. BETA builds against f6 references while
      live runtime is f9; the observed f9 interop signatures are not native
      method-body or live delivery proof.

## Deferred research: Packaging Stations — not a 1.3.0 release gate

The detailed packaging tests below are retained for the later Packaging
Stations release. The hidden legacy preference is forced inactive and
packaging Harmony adapters are excluded from the 1.3.0 public DLL. Do not use
the older packaging candidates as 1.3.0 builds.

The candidate scales only Mk1 `PackagingStation.PackSingleInstance()` calls.
Mk2 remains on native behavior. Use a copied save on each branch and record
slot quantities, output quality/packaging identity, inventory totals after
collection, and `Latest.log`; a successful build is not live proof. Pineapple
does not yet have a packaging-specific station selector/collector, so record
the values manually until that read-only adapter is added.

The diagnostic candidate logs one bounded transaction trace per Mk1
interaction: startup target/patch registration, root settings/type/depth,
pre-call baseline, root delta, each extra call and its re-entrant classification,
stop reason, exceptions, and finalizer guard release. Capture the matching log
lines when running this checklist.

- [ ] Stack Size Vanilla with Packaging Stations OFF, then ON: both should
      produce one native package per successful native interaction.
- [ ] At 2x, 4x and 8x with the setting OFF: confirm native package quantity.
- [ ] At each enabled multiplier: confirm one interaction commits up to that
      many packages, each consuming one packaging unit and exactly
      `PackagingDefinition.Quantity` loose product units.
- [ ] Verify package output count, item ID, quality and packaging identity;
      do not expect output-slot quantity to equal loose-product quantity when
      the recipe consumes multiple loose units per package.
- [ ] Test exact inputs, one short on packaging, one short on loose product,
      and partially/fully occupied output. Only complete native recipe units
      that pass native preflight may commit; no free output or unexplained
      resource loss/duplication is acceptable.
- [ ] Test enough output room, exact remaining output room, one package over,
      and zero output room. Verify all committed inputs have a corresponding
      packaged output before collecting.
- [ ] Cancel a Mk1 task before its success interaction; station inputs should
      remain unchanged. Confirm the repeated native transactions keep the
      usual interaction/coroutine cadence and do not alter processing time.
- [ ] Change 8x to 4x, 2x, Vanilla, and turn Packaging Stations OFF before a
      subsequent interaction. Existing slot contents must remain intact; only
      future successes use the new preference.
- [ ] Save/reload with loose inputs, partially occupied packaged output, and
      completed output waiting. Compare station slots and item quality/package
      state after reload. Record this as unproven until it passes in game.
- [ ] Confirm Mk2 throughput remains unscaled. The candidate clamps the native
      task plan to packaged-output room and skips a package commit before input
      mutation when the preflight cannot prove a complete transaction.
- [ ] Repeat on MAIN 0.4.6f13 and the current BETA 0.4.7f9 with their matching DLLs.

### Mk2 output-loss safety mitigation — preserved future validation

The BETA 0.4.7f9 capture documented a loss at output 159/160: one package
committed, then eight following native finalizations consumed inputs without
creating output. Use a fresh copied save for this candidate and inspect
`[PackagingMk2Safety]` and `[PackagingMk2]` lines in `Latest.log`.

- [ ] With output at 159/160, packaging 139+, product 109+, and Q=1, start
      Mk2. Confirm the safe native plan is exactly 1 package/1 loose product,
      the display and physical task represent one package, and the first
      commit changes station slots by -1 packaging, -1 product, +1 output.
- [ ] Confirm no second package commit consumes inputs after output reaches
      160. Any refused commit must leave packaging/product/output unchanged.
- [ ] Repeat with empty output, matching partial output with 2–4 units room,
      exact capacity, full output, incompatible packaged product, mismatched
      quality/packaging, and output/input slot locks. Unsupported/unknown
      preflight must refuse before input mutation.
- [ ] Change output capacity during an active task if practical. Confirm each
      later commit rechecks capacity and a refused physical commit cleans up
      without consuming its station inputs or deleting earlier output.
- [ ] Cancel after 0, 1 and several valid commits. Only completed valid
      commits may affect slots; staged objects must clean up normally.
- [ ] Save with a partial Mk2 task, after cancellation, and with output
      waiting; reload the copied save and compare station slot IDs, qualities
      and quantities. No reload may regenerate a staged package.
- [ ] Repeat on MAIN and current BETA. Vanilla-stack-size loss parity remains
      unknown; do not label this a base-game-only defect before testing it.

Packaging Mk2, chemistry stations, cauldrons, lab ovens, brick presses,
mushroom spawn stations, and Delivery Capacity remain unsupported pending
separate transaction, capacity, and persistence investigations.

## Optional edge check

- [ ] While a large drying-rack operation exists, reduce the multiplier. Confirm the existing quantity stays intact and an unsafe new start is refused without consuming input.
- [ ] While a scaled mixer operation exists, turn its setting off or reduce Stack Size. Confirm its saved quantity and progress are preserved and it completes once without output duplication.

## Release gate

- [ ] Re-hash each tested DLL and compare it with
      `release-candidates/1.3.0-delivery-capacity-deferred-20261004/manifest.txt`.
- [ ] Record the live results, then follow `RELEASING.md` for packaging, final security verification, and manual publication.
