# Developer Notes

This mod is organized around small services so we can reason about narration in isolation. Use the map below when adding features or debugging speech issues.

## Module boundaries
- `Mods/ScreenReaderMod/Common/Services/`: Speech pipeline (ISpeechProvider implementations, SpeechController throttling, ScreenReaderDiagnostics, RuntimeContext, WorldAnnouncementService).
- `Mods/ScreenReaderMod/Common/Systems/MenuNarration/`: MenuNarrationController with handler registry, MenuNarrationCatalog tables, and MenuUiSelectionTracker hover/focus capture.
- `Mods/ScreenReaderMod/Common/Systems/InGameNarration/`: NarrationScheduler coordinates hotbar, inventory, crafting/guide/reforge, cursor/smart cursor, NPC dialogue, settings/controls, world events/lock-on, positional audio, and world interactable tracker services.
- `Mods/ScreenReaderMod/Common/Systems/Guidance/`: GuidanceSystem state, keybinds, and exploration tracking hooks.
- `Mods/ScreenReaderMod/Common/Systems/BuildMode/`: BuildModePlayer and keybind wiring for selection and batch placement/clearing.
- `Mods/ScreenReaderMod/Common/Utilities/`: Shared formatters and helpers (TextSanitizer, GlyphTagFormatter, SliderNarrationHelper, CoinFormatter, SlotContextFormatter, etc.).

## Extension hooks
- Speech providers: implement `ISpeechProvider` and register it inside `ScreenReaderService` (BuildController). Providers should populate `Name`, `Initialized`, `Available`, and surface `LastMessage/LastError` so diagnostics snapshots stay useful.
- Menu handlers: implement `IMenuNarrationHandler` and add it to `MenuNarrationHandlerRegistry` in `MenuNarrationController`. Handlers receive `MenuNarrationContext` and emit `MenuNarrationEvent` instances; prefer MenuNarrationCatalog resolvers and MenuUiSelectionTracker predicates over reflection.
- In-game services: wrap new narrators in `DelegatedNarrationService` and register them in `InGameNarrationSystem.ConfigureNarrationScheduler` with appropriate `NarrationServiceGating` (category, menu/paused rules). Use `NarrationInstrumentationContext` to tag expensive calls or keys.
- Interactable definitions: `WorldInteractableTracker` registers sources via `RegisterSource`. Reuse `TileInteractableSource` with new `TileInteractableDefinition` entries or subclass `WorldInteractableSource` for bespoke scans. Pair each definition with an `InteractableCueProfile` so guidance/exploration cues and arrival labels stay consistent.

## Debugging and flags
- `SCREENREADERMOD_TRACE=1`: enable diagnostics snapshots and verbose speech/narration breadcrumbs.
- `SCREENREADERMOD_SPEECH_LOG_ONLY=1`: mute speech and log announcements instead (honors per-category throttles).
- `SCREENREADERMOD_SCHEDULER_TRACE_ONLY=1`: scheduler trace-only mode; services log ordering without speaking.
- `SRM_DEBUG_NARRATION=1`: dump inventory narration focus history.
- `SRM_NARRATION_HISTORY_DISABLED=1` / `SRM_NARRATION_HISTORY_MAX_AGE=<frames>`: adjust hover dedupe for inventory/tooltips.
- Logs live in `tModLoader-Logs/client.log` (Steam install exposes a `Logs.lnk` inside the tModLoader documents folder). Look for `[Narration]`, `[WorldNarration]`, `[MenuNarration]`, and `[NVDA]` breadcrumbs.
- Run `pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -NarrationLint` to scan the latest `client.log` for NVDA failures or missing menu narration lines after a playtest.
