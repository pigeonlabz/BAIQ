# Manual Testing Checklist

There is no automated game harness in this repository. Run the relevant checks
below against the exact DLL intended for release and review `Latest.log` for
Harmony, IL2CPP, lifecycle, and quantity-preservation errors.

Record the Schedule I, MelonLoader, Mod Manager & Phone App, and mod versions with
the result.

## Schedule I 0.4.7f12 MAIN compatibility candidate

The published MAIN `1.3.0` fails during Harmony patch discovery on game
`0.4.7f12` because `TryGetPlayerData` has two overloads. Test the staged MAIN
`1.3.1` DLL on a copied/disposable save with only
`BackpackInventoryQoL.dll` active. The locally supplied successful startup log
is from BETA `1.3.0-beta.47`; it does not validate this MAIN candidate.

- [ ] Record game `0.4.7f12`, MelonLoader, Mod Manager & Phone App, and candidate
      DLL versions. Confirm there is exactly one Backpack & Inventory QoL DLL in
      `Mods`.
- [ ] Launch MAIN and confirm the `0.4.7 MAIN build` compatibility log appears,
      initialization completes, and no `HarmonyException: Ambiguous match` or
      player-data patch failure appears.
- [ ] On separate copies of a MAIN-origin save and a BETA-origin save, confirm
      the player inventory and Backpack contents load once, with no missing or
      duplicated items.
- [ ] Check persistent Backpack slot locks and player slot locks across a
      save/reload. Check session storage slot locks still clear on a new session.
- [ ] Switch copied saves A → B → A in one game session. Confirm Backpack
      contents and locks stay with the correct player/save and do not cross over.
- [ ] Transfer representative ordinary items and cash between Backpack,
      inventory, and storage. Compare source and destination counts before and
      after each transfer, including partial stacks and a full destination.
- [ ] Save, fully exit, and reload each copied save. Confirm Backpack contents,
      lock state, stack quantities, and ordinary inventory remain conserved.
- [ ] Review `Latest.log` for save-path rejection, skipped writes, hydration
      retry exhaustion, or other persistence warnings.

Do not use a primary save for first-run compatibility checks. Stop testing and
restore the copy if any count, owner, or save association differs unexpectedly.

## Installation and Settings

- [ ] The development log identifies `Inventory Expanded - Backpack & Inventory
      QoL` with the expected version; the canonical short name remains Inventory
      Expanded.
- [ ] `BackpackInventoryQoL.dll` is the active 1.3.1 test DLL and the old active
      `Backpack.dll` is absent during normal testing.
- [ ] Mod Manager & Phone App shows **Inventory Expanded**, not Settings not found.
- [ ] Backpack Capacity, Backpack Open Key, Phone Backpack controls, contextual
      controls, transfer keybinds, and Cheats use appropriate numeric, keybind,
      toggle, and choice controls in their intended sections.
- [ ] Change Backpack Capacity, save settings, restart, and confirm it persists.
- [ ] A legacy 128-slot configuration migrates without deleting the old file.
- [ ] Migration version 7 resolves missing values per setting while preserving
      explicitly saved current values; a version 6 configuration receives the
      one-time repair without changing complete current settings.
- [ ] Normal defaults enable no cheats.
- [ ] **Diagnostic Logging** appears near the bottom of **INVENTORY & TRANSFERS**,
      defaults Off on a fresh configuration, persists, and does not change the
      configuration migration version.
- [ ] With Diagnostic Logging Off, normal Phone/Backpack use, storage Sort,
      transfers, cash routing, and shop fulfillment do not emit routine success
      traces; startup, migration, conflict, warnings, and errors remain visible.
- [ ] Toggle Diagnostic Logging On without restarting and repeat representative
      Phone, Backpack, Sort, transfer, cash, and shop actions. Confirm their
      concise diagnostics appear, then toggle Off and confirm they stop live.

## Legacy Conflict Safeguard

Primary restart acceptance sequence:

1. [ ] Install known legacy `Backpack.dll` alongside the current
       `BackpackInventoryQoL.dll`.
2. [ ] Launch Schedule I. Confirm MelonLoader reports both assemblies without a
       cyclic-dependency error, duplicate registration rejection, or duplicate
       `Backpack.PlayerBackpack` injection exception.
3. [ ] Confirm **Conflicting Backpack Mod Detected** appears once on the native
       main-menu canvas before a save loads. Inventory Expanded must have
       registered no custom types, installed no Harmony patches, and initialized
       no storage/runtime behavior.
4. [ ] Choose **Quarantine for me**.
5. [ ] Confirm the modal changes to **Legacy Backpack quarantined**, states that
       `Backpack.dll` moved to `Mods\Disabled`, explains that a manual Steam
       relaunch is required, and offers **Quit Game** plus **Later**.
6. [ ] Choose **Later**. Confirm the modal closes, the game remains running, and
       the DLL remains under `Mods\Disabled`.
