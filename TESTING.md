# Manual Testing Checklist

There is no automated game harness in this repository. Run the relevant checks
below against the exact DLL intended for release and review `Latest.log` for
Harmony, IL2CPP, lifecycle, and quantity-preservation errors.

Record the Schedule I, MelonLoader, Mod Manager & Phone App, and mod versions with
the result.

## MAIN 1.3.2 release-candidate acceptance

Build the candidate with `tools/prepare-release-candidate.ps1` using a new
`-StageName`. Install the exact staged `MAIN\BackpackInventoryQoL.dll` on the
host and guest in isolated test installs, with no other BAIQ DLL active. Use
disposable save copies and record the candidate SHA-256 on both peers. MPTEST1
runtime results are supporting evidence; they do not count as a pass for this
exact MAIN candidate.

### RC4 manual acceptance report (2026-10-12)

The tester reports that both peers loaded the exact RC4 DLL
(`6BBF85BC3284AF9C76E4A587981F122865A10490A711B097AC1E5301F2159384`) in a
genuine multiplayer session. Reported results include host lobby/role
verification, policy publication and exact readback, guest policy discovery and
acceptance, host x8 overriding a guest's saved Vanilla preference without
changing that preference, policy revalidation during scene loading, and
standalone/contextual/phone Backpack use. Transfers, full/partial stacks,
sorting, production multipliers, guest persistence through save/reconnect,
preference restoration after leaving, and the listed single-player, delivery,
shop, and cash-routing smoke checks were also reported successful.

These are manual observations, not exhaustive formal proofs of every stateful
item or quantity-conservation case. No RC4 logs were attached to this audit
turn, so the results are recorded as tester-reported rather than independently
correlated from log files. The report does not establish a separate result for
every Mk1 Packaging edge case, every invalid-policy permutation, or the Brick
Press locked-slot report below.

### RC6 NPC-reserved-slot release acceptance (2026-10-12)

The maintainer reports live testing of the exact RC6 DLL
(`D6D6868AEEC3BFD16CE7FD9F0ADF65261502E30D45D951A7CC7982014FE2E09D`). The
NPC/employee-reserved-slot correction was confirmed for shift-click, Take All,
and sorting. This records the reported test result; it does not claim that
permanent item loss was independently demonstrated or that every storage type
and multiplayer edge case was exhaustively tested. The RC4 acceptance record
above is retained as history.

### RC4 first-pass regression retest

- [ ] Start a genuine host lobby with Stack Size x8 and the supported production
      settings selected. Join from a guest whose saved stack setting is Vanilla.
- [ ] Without changing preferences, opening storage, or reloading a scene,
      confirm both logs transition from active-lobby detection to role agreement.
      The host must report `Host policy publication verified` after exact
      readback, and the guest must report policy discovery then `Host policy
      accepted`. Both reach a ready Host or GuestReady mode. If identity is
      initially incomplete, logs identify missing fields and retry; permanent
      failure stays Vanilla/off and paused. The diagnostics must not show zero
      identities as valid fingerprints.
- [ ] Confirm the candidate reads identity and metadata through Schedule I's
      active lobby service: `GetSessionConnectionIdentifier` resolves the owner,
      and the captured `SteamLobbyService.GetLobbyData`/`SetLobbyData` use that
      same current lobby. `Lobby` itself has no `GetLobbyData` method in f12.
      Exact runtime evidence is required because the previous host log showed an
      unverified legacy metadata readback.
- [ ] Confirm both peers can use `B` after readiness. Before guest acceptance,
      the phone Backpack may be visible as read-only, but no slot interaction,
      sort, quick-move, contextual accessor, or BAIQ transfer may occur.
- [ ] Open the phone Backpack immediately on Home without first opening a
      container. Switch Home → Mod Settings (or another unsupported landscape
      app) → Home several times. Confirm the panel hides, returns, and recalculates
      its layout; close and reopen the phone repeatedly.
- [ ] On a fresh single-player save, open the phone before any standalone or
      contextual Backpack/storage UI. Confirm the deferred canvas measurement
      shows it on first opening; if the initial geometry remains invalid, confirm
      bounded retries recover without another UI interaction.
- [ ] After those phone transitions, open and close the contextual Backpack.
      Confirm neither surface leaves ghost controls or a stale contextual
      accessor.
