# TraceDeck FE Development Specification

## Product

TraceDeck FE is a portable Windows 10/11 x64 WPF reference-overlay and color-assistance tool for manual Forza Horizon 6 vinyl/decal work. It is an unofficial community project and does not capture, inject into or read memory from the game.

Version 1.0.0 is feature complete. Changes on the v1 line should favor correctness, compatibility, documentation and focused fixes over scope expansion.

## Runtime architecture

- `MainWindow` is the always-available controller.
- `ForzaWindowTracker` observes target-window position, size, minimize/restore and foreground changes with native WinEvent hooks plus low-frequency lifecycle verification.
- `OverlayWindow` is an owned, non-activating and non-topmost window. Visibility is user state; Z-order follows normal Windows stacking.
- Reference placement is stored canonically as target-client-relative center X/Y and visual width. Pixel position and scale are derived, preventing repeated-resize drift.
- Move-only target changes move the overlay bounds without changing the reference transform. Automatic resize compensation is not an Undo/Redo action.
- Rendering and image effects are event-driven/dirty operations. There is no continuous FPS renderer.

## Reference and image processing

- Supported formats: PNG, JPG/JPEG, WebP, BMP, TIFF/TIF, SVG, ICO, AVIF and GIF.
- Animated GIF/WebP uses the first frame. TIFF uses the first page. SVG keeps its original vector bytes and refreshes a resolution-aware render cache when needed.
- Original bytes and original pixels are retained separately from display effects.
- Image Assist order is Original → Grayscale → Contrast → Display.
- Transform operations are move, scale, center-based rotation and horizontal/vertical flips.
- Grid and center guides are based on the target client area, not the reference bounds.

## Color and palette

- The picker is a single-shot operation and samples the retained original reference.
- Display coordinates are inverse-transformed through position, scale, rotation and flips before sampling.
- Outputs are source RGBA, HEX, RGB and normalized Forza HSB.
- Picker precision is preserved internally; the UI displays two or three HSB decimals according to settings.
- Palettes retain stable item IDs, names, RGBA, origin metadata and explicit order.
- Automatic palette extraction is explicit and cancellable, works on original pixels and produces 2–12 colors.

## Project persistence

- `.TDFE` v1 is a self-contained ZIP package.
- Required entries are validated JSON state and, when present, the embedded original reference with a manifest SHA-256.
- Opening is transactional and path-safe; the live document changes only after validation and decode succeed.
- Manual saves use same-directory temporary output, package revalidation, flush and atomic replacement/move.
- Runtime target handles, external source paths, render caches and Undo/Redo history are not serialized.
- Session Undo/Redo is bounded to 100 logical edits and shares immutable reference data instead of copying image bytes per action.

## Settings, shortcuts and recovery

- Application settings are separate from project state and are not Undo/Redo targets.
- Settings are written atomically after a debounce to `<exe>/data/settings.json`.
- Logs use `<exe>/data/logs/`; recovery uses `<exe>/data/recovery/<project-id>/`. AppData is not used.
- UI layouts are Compact, Wide and Auto with a 280–520 DIP controller-width range.
- Application shortcuts use routed WPF input. Work shortcuts are registered only while the controller or target is foreground.
- Autosave is dirty-only, single-flight and independent from manual `.TDFE` saves.
- Recovery keeps the three newest valid snapshots per project and deduplicates the original reference asset.
- English and Korean catalogs are immutable embedded JSON resources. Language changes apply at the next launch.

## Performance and safety requirements

- Preserve original dimensions, bytes, alpha and high-quality interpolation.
- Do not introduce arbitrary downscaling, automatic sharpening, AI enhancement, game capture, game memory access, injection or telemetry.
- Expensive raster decode, SVG rendering, effects and palette work may use cancellable background tasks. Stale work must never overwrite newer state.
- Avoid per-frame image work, `CompositionTarget.Rendering` loops and high-frequency polling.

## Release contract

- Product: TraceDeck FE v1.0.0; executable: `TraceDeckFE.exe`.
- Target: Windows 10/11 x64, self-contained, single-file portable build.
- Publish properties: `RuntimeIdentifier=win-x64`, `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishTrimmed=false`.
- Release root: `TraceDeckFE.exe`, `TraceDeck FE 사용방법.pdf` and `licenses/`.
- Release binaries belong in GitHub Releases, not the source repository.
- The source repository includes application source, tests, required resources, public documentation and reproducible packaging/documentation tools.

## V1 non-goals

- Installer, updater and code signing
- Cloud accounts, telemetry or sharing services
- AI or automatic tracing
- Game capture, memory access or injection
- Animated reference playback
- Persistent Undo/Redo history
- Dedicated multi-monitor optimization
- Reliable overlay operation in exclusive fullscreen

