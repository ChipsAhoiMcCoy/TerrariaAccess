# Terraria Access

A tModLoader mod that makes Terraria playable for blind and low-vision players. The mod provides speech narration for menus and in-game UI via multiple screen readers, plus positional audio cues for spatial awareness.

## Latest Release

Download the [latest ScreenReaderMod.tmod](https://drive.google.com/file/d/1Hm7q4lqIMEQE4_J8KxPZWmIBDCWc_zgr/view) and follow the installation steps below.

Full documentation is included with each release.

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
2. Place `Tolk.dll`, `nvdaControllerClient64.dll`, and `SAAPI64.dll` in `/steamapps/common/tmodloader`.
3. Place `ScreenReaderMod.tmod` in `/documents/my games/terraria/tmodloader/mods`.
4. Place the `enabled.json` in `/documents/my games/terraria/tmodloader/mods`.
5. Place the inputs file in `/documents/my games/terraria/tmodloader`.

## Building from Source

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/build.ps1
```

The script builds the mod and copies `ScreenReaderMod.tmod` into your local tModLoader Mods folder. Pass `-SkipDeploy` to only produce the `.tmod` artifact.

## Issues & Feedback

Report issues at https://github.com/Terraria-Accessibility-Mod/ScreenReaderMod/issues
