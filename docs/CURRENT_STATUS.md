# TraceDeck FE Current Status

Updated: 2026-08-29

## Release

| Item | Status |
| --- | --- |
| Current version | v1.0.0 |
| Product status | Stable / Release Ready |
| Feature status | Feature Complete |
| Platform | Windows 10/11 x64 |
| Distribution | Self-contained single-file portable ZIP |
| Source license | MIT, copyright 2026 Revo*32 |
| Blocking defects | None known |

Version 1.0.0 completed feature development, Final RC, physical Forza Horizon 6 acceptance, portable packaging and an additional external-PC usability pass. Runtime features are frozen for public-repository preparation.

## Verification

- Debug automated tests: 220 passed, 0 failed, 0 skipped.
- Release automated tests: 220 passed, 0 failed, 0 skipped.
- Debug and Release builds: 0 warnings, 0 errors.
- Physical FH6 acceptance: all 11 checklist groups passed.
- Repeated target resize, move tracking, native Z-order, Visible OFF persistence, minimize/restore, lock/click-through, cursor-centered zoom, opacity, original-pixel picker and target-close lifecycle were included.
- Large PNG and complex SVG reference-fidelity tests pass.
- Single-file publish, PE metadata/icon, embedded resources, Korean PDF, license payload and ZIP manifest validation pass.

## Distribution

The release asset is:

`TraceDeckFE-v1.0.0-win-x64-portable.zip`

Its clean root contains `TraceDeckFE.exe`, `TraceDeck FE 사용방법.pdf` and `licenses/`. The v1.0.0 ZIP SHA-256 is recorded in [the release notes](releases/v1.0.0.md).

Release binaries are published through GitHub Releases and are intentionally ignored by the source repository.

## Known limitations

- The v1.0.0 executable is unsigned and may trigger Windows reputation warnings.
- Windowed or Borderless Forza mode is recommended; exclusive fullscreen can hide a normal desktop overlay.
- V1 does not target dedicated multi-monitor optimization.
- Short idle and physical connected-idle checks are not a quantified GPU benchmark or multi-hour endurance certification.

## Documentation

- [Development specification](DEVELOPMENT_SPEC.md)
- [Architecture decisions](DECISIONS.md)
- [Asset provenance](ASSET_PROVENANCE.md)
- [Manual FH6 validation checklist](FH6_MANUAL_VALIDATION_CHECKLIST.md)
- [v1.0.0 release notes](releases/v1.0.0.md)
