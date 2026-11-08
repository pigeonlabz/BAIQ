# Experimental UI Workspace: Movable Storage Windows

This document describes the development-only prototype on the
`experimental/ui-workspace` branch. Its DLL is labelled as experimental and is
not a public or stable release.
Its MelonLoader version is `1.3.2-exp.ui-workspace.7` (valid SemVer
prerelease). The experimental DLL retains the stable MelonInfo name so the
Mod Manager can match it to the unchanged preference category IDs; the
prerelease version and separate DLL filename identify this testing build.
Stable MAIN remains version `1.3.2`.

## Supported UI

- The native Schedule I `StorageMenu` window when it is opened for a non-null
  `StorageEntity`. Initial manual targets are the small and large vanilla
  storage racks.
- Dragging starts on a full-width header above the native menu
  content and offsets the existing `StorageMenu.Container` owner. In the
  supplied 0.4.7f12 VM log, that owner spans the full screen, so its RectTransform
  bounds are not the visible window bounds. The handle and screen clamp use the
  measured active slots, visible Sort, and actual Done button hitbox. Stretched
  title bounds and the native owner/container are excluded from movement bounds.
  Measurement, pointer conversion, hit testing, and
  viewport clamping use the validated Canvas RectTransform as a shared
  coordinate space. The native owner can be parented to a plain Transform after
  closing; its parent is not assumed to be a RectTransform, and the hierarchy is
  not changed. Movement applies a Canvas-local offset in world space to the
  existing owner. Canvas discovery checks the container's current hierarchy
  first and only accepts the menu's declared Canvas if it still contains the
  container. A replaced container or Canvas triggers fresh measurement and a
  new handle.
  Slot measurement reuses `StorageSortButton`'s owner-matched `ItemSlotUI.Rect`
  discovery and local-corner transforms, which avoid the IL2CPP
  `GetWorldCorners` array issue. Capture waits for native anchored and world
  positions to settle, with a maximum 120-frame retry window. Diagnostic logging
  reports unavailable objects, active state, dimensions, transform paths, slot
  counts, and coordinate spaces with
  bounded output. Done bounds use the actual Button RectTransform; a missing
  hitbox fails safe and leaves the window at vanilla position rather than
  measuring a potentially stretched container. If required slot or Done
  bounds remain unavailable, the menu
  stays at vanilla position. The header spans the measured content width and
  contains an icon-only padlock. It starts locked; only an unlocked header can
  move the window, and clicks on the lock never start a drag. The header is
  outside the slot area, its background does not intercept raycasts, and it
  follows the owner without joining its layout.
- Vanilla slot layout, item slots, transfer behavior, container open/close
  lifecycle, and the single contextual Backpack remain on their existing paths.

The Backpack itself is excluded. Machine menus opened through an
`IItemSlotOwner` without an `OpenedStorageEntity` are excluded, as are
production equipment features. Runtime validation is pending, so verify that
only the intended basic rack windows are affected in the target game build.

## Preference and scope

The experimental build adds **Experimental: Movable Storage Windows** under
the contextual Backpack preferences. It defaults to off. When disabled, an
open window moved by the prototype returns to its captured vanilla position;
closing a window also restores that position before native close handling.
Reopening cleans up the previous open, restores vanilla world position, destroys
the old handle, and captures fresh references and bounds. Canvas-local
placements are retained in a bounded in-memory cache keyed by live
StorageEntity identity and reapplied after native placement settles. This temporary placement clears when the
experiment is disabled or the main scene changes; no placement is written to
save data.

This prototype has no snapping, docking, additional Backpack instances,
inventory routing, save changes, or capacity changes. It does not support
production equipment.

Mod Manager & Phone App 2.2.4 detects preference categories by matching their
identifiers to the MelonInfo name. The experimental DLL therefore retains the
stable BAIQ MelonInfo name and stable preference category IDs; only its version
and assembly filename are experimental. This avoids renaming categories or
migrating the test VM's existing settings.

## Build

From the project root, run:

```powershell
.\tools\build-experimental-ui-workspace.ps1
```