7. [ ] Repeat the setup, quarantine again, and choose **Quit Game**. Confirm the
       current Schedule I process exits cleanly without launching a helper.
8. [ ] Relaunch Schedule I normally through Steam.
9. [ ] Confirm the old `Backpack.dll` is under `Mods\Disabled` and was not deleted.
10. [ ] Confirm Inventory Expanded passes preflight, explicitly registers its
        required custom type once, installs Harmony patches, and initializes
        normally in the restarted process.
11. [ ] Confirm the conflict popup does not return. There must be no persistent
        preference or marker suppressing detection; the active filesystem state
        remains authoritative.
12. [ ] Load the existing save and confirm Backpack contents and settings remain
        intact.

Additional safety and fallback checks:

- [ ] Pre-create `Mods\Disabled\Backpack.dll`, repeat quarantine, and confirm the
      existing file remains untouched while the active DLL moves to a
      collision-safe destination such as `Backpack (1).dll`.
- [ ] Before quarantining, choose **Open Mods Folder** and confirm Windows
      Explorer opens the active Schedule I `Mods` directory without enabling
      Inventory Expanded.
- [ ] After a verified quarantine, choose **Quit Game** and confirm Schedule I
      closes without scheduling a relaunch or starting PowerShell/helper code.
- [ ] Test an apparent `Backpack.dll` whose assembly identity is not recognized.
      Confirm the popup explains that Inventory Expanded disabled itself for
      safety, offers **Open Mods Folder**, and never offers or performs automatic
      quarantine or restart.
- [ ] In every known and unknown conflict case, confirm saves, inventory data,
      MelonPreferences, `Backpack.cfg`, and `BackpackMod.cfg` are unchanged.
- [ ] With no legacy DLL active, confirm legacy `.cfg` files and OG Backpack
      preference categories alone do not trigger the conflict safeguard.

## Save and Lifecycle

- [ ] Start a new game and confirm the Backpack is created/unlocked as configured.
- [ ] Load an existing save with items beyond slot 12; all contents remain intact.
- [ ] Open with B, close with B, reopen, and confirm cursor/input state is normal.
- [ ] With known Backpack contents, get arrested, complete the release/respawn
      flow, and confirm the Backpack is usable again without restarting.
- [ ] After arrest recovery, confirm the same Backpack contents remain exact,
      only one Backpack UI/storage/controller exists, and B, Phone, and contextual
      access still work without new lifecycle or Harmony errors in `Latest.log`.
- [ ] Die and revive normally after the arrest check; Backpack access is disabled
      while dead and restored on revive as before.
- [ ] Save, return to menu, reload the same save, and confirm contents and UI.
- [ ] Without restarting, test Save A -> menu -> Save B -> menu -> Save A.
- [ ] Each save shows only its own Backpack contents.
- [ ] Standalone/contextual grids rebuild and no roots or raycasters accumulate.
- [ ] Empty and occupied oversized stacks save/reload without quantity loss.

## Standalone Backpack

- [ ] Open and close the Backpack with `B` five times at approximately 40 slots,
      then repeat at 128 slots; subsequent opens remain responsive.
- [ ] With Diagnostic Logging enabled, confirm one `[BackpackPerf]` line is emitted per `B` open, with no duplicate
      line from delegated native `StorageMenu.Open` overloads.
- [ ] Compare `native`, `ensure`, `capture`, `layout`, `wrappers`, `bind`, and
      `activate` timings; report any stage that still scales sharply with capacity.
- [ ] After reopen, every visible slot shows the correct item, quantity, lock
      state, Search projection, and page without duplicate lock controls.

- [ ] The Backpack title sits above the slot grid without overlap.
- [ ] Layout remains usable at the current resolution and UI scale.
- [ ] Previous, page indicator, Next, Search, and Sort remain visible and aligned.
- [ ] Pagination reaches all configured slots, including a 128-slot Backpack.
- [ ] Search matches item display names without moving or altering items.
- [ ] A no-result Search shows no item slots/results without errors or mutation.
- [ ] Clearing Search restores normal capacity-based pagination.
- [ ] Increase and reduce configured capacity; occupied contents remain intact.
- [ ] Sort operates across all pages and refreshes the current page.
- [ ] Sort preserves exact item IDs and total quantities.
- [ ] Sort leaves any cash instance in an ordinary Backpack slot untouched with
      the same dollar balance and slot position.

## Contextual Backpack

- [ ] Open a Huge Storage Closet using its door interaction.
- [ ] The panel appears independently and native slots/Done controls do not move.
- [ ] At minimum size, the title and Search field remain inside the frame, and the
      grid does not overlap the footer.
- [ ] At minimum and narrow sizes, Previous, page, Next, and Sort stay readable
      and remain within panel bounds.
