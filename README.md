# Terraria Access

A screen-reader-first tModLoader mod that makes Terraria playable for blind and low-vision players. The mod narrates menus, in-game UI, navigation cues, and world events while layering positional audio to keep you oriented without visuals.

## Latest release
- Download: https://drive.google.com/file/d/1Hm7q4lqIMEQE4_J8KxPZWmIBDCWc_zgr/view

## Requirements
- tModLoader (Steam install or manual distribution)
- NVDA with `nvdaControllerClient64.dll` placed next to `tModLoader.exe` (or in `Mods/ScreenReaderMod/Libraries/`)

## Install & Play
1. Install tModLoader and ensure NVDA is running with `nvdaControllerClient64.dll` beside `tModLoader.exe` (or in `Mods/ScreenReaderMod/Libraries/`).
2. Download the latest `.tmod` release: https://drive.google.com/file/d/1Hm7q4lqIMEQE4_J8KxPZWmIBDCWc_zgr/view
3. Drop `ScreenReaderMod.tmod` into `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\Mods`. Alternatively, run `pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1` to build and deploy it; the script resolves your tModLoader install and copies the artifact unless `-SkipDeploy` is used.
4. Launch tModLoader, enable **Terraria Access** from the Mods menu, and restart tModLoader to activate hooks.
5. Bind the mod keybinds under `Settings > Controls > Keybindings > Screen Reader` (defaults listed below).

## Feature highlights
- **Menu narration:** Title, player/world creation & deletion, settings (audio/video/interface/gameplay/cursor/effects/resolution), multiplayer/host & play, join by IP, tModLoader settings, mod browser/workshop, and mod configuration screens.
- **In-game narration:** Inventory/storage/shop slots (including prices and sell values), crafting/guide/reforge tooltips, hotbar selection, smart cursor targets, NPC dialogue, in-game settings, controls, and lock-on targets.
- **Navigation & awareness:** Named waypoints with positional tones, exploration/gathering tracker for nearby interactables, biome announcements, hostile NPC “static” cues, treasure bag beacons, and footstep tones.
- **Build Mode:** Mark a rectangle, then clear or place tiles/walls across the selection using the held tool, with range extension to the current viewport and completion summaries.
- **Speech pipeline:** NVDA-driven speech with repeat suppression and a speech-interrupt toggle; world announcements use a SAPI fallback while respecting mute/interrupt state.

See `Docs/features.md` for deeper coverage of each system.

## Keybinds (defaults)
| Action | Default | Notes |
| --- | --- | --- |
| Speech Interrupt | `F2` | Cancel current speech and toggle interrupt on/off. |
| Guidance Category Next/Previous | `]` / `[` | Cycle between None, Exploration, Interactable, NPC, Player, and Waypoint tracking modes. |
| Guidance Entry Next/Previous | `PageDown` / `PageUp` | Cycle entries within the active guidance category. |
| Create / Delete Waypoint | `\` / `Delete` | Create a waypoint at your position; delete the selected waypoint. |
| Guidance Teleport | `P` | Teleport to the active guidance target when a safe landing spot exists. |
| Build Mode Toggle / Place Corner | Unbound | Set your own bindings; when active, use the place key (or Quick Mount/mouse left) to mark corners. |

Configure bindings under `Settings > Controls > Keybindings > Screen Reader` to match your controller or keyboard preferences.

## Building from source
Run the repo-root command (from WSL or PowerShell):

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1
```

The script builds the mod and copies `ScreenReaderMod.tmod` into your local tModLoader Mods folder. Pass `-SkipDeploy` if you only want the `.tmod` artifact. Build logs include `[Narration]` and `[NVDA]` lines for debugging.

## Troubleshooting & docs
- Check `tModLoader-Logs/client.log` for `[Narration]`, `[WorldNarration]`, `[MenuNarration]`, and `[NVDA]` entries when validating speech output.
- Additional docs live in `Docs/`, including `Docs/accessibility-notes.md`, `Docs/features.md`, `Docs/world-interactable-tracking.md`, and `Docs/tmodloader-setup.md`.