- [ ] Confirm the guest's effective stack setting is x8 while connected, then
      disconnect and verify the saved local Vanilla setting becomes effective
      again. Reconnect and confirm host settings are applied before BAIQ
      inventory operations resume.
- [ ] Record aggregate item counts before/after representative full and partial
      transfers. Confirm no item is lost, duplicated, clamped, or silently
      split. Capture both logs; these steps do not count as passed without them.
- [ ] In single-player, click the phone Sort button and invoke each supported
      Sort path; verify normal sorting. During pending multiplayer policy, verify
      the phone Sort control is disabled and direct Sort calls do not mutate.
- [ ] During an active lobby, load/reload the Main save scene. Confirm policy
      pauses, the current lobby is re-verified, and the host/guest return to
      ready without ending the session or editing preferences.

### Single-player baseline

- [ ] Load a MAIN-origin save and a BETA-origin save. Compare native inventory,
      Backpack contents, stack quantities and stateful/non-stackable items.
- [ ] Move items between Backpack, inventory, contextual containers and
      supported machine UIs. Check sorting, search, pagination and slot locks.
- [ ] Save, reload, and switch copied saves A → B → A. Confirm contents and
      locks remain with the correct save and no item quantity changes.
- [ ] Exercise Vanilla and 8x stack settings, plus each enabled production
      setting. Confirm normal Vanilla-mode achievements remain available.

### Two-client Backpack persistence

- [ ] Start a genuine Schedule I `0.4.7f12` host/guest lobby with the same
      candidate hash and Diagnostic Logging enabled. Capture both logs.
- [ ] Give the guest distinct stackable and stateful items; record guest and
      host-visible Backpack slot counts and quantities. Save from the normal
      lobby flow and confirm a verified host-side sidecar write.
- [ ] Save again without a mutation. Confirm there is no new dirty/write event
      and the host-side `Backpack.json` hash is unchanged.
- [ ] Disconnect the guest normally, reconnect, and compare contents and
      quantities. Repeat after a normal host save, host lobby close, host reload,
      and guest reconnect.
- [ ] Empty the guest Backpack intentionally, save, reconnect and confirm it
      remains empty. Confirm the host's own Backpack stays independent.
- [ ] Switch the host to a different disposable save and repeat a guest save.
      Confirm a prior save's dirty snapshot cannot be written to the new save.

### Host-controlled settings acceptance

These are runtime gates for the exact new candidate; the automated payload/state
tests do not prove the Steam backend's owner-only write rule, timing, FishNet
authority, or quantity conservation in Schedule I.

- [ ] **A — Host x8 / guest local Vanilla:** Before joining, set the host to 8x
      and the guest's saved Stack Size to Vanilla. Confirm guest logs show the
      current owner policy accepted, effective x8, and local preferences
      unchanged. Confirm the guest description says “Controlled by host.” Test
      full/partial inventory, Backpack and shared-storage transfers with known
      aggregate counts.
- [ ] **B — Disconnect restore:** Leave the lobby normally. Confirm the guest's
      saved Vanilla preference remains unchanged and becomes effective again;
      verify a local Vanilla stack behaves at its native limit.
- [ ] **C — Reconnect barrier:** Rejoin the same x8 host. Before metadata is
      accepted, confirm the Backpack cannot open and BAIQ Sort, Take All and
      quick-move additions remain paused. After acceptance, confirm x8 is
      applied before those BAIQ interactions become available.
- [ ] **D — Production settings:** Select host production settings, then
      confirm the guest receives the same effective drying, mixing, plant-yield
      and Mk1 packaging values. Compare input/output quantities and item state
      on both peers; confirm each successful native operation applies its
      multiplier once. Mk2 packaging throughput remains native. Verify
      achievements are suppressed when the effective stack cheat is active and
      behave normally when all effective cheats are off.
- [ ] **E — Backpack persistence:** Deposit identifiable guest items, save,
      disconnect and reconnect. Confirm exact contents and quantities survive;
      repeat with an intentionally empty Backpack and host save/reload.
- [ ] **F — Single-player:** Load MAIN- and BETA-origin disposable saves, test
      Vanilla and 8x, production settings, save/reload and save switching. Confirm
      local settings, slot locks and Backpack contents remain unchanged. At
      Vanilla with all production settings off, achievements remain enabled.