- [ ] Expand near the maximum canvas size; substantially more slots appear while
      header, Search, and footer controls retain their normal size.
- [ ] Narrow/tall and wide layouts recalculate columns, rows, visible-slot count,
      and page count from the usable inner grid area.
- [ ] Dragging preserves the exact title-bar grab point under the cursor.
- [ ] Dragging clamps at screen edges at the current resolution/UI scale.
- [ ] Drag the panel continuously at minimum and near-fullscreen sizes; movement
      remains smooth with Search active and with several locked slots visible.
- [ ] Moving the panel changes position only: grid dimensions, page, slot
      bindings, and panel size remain stable until movement ends.
- [ ] Bottom-right and top-left resize handles preserve their initial pointer grab
      relationship and keep the opposite corner effectively anchored.
- [ ] Both handles obey minimum, near-fullscreen maximum, and screen-edge clamps.
- [ ] Resize slowly within one row/column range; slots are not repeatedly rebound
      until the usable grid crosses a row or column threshold.
- [ ] Saved panel position and near-fullscreen size restore safely after
      close/reopen and restart.
- [ ] Change resolution or UI scale where practical; restored geometry clamps
      into the new canvas without discarding an otherwise valid saved size.
- [ ] Reset Panel Layout restores the default reachable position and size.
- [ ] Search, pagination, and full-Backpack Sort match standalone behavior.
- [ ] Repeat with an active Search and with occupied and empty locked slots.
- [ ] Repeat on a normal storage container and one representative machine.
- [ ] Open the Character screen through the phone, close it, and confirm the
      contextual Backpack disappears immediately; repeat 5-10 times and verify
      normal contextual access still opens afterward.
- [ ] Repeat the Character transition with the standalone Backpack previously
      open and confirm no Backpack canvas remains over the phone/gameplay UI.
- [ ] Tooltips and the held-item icon render above the contextual panel.
- [ ] Native slot hover, click, drag, drop, and quick move remain interactive.

## Phone Backpack

- [ ] With **Show Backpack with Phone** enabled, Home, Messages, and Dealers show
      the portrait Phone Backpack without covering the Phone app.
- [ ] Products and Deliveries show the adaptive landscape presentation, with one
      logical Backpack split around the authoritative Phone/app bounds.
- [ ] Character, Journal, Contacts, Map, Mod Settings, and an unknown app suppress
      the Phone Backpack cleanly.
- [ ] Portrait and landscape views expose the same authoritative Backpack slots;
      page changes and Sort affect the full Backpack, not a copied inventory.
- [ ] Pagination controls and mouse-wheel paging reach all configured slots.
- [ ] Sort, drag/drop, shift-click, and player/Backpack slot locks behave exactly
      as in the standalone/contextual projections.
- [ ] Scrolling over the Phone Backpack does not scroll the underlying Phone app,
      and scrolling over the Phone app does not page the Backpack.
- [ ] Gameplay letter hotkeys remain responsive while the Phone is open; no Phone
      Backpack Search input captures text.
- [ ] Close/reopen the Phone repeatedly and switch between supported portrait,
      landscape, and suppressed apps without stale panels or raycasters.
- [ ] Test representative Interface Scale values including 80% and 100%; panel
      proportions remain comfortable and do not overlap the native hotbar, cash,
      card, management, Discard, Phone, or app UI.
- [ ] At Phone Backpack UI Scale 100%, confirm the presentation matches the
      existing automatically compensated footprint.
- [ ] Test Phone Backpack UI Scale at 50%, 75%, 125%, and 150% in portrait and
      landscape. Controls and slots scale together, safe bounds remain
      authoritative, and insufficient regions reduce capacity or hide safely.
- [ ] Save and restart with a non-default Phone Backpack UI Scale; confirm it
      persists without changing standalone or contextual Backpack scale.
- [ ] Open Character from a supported app and confirm immediate cleanup, then
      return to Phone and confirm the correct projection can open again.

## Transfers and Routing

Test both player inventory and Backpack as sources where applicable.

- [ ] Drag/drop player <-> Backpack preserves item and quantity.
- [ ] Drag/drop Backpack <-> storage preserves item and quantity.
- [ ] Quick move player <-> Backpack routes to valid destinations.
- [ ] With Inventory First selected, an eligible shift-click attempts Inventory
      first and falls back to Backpack when Inventory cannot accept the item.
- [ ] With Backpack First selected, the reverse priority and fallback apply.
- [ ] Quick move Backpack <-> storage routes to valid destinations.
- [ ] Compatible partial stacks fill before valid empty slots.
- [ ] Multiple compatible partial stacks fill in native destination order.
- [ ] A full Backpack leaves the source quantity unchanged.
- [ ] A full player inventory leaves the source quantity unchanged.
- [ ] Full storage leaves the source quantity unchanged.
- [ ] A filtered destination accepts only its configured items/qualities.
- [ ] A mismatched item does not enter a filtered slot.
- [ ] Add-locked and machine output slots never receive quick-moved inputs.
- [ ] With only partial capacity, the exact remainder stays in the source.
- [ ] With no valid capacity, the source remains completely unchanged.
- [ ] Cash moves into the dedicated money slot and tops up partial balances.
- [ ] Cash overflow remains at the source when the money slot is full.
- [ ] With no active storage destination, cash quick move has no ordinary-slot
      fallback when the dedicated money slot is unavailable or full.