The script verifies the pinned Schedule I 0.4.7f12 `Assembly-CSharp.dll`
reference hash, restores/builds IL2CPP without deploying to the game, and writes
the labelled DLL and build output under a unique directory in
`test-output\ui-workspace\`. The generated test-output directory is ignored by
Git and is separate from release packages and stable MAIN output.

## Manual test steps

Use a disposable game save and install only the experimental DLL. The game
runtime is not part of the build validation.

1. Leave the preference off. Open a small rack and a large rack and confirm
   each uses its normal vanilla position and layout.
2. Open Mod Manager **Settings** and confirm BAIQ categories load, including
   **Experimental: Movable Storage Windows**. Enable it, close and reopen each
   rack. Click each header's padlock to unlock it, drag from the header, and
   click the padlock again. Confirm movement stops immediately without changing
   the current position; unlock again and confirm movement resumes from that
   position. Confirm the title, close
   control, and rack contents stay aligned and the window remains within the
   screen.
3. Move items by drag-and-drop, quick move, and shift-click. Confirm a drag
   beginning on a slot never moves the window and all native rack slots remain
   available.
4. Open the contextual Backpack beside a rack. Confirm it remains the existing
   single contextual panel and can be used without moving the rack accidentally
   when interacting with the Backpack.
5. Close and reopen a moved rack. Confirm it returns to its chosen workspace
   position after native placement settles, while diagnostic logs show vanilla
   world position restored on close. Disable the preference while a moved rack
   is open and confirm vanilla position is restored and temporary placement is
   cleared.
6. Repeat open, unlock, move horizontally and vertically, close, and reopen the
   same Medium Storage Rack at least five times. Confirm the handle appears
   once per open, movement starts from the retained position, and no offset
   accumulates. Repeat with the Large Storage Closet or another
   supported container.
7. Open representative machine and production interfaces. Confirm those
   interfaces retain their existing behavior and are not moved by this feature.
8. Save and reload the disposable save twice. After each reload, confirm storage
   contents and ordinary container lifecycle are unchanged, and stale placement
   memory did not transfer to a different or re-created StorageEntity.

With **Diagnostic Logging** enabled, the build emits bounded open, capture,
header/lock creation, lock state, pointer-down acceptance/rejection, drag
start/end, position-reset/correction, retained/reapplied placement, and restore
messages. Screen clamp limits come from
the active screen or camera viewport transformed into the Canvas RectTransform's
local space, which accounts for UI scale and resolution. It samples each screen
axis at most once per drag, reporting screen pointer deltas, Canvas-local deltas,
requested/applied offsets, visible bounds converted to Canvas space, and
per-axis clamp ranges. `Screen clamp has no fitting range` means the measured
window is wider or taller than the available viewport. Preference logs include the MelonInfo
name/category-prefix match, registered category IDs, whether the experimental
key was present at load, its effective value, and the runtime activation result.
Pointer attempts are logged only when they occur over measured native content
or the storage header. Position-before and position-after values are logged at
drag start/end; no per-frame movement logs are emitted.

The implementation polls `UnityEngine.Input` from the mod update loop. It does
not depend on `TitleLabel` receiving Unity pointer callbacks, and it selects
the Canvas event camera for screen-to-local conversion when the Canvas is not
screen-space overlay. Diagnostic logs report the actual container, title, slot,
Sort, and Done transform paths; their screen bounds; descendant relationships;
the Canvas mode/camera/raycaster; and the title and Done raycast targets.
After movement, it reports the first detected difference between its last
written position and the observed position, which can reveal a native layout
reset without logging every frame.

The supplied VM log confirmed `StorageMenu.Container` is the full-screen
`UI/StorageMenu/Container`, `Slots` and `Done` are its descendants, Sort is
under `Slots`, and the prior handle was at screen y=904–920 on a 1440x900
display. `TitleLabel` did not resolve to a RectTransform in that capture. The
new build measures visible active slot/control rectangles and places/clamps the
handle using those bounds. Re-run in the VM to confirm the updated bounds, drag
movement, layout stability, and Mod Manager rendering; Codex cannot inspect the
separate VM directly.

With diagnostic logging enabled, a successful open should report
`slotsMeasured=True`, nonzero `usable` slots (6 for the Medium Storage Rack and
12 for the Large Storage Closet), and `doneIncluded=True`, followed by
`Storage header bound`. A failed measurement reports slot discovery
counts and each title/Sort/Done transform's path, active state, dimensions, and
coordinate spaces. On this game version `TitleLabel` has appeared absent in
the provided runtime trace; it is included when exposed but is not a required
measurement for the supported storage layouts.
