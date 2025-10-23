# tModLoader Setup Notes

1. Download the official release archive: `https://github.com/tModLoader/tModLoader/releases/latest/download/tModLoader.zip`.
2. Extract the archive into `tModLoader_dist/` in this repository; the `start-tModLoader*.bat` launchers live in that folder.
3. Launch modded Terraria locally by running `tModLoader_dist/start-tModLoader.bat` from PowerShell (or use the Steam install if preferred).
4. To iterate on this project, symlink or copy `Mods/ScreenReaderMod/` into `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\ScreenReaderMod` before rebuilding from the in-game Mods menu.
5. Place `nvdaControllerClient64.dll` beside `tModLoader.exe` (or in `ModSources/ScreenReaderMod/Libraries/`) and keep NVDA running to enable spoken narration events.
6. The menu narration hooks reside in `Common/Systems/MenuNarrationSystem.cs`; watch the logs for `[Narration]` entries to confirm events are firing.
