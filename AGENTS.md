# Repository Guidelines

These instructions help contributors keep the screen-reader-first Terraria mod aligned with tModLoader expectations and accessible testing flows.

## Project Structure & Module Organization

- Core source lives under `Mods/ScreenReaderMod/`; C# logic belongs in `Common/`, with systems/hooks in `Common/Systems` and shared services in `Common/Services`.
- Future assets land in `Content/` or `Assets/`; documentation and accessibility notes stay in `Docs/`; tooling experiments belong in `Tools/`.
- Decompiled references reside in `Tools/Decompiled/` plus the Steam installs at `/steam/steamapps/common/terraria` and `/steam/steamapps/common/tmodloader` for cross-checking menu arrays.
- Keep `Content/`, vanilla binaries, installers, and `tModLoader_dist/` out of Git per the root `.gitignore`.

## Build, Test, and Development Commands

```powershell
powershell -ExecutionPolicy Bypass -File Tools/build.ps1
```
Always build through the `Tools/` script; it wraps the tModLoader host and places outputs where the game expects them. Manual testing requires NVDA running with `nvdaControllerClient64.dll` beside `tModLoader.exe` (or `Libraries/` under the mod). Watch `tModLoader-Logs/client.log` for `[Narration]`, `[MenuNarration]`, and `[NVDA]` breadcrumbs when validating speech.

## Coding Style & Naming Conventions

- Target C# 10 with four-space indentation and namespace prefix `ScreenReaderMod.*`.
- JSON/NBT payload keys stay lowercase-with-hyphen. Batch or PowerShell helpers remain CRLF-terminated with uppercase verbs (`REM`, `SET`, `Write-Host`).
- Extend menu narration via `MenuNarrationCatalog.ModeResolvers`, using reflection-safe lookups (`Lang.menu[...]` plus live state) instead of hard-coded strings.

## Testing Guidelines

- No automated suite yet; log NVDA walkthroughs and expected narration in `Docs/accessibility-notes.md`.
- Capture focus indices, spoken text, and reproduction steps whenever menu narration drifts so nightly testers can replay scenarios quickly.

## Commit & Pull Request Guidelines

- Use concise, imperative commit titles and include NVDA observations or repro notes in the body when relevant.
- Reference related branches (e.g., `feature/singleplayer-menu-gamepad`) and attach snippets from `tModLoader-Logs/` when they inform reviewers.
- Pull requests should outline menu modes touched, linked issues, controller/keyboard coverage, and any remaining narration gaps.

## Accessibility & Diagnostics Tips

- Keep NVDA active during playtests; the in-game chat fallback is secondary and should be noted if used.
- When narration desynchronizes, validate against the decompiled helpers in `Tools/Decompiled/` or the Steam directories noted above, then document findings before merging.
