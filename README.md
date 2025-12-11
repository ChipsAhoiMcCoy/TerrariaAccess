# Terraria Access

A screen-reader-first tModLoader mod that makes Terraria playable for blind and low-vision players. The mod narrates menus, in-game UI, navigation cues, and world events while layering positional audio to keep you oriented without visuals.

## Latest release
- Grab the [latest ScreenReaderMod.tmod](https://drive.google.com/file/d/1Hm7q4lqIMEQE4_J8KxPZWmIBDCWc_zgr/view) before following the install steps below.

## Requirements
- tModLoader (Steam install or manual distribution)
- NVDA with `nvdaControllerClient64.dll` placed next to `tModLoader.exe` (or in `Mods/ScreenReaderMod/Libraries/`)

## Install & Play
1. Install Terraria and tModLoader.
2. Place `nvdaControllerClient64.dll` in `/steamapps/common/tmodloader`.
3. Place `ScreenReaderMod.tmod` in `/documents/my games/terraria/tmodloader/mods`.
4. Place the `enabled.json` in `/documents/my games/terraria/tmodloader/mods`.
5. Place the inputs file in `/documents/my games/terraria/tmodloader`.

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
| Guidance Category Next/Previous | Right bracket / Left bracket | Cycle between None, Exploration, Interactable, NPC, Player, and Waypoint tracking modes. |
| Guidance Entry Next/Previous | Page Down / Page Up | Cycle entries within the active guidance category. |
| Create / Delete Waypoint | Backslash / Delete | Create a waypoint at your position; delete the selected waypoint. |
| Guidance Teleport | P | Teleport to the active guidance target when a safe landing spot exists. |
| Build Mode Toggle | Start (gamepad) | Toggles build mode by default on controllers; rebind under Settings > Controls if you want a keyboard shortcut. |
| Build Mode Place Corner | A (gamepad) | Marks selection corners; also works with Quick Mount or mouse left while build mode is active. |

To change any defaults, open the in-world menu, go to Settings > Controls, and select the input tab at the top (Gamepad, Keyboard, etc.) before binding. The **Screen Reader** section lists these actions for rebinding.

## Building from source
Run the repo-root command (from WSL or PowerShell):

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1
```

The script builds the mod and copies `ScreenReaderMod.tmod` into your local tModLoader Mods folder. Pass `-SkipDeploy` if you only want the `.tmod` artifact. Build logs include `[Narration]` and `[NVDA]` lines for debugging.

## Troubleshooting & docs
- Check `tModLoader-Logs/client.log` for `[Narration]`, `[WorldNarration]`, `[MenuNarration]`, and `[NVDA]` entries when validating speech output.
- Additional docs live in `Docs/`, including `Docs/accessibility-notes.md`, `Docs/features.md`, `Docs/world-interactable-tracking.md`, and `Docs/tmodloader-setup.md`.
