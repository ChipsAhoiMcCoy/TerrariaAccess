# Accessibility Notes

- Enable the Screen Reader mod from the Mods menu and restart tModLoader to load runtime hooks.
- Toggle narration verbosity under `Settings > Mod Configuration > Screen Reader` once implemented.
- When reporting menu narration gaps, note the menu mode (Main, Multiplayer, Mod Browser) and the focused option text shown in the top-left tooltip.
- For speech output, ensure NVDA is running and copy `nvdaControllerClient64.dll` next to `tModLoader.exe` (or into `Mods/ScreenReaderMod/Libraries/`) so the mod can attach to the NVDA controller API.
- Inventory hovers now call out the slot (hotbar, coins, ammo, armor, chests, banks, shops), announce empty slots, surface the Settings/Save & Exit buttons, and still read the item on the cursor as soon as you pick it up.
- In-game options menu mapping (slider names, category toggles) is partially wired: sliders and hair selector speak, but toggle rows still rely on the debug fallback. Future passes can use `[IngameOptionsNarration]` log entries to bind labels for remaining controls.
- The narration scheduler now drives the active in-game narrators; set `SCREENREADERMOD_SCHEDULER_TRACE_ONLY=1` to disable speech and log call order only. With `SCREENREADERMOD_TRACE=1`, expect per-tick breadcrumbs in this order: Hotbar -> Inventory -> CraftingGuideReforge -> CursorAndSmartCursor -> NpcDialogue -> SettingsAndControls (paused only) -> LockOn -> WorldEvents -> WorldAudio (biome/footstep/hostile static/treasure beacon) -> InteractableTracker (skips when paused). Scheduler timings log as `[NarrationScheduler][Timing]` when trace is enabled.
- Inventory narration history can be tuned via env vars: `SRM_NARRATION_HISTORY_DISABLED=1` disables deduping, while `SRM_NARRATION_HISTORY_MAX_AGE=<frames>` lets repeats through after the specified frame window. Debug logs now include allowed/active UI areas and focus context when narration is suppressed.