- [ ] **G — Missing or invalid policy:** Use disposable saves to test missing
      metadata, wrong schema, wrong mod version, stale owner, invalid values and
      host-owner change. The guest must remain at Vanilla/production-off, keep
      BAIQ Backpack and custom inventory operations paused, preserve local
      preferences, and continue to use ordinary native inventory. Confirm logs
      clearly report the unsupported state. An unmodded peer cannot be forced
      to apply this policy and remains an unsupported shared-stack pairing.

Guest custom slot-lock replication remains unimplemented; record host and guest
slot-lock state separately.

Backpack capacity remains local because changing it to match the host could
remove occupied slots. Guest custom slot-lock replication is not implemented;
record those states separately and treat multiplayer slot-lock persistence as
unverified.

### NPC-reserved storage slot routing regression

- [ ] In a disposable save, let an employee reserve a visible slot in a Huge
      Storage Closet. Shift-click a compatible item toward the closet; confirm
      the reserved slot is skipped and another eligible slot receives the item.
- [ ] Record source and destination quantities before and after. Repeat with a
      partial stack, an existing compatible stack, multiple reservations, and
      all destinations reserved/locked. Rejected routes must leave the source
      unchanged; no reserved destination may change.
- [ ] Repeat using Take All and Sort. Confirm native employee-reserved slots
      remain byte-for-byte/item-for-item unchanged while other eligible slots
      continue to work. Repeat at Vanilla and 8x.
- [ ] Confirm a BAIQ user-locked slot still remains blocked independently of the
      native reservation, and that ordinary unlocked storage still accepts
      shift-click transfers.
- [ ] Repeat on a host and guest with the supported policy ready. Machine
      reservation behavior, including Brick Press employee-locked slots, still
      requires separate in-game verification; do not infer it from the closet
      result.

### Shared inventory conservation

- [ ] With the accepted host policy, test distinct item types, full and partial
      stacks, Backpack ↔ inventory, Backpack ↔ shared storage, drag/drop,
      shift-click and Sort on both peers.
- [ ] Record aggregate counts and stateful item identity before and after each
      action. Confirm totals are conserved and no stack is clamped, duplicated or
      silently removed.
- [ ] Repeat host 8x / guest local Vanilla after confirming the guest policy
      marker; any client quantity/state mismatch is a release blocker, not a
      cosmetic-only result.

### Compatibility and authorization limits

- [ ] Test modded host + unmodded guest, unmodded host + modded guest, and
      mismatched BAIQ versions only with disposable saves. Record which player
      can load and persist the custom Backpack; these combinations are not
      promised as supported until verified.
- [ ] In the normal UI, check whether the game permits any player to mutate a
      different player's Backpack. Do not craft RPCs or use an exploit. The
      existing diagnostic observed normal ownership matches but did not prove
      native pre-reader rejection for unauthorized calls.
- [ ] Review both logs for path-binding rejection, failed atomic write/readback,
      hydration errors, Harmony failures and unhandled exceptions.

When Diagnostic Logging is enabled, the local-owner write target should include
the native `ISaveable.SaveFolderName` segment (for example
`Players\Player_0\Backpack.json`), matching the `LoadInventory` path. This log
correction does not alter native `WriteSubfile` persistence.

Do not use the host-alone lobby reload scenario as a gate; it is not part of the
tested normal multiplayer flow.

## f12 Fish-Net authorization probe (development only)

Build with `tools/build-fishnet-auth-diagnostic.ps1`. It creates a unique
`.release-work\fishnet-auth-diagnostic\runs\…` output and never deploys into a
game folder. The DLL is named
`BackpackInventoryQoL-FishNetAuth-Diagnostic.dll`, identifies itself as
`1.3.1-FISHNET-AUTH-DIAGNOSTIC`, and must replace every other BAIQ DLL in each
isolated test install. It includes ordinary MAIN 1.3.1 persistence only; it
does not include MPTEST1's guest write path. Enable **Diagnostic Logging** on
both peers.

1. Use copied/disposable host and guest saves on Schedule I `0.4.7f12` and
   MelonLoader `0.7.3`, with the same diagnostic DLL hash. Start one genuine
   two-player lobby.
   This read-only DLL is based on MAIN 1.3.1, not MPTEST1: use only expendable
   items, do not use this run to judge guest persistence, and discard the copied
   saves after collecting logs.