- [ ] On MAIN and BETA, shift-click cash both directions between the player
      inventory and an active employee locker, native safe, ordinary storage
      closet, storage rack, and a second ordinary placeable container. Record
      source and destination balances before/after; totals must be exact.
- [ ] Repeat for a partially occupied cash slot, exact remaining balance room,
      full storage, add-locked/user-locked slots, hard/player-filtered slots,
      incompatible occupied slots, and an unknown storage owner. Rejected or
      overflow cash must remain at its source.
- [ ] Confirm a BedItem/special placeable, Packaging Station, Drying Rack,
      Mixing Station V1/V2, Chemistry Station, Lab Oven, Cauldron, Brick Press,
      Mushroom Spawn Station, and any other machine inventory do not appear as
      cash quick-move destinations.
- [ ] Confirm Dead Drop and Supplier Stash remain accepted only when the
      active `StorageEntity` matches the exact storage reference owned by that
      component; arbitrary storage names must not grant eligibility.
- [ ] Sort the closet with cash present and confirm its item, balance, and slot
      position remain unchanged.
- [ ] Press Take All in an open dead drop with items and cash. Confirm valid
      items follow the configured player/Backpack priority and cash goes only to
      the dedicated cash slot, respecting its balance limit and locks.
- [ ] Change Take All Key, repeat with that key, then restore `R` and verify the
      default. Also verify the stable build retains its prior cash handling.
- [ ] With an employee locker open, quick-move cash from player inventory and
      Backpack; the locker receives it before the dedicated money slot.
- [ ] On MAIN and BETA, repeat with safe, closet, and rack destinations and
      confirm the same category decisions and exact balances.
- [ ] Repeat Dead Drop and Supplier Stash tests and confirm their authoritative
      storage ownership and native cash pathways remain intact.
- [ ] Partially fill the active locker/dead drop/supplier stash, then quick-move a larger cash
      balance; native capacity is respected and the exact overflow stays at source.
- [ ] Fill every cash-compatible slot in the active locker/dead drop/supplier
      stash; quick move leaves cash at source instead of falling back to the
      player money slot.
- [ ] Cash moved out of general storage/locker/dead drop uses only the
      dedicated player money slot; no cash fallback to Backpack or machine slots.
- [ ] Repeat drag/drop and quick move with a compatible machine inventory.

## Shop Purchase Routing

- [ ] Inventory has full capacity; purchase lands in player inventory only.
- [ ] Inventory has partial capacity and Backpack has capacity; exact remainder
      lands in Backpack.
- [ ] Inventory and Backpack each have only partial capacity; inventory fills
      first, Backpack fills second, and only the exact remainder reaches native
      shop overflow.
- [ ] Inventory is full and Backpack has capacity; purchase lands in Backpack.
- [ ] Inventory is full and Backpack has only partial capacity; exact remainder
      goes to the native overflow pallet.
- [ ] Inventory and Backpack are both full; vanilla overflow pallet behavior
      remains unchanged.
- [ ] Repeat with stackable purchased items and stateful/non-stackable items.
- [ ] Repeat the full/partial cases with Backpack add locks and filters; invalid
      Backpack slots are skipped without bypassing native restrictions.
- [ ] Repeat representative shop purchases with Diagnostic Logging Off and On;
      destination order is identical in both modes.
- [ ] Repeat with Vanilla/1x and a non-vanilla stack-size multiplier.
- [ ] Save/reload after purchase and account for the exact purchased quantity
      across inventory, Backpack, and overflow.

## Reload Sources

- [ ] Compatible magazine in player inventory keeps native/default priority.
- [ ] Compatible magazine only in Backpack reloads through the native weapon path.
- [ ] Partial compatible magazine only in Backpack is discovered and consumed
      without duplication or loss.
- [ ] Several compatible Backpack magazines with different loaded counts behave
      in Backpack slot order.
- [ ] Incompatible Backpack magazines are ignored.
- [ ] Empty/full Backpack states do not change ordinary inventory reload behavior.
- [ ] Interrupted or cancelled reload does not consume or corrupt a Backpack item.
- [ ] Reload followed by save/reload preserves resulting weapon and magazine
      state.

## Developer Helper: Magazine Consolidation

`MagazineConsolidator` is an internal, currently unexposed helper. It has no
public UI or hotkey and is not a normal 1.2.2 release-acceptance requirement.
Run these checks only when changing or deliberately exposing that helper.

