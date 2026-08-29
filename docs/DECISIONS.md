# TraceDeck FE Architecture Decisions

## Product boundaries

- TraceDeck FE assists manual tracing; it does not create or inject vinyl/decal data.
- The app does not capture the game, read or modify game memory, inject code, use telemetry or run a continuous renderer.
- Original reference fidelity takes priority over destructive memory/performance shortcuts.
- Windowed or Borderless mode is the supported game workflow. Multi-monitor specialization and exclusive-fullscreen overlays are outside v1.

## Window and transform model

- Overlay Visible state and Windows foreground/Z-order are independent.
- The overlay is owned, non-activating and non-topmost so unrelated foreground apps naturally cover it.
- Reference geometry uses normalized target-client-relative center and visual width as canonical state.
- Target move/resize/stacking changes are not project edits and do not enter Undo/Redo.
- Rotation is center-based; position reset is separate from scale/rotation/flip reset.

## Image and color model

- Original source data, decoded pixels, display effects and rendered presentation are separate layers.
- The fixed display-effect order is Original → Grayscale → Contrast → Display.
- Grid and center guides belong to the target client area and remain independent from Reference Visible.
- The picker inverse-maps the displayed transform and samples retained original pixels, never the screen.
- Transparent pixels remain valid picker results. Automatic palette generation alone excludes fully transparent pixels.
- GIF and animated WebP use a static first frame; TIFF uses the first page.

## Project, history and recovery

- `.TDFE` v1 is self-contained and embeds the original reference with a SHA-256 integrity value.
- Project loading is transactional and archive paths are never extracted to arbitrary filesystem locations.
- Saves use validated same-directory temporary output and atomic replacement.
- Save As retains the project ID; New creates a new ID.
- Undo/Redo is session-only, limited to 100 logical actions and shares immutable reference objects.
- Application settings, target lifecycle and automatic viewport compensation are excluded from project dirty state and history.
- Autosave never overwrites a user's `.TDFE`; recovery uses separate bounded snapshots under portable `data/recovery/`.

## Settings and localization

- `IApplicationPaths` resolves all application-owned state below `<exe>/data`; AppData and registry persistence are not used.
- Settings use safe field-level fallback, debounced atomic writes and a normal-exit flush.
- Work hotkeys are reserved only in controller/target foreground context and registration failures disable only the affected mapping.
- Application settings own layout, widths, card state, input preferences, picker precision, shortcuts and recovery timing.
- English and Korean catalogs are embedded and paired. Language changes apply at next launch and do not mutate project data or numeric serialization.

## Branding and typography

- The supplied TraceDeck FE logo remains the project branding asset. No Microsoft, Xbox, Playground Games or Forza official logo is used.
- The application uses official Pretendard 1.3.9 Static Regular, Medium and SemiBold as WPF resources without system installation.
- The PDF guide embeds the official static TTF counterparts. Pretendard remains under the SIL Open Font License 1.1.

## Distribution and public repository

- V1 ships as a self-contained single-file `win-x64` executable with trimming disabled and native self-extraction enabled.
- The portable package root contains only the EXE, Korean PDF manual and full license/notice directory.
- Runtime user data is never preloaded in the release package.
- Original TraceDeck FE source is public under MIT: `Copyright (c) 2026 Revo*32`.
- Third-party software and fonts retain their upstream licenses; see `THIRD_PARTY_NOTICES.md` and `licenses/`.
- Release EXEs/ZIPs and generated PDFs are not source-controlled. Reproducible source, tests and build/documentation scripts are tracked.
- Minimal GitHub Actions CI restores, builds and tests on `windows-latest`; it has no publishing job or repository secret requirement.