2. Have the guest transfer one known item into and out of the guest's own
   Backpack. Preserve both logs. On the host, expect
   `[FishNetAuthTrace] event=rpc-reader-enter` for any reader that runs, with
   `senderSource=FishNet-reader-final-argument`, hashed `targetPlayerKey`,
   Player/Storage network object IDs, and sender/target/storage-owner match
   fields. For `SetStoredInstance`, the server mutation setter should report
   `setStoredInstanceConnectionArgumentClientId` separately from
   `readerSenderClientId`, plus whether those connection objects match. The
   setter also reports slot occupancy and quantity before and after. No item
   IDs or item payloads are logged.
3. Have the guest move one item through ordinary shared storage. The host trace
   should identify a non-player/shared target while retaining the actual reader
   sender. This checks normal shared-storage interaction without introducing a
   BAIQ restriction.
4. Do not craft or inject RPCs. If the existing game UI naturally allows a
   player to attempt a transfer into another player's Backpack, test that only
   in the disposable copy with an expendable item. If the UI prevents the action
   or provides no such route, record that the server-side mismatch case remains
   unresolved; the diagnostic DLL sends no test RPC.

Interpretation limits: `rpc-reader-enter` proves Fish-Net invoked the game's
registered reader and exposes its final connection argument as the sender.
The `SetStoredInstance_Internal` connection argument is captured separately from
the reader-final sender. The f12 generated wrapper signatures establish these
as distinct method arguments, but do not expose the reader body or prove the
setter argument's wire origin. Preserve both values in the logs; do not treat
the setter argument as authenticated identity. The available native reader dump
is not pinned to the current f12 binary hash, so it cannot settle this point.
The f12 wrappers also contain no RPC registration attributes or method bodies,
so this probe cannot see dispatch rejection before the managed reader is invoked.
`storage-setter-exit` proves the setter returned; a presence/quantity delta is
evidence of a visible slot change, while no delta does not exclude replacement
by an equal-summary stateful instance. A missing reader event does **not** prove
Fish-Net rejected the request; it could also mean no request was sent or that
the request was filtered before the managed reader. The probe does not inspect
the native registration attribute or observe pre-reader rejection decisions.
An entered setter shows dispatch reached that method, not by itself that a
player-bound Backpack mutation was authorized. Keep that boundary explicit
when using the logs for a production decision.

## Schedule I 0.4.7f12 MAIN compatibility candidate

The published MAIN `1.3.0` fails during Harmony patch discovery on game
`0.4.7f12` because `TryGetPlayerData` has two overloads. The unified MAIN
`1.3.2` candidate is based on the BETA-tested 0.4.7 implementation, compiled
against MAIN's f12 references and carrying MAIN's established identity. The
successful BETA startup log is useful compatibility evidence, but does not
replace testing this exact MAIN DLL. Use a copied/disposable save and only
`BackpackInventoryQoL.dll`.

- [ ] Record game `0.4.7f12`, MelonLoader, Mod Manager & Phone App, and candidate
      DLL versions. Confirm there is exactly one Backpack & Inventory QoL DLL in
      `Mods`.
- [ ] Launch the candidate and confirm the `0.4.7 MAIN build` compatibility
      log appears, initialization completes, and no
      `HarmonyException: Ambiguous match` or player-data patch failure appears.
- [ ] On separate copies of a MAIN-origin save and a BETA-origin save, confirm
      the player inventory and Backpack contents load once, with no missing or
      duplicated items.
- [ ] Check persistent Backpack slot locks and player slot locks across a
      save/reload. Check session storage slot locks still clear on a new session.
- [ ] Switch copied saves A → B → A in one game session. Confirm Backpack
      contents and locks stay with the correct player/save and do not cross over.
- [ ] Transfer representative ordinary items and cash between Backpack,
      inventory, and storage. Compare source and destination counts/balances
      before and after each transfer, including partial and full destinations.
- [ ] Exercise cash Take All into compatible existing and empty slots. Check
      dedicated cash slots, ordinary cash slots, filters, add/user locks,
      exact-capacity and overflow cases; the aggregate balance must be conserved.
- [ ] Save, fully exit, and reload each copied save. Confirm Backpack contents,
      lock state, stack quantities, and ordinary inventory remain conserved.
- [ ] Review `Latest.log` for save-path rejection, skipped writes, hydration
      retry exhaustion, or other persistence warnings.

Do not use a primary save for first-run compatibility checks. Stop testing and
restore the copy if any count, owner, or save association differs unexpectedly.

