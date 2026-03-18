## Terraria Access v0.1.9 Fixes

This is a patch follow-up release for `v0.1.9`, focused on fixes and cleanup.

### Highlights

- Improved chat handling and narration when chat opens and closes.
- Merged PR #14, which fixes smart cursor, lock-on, and mod config narration issues.
- Fixed several gameplay accessibility regressions reported after `v0.1.9`.
- Fixed the repeated gamepad emulation exception that could fire every update tick.
- Fixed the wall collision sound not triggering while mounted.
- Improved guidance naming input handling and guidance sweep behavior.
- Added the Terraria Access gamepad bindings to the bundled `input profiles.json`.
- Cleaned up dead code and reflection hot paths to reduce maintenance risk and stabilize behavior.

### Notes

- This release keeps the installer-based distribution introduced in `v0.1.9`.
- The recommended download remains `TerrariaAccessSetup.exe`.

### Downloads

- `TerrariaAccessSetup.exe` — full installer
- `Terraria.Access.Documentation.html` — documentation and keybind reference