- [ ] Explicit helper only: ordinary Sort does not consolidate loaded magazines.
- [ ] `3/7 + 3/7` becomes `6/7 + 0/7`.
- [ ] `3/7 + 6/7` becomes `7/7 + 2/7`.
- [ ] `2/7 + 3/7 + 6/7` becomes `7/7 + 4/7 + 0/7`.
- [ ] `7/7 + 7/7` remains unchanged.
- [ ] `0/7 + 7/7` remains ammo-conserving and leaves both magazine instances
      present.
- [ ] Different magazine definitions, incompatible ammo types, ambiguous native
      capacities, locked slots, and non-magazine integer items are skipped.
- [ ] Repeat in Backpack and player inventory.
- [ ] Save/reload preserves every magazine instance and exact aggregate ammo.

## Item Safety

- [ ] Normal stackable items retain exact aggregate quantity.
- [ ] Items with different native stack limits retain those differences.
- [ ] Stateful and native-limit-1 items remain non-stackable and preserve state.
- [ ] Oversized stacks remain lossless through movement, Sort, and save/reload.
- [ ] Partial transfers preserve the exact source remainder.
- [ ] No tested path loses, duplicates, clamps, or world-drops an item.

## Native Inventory Tools

- [ ] Storage Sort is visible without moving the native Done button or slot grid.
- [ ] Storage Sort preserves filters, locked roles, items, and total quantities.
- [ ] `R` takes every eligible item from the active supported container while
      respecting filters, locked roles, machine rules, capacity, and exact remainders.
- [ ] `Backslash` sorts the active supported Backpack, player, storage, or machine
      container without changing unsupported interfaces; custom/None bindings persist.
- [ ] Use Current Item creates a whitelist for the item occupying the slot.
- [ ] Copy Filter and Paste Filter use the native filter state successfully.
- [ ] The native advanced filter submenu remains available and functional.
- [ ] Filters persist through close/reopen and save/reload as the game normally does.

## Shared Sort Priority

- [ ] In Backpack, player inventory, storage, and a representative machine, Sort
      groups packaged product instances first, unpackaged product instances second,
      and all other items third.
- [ ] Within each group, existing item-ID/name/source-slot secondary ordering is
      preserved; non-drug products are handled by the same product-instance rule.
- [ ] Sort preserves exact item instances, states, quantities, filters, machine
      roles, user-locked slots, oversized stacks, and cash restrictions.

## User-Locked Slots

Visual controls:

- [ ] Unlocked occupied and empty slots show one compact Lock Slot control.
- [ ] Locked occupied and empty slots show one compact Unlock Slot control.
- [ ] Move the pointer rapidly across player, Backpack, and storage slots; frame
      rate remains stable and lock controls do not duplicate or flicker.
- [ ] Reopen inventories repeatedly; every supported slot still has exactly one
      lock control and one click toggles the lock exactly once.
- [ ] The dedicated money slot never shows a user-lock control.
- [ ] Padlocks do not obscure item icons, quantity, quality, filters, or native
      slot information.

Backpack:

- [ ] Lock an occupied slot, Sort, and confirm the exact item instance, state,
      quantity, and authoritative index remain unchanged.
- [ ] Lock an empty slot, Sort, and confirm it remains empty.
- [ ] Page or Search away and back; the same authoritative slot remains locked.
- [ ] Close/reopen standalone and contextual Backpack views without duplicate or
      stale padlock controls.
- [ ] Save/reload and confirm occupied and empty Backpack locks persist.
- [ ] Reduce and restore Backpack capacity; retained locks do not move and a lock
      beyond the reduced capacity returns at its original index.

Player inventory:

- [ ] Lock occupied and empty player slots and run player inventory Sort; both
      remain exact and reserved respectively.
- [ ] Drag an item out of a locked slot, then place a different item into it; the
      lock remains attached to the slot throughout.
- [ ] Use explicit quick move from a locked slot; movement remains available and
      the emptied source slot remains locked.
- [ ] Save/reload and confirm player slot locks persist at the same indices.

Storage:

- [ ] Lock occupied and empty storage slots, run Storage Sort, and confirm neither
      slot changes.
- [ ] Close/reopen the same storage during the active save session; locks remain.
- [ ] Return to menu and reload; storage locks are cleared and never attach to a
      different entity.

Sort interactions:

- [ ] Test an unlocked packaged drug with a locked ordinary item, then lock the
      packaged drug itself; unlocked packaged-drug bias remains correct.
- [ ] Put matching stacks on either side of a locked slot and confirm Sort neither
      consumes from nor consolidates into the locked stack.
- [ ] Test a locked partial stack beside an unlocked matching stack.
- [ ] Test a locked oversized stack; it is not normalized or copied.
- [ ] Test an unlocked oversized stack with locked empty slots reducing available
      normalization space; an insufficient plan leaves all slots unchanged.
