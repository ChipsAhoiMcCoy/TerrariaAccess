# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Terraria Access is a tModLoader mod that makes Terraria playable for blind and low-vision players. It provides speech narration for menus and in-game UI via multiple screen readers, plus positional audio cues for spatial awareness.

**Key technologies:** C# (.NET 8.0), tModLoader, Tolk library for universal screen reader support (JAWS, NVDA, Window-Eyes, SuperNova, System Access, ZoomText, SAPI fallback)

## Build Commands

```powershell
# Build and deploy to local tModLoader Mods folder
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1

# Build only (no deployment)
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -SkipDeploy

# Build with narration lint (checks client.log for NVDA failures)
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -NarrationLint
```

The build script invokes tModLoader's build system (`dotnet tModLoader.dll -build`), not MSBuild directly. Output is `TerrariaAccess.tmod`.

When done with a task, build and deploy (without `-SkipDeploy`) so the mod is ready to test in tModLoader immediately.

## Architecture

### Core Systems (Mods/TerrariaAccess/Common/)

**Entry Point:** `TerrariaAccess.cs` - Initializes services and keybinds on mod load.

**Services Layer (`Services/`):**
- `ScreenReaderService` - Central speech API. Manages announcement categories (Default, Tile, Wall, Pickup, World) with per-category rate limiting. Routes to `SpeechController` -> `TolkSpeechProvider`.
- `SpatialAudioPanner` - Calculates stereo panning/pitch based on world position relative to player.
- `WorldAnnouncementService` - Handles world event announcements (blood moon, invasions, biome changes).
- `ChatHistoryService` - Chat message history tracking for review/narration.
- `ContextualInputRouter` - Routes keyboard input based on active UI context.
- `UiTickSoundPlayer` - Plays tick sounds on UI navigation changes.
- `SoundLoudnessUtility`, `SynthesizedSoundFactory` - Audio generation utilities.
- `UiAreaNarrationContext` - Tracks which UI area is active for narration context.
- `ScreenReaderDiagnostics` - Diagnostics/logging for Tolk communication.

**Systems Layer (`Systems/`):**
- `InGameNarrationSystem` - Partial class coordinating all in-game narrators via `NarrationScheduler`. Hooks into Terraria's ItemSlot, Main.NewText, PopupText, IngameOptions, etc.
- `MenuNarrationSystem` - Hooks into `Main.DrawMenu` to narrate main menu UI states.
- `GuidanceSystem` - Waypoint/target tracking with audio pings. Partial class split across:
  - `.cs` - Core logic, category cycling, waypoint management
  - `.Audio.cs` - Ping emission, tone generation
  - `.Scan.cs` - NPC/Player/Interactable/DroppedItem/Critter/Plantlife scanning
  - `.State.cs` - Selection mode state, waypoint storage
  - `.Networking.cs` - Multiplayer sync
  - Extracted helpers in `Guidance/` subdirectory: `GuidanceEntry`, `GuidanceScanner`, `GuidanceAudioPlayer`, `GuidanceStateManager`, `GuidanceNetworking`, `GuidanceKeybinds`

**Narrators (nested in InGameNarrationSystem):**
- `HotbarNarrator` - Hotbar slot navigation
- `InventoryNarrator` - Inventory navigation, split across partials:
  - `.Core.cs` - Main logic and slot focus tracking
  - `.Regions.cs` - Region detection and display names
  - `.Focus.cs`, `.Models.cs`, `.Tooltips.cs`, `.SpecialSelections.cs` - Supporting concerns
- `CraftingNarrator` - Recipe navigation, split across partials:
  - `.cs` - Core crafting UI handling
  - `.Guide.cs` - Guide menu and Goblin Tinkerer reforge
  - `.Recipe.cs` - Recipe types, resolution, and requirement building
- `CursorNarrator`, `SmartCursorNarrator` - Tile/cursor position narration
- `NpcDialogueNarrator` - NPC chat and shop interactions
- `ChatInputNarrator` - Chat text input narration
- `LockOnNarrator` - Lock-on target narration
- `WireColorMenuNarrator` - Wire color selection UI
- `IngameSettingsNarrator`, `ControlsMenuNarrator` - Settings UI
- `WorldInteractableTracker`, `TileToggleAnnouncer` - World interaction tracking
- Audio emitters: `FootstepAudioEmitter`, `ClimbAudioEmitter`, `BiomeAnnouncementEmitter`, `HostileStaticAudioEmitter`, `MultiplayerFootstepAudioEmitter`, `FootstepToneProvider`
- Descriptor services: `CursorDescriptorService`, `TileStateDescriptorService`, `WorldPositionalAudioService`

