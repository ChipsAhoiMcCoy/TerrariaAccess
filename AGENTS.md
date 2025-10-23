# Repository Guidelines

## Project Structure & Module Organization
This repo exists to deliver a screen-reader-first Terraria experience via tModLoader. Source lives under `Mods/ScreenReaderMod/` (C# in `Common/`, future assets in `Content/` or `Assets/`). Tooling experiments belong in `Tools/`. The root `.gitignore` keeps the vanilla binaries, installers, `Content/`, and extracted `tModLoader_dist/` out of Git—leave that list intact when upgrading the base game. Store documentation in `Docs/` and keep any sample configs or logs lightweight.

## Coding Style & Naming Conventions
Target C# 10 with four-space indentation and the `ScreenReaderMod.*` namespace hierarchy. Shared services live in `Common/Services`, systems and hooks in `Common/Systems`. JSON/NBT payloads should keep the project's lowercase-with-hyphen keys. Batch or PowerShell helpers must stay CRLF-terminated, with command verbs (`REM`, `SET`, `Write-Host`) uppercased for consistency.

## Speech Integration & Accessibility Targets
`ScreenReaderService` now routes narration through NVDA when `nvdaControllerClient64.dll` is placed beside `tModLoader.exe` (or inside `Libraries/` under the mod source). Always keep NVDA running during manual tests; log output falls back to on-screen chat so players without NVDA still see status. Current focus is main menu narration-map every `menuMode` you touch and document gaps in `Docs/accessibility-notes.md`.

## Diagnostics & Troubleshooting
Enable verbose logging by watching `tModLoader-Logs/client.log`. Key breadcrumbs: `[Narration]` (final speech queue), `[MenuNarration]` (focus tracking source), `[NVDA]` (controller status). When something misaligns, capture the focus index plus the expected label; add a targeted lookup rather than relying on `Lang.menu`. Leave repro steps in the nightly commit body so testers can validate quickly.
- Steam installs drop the log at `C:\Program Files (x86)\Steam\steamapps\common\tModLoader\tModLoader-Logs\client.log`; surface new findings there when sharing logs.
- That `tModLoader-Logs` directory also captures auxiliary files (crash, networking, etc.); keep an eye on the set when you need deeper traces beyond `client.log`.
- Single-player menu work in progress: `feature/singleplayer-menu-gamepad`. We restored vanilla gamepad navigation, hooked menu narration into UI reflection, and now annotate button actions by index. Player rows: Play/Favorite/Move to Cloud/Rename/Delete. World rows: Play/Favorite/Move to Cloud/Copy Seed/Rename/Delete, with seeds read aloud and favorite/cloud states announced.
- `Tools/Decompiled/` stores `ilspycmd` dumps of `Main` and UI helper types—handy for cross-checking menuMode arrays when narration falls out of sync.
- Main menu narration resolves via `MenuNarrationCatalog.ModeResolvers`; add new menuMode keys there with reflection-safe lookups (prefer `Lang.menu[...]` + live state) so the focus system stays accurate. 
- When compiling from WSL, use the bundled runtime directly: `./dotnet_wsl/dotnet tModLoader.dll -build "../Mods/ScreenReaderMod"`. Forward slashes avoid `CS8203` path issues while still packaging the mod. 