## Multiplayer Backpack Persistence Diagnostic Build (Development Only)

Build the isolated diagnostic DLL with
`tools/build-multiplayer-persistence-diagnostic.ps1`. It has a distinct assembly
and filename, does not deploy to the game, and is not a release candidate. Use
the same diagnostic DLL on both peers and remove other active Backpack DLLs from
both `Mods` directories while testing. Enable **Diagnostic Logging** on both
peers before loading a disposable host save. The trace is silent for successful
events while that preference is Off.

Use a copied save and record the starting Backpack and slot-lock state outside
the logs. Put a few distinguishable items into the guest's Backpack, then run
these cases independently:

1. Have the guest leave the lobby normally. Save/close the host session, then
   reconnect the guest and check the same Backpack. Compare the host's
   `Players\\<player folder>\\Backpack.json` before departure, after departure,
   and after reconnect.
2. With the guest still connected and Backpack contents changed again, close
   the host lobby normally. Reload that save on the host and reconnect the
   guest. Compare the same host-side sidecar and verify item counts and slot
   locks.
3. Repeat one departure after waiting for the normal save flow and one with an
   immediate leave. This distinguishes a missing write path from a shutdown
   timing issue.

Collect `Latest.log` from both peers plus the game, MelonLoader, mod and DLL
versions. Do not post save files or item contents. Correlate the logs by the
short `playerKey` hash; it is derived from PlayerCode and does not expose the
raw code. Native `ISaveable.WriteSubfile` currently returns the subfile name
`Backpack.json`, not a full path. The diagnostic writer result therefore records
both that return value and the candidate Player-specific path; MPTEST1 verifies
the candidate under that Player's save directory. An owner-guard skip reports
only a candidate path because no write was attempted. Absolute paths can include
Windows account or save-directory names; redact those before sharing logs while
retaining the relative `Players` path and host/client labels.

Expected trace markers and interpretation:

- `event=request-save-call` records the local `Player.RequestSavePlayer`
  invocation. Its matching `request-save-method-return` is only a method return,
  not proof that the host received or committed the request.
- `event=manager-save-enter` / `manager-save-return` records the ordinary
  `PlayerManager.SavePlayer` boundary. The return marker does not prove that an
  asynchronous save callback or client acknowledgment finished.
- `event=player-write-begin` reports role, player key, owner state and the
  candidate file path. `event=backpack-write-skipped` with
  `reason='non-owner guard...'` means BAIQ did not inspect or write that remote
  player's Backpack on this process. A guest-local write should instead show
  `lobbyRole=guest`, `owner=True`, and a `backpack-write-result` with its local
  resolved path, slot counts and file byte length.
- In MPTEST1, a host-side write for a guest shows the same `playerKey`,
  `lobbyRole=host`, and `owner=False`, followed by
  `guest-backpack-snapshot-ready`. A verified write reports the player-specific
  path and exact-content readback; an unverified write remains dirty for retry.
- `event=player-data-path-resolved`, `backpack-read-appended`, the existing
  `0.4.7 payload cached` marker, and `0.4.7 Backpack hydration completed once`
  correlate host-file lookup through guest hydration on reload. A missing host
  sidecar followed by a fresh-save load explains why a guest-local file alone
  is insufficient.
- On the host, a remote Player may have its sidecar read and payload appended,
  then fail the local-owner hydration gate. A diagnostic-level remote-player
  skip confirms the file was read, not that the host applied it to the remote
  storage replica. Verify the owning guest's hydration separately and test host
  reloads with the guest absent before reconnecting.
- `event=player-exit-all`, `event=backpack-component-lifecycle`, and
  `event=main-scene-unloading` record nearby cleanup. They do not prove a
  FishNet disconnect callback or save acknowledgment.

For safety, this build observes `RequestSavePlayer`, `PlayerManager.SavePlayer`,
`Player.WriteData`, the existing player-data read and hydration patches, and
scene/component cleanup only. It does not patch generated RPC methods, so the
packet arrival and final client acknowledgment remain unobservable from this
instrumentation. Diagnose those gaps from the correlated method markers and
file state before proposing any ownership or synchronization change.

## MPTEST1 Reference Build (Historical; Not the Current MAIN Candidate)

The steps below describe the earlier experimental MPTEST1 DLL. MAIN 1.3.2 now
uses a PlayerCode-bound active-save path and staged atomic sidecar writes. Use
the acceptance matrix above for the current candidate; preserve this section as
the record of how MPTEST1 was validated.

