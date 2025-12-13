# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Terraria Access is a tModLoader mod that makes Terraria playable for blind and low-vision players. It provides NVDA-driven speech narration for menus and in-game UI, plus synthesized positional audio cues for spatial awareness.

## Build Commands

```bash
# Build the mod and deploy to tModLoader Mods folder
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1

# Build without deploying
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -SkipDeploy

# Lint client.log for NVDA failures or missing narration after playtest
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -NarrationLint
```

The build script produces `ScreenReaderMod.tmod` and copies it to `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\Mods`.

## Debugging

Logs are in `tModLoader-Logs/client.log`. Key log tags:
- `[Narration]`, `[WorldNarration]`, `[MenuNarration]` - speech events
- `[NVDA]` - NVDA controller API calls
- `[NarrationScheduler][Timing]` - scheduler performance (when trace enabled)

Environment variables for debugging:
- `SCREENREADERMOD_TRACE=1` - verbose speech/narration breadcrumbs
- `SCREENREADERMOD_SPEECH_LOG_ONLY=1` - log announcements without speaking
- `SCREENREADERMOD_SCHEDULER_TRACE_ONLY=1` - scheduler trace-only mode
- `SRM_DEBUG_NARRATION=1` - dump inventory narration focus history
- `SRM_NARRATION_HISTORY_DISABLED=1` - disable hover deduplication
- `SRM_NARRATION_HISTORY_MAX_AGE=<frames>` - adjust dedupe window

## Architecture

The mod lives in `Mods/ScreenReaderMod/` with this structure:

### Speech Pipeline (`Common/Services/`)
- `ISpeechProvider` - interface for speech backends
- `NvdaSpeechProvider` - NVDA integration via `nvdaControllerClient64.dll`
- `SpeechController` - throttling and interrupt handling
- `WorldAnnouncementService` - SAPI fallback for world events
- `ScreenReaderService` - provider registration and lifecycle

### Menu Narration (`Common/Systems/MenuNarration/`)
- `MenuNarrationController` - main menu narration coordinator
- `IMenuNarrationHandler` / `MenuNarrationHandlerRegistry` - handler pattern for different menus
- `MenuNarrationCatalog.*` - lookup tables for menu labels (MainMenus, Settings, Multiplayer)
- `MenuUiSelectionTracker` - captures keyboard/gamepad focus across menu types (character creation, world creation, mod browser)

### In-Game Narration (`Common/Systems/InGameNarration/`)
- `InGameNarrationSystem` - entry point, configures the scheduler
- `NarrationScheduler` - coordinates multiple narrator services with gating rules
- Partial classes for each narrator domain:
  - `InventoryNarrator.*` (Core, Focus, Models, Tooltips, SpecialSelections)
  - `CraftingNarrator`, `HotbarNarrator`, `CursorNarrator`, `SmartCursorNarrator`
  - `NpcDialogueNarrator`, `LockOnNarrator`, `ControlsMenuNarrator`, `IngameSettingsNarrator`
  - `WorldInteractableTracker` - tile/item scanning for exploration mode
  - Audio emitters: `FootstepAudioEmitter`, `BiomeAnnouncementEmitter`, `HostileStaticAudioEmitter`, `TreasureBagBeaconEmitter`, `ClimbAudioEmitter`

### Guidance System (`Common/Systems/Guidance/` and `Common/Systems/GuidanceSystem*.cs`)
- Waypoint management, category cycling (None/Exploration/Interactable/NPC/Player/Waypoint)
- `GuidanceSystem.State.cs` - state machine for active target
- `GuidanceSystem.Audio.cs` - positional tone synthesis
- `GuidanceSystem.Scan.cs` - entity scanning
- `GuidanceKeybinds` - keybind registration

### Build Mode (`Common/Systems/BuildMode/`)
- `BuildModePlayer` - selection marking and batch tile/wall operations
- `BuildModeKeybinds` - toggle and corner placement bindings
- `BuildModeRangeManager` - viewport range extension
- `BuildModeNarrationCatalog` - completion/progress announcements

### Utilities (`Common/Utilities/`)
- `TextSanitizer` - clean text for speech
- `GlyphTagFormatter` - convert glyph tags to readable text
- `SliderNarrationHelper` - format slider values
- `CoinFormatter`, `SlotContextFormatter` - item/slot formatting
- `LocalizationHelper` - localization key resolution

## Extension Points

**Speech providers:** Implement `ISpeechProvider`, register in `ScreenReaderService`. Populate `Name`, `Initialized`, `Available`, `LastMessage`, `LastError`.

**Menu handlers:** Implement `IMenuNarrationHandler`, add to `MenuNarrationHandlerRegistry`. Use `MenuNarrationCatalog` resolvers over reflection.

**In-game narrators:** Wrap in `DelegatedNarrationService`, register in `InGameNarrationSystem.ConfigureNarrationScheduler` with `NarrationServiceGating`.

**Interactables:** Register sources via `WorldInteractableTracker.RegisterSource`. Use `TileInteractableDefinition` with `InteractableCueProfile` for new tile types, or subclass `WorldInteractableSource` for custom scans.

## Key Files

- `ScreenReaderMod.cs` - mod entry point
- `Localization/en-US_Mods.ScreenReaderMod.hjson` - all user-facing strings
- `build.txt` - tModLoader mod metadata
