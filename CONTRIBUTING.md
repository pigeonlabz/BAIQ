# Contributing to Inventory Expanded

PigeonLabz has opened this source so other Schedule I modders can study it,
repair compatibility breaks, and continue development. Contributions and forks
are welcome. PigeonLabz may not be available to review issues or pull requests
regularly; a fork can continue independently under the MIT license.

## Before changing code

- Read [the development guide](docs/DEVELOPMENT.md),
  [architecture and invariants](docs/ARCHITECTURE.md), and
  [testing checklist](docs/TESTING.md).
- Keep the existing `Backpack` save subfile, preferences, assembly identity, and
  migration paths compatible unless a deliberate migration is fully tested.
- Treat every inventory transfer, Sort, capacity change, and save operation as
  a possible item-loss path. Test with backed-up saves and verify item identity
  and quantity before and after the change.
- Check behavior in Schedule I itself. A successful build or managed test does
  not establish that IL2CPP patches and Unity UI work at runtime.

## Building from a clone

You need a legally installed Schedule I copy with MelonLoader initialized,
generated IL2CPP assemblies, and a .NET SDK that supports `net6.0`.
Game and MelonLoader assemblies are not included in this repository.

The current IL2CPP target was developed against Schedule I `0.4.7f12`. Set
`SCHEDULE_I_DIR` to the game directory before building, or pass that directory
as `-p:S1Dir=...`. The build checks the `Assembly-CSharp.dll` hash and stops if
the generated references do not match the supported target.

```powershell
$env:SCHEDULE_I_DIR = 'C:\Path\To\Schedule I'
dotnet restore .\InventoryExpanded.sln
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj -c IL2CPP
```

The DLL is written to
`src/InventoryExpanded/bin/IL2CPP/net6.0/BackpackInventoryQoL.dll`. Keep game
assemblies, generated references, local logs, saves, and credentials out of
commits. See the development guide for local testing and release details.

## Reporting and reviewing changes

For a bug report, include the game, MelonLoader, and mod versions; a short
reproduction; the relevant `Latest.log` excerpt; and whether the same save
works without the mod. Remove account IDs, save contents, and other private
information from logs before sharing them.

For a change, explain the observed behavior, the root cause, and the exact
manual and automated checks performed. Call out any untested multiplayer,
save/load, or inventory paths. Published packages and release notes are tracked
on [Nexus Mods](https://www.nexusmods.com/schedule1/mods/2465).

## Credit and licensing

Inventory Expanded is a PigeonLabz project built on OG Backpack by D-Kay.
Keep both copyright notices and the MIT license in source distributions.
Please credit PigeonLabz and D-Kay in forks and redistributed builds. The
license permits independent maintenance; it does not require PigeonLabz to
approve or publish third-party work.