Build with `tools/build-mptest1.ps1`. The script runs the three existing
regression suites, restores/builds into a unique `.release-work\MPTEST1\runs\…`
directory, inspects the compiled Harmony metadata, and never copies a DLL into a
game `Mods` folder. The artifact is named
`BackpackInventoryQoL-MPTEST1.dll` and reports Melon version
`1.3.1-MPTEST1`; it must not be installed with another BAIQ DLL active.

The experimental host path serializes the host's native, Player-bound Backpack
`StorageEntity` only after a native server-side item-instance or quantity setter
was observed for that storage in the current Main save session. The
normal local-owner path is unchanged. The guest identity still comes from the
host's normal player-data lookup; no custom RPC or client snapshot is added. A
guest snapshot is considered persisted only if the existing `WriteSubfile`
returns the expected `Backpack.json` name, the target Player-specific path
resolves, and the exact payload reads back from that path. A full path returned
by the native writer must match the candidate target; ambiguous relative paths,
missing files and content mismatches stay unverified and leave the mutation
marker dirty.
The host's own `InventoryExpandedSlotLocks` registry is never written under a
guest's player path. Guest custom slot-lock replication is outside this test
build's scope.

Use only isolated host and guest installs with one genuine connected two-player
session, disposable copies of saves, and the same MPTEST1 hash on both clients.
Enable **Diagnostic Logging** on both. Record each peer's game, MelonLoader and
DLL versions, the pre-test save copy, Backpack item/stack totals, and the
host-side `Backpack.json` hash. Do not inspect or share a real save.

1. Load the disposable host save, join with the guest, and confirm both players
   see the expected Backpack contents. Include at least one stack and one item
   with meaningful per-instance state when available.
2. First make a baseline guest Backpack change, save, and confirm a verified
   write clears the dirty marker. Then change a supported filter without
   changing contents and save; confirm there is no new
   `guest-backpack-snapshot-ready` marker and the sidecar hash is unchanged.
   Next add items to the Backpack, split/merge a stack, and transfer an item out.
   Wait for each UI/network operation to finish before triggering a normal save.
   Check the host log for
   `guest-backpack-snapshot-ready` followed by
   `guest-backpack-snapshot-write-verified` for the same hashed `playerKey`.
   Compare the host sidecar with its pre-test hash and confirm parsed item IDs,
   quantities, ordering/slot placement, and stateful item data.
3. Disconnect the guest normally, reload it into the same lobby, and verify the
   Backpack on both clients. Confirm no item is missing or duplicated and the
   host sidecar still matches the guest's accepted contents.
4. Repeat after changing contents, then close the host lobby normally. Reload
   the disposable save on the host, reconnect the guest, and repeat the item,
   stack, transfer and state checks.
5. Empty the guest Backpack intentionally, wait for the changes to settle, save
   and reconnect. Confirm an observed, intentional empty state replaces the old
   sidecar rather than resurrecting prior contents. Separately save without
   changing the guest Backpack and confirm there is no new snapshot marker and
   no sidecar rewrite.
6. Repeat one test immediately after a completed transfer and one after normal
   quiescence. Exercise a save-session switch and a reconnect that reuses the
   same authenticated PlayerCode. Inspect logs around storage destruction and
   save callbacks; timestamps across the two machines are not a total ordering.
7. With the host alone, verify ordinary Backpack persistence and host slot locks
   still save/load. Confirm guest Backpack slot locks separately and record the
   result as an explicit limitation: this candidate does not synchronize the
   guest's BAIQ lock registry.

Acceptance requires conserved counts and per-instance state in the guest view,
the host replica, the persisted sidecar, and after reload; successful sidecar
readback; no writes on stale/unobserved state; and no regressions to local owner
saves. The supplied MPTEST1 logs show successful reconnect, save and reload for
non-empty contents on the previously tested DLL. Repeat on the corrected
candidate, including intentional emptying and a host reload before the guest
reconnects. The candidate remains experimental until these checks pass on its
exact DLL. A post-write readback mismatch leaves the marker dirty for retry, but
the native writer is not atomic and may already have changed the sidecar; retain
the disposable pre-test copy and record that outcome as a failed test.

### Multiplayer stack multiplier comparison