**Players (`Players/`):**
- `BuildModePlayer` - Keyboard-driven tile placement mode
- `DamageAnnouncementPlayer` - Combat damage narration
- `DeathNarrationPlayer` - Death event narration
- `NpcDialogueInputPlayer` - NPC dialogue input handling
- `GuidancePlayer` - Guidance system per-player state
- `StatusCheckPlayer` - Status check per-player state
- `WireColorMenuPlayer` - Wire color menu per-player state

**Build Mode (`Systems/BuildMode/`):**
- Provides keyboard-based cursor movement for placing/breaking tiles without mouse

**Gamepad Emulation (`Systems/GamepadEmulation/`):**
- Allows keyboard users to trigger gamepad-only UI navigation
- `DpadVirtualizationSystem` - Virtual D-pad from keyboard input
- `VirtualTriggerService`, `VirtualStickService` - Emulated gamepad controls
- `HousingQueryHandler` - Housing query integration
- `EmulationReflectionCache` - Reflection handles for gamepad internals

**ModBrowser (`Systems/ModBrowser/`):**
- `SearchModeInputHook`, `SearchModeManager` - Mod browser search input handling

### Key Patterns

1. **Hook-based architecture**: Uses tModLoader's `On_*` detours to intercept Terraria methods
2. **Narration scheduling**: `NarrationScheduler` coordinates multiple narrators, handles rate limiting per category
3. **Partial classes**: Large systems split across multiple files by concern (GuidanceSystem, InGameNarrationSystem)
4. **Reflection for private access**: Uses `ReflectionCache` (`Utilities/ReflectionCache.cs`) for centralized, lazy-initialized reflection handles to tModLoader internals
5. **State machine pattern**: `MenuNarration/NarrationStateMachine.cs` contains:
   - `ModConfigNarrationStateMachine` - explicit state transitions for config menu narrator
   - `NarrationFrameTimers` - frame-based suppression for hover/input
   - `SliderRepeatState` - hold-to-repeat slider adjustment
6. **Base class extraction**: `ModMenuAccessibilityBase` (`ModMenuAccessibility/`) provides shared navigation infrastructure for mod menu screens
7. **Handler/registry pattern**: `MenuNarration/` uses `IMenuNarrationHandler` with `MenuNarrationHandlerRegistry` for extensible menu state handling
8. **Abstraction/adapter pattern**: `Abstractions/` and `Adapters/` provide testable interfaces (e.g., `ITerrariaLocalization` / `TerrariaLocalizationAdapter`)

### Utilities (`Utilities/`)

- `ReflectionCache` - Centralized lazy reflection handles organized by source type (UIMods, UIModBrowser, UIModConfig, etc.). All reflection access should go through this cache.
- `NarrationStringCatalog` - Shared string constants for narration.
- `SliderNarrationHelper` - Slider value narration utilities.
- `ChatLineParser` - Parses chat messages for narration.
- `CoinFormatter` - Formats coin values as readable text.
- `GlyphTagFormatter` - Converts glyph tags to readable text.
- `SlotContextFormatter` - Formats inventory slot context descriptions.
- `TextSanitizer` - Strips markup/formatting for screen reader output.
- `UiSlotSpatialAudio` - Spatial audio cues for UI slot navigation.
- `ItemSlotContextFacts`, `LocalizationHelper` - Support utilities.

### InGameNarration Helpers (`Systems/InGameNarration/`)

- `SlotNavigationHelper` - Shared utilities for UI slot navigation, link point resolution, and chest/inventory slot mapping
- `NarrationScheduler` - Coordinates multiple narrators with rate limiting per category
- `NarrationTextFormatter` - Item label composition and text formatting
- `NarrationServices` - Service container/locator for narrator dependencies
- `NarrationInstrumentation` - Debug/diagnostic instrumentation for narration events

### Mod Menu Accessibility (`ModMenuAccessibility/`)

- `ModMenuAccessibilityBase` - Abstract base class providing common navigation infrastructure (input handling, UILinkPoint management, announcement patterns)
- `LinkIdRegistry` - Central registry of base link IDs to prevent UILinkPointNavigator collisions