- [ ] Repeat relevant cases at Vanilla/1x, 2x, 4x, and 8x.

Lifecycle:

- [ ] Run Save A -> menu -> Save B -> menu -> Save A without restarting.
- [ ] Player and Backpack locks are specific to each save, and session storage
      locks never leak across either transition.
- [ ] `Latest.log` contains no lock save parsing, Harmony, wrapper-lifetime, or
      Sort safety errors.

## Representative Interfaces

Repeat contextual visibility, drag/drop, quick move, filters, held-item layering,
and native-layout checks on:

- [ ] Huge Storage Closet or another storage container.
- [ ] Mixing Station.
- [ ] Packaging Station.
- [ ] Chemistry Station.
- [ ] Brick Press.
- [ ] Any newly supported interaction using a materially different native UI path.

## Oversized Stacks and Cheats

With multiplier 4x, create a quantity 37 stack whose current limit becomes 10
after returning to Vanilla/1x.

- [ ] Lowering the multiplier leaves quantity 37 unchanged.
- [ ] The oversized stack reports zero available capacity.
- [ ] Manual move to a valid empty slot preserves all 37 together.
- [ ] Quick move distributes 10 + 10 + 10 + 7 when space permits.
- [ ] Compatible partial stacks fill before empty slots.
- [ ] If destination capacity is 25, exactly 25 moves and 12 remains.
- [ ] A full inventory aborts safely with all 37 preserved.
- [ ] Sort normalizes only when enough valid slots exist.
- [ ] Sort preserves the oversized source when normalization cannot fit.
- [ ] Repeat the cases across Backpack, player inventory, and storage.
- [ ] Save/reload retains every oversized quantity exactly.
- [ ] Vanilla/1x, 2x, 4x, and 8x produce the expected effective limits.
- [ ] Native per-item stack-limit differences remain proportional at each value.
- [ ] Stateful and native-limit-1 items remain protected at every multiplier.
- [ ] Achievement unlocks are suppressed while any cheat is non-vanilla.
- [ ] Returning all cheats to Vanilla re-enables achievement unlocks.

## Increase Production: Drying Racks (MAIN and BETA)

Use a copied/test save on each independent target (current MAIN 0.4.7f12 and
BETA 0.4.7f7). Pineapple capture and managed policy tests do not replace these
runtime checks. Record authoritative input, all active operation quantities,
output slot quantity/capacity, and the destination after collection.

- [ ] With Stack Size Vanilla, confirm Drying Racks has no effect.
- [ ] With Stack Size 2x/4x/8x and Drying Racks off, confirm native
      `ItemCapacity` behavior remains unchanged.
- [ ] With Drying Racks on, confirm new operation capacity is native
      `ItemCapacity × 2/4/8` (20/40/80/160 for the observed rack).
- [ ] Confirm drying duration and quality progression match vanilla for the
      same rack, temperature and starting quality.
- [ ] Check empty, partly occupied, exact remaining output capacity, one item
      short, and zero output room. A refused/partial completion must retain the
      exact operation remainder and never consume output twice.
- [ ] At final quality, test a partial manual claim, collect it, then finish the
      remainder. Account for input, in-progress quantity, rack output and
      collected destination together.
- [ ] Start a 160-unit operation at 8x, then reduce to 2x and Vanilla. Confirm
      the operation quantity remains 160, input is unchanged, and a new start is
      refused without adding quantity back to input. Restore 8x and confirm the
      operation can complete normally; separately test native partial claims
      while output capacity is lower.
- [ ] Repeat save/reload with an empty rack, active multiplied operation,
      completed output waiting, and reduced multiplier. Compare rack input,
      operation quantities/quality/time, output and collected destination.
- [ ] Confirm Drying Racks defaults Off and achievements remain
      suppressed under the existing non-Vanilla Stack Size cheat rules.
- [ ] Test with employees assigned to a rack; their rack-filling decisions are
      not expanded by this first manual StartOperation/CanStartOperation patch.

## Increase Production: Mixing Stations (MAIN and BETA)

Use a copied save for each target and compare against a native-capacity batch
using the same recipe, station and elapsed-time conditions. Standard and Mk2
stations share the base mixer path; include both where available.

- [ ] Confirm Mixing Stations defaults Off and has no effect at Vanilla or when
      its setting is off.
- [ ] At 2x, 4x and 8x, verify `GetMixQuantity()` never exceeds both matching
      input quantities or the multiplied native maximum.
- [ ] Confirm each accepted batch consumes equal amounts from both input slots
      and persists that full quantity in one native `MixOperation`.
- [ ] Confirm output count, product identity and quality match the native
      operation for the consumed inputs; insufficient inputs create no output.
- [ ] Compare a full multiplied batch's elapsed time to the same station's
      native-capacity batch; the larger batch should finish in the
      native-capacity timeframe without changing station update cadence.