`StackSizeMultiplier` remains a per-client saved preference but the accepted
host policy supplies the effective session value. For baseline comparisons,
confirm the host-policy-accepted marker first, then compare the same live slot's
`ItemSlot.Quantity` and computed `BaseItemInstance.StackLimit` on each peer while
the storage UI remains open. Repeat after closing/reopening the UI, reconnecting,
and restarting. The getter patch does not rewrite quantities; do not split,
clamp, or normalize stacks as part of this diagnostic. Aggregate save totals
alone cannot prove per-slot equality. If policy is missing, invalid, or changes
unexpectedly, the guest returns to safe Vanilla/off settings and BAIQ inventory
extras remain paused.

Benign two-client tests do not prove a malicious peer cannot invoke a native
`StorageEntity` setter on another player's object. Before production approval,
confirm from f12 server behavior that mutations observed for the target
Player-bound object are authorized for that player's FishNet connection, or
identify a supported connection-to-Player check. Also verify the host remains
recognized as the lobby server during the final save callback on lobby shutdown;
the experiment intentionally skips if that role check has already cleared.

## Installation and Settings

- [ ] The development log identifies `Inventory Expanded - Backpack & Inventory
      QoL` with the expected version; the canonical short name remains Inventory
      Expanded.
- [ ] `BackpackInventoryQoL.dll` is the active 1.3.2 test DLL and the old active
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

### Phone / Character lifecycle regression

Use a disposable save with a known Backpack inventory. Keep Diagnostic Logging
enabled and record the exact MAIN DLL hash. The Backpack panel is a presentation
of the existing player Backpack; these checks must not change its contents.

- [ ] Load the save, open Phone, and confirm the Phone Backpack is visible.
- [ ] Switch Phone → Character → Phone. Confirm Character hides the Phone panel
      while its own contextual Backpack stays visible; returning to Phone restores
      the same single Phone panel without pressing B.
- [ ] Repeat Phone → Character. Confirm the Character contextual panel remains
      active after `GameplayMenu.SetScreen` completes and Phone suppression runs;
      the Phone presenter must not disable Character's shared native quick-move
      context or release its Backpack accessor.
- [ ] With Character selected, close and reopen the gameplay menu while retaining
      Character. Confirm the contextual panel closes when the native context
      genuinely ends, then reopens once when Character becomes active again.
- [ ] Repeat Character → Phone → Character at least five times. Confirm the
      contextual panel and Phone presenter each follow the selected screen, with
      no stale raycasters, duplicate roots, or interactive panels left behind.
- [ ] Close Phone while Character remains selected, then reopen Phone and switch
      back to Character. Confirm closing Phone does not clear Character's
      contextual Backpack accessor or hide its panel.
- [ ] With Character selected, close the gameplay menu/Phone using the exact
      reported sequence. Confirm the Phone Backpack does not appear over
      Character, and reopening the Phone restores it.
- [ ] Repeat Phone → Character → Phone at least five times, including switching
      from different supported Phone apps. Confirm visibility follows each
      screen and the panel never flashes over Character.
- [ ] Toggle B before the first transition and after returning to Phone. Confirm
      standalone B-key access stays independent and no second Backpack window
      is created. While Character is open, confirm B follows the game's normal
      external-menu gating and does not create an overlapping window.
- [ ] Close Phone, reopen it, then repeat the Character round trip. Confirm the
      deferred first-open layout and subsequent resume both work.
- [ ] Save, return to menu, reload the same disposable save, and repeat the
      sequence. Confirm Phone presentation works and Backpack contents,
      quantities, and stateful item identities match the recorded baseline.
- [ ] Open and close ordinary storage and supported machine contexts before and
      after these transitions. Confirm their contextual Backpack remains visible
      for the active context, closes on genuine `DisableQuickMove`, and never
      inherits Character ownership or remains attached to the Phone.
- [ ] In a compatible two-client session, repeat the transitions as host and
      guest. Confirm UI ownership stays local, native transfers replicate once,
      and the Backpack contents and save/reload state remain unchanged.
- [ ] Inspect the hierarchy and `Latest.log`: exactly one Phone Backpack root
      is active on Phone and exactly one contextual root while Character owns the
      gameplay screen; no duplicate windows, stale raycasters,
      unhandled lifecycle errors, item loss/duplication, or unintended UI changes
      occur.
