# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Terraria Access is a tModLoader mod that makes Terraria playable for blind and low-vision players. It provides NVDA-driven speech narration for menus and in-game UI, plus positional audio cues for spatial awareness.

**Key technologies:** C# (.NET 8.0), tModLoader, NVDA screen reader integration via nvdaControllerClient64.dll

## Build Commands

```powershell
# Build and deploy to local tModLoader Mods folder
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1

# Build only (no deployment)
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -SkipDeploy

# Build with narration lint (checks client.log for NVDA failures)
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1 -NarrationLint
```

The build script invokes tModLoader's build system (`dotnet tModLoader.dll -build`), not MSBuild directly. Output is `ScreenReaderMod.tmod`.

## Architecture

### Core Systems (Mods/ScreenReaderMod/Common/)

**Entry Point:** `ScreenReaderMod.cs` - Initializes services and keybinds on mod load.

**Services Layer (`Services/`):**
- `ScreenReaderService` - Central speech API. Manages announcement categories (Default, Tile, Wall, Pickup, World) with per-category rate limiting. Routes to `SpeechController` -> `NvdaSpeechProvider`.
- `SpatialAudioPanner` - Calculates stereo panning/pitch based on world position relative to player.
- `WorldAnnouncementService` - Handles world event announcements (blood moon, invasions, biome changes).

**Systems Layer (`Systems/`):**
- `InGameNarrationSystem` - Partial class coordinating all in-game narrators via `NarrationScheduler`. Hooks into Terraria's ItemSlot, Main.NewText, PopupText, IngameOptions, etc.
- `MenuNarrationSystem` - Hooks into `Main.DrawMenu` to narrate main menu UI states.
- `GuidanceSystem` - Waypoint/target tracking with audio pings. Partial class split across:
  - `.cs` - Core logic, category cycling, waypoint management
  - `.Audio.cs` - Ping emission, tone generation
  - `.Scan.cs` - NPC/Player/Interactable/DroppedItem/Critter/Plantlife scanning
  - `.State.cs` - Selection mode state, waypoint storage
  - `.Networking.cs` - Multiplayer sync

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
- `IngameSettingsNarrator`, `ControlsMenuNarrator`, `ModConfigMenuNarrator` - Settings UI
- Various audio emitters: `FootstepAudioEmitter`, `BiomeAnnouncementEmitter`, `HostileStaticAudioEmitter`, `TreasureBagBeaconEmitter`

**Players (`Players/`):**
- `BuildModePlayer` - Keyboard-driven tile placement mode
- `DamageAnnouncementPlayer` - Combat damage narration

**Build Mode (`Systems/BuildMode/`):**
- Provides keyboard-based cursor movement for placing/breaking tiles without mouse

**Gamepad Emulation (`Systems/GamepadEmulation/`):**
- Allows keyboard users to trigger gamepad-only UI navigation

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

### Utilities (`Utilities/`)

- `ReflectionCache` - Centralized lazy reflection handles organized by source type (UIMods, UIModBrowser, UIModConfig, etc.). All reflection access should go through this cache.

### InGameNarration Helpers (`Systems/InGameNarration/`)

- `SlotNavigationHelper` - Shared utilities for UI slot navigation, link point resolution, and chest/inventory slot mapping
- `NarrationScheduler` - Coordinates multiple narrators with rate limiting per category
- `NarrationTextFormatter` - Item label composition and text formatting

### Mod Menu Accessibility (`ModMenuAccessibility/`)

- `ModMenuAccessibilityBase` - Abstract base class providing common navigation infrastructure (input handling, UILinkPoint management, announcement patterns)
- `LinkIdRegistry` - Central registry of base link IDs to prevent UILinkPointNavigator collisions

**Inheritance hierarchy:**
```
ModMenuAccessibilityBase (abstract)
├── ManageModsAccessibilitySystem  (LinkId: 3100)
├── DownloadModsAccessibilitySystem (LinkId: 3200)
└── ModInfoAccessibilitySystem      (LinkId: 3300)
```

### Guidance System Types (`Guidance/`)

- `GuidanceEntry` - Unified struct for all scannable targets (NPC, Player, Interactable, DroppedItem, Critter, Plantlife) with factory methods for each category

### Configuration

- `ScreenReaderModConfig.cs` - Client-side mod settings (volumes, toggles)
- `Localization/en-US_Mods.ScreenReaderMod.hjson` - All user-facing strings

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
2. NVDA screen reader running
3. `nvdaControllerClient64.dll` in tModLoader directory

Use `-NarrationLint` flag to scan client.log for NVDA communication failures after gameplay sessions.

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

- Version defined in `Mods/ScreenReaderMod/build.txt`
- Client-side only (`side = Client`)