- [ ] Test full and partly occupied output slots, including a quantity that
      exactly fills remaining capacity and one that exceeds it. Confirm no
      output is lost, duplicated or committed twice.
- [ ] Save/reload an active scaled operation. Confirm its quantity and progress
      resume and it creates output exactly once.
- [ ] Reduce Stack Size or turn Mixing Stations off during scaled work. Confirm
      existing quantity/progress remain intact and complete once.
- [ ] Repeat on standard and Mk2 stations and on each branch that will ship.

## Increase Production: Plant Yield (player hand-harvest; MAIN and BETA)

Plant Yield has been live-tested successfully on MAIN and BETA per user report.
The checks below remain a regression matrix; they do not replace that recorded
feature-level validation.

Use copied saves. This candidate adjusts `PlantHarvestable.ProductQuantity` only
for the synchronous `HarvestPlant.Update` frame, covering the native inventory
preflight and harvest commit. Employee/NPC harvesting remains vanilla.
Pineapple can capture selected plants and active buds read-only; a disk save and
subsequent reload must still be performed manually on a copied save.

- [ ] With Plant Yield OFF, compare vanilla plant yield at Stack Size Vanilla,
      2x, 4x and 8x.
- [ ] With Plant Yield ON, confirm Vanilla still has no effect; at 2x/4x/8x
      confirm exact multiplied quantity and unchanged item ID/quality.
- [ ] Confirm an exact-capacity inventory accepts one full multiplied harvest.
- [ ] Confirm partial and full inventory capacity blocks harvest without item
      movement and leaves the bud active.
- [ ] Confirm a successful harvest calls the normal bud-deactivation path once;
      repeat the interaction and confirm it cannot grant output again.
- [ ] Save and reload after a multiplied harvest. Compare inventory quantity
      and active-bud state; confirm output is neither lost nor regenerated.
- [ ] Confirm plant growth/care time, visuals, interaction hold time and
      employee/NPC plant harvesting remain unchanged.
- [ ] If testing multiplayer, confirm which peer owns inventory insertion and
      the bud-deactivation RPC; this is not covered by managed tests.

## Deferred research: Packaging Stations (not part of 1.3.0)

The following packaging guidance and safety analysis is preserved for a later
release. The 1.3.0 public DLL hides and disables the legacy preference and does
not compile the packaging Harmony adapters. These checks are not a 1.3.0
release gate; do not interpret prior candidate behavior as current public
support.

Earlier experimental packaging adapters repeated Mk1 native
`PackSingleInstance()` transactions. The 1.3.0 DLL excludes those adapters.
Mk2 throughput was not scaled; earlier candidate source includes a separate
plan clamp and pre-commit safety guard. Before reviving either adapter, use a
copied save and compare the station input/output slots and player inventory
after collection. No live Pineapple packaging collector exists yet. For recipe ratio Q,
each completed native unit consumes one packaging item and Q loose products
and commits one packaged output instance.

The prior diagnostic candidate logged one bounded trace per Mk1 transaction:
resolved patch registration, setting/multiplier/type/depth, root baseline and delta,
reentrant nested calls, each extra native unit, stop reason, exceptions, and
finalizer guard release. Capture the `[PackagingProduction]` lines during live
testing; at 8x the adapter can make at most seven nested native calls.

- [ ] Confirm OFF and Stack Size Vanilla leave one native package per successful
      interaction. With the setting OFF, repeat at 2x/4x/8x and confirm native
      behavior.
- [ ] At 2x, 4x and 8x with Packaging Stations ON, confirm one success commits
      up to the selected number of packages using repeated native unit calls.
- [ ] Record the packaging item ID/count, loose product ID/quality/count,
      `PackagingDefinition.Quantity`, output ID/quality/packaging identity,
      and all stack quantities before and after each interaction.
- [ ] Test exact input, one packaging unit short, one loose product unit short,
      and amounts that allow K complete recipes but not K+1. Only K complete
      native transactions may commit.
- [ ] Test empty output, partial output, exact remaining capacity, one unit
      over capacity, severe shortage and zero output room. Confirm each new
      native preflight stops safely and no input is consumed without one output.
- [ ] Verify one package remains one output-slot unit even when its recipe
      consumes Q loose items; the invariant is `packaging consumed = packages
      created` and `loose product consumed = Q × packages created`.
- [ ] Verify the native product ID, quality, and packaging state are preserved.
      No custom item creation, global stack change, or generic insertion patch
      should appear in the execution path.
- [ ] Cancel a Mk1 task before success; input slots must remain unchanged.
      Confirm the added package units do not change the normal interaction or
      employee coroutine cadence.
- [ ] Change Stack Size 8x to 4x, 2x or Vanilla and turn the setting OFF before
      the next interaction. Existing station/output items remain intact; only
      future successful interactions use the new target.
