# Inventory Expanded - Backpack & Inventory QoL

**By PigeonLabz. Community handoff.** This repository contains the source for the Schedule I mod
[Inventory Expanded on Nexus Mods](https://www.nexusmods.com/schedule1/mods/2465).
PigeonLabz is making the source available for others to maintain and improve.
The Nexus page remains the record of published files and release notes.
PigeonLabz is winding down active maintenance, so issues and pull requests may
go unanswered. Forks are welcome under the MIT license.

Expanded Backpack storage and inventory quality-of-life tools for Schedule I.
Inventory Expanded continues the save-compatible storage foundation of
[OG Backpack](https://www.nexusmods.com/schedule1/mods/818) without replacing
the game's inventory model.

Current public Nexus release: **1.3.0**

**1.3.3 — Shift-Click Priority Fix** is being prepared for manual Nexus upload. The live
Nexus file remains 1.3.0 until the maintainer uploads this package. Version
1.3.2 targets Schedule I 0.4.7f12 IL2CPP and uses one DLL for single-player,
hosts, and guests.

[Download Inventory Expanded on Nexus Mods](https://www.nexusmods.com/schedule1/mods/2465)

The canonical short name is **Inventory Expanded**. The primary descriptive
title is **Inventory Expanded - Backpack & Inventory QoL**. Internal
`InventoryExpanded`, `Backpack`, and `OGBackpack` identifiers remain unchanged
where required for backwards compatibility.

## Dependencies

- **Required — [MelonLoader](https://github.com/LavaGang/MelonLoader):** loads BAIQ in Schedule I.
- **For in-game settings — [Mod Manager - Phone App](https://www.nexusmods.com/schedule1/mods/397):** provides the Mod Manager and phone-based settings interface. Core Backpack features can run without the settings app, but installing it is recommended.

The last locally verified setup used **MelonLoader 0.7.3 Open-Beta** and **Mod Manager - Phone App 2.2.4** (Schedule I IL2CPP `0.4.7f12`). These are *tested versions*, not minimum-version guarantees. Obtain both separately from their linked upstream sources; neither dependency is bundled in this source archive.

## Features

- Configurable Backpack capacity up to 128 slots.
- Responsive layout, pagination, Search, and full-inventory Sort.
- Contextual Backpack access beside compatible containers and machines.
- Adaptive Phone Backpack on supported portrait and landscape apps.
- Native drag-and-drop and validated quick-move routing.
- Backpack-first or Inventory-first shift-click destination priority.
- Take All and active-container Sort hotkeys.
- Sort controls for Backpack and storage inventories, with packaged products
  ahead of unpackaged products and then other items.
- Persistent player/Backpack slot locks and session-safe storage slot locks.
- Use Current Item, Copy Filter, and Paste Filter shortcuts for native slot filters.
- Native Mod Manager & Phone App settings through MelonPreferences.
- Optional Stack Size, drying-rack, mixing-station, and player plant-harvest
  cheats, kept separate from normal quality-of-life settings and default off.

The dedicated **B** key remains available, but contextual and Phone access put
the same Backpack beside the inventory currently in use, so it often does not
need to be opened separately.

## Prepared 1.3.3 Installation (manual upload pending)

1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) for Schedule I.
2. Install [Mod Manager - Phone App](https://www.nexusmods.com/schedule1/mods/397) for in-game settings.
3. Download `InventoryExpanded-v1.3.3.zip` from the Nexus Mods page after it is
   uploaded.
4. Extract `BackpackInventoryQoL.dll` and place it in `<Schedule I>\Mods`.
5. Start the game and confirm `Inventory Expanded` version `1.3.3` appears in the
   MelonLoader log.

Existing OG Backpack saves and settings remain compatible. The DLL, Backpack
save subfile, preference identifiers, and legacy configuration paths retain their
old internal names deliberately.

> **Migration and uninstall warning:** Empty the other mod's backpack before
> uninstalling it or switching from another backpack mod. Also empty the
> Backpack before uninstalling this mod. Items left in mod-owned storage may
> become inaccessible when that mod is removed.

Back up important saves before changing inventory mods.

### Schedule I 0.4.7f12 compatibility

The currently published MAIN `1.3.0` file was built for Schedule I `0.4.6f13`.
On `0.4.7f12`, its name-only `PlayerManager.TryGetPlayerData` Harmony target
can fail because that method has multiple overloads. The prepared unified MAIN
`1.3.2` package uses the explicit overload and the 0.4.7 player-data lifecycle
already exercised by the former BETA build. It includes host-controlled
gameplay settings, guest Backpack persistence, and the live-tested native
employee-reserved-slot routing fix. The exact RC6 DLL is staged for manual
Nexus upload; until upload, the public file remains 1.3.0. Do not use public
MAIN `1.3.0` on `0.4.7f12`.

Earlier RC3 logs confirmed the `Lobby.LobbyID` wrapper remained zero while the
game was in a Steam lobby; its fingerprint represented the default zero value.
The final 1.3.2 implementation captures the active `SteamLobbyService` at its
native lifecycle callbacks, cross-checks the owner identity, and reads and
writes policy metadata through that service. It defers first phone layout
measurement until Unity's canvas layout pass. The maintainer reports successful
host publication, exact readback, guest policy acceptance, Backpack use, and
the employee-reserved-slot routing fix in live tests. Occasional delayed
contextual Backpack availability for a multiplayer guest remains unexplained.

The separate BETA release channel is retired from active development. Its
tested implementation and old DLLs are preserved as historical material; the
release uses the established MAIN DLL name, version identity and settings.
Install only one `BackpackInventoryQoL.dll`.

## Upgrading from 1.1.14 or an Older Backpack Build

1. Close Schedule I.
2. Remove or move the old active `Mods\Backpack.dll` out of the Mods directory.
3. Install `BackpackInventoryQoL.dll` in `<Schedule I>\Mods`.
4. Keep `UserData\MelonPreferences.cfg`, `UserData\Backpack.cfg`,
   `UserData\BackpackMod.cfg`, existing saves, and OG Backpack preference
   sections; they are supported compatibility inputs.
5. Load a save and verify the Backpack contents before continuing play.

Do not leave `Backpack.dll` and `BackpackInventoryQoL.dll` active together.
Inventory Expanded 1.2.0 detects known legacy Backpack assemblies and refuses to
initialize its runtime or Harmony patches when one is present. Its warning can
move a positively identified old DLL to `Mods\Disabled` without deleting it or
touching saves/configuration; a restart is still required.

## Controls

- **B** opens or closes the standalone Backpack by default.
- **Search** filters visible slots by item display name without changing order.
- **Previous** and **Next** change pages; the mouse wheel also pages the view.
- **Sort** sorts the complete inventory, including slots on other pages.
- **R** uses Take All on a supported open storage container by default.
- **Backslash** sorts the active supported container by default.
- Drag the contextual Backpack by its header and resize it from the lower corner.
- Use the game's normal drag/drop and quick-move controls to transfer items.

Standalone and contextual Backpack views retain Search. Phone Backpack uses
pagination and Sort but intentionally has no Search input, so gameplay letter
hotkeys remain responsive while the Phone is open.

The Backpack Open Key is configurable in Mod Manager & Phone App.

## Configuration

Normal settings appear under **Inventory Expanded**. They use MelonPreferences
directly, so no JSON file selection or manual configuration editing is required.

### Backpack

| Setting | Default | Description |
| --- | --- | --- |
| Show Backpack with Phone | On | Shows the adaptive Backpack beside supported Phone apps. |
| Phone Backpack Distance | 40 | Controls portrait spacing between the Phone and Backpack panels. |
| Phone Backpack UI Scale | 100% | Scales the complete Phone Backpack from 50–150% after automatic Interface Scale compensation. |
| Backpack Capacity | 128 | Number of Backpack slots, from 1 to 128. Reducing capacity does not silently discard occupied slots. |
| Backpack Open Key | B | Keyboard key used to open or close the Backpack. |
| Include Backpack in Police Searches | Off | Allows police body searches to find Backpack contents. |

### Contextual Backpack

| Setting | Default | Description |
| --- | --- | --- |
| Show Contextual Backpack | On | Shows the Backpack beside compatible containers and machines. |
| Open Contextual Backpack Automatically | On | Opens the contextual Backpack with compatible inventories. |
| Remember Panel Position | On | Saves the contextual panel position. |
| Remember Panel Size | On | Saves the contextual panel size. |
| Reset Panel Layout | Off | Restores the contextual panel's default position and size once. |

### Inventory & Transfers

| Setting | Default | Description |
| --- | --- | --- |
| Show Sort Buttons | On | Shows Sort controls added by Inventory Expanded. |
| Shift-Click Destination Priority | Backpack First | Chooses which valid player destination is attempted first. |
| Take All Key | R | Transfers valid items from supported simple storage. `None` unbinds it. |
| Sort Active Container Key | Backslash | Sorts the active supported container. `None` unbinds it. |
| Diagnostic Logging | Off | Writes detailed Inventory Expanded diagnostics to the MelonLoader log for troubleshooting and bug reports. |

Phone controls intentionally live at the top of **BACKPACK** rather than in a
separate visible category. Preferred Grid Columns and Required Rank/Tier are not
active controls; rank/tier values remain hidden only for compatibility and do
not gate access.

Migration version 6 resolves missing settings independently. An explicitly
saved current key wins, followed by a compatible former Inventory Expanded
value, OG Backpack value, legacy `.cfg` value, then the current default. Version
5 installations receive one repair pass. Migration is idempotent, and legacy
files/sections are not deleted.

## Inventory Tools

Search is display-only: it never changes slot order or contents. Clearing Search
restores normal pagination. Sort always operates on the complete authoritative
inventory rather than only the visible page.

The shared Sort order places packaged `ProductItemInstance` items first,
unpackaged product instances second, and all other items third. Existing
ID/name/source ordering remains in effect inside each group.

Contextual access is available wherever Schedule I exposes the supported native
quick-move interfaces. This mod retains native slot filters, add locks,
machine input/output rules, partial transfers, and dedicated cash routing.

The native slot-filter submenu remains available. The added quick actions reuse
the game's filter state and mutation paths:

- **Use Current Item** creates a whitelist from the item already in the slot.
- **Copy Filter** copies the current native filter.
- **Paste Filter** applies the copied filter through the native panel.

## Cheats

Gameplay-changing options appear separately under **CHEATS**. The Stack Size
Multiplier offers Vanilla/1x,
2x, 4x, and 8x while preserving each item's original stack-size differences.

Independent default-off Increase Production settings in the published 1.3.0
build cover drying racks, mixing stations, and player plant hand-harvests. Each
uses the selected Stack Size multiplier only for its own system. Plant harvests
require room for the complete multiplied yield; otherwise the native harvest
interaction does not take the bud. Employee/NPC harvesting remains vanilla.
Packaging Stations is deferred from the 1.3.0 public feature set; its retained
preference is hidden and inactive in the 1.3.0 builds.

The MAIN 1.3.2 candidate synchronizes effective Stack Size and
production settings from the Steam lobby owner for compatible modded peers.
Guest preferences remain saved locally and return after leaving the lobby. Its
opt-in Mk1 Packaging Stations setting is visible in the candidate; Mk2
throughput remains native. During a compatible multiplayer session, gameplay
settings follow the host while personal UI preferences remain local. This is a
session override; guest preferences are not overwritten. The 1.3.2 package is
not part of the published 1.3.0 release.

Stack Size defaults to Vanilla, and each production checkbox defaults Off.
Production settings have no effect at Vanilla. Achievement suppression remains
tied to the active non-Vanilla Stack Size cheat; returning to Vanilla
re-enables achievements. Reducing the multiplier preserves existing oversized
stacks instead of clamping or deleting items.

## Compatibility

The current MAIN compatibility build target is Schedule I `0.4.7f12`, with a
preserved generated-reference profile at `dev-bootstrap/refs/MAIN_047f12`.
The published MAIN `1.3.0` targeted `0.4.6f13`; that older reference snapshot
remains preserved for audit history. The current local runtime reports:

- Schedule I `0.4.7f12`;
- MelonLoader `0.7.3 Open-Beta`;
- Mod Manager & Phone App `2.2.4`.

These versions are confirmed from local runtime logs and project references.
They are reference versions, not a guarantee for later game or loader builds.
The current public build targets Schedule I's IL2CPP release. A MONO project
configuration remains in the source tree but is not the validated public target.

The 1.3.2 release candidate requires compatible modded builds on all peers for
host-controlled gameplay settings. Unmodded or mismatched clients do not receive
the policy; non-Vanilla shared stack settings are unsupported with those peers.
Guest slot locks remain local. Some multiplayer guests may experience a delay
before contextual Backpack availability; its cause is unconfirmed.

## Troubleshooting

- Check `<Schedule I>\MelonLoader\Latest.log` for `[Inventory_Expanded]`
  messages. Enable **Diagnostic Logging** under **INVENTORY & TRANSFERS** before
  reproducing a UI, routing, Sort, transfer, or performance issue; disable it
  again for normal play.
- Confirm only `BackpackInventoryQoL.dll` is active. Remove or move any old
  active `Backpack.dll` while preserving legacy `.cfg` files, preferences, and
  save data.
- If the mod does not load, verify MelonLoader generated
  `MelonLoader\Il2CppAssemblies` and that the game/loader versions are compatible.
- If a contextual panel is hidden or misplaced, use **Reset Panel Layout** in
  Mod Manager & Phone App.
- If contents appear wrong after switching backpack mods, stop playing and
  restore a backup; do not save repeatedly over the affected file.
- Include `Latest.log`, game version, MelonLoader version, the Diagnostic Logging
  state, the installed DLL's SHA-256, and a short reproduction sequence in bug
  reports. Include a Mods-directory listing when startup fails so stale or
  duplicate DLLs can be ruled out.

## Credits

Inventory Expanded is developed and released by **PigeonLabz**. Some existing
assembly and MelonLoader metadata spells the name `PigeonLabs`; that historical
identifier is retained in builds and tooling.

### Upstream attribution

Inventory Expanded is a save-compatible continuation of **OG Backpack by
D-Kay**. Both the original author's and PigeonLabz's notices are retained in
[`LICENSE`](LICENSE); other third-party license notices remain with their
respective components.

## Documentation

- [Contributing and building from a clone](CONTRIBUTING.md)
- [Development guide](docs/DEVELOPMENT.md)
- [Manual testing checklist](docs/TESTING.md)
- [Architecture and invariants](docs/ARCHITECTURE.md)
- [Release process](docs/RELEASING.md)
