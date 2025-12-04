# Terraria Access

Terraria Access is a tModLoader mod focused on making Terraria playable for blind and low‑vision players. The project adds screen reader integrations and narration systems so that the game’s menus, inventory, and in‑world interactions can be understood through speech output instead of visuals.

## Key Goals

- Provide end-to-end narration coverage for main menus, world and player selection, and in-game UI.
- Bridge Terraria events to NVDA via `nvdaControllerClient64.dll`, with graceful fallbacks to on-screen chat.
- Describe traditionally visual-only information—inventory contents, hotbar state, map cues—through concise audio messages.
- Maintain screen-reader-first workflows while preserving vanilla controls for all players.

## Getting Started

1. Install tModLoader (Steam or manual distribution) and clone this repository.
2. Place `nvdaControllerClient64.dll` next to `tModLoader.exe` (or inside `Mods/ScreenReaderMod/Libraries/`) and launch NVDA.
3. Run `Tools/build-and-copy.ps1` from PowerShell to build and copy the mod into your Terraria Mods directory.
4. Enable **Terraria Access** from the in-game Mods menu and restart tModLoader to activate the narration hooks.

## Contributing

Contributions are welcome—especially around expanding menu coverage, adding new narration domains, and improving accessibility docs. Please open an issue describing the scenario you are addressing, then submit a pull request targeting `main`.

For build instructions, repo guidelines, and current accessibility notes, see the files under `AGENTS.md` and `Docs/`.
