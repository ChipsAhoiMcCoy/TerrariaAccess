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
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1
```

The script builds the mod and copies `TerrariaAccess.tmod` into your local tModLoader Mods folder. Pass `-SkipDeploy` to only produce the `.tmod` artifact.

## Issues & Feedback

Report issues at https://github.com/ChipsAhoiMcCoy/TerrariaAccess/issues