**Inheritance hierarchy:**
```
ModMenuAccessibilityBase (abstract)
├── ManageModsAccessibilitySystem      (LinkId: 3100)
├── DownloadModsAccessibilitySystem    (LinkId: 3200)
├── ModInfoAccessibilitySystem         (LinkId: 3300)
├── ModPacksAccessibilitySystem        (LinkId: 3500)
├── ModSourcesAccessibilitySystem      (LinkId: 3600)
├── AchievementsAccessibilitySystem    (LinkId: 3700)
├── BestiaryAccessibilitySystem        (LinkId: 3800)
└── WorkshopHubAccessibilitySystem     (LinkId: 3000, hardcoded)
```

### Guidance System Types (`Systems/Guidance/`)

- `GuidanceEntry` - Unified struct for all scannable targets (NPC, Player, Interactable, DroppedItem, Critter, Plantlife) with factory methods for each category
- `GuidanceScanner` - Entity scanning extracted from GuidanceSystem.Scan partial
- `GuidanceAudioPlayer` - Audio playback extracted from GuidanceSystem.Audio partial
- `GuidanceStateManager` - State management extracted from GuidanceSystem.State partial
- `GuidanceNetworking` - Multiplayer sync extracted from GuidanceSystem.Networking partial

### Menu Narration (`Systems/MenuNarration/`)

- `MenuNarrationSystem` - Main menu narration coordinator
- `MenuNarrationController` - Controls narration flow via `IMenuNarrationHandler` implementations
- `MenuNarrationHandlerRegistry` - Registry of handlers for different menu states
- `MenuNarrationCatalog` - Partial class with menu state descriptions (`.Core`, `.MainMenus`, `.Multiplayer`, `.Settings`)
- `MenuUiSelectionTracker` - Tracks UI selections, split across partials for `.CharacterCreation` (including `.HairStyles`, `.ClothingStyles`), `.WorldCreation`, `.ModBrowser`
- `MenuFocusResolver` - Resolves current focused menu element
- `ModConfig/` subdirectory - Mod configuration menu narration:
  - `ModConfigNarrationCoordinator` - Orchestrates mod config screen narration
  - `ModConfigListHandler`, `ModConfigEditHandler` - List/edit mode handling
  - `ConfigElementDescriber` - Describes config elements for narration
  - `ConfigSliderHandler` - Slider adjustment narration
  - `AnnouncementGate` - Controls announcement timing/deduplication

### Additional Top-Level Systems

- `WireColorMenuSystem`, `AccessibleWireColorMenu` - Wire color selection accessibility
- `NpcDialogueInputTracker` - Tracks NPC dialogue input state
- `HairStyleNavigationSystem` - Hair style picker navigation
- `PlayerSelectGamepadSystem` - Player selection screen gamepad support
- `ModBrowserGamepadSystem` - Mod browser gamepad navigation
- `ExplorationTargetRegistry` - Registry of explorable targets
- `ChunkMinerSystem` - Chunk-based world data processing
- `AudioVolumeDefaults` - Default volume level management

### Configuration

- `TerrariaAccessConfig.cs` - Client-side mod settings (volumes, toggles)
- `Localization/en-US_Mods.TerrariaAccess.hjson` - All user-facing strings

### Keybinds

Defined in multiple `*Keybinds.cs` files:
- `GuidanceKeybinds` - Waypoint navigation
- `BuildModeKeybinds` - Build mode controls
- `GamepadEmulationKeybinds` - Virtual gamepad
- `SpeechInterruptKeybinds` - Speech control
- `StatusCheckKeybinds` - Status announcements

## Testing

No automated test suite. Manual testing requires:
1. Terraria + tModLoader installed
2. A supported screen reader running (NVDA, JAWS, etc.)
3. `Tolk.dll` in tModLoader directory

Use `-NarrationLint` flag to scan client.log for Tolk communication failures after gameplay sessions.

## Code Intelligence

C# LSP is configured for this project. Prefer using LSP operations over grep/search when working with C# code:
- Use `goToDefinition` to navigate to symbol definitions instead of searching for class/method names
- Use `findReferences` to find all usages of a symbol
- Use `hover` to get type information and documentation
- Use `documentSymbol` to list all symbols in a file

## Decompiled Sources

When you need to reference Terraria or tModLoader internals, decompiled sources are available:

- **tModLoader:** `C:\Program Files (x86)\Steam\steamapps\common\tModLoader\TModLoaderDecompiled\`
- **Terraria:** `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Decompilations\`

Use these to understand Terraria's internal APIs, find hook points, or verify method signatures.

## Mod Metadata

- Version defined in `Mods/TerrariaAccess/build.txt`
- Client-side only (`side = Client`)
