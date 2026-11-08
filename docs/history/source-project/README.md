# Inventory Expanded - Backpack & Inventory QoL

Expanded Backpack storage and inventory quality-of-life tools for Schedule I.
Inventory Expanded builds on OG Backpack's save-compatible storage instead of
replacing the game's inventory system.

Current public Nexus release: **1.1.14**  
Current unreleased development/release-candidate target: **1.2.0**

Official Nexus Mods page:
[Inventory Expanded - Backpack & Inventory QoL](https://www.nexusmods.com/schedule1/mods/2465)

The canonical short name is **Inventory Expanded**. The primary descriptive
title is **Inventory Expanded - Backpack & Inventory QoL**. Internal
`InventoryExpanded`, `Backpack`, preference, save, and DLL identifiers remain
where required for backwards compatibility.

## Features

- configurable Backpack capacity up to 128 slots;
- responsive grid, pagination, Search, and full-inventory Sort;
- contextual Backpack access beside compatible containers and machines;
- an adaptive Phone Backpack on supported Phone apps;
- native drag-and-drop and validated quick-move routing;
- configurable shift-click destination priority with safe fallback;
- Take All and Sort Active Container shortcuts;
- Sort controls for the Backpack and storage inventories, including expanded storage layouts;
- packaged, then unpackaged, then other-item product sort priority;
- persistent player/Backpack slot locks for automation safety;
- shortcuts for using, copying, and pasting native slot filters;
- native Mod Manager & Phone App configuration.

## Installation

The current public 1.1.14 package installs `Backpack.dll`. For the unreleased
1.2.0 release candidate, close the game, remove the old active
`Mods\Backpack.dll`, and install `BackpackInventoryQoL.dll` in:

```text
<Schedule I>\Mods
```

Do not delete `Backpack.cfg`, `BackpackMod.cfg`, OG Backpack preference
categories, or existing save/configuration data. They are supported migration
inputs. Migration version 6 resolves each setting independently: an explicitly
saved current key wins, followed by a compatible former Inventory Expanded
value, OG Backpack value, legacy `.cfg` value, and finally the current default.

Do not leave both DLLs active. Inventory Expanded 1.2.0 detects known conflicting
legacy Backpack DLLs, warns the user, and withholds its runtime/Harmony patches.
When the DLL is positively identified, **Disable it for me** moves it to
`Mods\Disabled` without deleting or overwriting it. Saves and configuration are
not touched, and the game must still be restarted.

### Migration and Uninstall Warning

**Empty the other mod's backpack before uninstalling it or switching from another
backpack mod. Also empty this mod's Backpack before uninstalling Inventory
Expanded.** Items left in mod-owned storage may become inaccessible when
the mod that owns that storage is removed.

## Controls

- **B** opens or closes the Backpack by default.
- **R** takes all eligible items from the active supported container by default.
- **Backslash** sorts the active supported container by default.
- **Search** filters visible slots by item name without changing inventory order.
- **Previous** and **Next** change pages. The mouse wheel also pages the open view.
- **Sort** sorts the complete inventory, including slots on other pages.
- Drag the contextual Backpack by its header and resize it from the lower corner.

Search remains available in the standalone and contextual Backpack. The Phone
Backpack intentionally has no Search input so gameplay letter hotkeys remain
responsive.

The Backpack Open Key can be changed in Mod Manager & Phone App.

## Configuration

Normal settings appear under **Inventory Expanded**. They use MelonPreferences
directly; no JSON file selection or manual configuration editing is required.

### Backpack

| Setting | Default | Description |
| --- | --- | --- |
| Backpack Capacity | 128 | Sets the Backpack to 1-128 slots. Occupied slots are not silently discarded when reducing capacity. |
| Backpack Open Key | B | Opens or closes the standalone Backpack. |
| Show Backpack with Phone | On | Shows the adaptive Backpack beside supported Phone apps. |
| Phone Backpack Distance | 40 | Controls portrait spacing between the Phone and Backpack panels. |
| Include Backpack in Police Searches | Off | Allows police body searches to find Backpack contents. |

### Contextual Backpack

| Setting | Default | Description |
| --- | --- | --- |
| Show Contextual Backpack | On | Shows the Backpack beside compatible inventories. |
| Open Contextual Backpack Automatically | On | Opens that panel with compatible containers and machines. |
| Remember Panel Position | On | Saves the contextual panel position. |
| Remember Panel Size | On | Saves the contextual panel size. |
| Reset Panel Layout | Off | Restores the contextual panel's default position and size once. |

### Inventory & Transfers

| Setting | Default | Description |
| --- | --- | --- |
| Show Sort Buttons | On | Shows Sort controls added by Inventory Expanded. |
| Shift-Click Destination Priority | Backpack First | Chooses which valid destination is attempted first, with safe fallback to the other. |
| Take All Key | R | Takes all eligible items from the active supported container. `None` unbinds it. |
| Sort Active Container Key | Backslash | Sorts the active supported container. `None` unbinds it. |

Phone Backpack controls appear near the top of **Backpack**, not in a separate
visible category. Preferred Grid Columns and Required Rank/Tier are not active
settings; hidden compatibility values do not gate Backpack access.

## Cheats

Gameplay-changing options are kept in the separate **Cheats** category. The Stack Size Multiplier offers Vanilla/1x, 2x, 4x, and 8x while
preserving each item's original stack-size differences.

All cheat settings default to Vanilla, so a default installation enables no cheats.

Any non-vanilla cheat suppresses achievement unlocks while it is active. Returning
all cheat settings to vanilla re-enables achievements. Existing oversized stacks
are preserved when the multiplier is reduced; Inventory Expanded never clamps or
deletes their contents.

## Compatibility

This release was tested with:

- Schedule I `0.4.6f13`;
- MelonLoader `0.7.3 Open-Beta`;
- Mod Manager & Phone App `2.2.4`.

Legacy values may be recovered from compatible preference categories and
`UserData\Backpack.cfg` or `UserData\BackpackMod.cfg` when needed. Those files
are never deleted. Installations previously marked as migration version 5 receive
an idempotent, one-time version 6 repair for missing target keys.

Inventory Expanded retains the game's native inventory rules. Contextual access is
available for inventories that use the supported native quick-move interfaces.

## Credits

Originally based on **OG Backpack by D-Kay**. Inventory Expanded is maintained
with compatibility and inventory-quality contributions from the community.

## Maintenance

Maintenance is planned around the next two to three meaningful public releases,
including focused compatibility fixes and backwards-compatible improvements. This
is a practical near-term commitment rather than a promise of indefinite support.

Developer invariants and regression checks are documented in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
Public versioning and release safeguards are documented in
[docs/RELEASE_POLICY.md](docs/RELEASE_POLICY.md).
