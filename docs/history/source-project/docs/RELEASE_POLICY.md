# Backpack & Inventory QoL Release Policy

Backpack & Inventory QoL is public production software. Version `1.0.0` is the
first public release, `1.1.3` is the current canonical public release, and earlier
version numbers in the changelog are pre-public history. Legacy/internal
`Inventory Expanded` identifiers intentionally remain for compatibility.

## Version Convention

- `MAJOR` releases are breaking/incompatible generations or major redesigns.
- `MINOR` releases add backwards-compatible features.
- `PATCH` releases contain bugfixes, compatibility fixes, performance work, or
  minor UI corrections.
- Diagnostic and internal-only builds do not receive public version bumps.

The next bugfix is `1.1.4`; the next feature release is `1.2.0`; its first
bugfix is `1.2.1`; a breaking generation is `2.0.0`. Do not retrospectively
renumber releases or reconstruct incomplete early post-release history without
authoritative evidence.

## Compatibility and Safety

- Preserve existing saves, Backpack contents, preference keys, and persistence
  paths wherever technically possible.
- Never perform destructive inventory migrations without explicit validation,
  quantity-preservation checks, and a safe abort path.
- Keep default settings non-cheat and keep Cheats visibly separate from QoL.
- Preserve achievement suppression whenever any cheat is enabled.
- Prefer focused fixes over unrelated refactors.
- Retain legacy, archive, and reference material unless removal is requested.

Users moving from another backpack mod must empty that mod's backpack before
uninstalling or switching. Users should also empty this mod's Backpack before
uninstalling Backpack & Inventory QoL.

## Release Checklist

1. Confirm all public version declarations agree.
2. Keep `CHANGELOG.md` concise, user-facing, and limited to completed behavior.
3. Build the IL2CPP release from a clean output directory.
4. When a DLL/build is explicitly marked **for release**, run the final release
   VirusTotal workflow against the exact release `Backpack.dll` using
   `VT_API_KEY`, reuse an exact-hash report when available, upload only when
   necessary, verify the VirusTotal SHA-256 matches the local SHA-256 exactly,
   and return the permanent report URL plus the standard Nexus Security block.
5. Verify assembly version, Melon metadata, Nexus ID, DLL hash, and filename.
6. Smoke-test save load/reload, Backpack contents, normal and contextual UI,
   transfers, Sort, settings persistence, and cheat achievement suppression.
7. Upload to Nexus Mods only after manual in-game acceptance.

## Maintenance Horizon

Plan work around the next two to three meaningful public releases. Reassess scope
after that period; this policy does not promise indefinite maintenance.
