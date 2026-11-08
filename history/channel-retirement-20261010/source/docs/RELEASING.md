# Release Process

The canonical public page is
[Inventory Expanded - Backpack & Inventory QoL on Nexus Mods](https://www.nexusmods.com/schedule1/mods/2465).
Do not upload or publish automatically from this repository.

## Version Convention

Inventory Expanded uses `MAJOR.MINOR.PATCH` from the first public release,
`1.0.0`:

- `MAJOR` = breaking/incompatible generation or major redesign;
- `MINOR` = backwards-compatible feature update;
- `PATCH` = bugfix, compatibility, performance, or minor UI correction;
- no public bump = diagnostics, internal builds, and unreleased test DLLs.

The README records MAIN `1.3.0` as the current published package. The next
candidate is MAIN `1.3.1`, a compatibility fix for Schedule I `0.4.7f12`;
BETA remains the separate `1.3.0-beta.47` artifact. Further fixes for this
feature set belong on the `1.3.x` line. A `2.0.0` release would denote
a breaking change or major generation.

Do not retrospectively renumber releases. Early post-release numbering and history
were not recorded consistently before source/release discipline was established;
do not reconstruct missing intermediate entries without authoritative evidence.

## 1. Prepare

For an unreleased candidate, run `tools/prepare-release-candidate.ps1` from
the project. It runs the migration, post-release and pre-release managed test
suites, verifies the separate reference profiles, builds both targets without
deployment, reports Melon/product versions and SHA-256 hashes, and records the
test summaries in the candidate manifest. Choose a new leaf name with
`-StageName`, for example `1.3.1-main-047f12-20261010`, so an older
candidate remains preserved. The script refuses to overwrite a different DLL
or manifest at the selected path. Use
[TESTING.md](TESTING.md) for current live checks. Keep the 1.3.0 checklist,
notes, package and candidate directories as historical records. The public
README version stays at its currently published value until release.

1. Review the working tree or equivalent source change list and remove accidental
   build output; do not discard unrelated user work.
2. Confirm the intended scope is complete and contains no unrelated refactor.
3. Preserve saves, Backpack contents, preference keys, and persistence paths.
4. Keep normal defaults non-cheat and achievement suppression active for cheats.
5. Update `CHANGELOG.md` with concise completed user-facing behavior only.
6. Update tested versions or known limitations in `README.md` only when verified.
7. Ensure legacy/archive/reference material has not been removed.
8. Verify migration version 8 against fresh, organized, former Inventory Expanded,
   OG Backpack, legacy `.cfg`, mixed/partial, version 6/7 repair, and malformed-value
   cases. The former single Increase Production value migrates only to Drying
   Racks; new system settings stay off. Preserve every legacy source after migration.

The migration/uninstall warning must remain prominent: users should empty another
backpack mod before switching and empty this Backpack before uninstalling.

## 2. Synchronize Versions

For a published release, the following public version locations must agree.
While `1.3.1` is unreleased, the README continues to describe the published
1.3.0 package, and the candidate version is checked against its build metadata
and staged manifest:

- `src/InventoryExpanded/BackpackMod.cs` `MelonInfo` version;
- `src/InventoryExpanded/InventoryExpanded.csproj` `<Version>`;
- `README.md` current public version;
- the newest public entry in `CHANGELOG.md`.

Also verify the Melon name/author/Nexus URL, assembly product/title/description,
and `NexusModID` metadata. Beginning with 1.2.0 the assembly is
`BackpackInventoryQoL.dll`; do not rename the `Backpack` namespace, save
identities, or preference keys solely for branding.

The 1.2.0-and-later public upgrade instructions must explicitly tell users to remove the
old `Backpack.dll` before installing `BackpackInventoryQoL.dll`. Loading both
filenames would create a legacy-mod conflict. Do not tell users to delete
`Backpack.cfg`, `BackpackMod.cfg`, OG Backpack preferences, or save data.

## 3. Clean Build

Close Schedule I. From the repository root:

```powershell
dotnet restore .\InventoryExpanded.sln
dotnet clean .\src\InventoryExpanded\InventoryExpanded.csproj -c IL2CPP
dotnet build .\src\InventoryExpanded\InventoryExpanded.csproj `
  -c IL2CPP --no-restore
```

The public release configuration is named `IL2CPP`, not `Release`. Do not set
`DeployToGame=true` for a clean artifact build because that target kills,
copies to, and restarts the game.

Expected artifact:

```text
src\InventoryExpanded\bin\IL2CPP\net6.0\BackpackInventoryQoL.dll
```

Verify metadata and record the artifact hash:

```powershell
$dll = Resolve-Path .\src\InventoryExpanded\bin\IL2CPP\net6.0\BackpackInventoryQoL.dll
[Reflection.AssemblyName]::GetAssemblyName($dll).Version
Get-FileHash -LiteralPath $dll -Algorithm SHA256
Get-Item -LiteralPath $dll | Select-Object FullName, Length, LastWriteTime
```

`dotnet build` may report NuGet vulnerability-metadata warnings when the package
source is offline. Record warnings, but do not treat a successful compile as
runtime acceptance.

## 3.5. "For Release" VirusTotal Workflow

When a DLL/build is explicitly designated **for release**, the final release
workflow must also include VirusTotal verification for the exact final
`BackpackInventoryQoL.dll`.

1. Build the final release DLL.
2. Calculate its SHA-256 locally.
3. Query VirusTotal for that exact SHA-256 using `VT_API_KEY` from the
   environment.
4. Never print, store, commit, or expose the API key.
5. If an exact-hash VirusTotal report already exists, reuse it.
6. Otherwise upload the final release DLL and wait for the report.
7. Verify VirusTotal reports the exact same SHA-256 as the local DLL.
8. Return:
   - DLL path;
   - DLL filename;
   - version/build result;
   - local SHA-256;
   - VirusTotal status;
   - permanent VirusTotal report URL;
   - copy/paste-ready Nexus BBCode Security block.

Permanent report URL format:

```text
https://www.virustotal.com/gui/file/<SHA256>/detection
```

Standard Nexus output:

```bbcode
[size=4][b]⚠️ Security[/b][/size]

[b]THIS MOD ONLY EXISTS ON NEXUS MODS[/b]

The release DLL can be checked independently on VirusTotal:
[url=<VIRUSTOTAL_URL>]View VirusTotal scan[/url]

SHA-256:
[code]<SHA256>[/code]
```

## 4. Test the Artifact

1. With the game closed, remove the old active `Backpack.dll` and copy the exact
   built `BackpackInventoryQoL.dll` to `<Schedule I>\Mods`. Preserve legacy
   configuration and save data.
2. Run the complete [manual testing checklist](TESTING.md), or every section
   affected by a patch release plus the lifecycle and safety passes.
3. Manually test the known-legacy-DLL conflict safeguard, including withheld
   initialization, warning, safe move to `Mods\Disabled`, and required restart.
4. Exercise migration version 7, including its one-time version 6 repair path.
5. Confirm the MelonLoader log has no patch failures, trampoline errors, access
   violations, or unhandled exceptions.
6. Re-hash the exact tested DLL and confirm it matches the release artifact
   before packaging and VirusTotal lookup/upload.

No automated test suite currently exercises Schedule I runtime behavior.

## 5. Package

Create a simple archive containing the tested `BackpackInventoryQoL.dll`:

```powershell
Compress-Archive `
  -LiteralPath .\src\InventoryExpanded\bin\IL2CPP\net6.0\BackpackInventoryQoL.dll `
  -DestinationPath .\InventoryExpanded-vX.Y.Z.zip
```

New 1.2.0-and-later Nexus main-file archives use the
`InventoryExpanded-vX.Y.Z.zip` naming convention; for example,
`InventoryExpanded-v1.2.0.zip`. Historical published artifacts keep their actual
names, including `BAIQ-MAIN-v1.1.14.zip`.

Inspect the archive contents and hash. Do not include local game assemblies,
MelonLoader binaries, logs, saves, `bin`, `obj`, or developer-only diagnostics.

Extract the archive to a temporary directory, install that extracted DLL, and run
at least the launch, existing-save, Backpack open/close, and content-persistence
smoke tests. Hash the extracted DLL and confirm it matches the built artifact.

## 6. Record and Publish Manually

1. Preserve the release DLL/hash in the project's existing release archive only
   when intentionally recording a completed release; never overwrite old records.
2. If working from a Git-enabled source checkout, commit the release docs/metadata
   and create an annotated `vX.Y.Z` tag.
3. Upload the tested package to Nexus Mods manually.
4. Paste concise release notes derived from the matching changelog entry.
5. Confirm the Nexus file version, description, requirements, credits, and
   migration/uninstall warning.
6. Download or inspect the published archive and perform a final launch smoke test.

The current source snapshot may be distributed without `.git` metadata. In that
case, tagging and `git diff` verification must happen in the canonical publishing
checkout, not be inferred from this snapshot.