- [ ] With Diagnostic Logging enabled, verify
      `[PhoneBackpack] Native quick move ownership transferred` appears when
      Character's native context replaces Phone's shared state. The contextual
      panel must not close with reason
      `ContextualBackpackPanel.Update: ItemUIManager.QuickMoveEnabled=false`
      while Character remains selected. On genuine Character/menu closure,
      confirm `[ContextualLifecycle] closing` reports the native
      `ItemUIManager.DisableQuickMove` reason and the panel becomes inactive.
- [ ] With Diagnostic Logging enabled, confirm `GameplayMenu.SetScreen` prefix
      and postfix records identify the Phone/Character owner transitions.
      `[PhoneBackpackLifecycle]` records should agree on Phone open state,
      selected screen, suppression, desired visibility, root active state/ID,
      and root parent before/after Phone close and reopen.
- [ ] Reproduce the reported mismatch with `characterOpen=False` and the
      suppression latch previously true. After `GameplayMenu.CurrentScreen=Phone`,
      confirm suppression clears, `owner=Home` (or the currently supported Phone
      app), `desiredVisible=True`, and the existing root becomes active. A stale
      `CharacterInterface.IsOpen` value must not override the selected Phone
      gameplay screen.

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
- [ ] For each priority setting, record source and destination quantities and
      repeat shift-click with no matching destination stacks, matching full
      stacks, a matching partial stack only in the lower-priority destination,
      and compatible partial stacks in both destinations. The selected tier
      must receive items first, including when it has empty slots and the other
      tier has a partial stack.
- [ ] For each priority setting, fill the preferred destination completely and
      confirm the eligible fallback destination receives the remainder. Repeat
      with filtered, add-locked, removal-locked, and employee-reserved preferred
      slots; ineligible preferred slots must fall through safely. A BAIQ user-lock
      marker alone must retain its existing quick-move behavior.
- [ ] Repeat the priority cases with native and modified stack limits. Confirm
      preferred capacity reflects the active multiplier and compatible partial
      stacks consolidate within each selected destination before its empty
      slots are used.
- [ ] Compare total source + Inventory + Backpack quantities before and after
      every transfer. A rejected destination write must leave the source
      unchanged; a partial transfer must leave the exact remainder at source.
- [ ] With Diagnostic Logging enabled, reproduce a lower-priority partial stack
      beside an empty preferred slot and confirm
      `[QuickMovePriority] deferred a fallback destination` is logged once for
      that transfer.
- [ ] Quick move Backpack <-> storage routes to valid destinations.
- [ ] Compatible partial stacks fill before valid empty slots within the
      selected destination tier.
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
- [ ] In the supported MAIN build, shift-click cash both directions between the player
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
      default. Verify cash and ordinary items both use the active storage owner.
- [ ] With an employee locker open, quick-move cash from player inventory and
      Backpack; the locker receives it before the dedicated money slot.
- [ ] In the supported MAIN build, repeat with safe, closet, and rack destinations and
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

## Increase Production: Drying Racks (MAIN 0.4.7f12)

Use a copied/test save with the exact MAIN 0.4.7f12 candidate. Pineapple capture
and managed policy tests do not replace these runtime checks. Record
authoritative input, all active operation quantities,
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

## Increase Production: Mixing Stations (MAIN 0.4.7f12)

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
- [ ] Repeat on standard and Mk2 stations in the supported MAIN build.

## Increase Production: Plant Yield (player hand-harvest; MAIN 0.4.7f12)

The former BETA baseline has reported live validation for Plant Yield. Repeat
the regression matrix against the unified MAIN candidate before publication.

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
- [ ] Confirm the safety hooks resolve on MAIN 0.4.7f12 and inspect the log for
      method-resolution or registration errors. Historical f9 BETA references
      are retained for comparison, not as a supported build target.

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
- [ ] Confirm registration and method signatures in the supported MAIN 0.4.7f12
      log. Historical BETA f6/f9 profiles remain comparison material only.

## Final Safety Pass

- [ ] No item was clamped, deleted, duplicated, or world-dropped.
- [ ] Existing Backpack contents and external inventory persistence remain intact.
- [ ] Police search, shop/cart, and multiplayer configuration behavior touched by
      the mod still behaves as expected for the tested scenario; Backpack access
      has no active rank/tier gate.
- [ ] `Latest.log` contains no new Harmony patch failures, IL2CPP trampoline
      errors, access violations, or unhandled exceptions.