- [ ] Save/reload with loose inputs and with packaged output waiting. Compare
      slot quantities, product quality and packaging identity. Record a failure
      or uncertainty without claiming persistence proof.
- [ ] Confirm Mk2 throughput remains native at each multiplier and setting;
      the plan clamp is a capacity safety measure, not scaling.
- [ ] Repeat on MAIN 0.4.7f12 and BETA 0.4.7f9. BETA still builds against f6
      references, so confirm both safety hooks resolve in the live f9 log.

Mk2 throughput, chemistry stations, cauldrons, lab ovens, brick presses,
mushroom spawn stations, and Delivery Capacity remain unsupported pending
separate transaction, capacity, and persistence investigations. Mk2 currently
has only the separate output-loss safety candidate described below.

### Preserved Mk2 output-loss mitigation research (not scaled or shipped)

The Mk2 task plan is bounded by safely measured output room before native tool
initialization. The capacity probe clones the loose product, applies the
station's actual packaging definition, and uses native `GetCapacityForItem`;
the probe is never inserted. The native plan is reduced to the smaller of its
own package/product plan, available complete input recipes, and output room.
The native station stock visuals remain under their task lock; the screen and
staged work use the clamped arguments, and normal task-stop refreshes the stock
visuals. A second guard runs
immediately before each inherited `PackSingleInstance` call. If required
inputs, output compatibility/room, or slot lock state cannot be proven, it
skips the native method before its input decrements. This does not scale Mk2
throughput and has not yet been proven by a live post-fix run.

Use a copied save and capture `[PackagingMk2]` plus `[PackagingMk2Safety]` log
lines. `START` reports runtime station type/GUID, settings, native task plan
P/P×Q and packaged-output capacity. Bounded tool events report display text,
physical object counts, native commit deltas, safety refusals and exceptions.
Only eight detailed events are emitted per task. The current BETA f9 failure
capture showed output 159→160 with one valid commit, then eight input-consuming
commits without output; the mitigation must be re-tested at output 159, at
partial capacity, with incompatible/locked output, and during task cancellation.
Also test output-empty and output-full plans, then save/reload after partial
completion. No save/load hooks are added.

The native display is expected to show remaining loose-product units, not
remaining packages: `PackagingTool.Initialize` starts `ProductQuantity` at
P×Q, `UpdateScreen` displays it, and `DropProduct` decrements it. Confirm this
in each live branch, especially for a recipe where Q is greater than 1.

- [ ] At Stack Size Vanilla with Packaging Stations OFF and ON, start Mk2 with
      enough inputs; record native plan P/P×Q, safe plan, display, physical
      objects and each native commit. Throughput remains unscaled.
- [ ] Repeat at 2x, 4x and 8x with the setting ON. Confirm the safety clamp
      uses the same currently effective output capacity; it does not multiply
      the Mk2 plan by the setting.
- [ ] Use a copied save to exercise packaging-limited, product-limited,
      insufficient-product and insufficient-packaging plans. Compare the
      observed native plan with `min(packaging available,
      floor(loose product available / Q))`; record product ID, quality,
      packaging ID and Q.
- [ ] Test output empty, partially occupied, exactly one package of room, and
      full. Record whether the task starts, the clamped plan, and every commit.
      Each successful package is -1 packaging, -Q loose product, +1 packaged
      output. A refused commit must leave all station quantities unchanged.
- [ ] Start with a plan of at least eight when resources permit. Cancel after
      staging but before any commit, then repeat after 1 and 4 committed units.
      Record station slots, output, physical objects before/after cleanup,
      display count, and whether restarting produces exactly the uncommitted
      work without consuming it early. The native stop method does not identify
      completion versus cancellation in the current trace.
- [ ] Complete the full plan and confirm the physical packaging objects are
      finalized/removed once per native commit. Reconcile planned packages,
      committed packages, packaging consumed, loose product consumed and output
      created; inspect ID/quality on the output.
- [ ] Save/reload with staged objects, partial progress, and completed output
      waiting. Compare authoritative station slots and active task/object state.
      Static station persistence saves slot contents only; reload behavior is
      not yet live-proven.
- [ ] Repeat on MAIN 0.4.7f12 and current BETA 0.4.7f9. Confirm registration
      and method signatures in the logs; the BETA build profile is f6 and is
      not complete implementation proof for f9.

## Final Safety Pass

- [ ] No item was clamped, deleted, duplicated, or world-dropped.
- [ ] Existing Backpack contents and external inventory persistence remain intact.
- [ ] Police search, shop/cart, and multiplayer configuration behavior touched by
      the mod still behaves as expected for the tested scenario; Backpack access
      has no active rank/tier gate.
- [ ] `Latest.log` contains no new Harmony patch failures, IL2CPP trampoline
      errors, access violations, or unhandled exceptions.
