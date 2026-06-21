# Terraria Access

A tModLoader mod that makes Terraria playable for blind and low-vision players. The mod provides speech narration for menus and in-game UI via multiple screen readers, plus positional audio cues for spatial awareness.

## Latest Release

Download the latest `TerrariaAccessSetup.exe` from the [Releases page](https://github.com/ChipsAhoiMcCoy/TerrariaAccess/releases) and run the installer.

## Supported Screen Readers

The mod uses the Tolk library for universal screen reader support:
- NVDA
- JAWS
- Window-Eyes
- SuperNova
- System Access
- ZoomText
- SAPI (fallback)

## Feature Coverage

Terraria Access includes narration and audio support for:
- Main menus, character creation, world creation, settings, mod config, Workshop, mod browser, achievements, bestiary, inventory, equipment, crafting, shops, chests, NPC dialogue, signs, chat, and Journey mode menus.
- Keyboard gamepad emulation for controller-only UI flows, including inventory section navigation, smart select, quick use, D-pad cursor movement, shop selling, dialogue focus, and menu activation.
- Guidance tracking for NPCs, players, waypoints, custom trackers, dropped items, critters, hostile mobs, ores, gems, crafting stations, chests, life crystals, statues, fossils, Jungle Spores, Abigail's Flower, Nature's Gift, Enchanted Sword shrines, crystal shards, amber gems, and other world targets.
- Spatial audio for footsteps, enemy proximity, multiplayer footsteps, cursor position, UI slots, wall collision, edge detection, fall proximity, passage detection, overhead traversal cues, and cavity sonar.
- Status and progression announcements for health, mana, defense, biome, time of day, active buffs, info accessories, armor set bonuses, minion slots, death/respawn, world events, invasion progress, moon events, lunar pillars, and boss attack warnings.
- Build Mode for keyboard-driven tile placement, wall placement, terrain shaping, and wiring support.

## Resources

The bundled documentation includes written setup/play guidance and links to community video resources. Ilikeoiseaux has shared a Terraria Access video playlist here:
https://www.youtube.com/playlist?list=PL-YdS0ol4JN5teP2FK5-Y6GYqd9QMzcIp

## Requirements

- Terraria and tModLoader (Steam install)
- A supported screen reader running

## Installation

1. Install Terraria and tModLoader from Steam.
2. Download and run `TerrariaAccessSetup.exe` from the [Releases page](https://github.com/ChipsAhoiMcCoy/TerrariaAccess/releases).
3. The installer will auto-detect your tModLoader installation and place all files in the correct locations.
4. Make sure your screen reader is running before launching tModLoader.

### Manual Installation

If you prefer to install manually instead of using the installer:

1. Install Terraria and tModLoader from Steam.
2. Launch tModLoader at least once, then close it. This creates the necessary folders.
3. Clone this repository or download the source code using your preferred method.
4. Place the following files in your tModLoader Steam directory (e.g., `C:\Program Files (x86)\Steam\steamapps\common\tModLoader`):
   - `Tolk.dll`
   - `nvdaControllerClient64.dll`
   - `SAAPI64.dll`
5. Place the following files in your tModLoader Mods folder (e.g., `Documents\My Games\Terraria\tModLoader\Mods`):
   - `TerrariaAccess.tmod`
   - `enabled.json`
6. Place `input profiles.json` in your tModLoader user folder (e.g., `Documents\My Games\Terraria\tModLoader`).
7. Make sure your screen reader is running before launching tModLoader.

## Building from Source

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1
```

The script builds the mod and copies `TerrariaAccess.tmod` into your local tModLoader Mods folder. Pass `-SkipDeploy` to only produce the `.tmod` artifact.

## Issues & Feedback

Report issues at https://github.com/ChipsAhoiMcCoy/TerrariaAccess/issues
