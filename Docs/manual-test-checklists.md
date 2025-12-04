# Manual Test Checklists

Use these checklists during NVDA playtests to validate narration after refactors. Keep `tModLoader-Logs/client.log` open and watch for `[Narration]`, `[WorldNarration]`, `[MenuNarration]`, and `[NVDA]` lines. Enable extra traces as needed:
- `SCREENREADERMOD_TRACE=1` to capture diagnostics snapshots.
- `SCREENREADERMOD_SPEECH_LOG_ONLY=1` to mute speech and log announcements.
- `SCREENREADERMOD_SCHEDULER_TRACE_ONLY=1` to verify in-game narrator ordering without speaking.
- `SRM_DEBUG_NARRATION=1` for inventory focus history.

## Menu narration
- Title screen: focus Single Player/Multiplayer/Achievements; ensure focus follows keyboard/gamepad and no missing labels (check client.log for `[MenuNarration] menuItems reflection returned empty`).
- Player/world selection and delete prompts: confirm table-driven labels speak; hover and focus alternate without repeats.
- Settings submenus (audio/video/interface/gameplay/cursor/effects/resolution): adjust sliders and toggles; confirm slider phrasing uses sanitized labels and percent updates.
- Mod browser and workshop: hover list entries and filters; ensure catalog names/authors sanitize correctly.
- Mod config menus: verify they share hover dedupe with menu narration and emit `[MenuNarration]` events.

## Inventory and storage
- Move focus across hotbar, inventory, armor/accessories, coins/ammo, trash. Expect slot context + item names, prefixes, stacks, and favorited state; check `SRM_DEBUG_NARRATION` output when enabled.
- Open chest/bank/safe/void vault/shop: hover slots and empty slots; verify location labels stay accurate and price narration uses CoinFormatter.
- Tooltip hover: ensure repeated hovers respect narration history (`SRM_NARRATION_HISTORY_*` env vars).
- Hotbar cycling while crafting or opening storage should keep correct area naming and suppress duplicates when unchanged.

## Crafting, guide, and reforge
- Crafting grid hover (mouse and gamepad): recipe names, ingredient counts, and availability should speak; stale mouse text should not bleed through.
- Guide/reforge slots: placing/removing items narrates both action and resulting tooltip; verify reforge cost matches CoinFormatter output.
- Smart cursor/cursor: hover tiles, walls, and entities; cursor descriptor should suppress grass/dirt variants and announce objects accurately.

## NPC dialogue and input
- Dialogue option navigation (keyboard/gamepad): options speak with indices; typed responses echo via input tracker, not mouse text timing.
- Multiplayer safety: confirm no duplicate narration when other players open dialogue near you.

## Settings in-world and controls
- Open in-game settings: sliders/toggles share phrasing with menu narrator; closing should restore previous narration state.
- Control bindings: rebind a key and confirm announcement covers action name and new binding; ensure locks respect pause/menu gating.

## World events, biome, lock-on, and positional audio
- Trigger biome change: one-time biome announcement per entry; no spam while stationary.
- Spawn hostile NPCs or bosses: hostile static pings prioritize closer targets; lock-on announces target and updates as it changes.
- Drop treasure bags: beacon tone plays with cadence tied to distance; pickup narration should not fire repeatedly.
- Footsteps: tones play while moving; stop when idle or muted by menu/pause.

## Guidance and exploration
- Waypoints: create, cycle, delete; arrival detection at ~4 tiles should announce once. Teleport to active target and watch logs for `[GuidanceTeleport]` diagnostics when failing near walls/edges.
- Exploration mode: cycle tracked interactables (chest/heart/altar/orb/larva/star) and verify positional pings align with nearest target; muting when menu/pause should silence cues.
- Guidance category cycling: Page Up/Page Down (or bindings) should switch categories and narrate selection plus current entry.

## Build Mode
- Toggle build mode on/off; ensure quick-mount is suppressed while active.
- Mark first and second corners; cursor offset narration should describe rectangle bounds.
- Hold use to clear/place tiles and walls; completion summary reports tiles/walls affected and uses auto-tool swap when applicable.
- Range expansion: zoom in/out and confirm placement range follows viewport and restores after exiting build mode.

## Speech pipeline and NVDA
- Ensure NVDA DLL is present; check client.log for `[NVDA] Connected` and no failures. Toggle speech mute/interrupt and verify state changes announce correctly.
- Set `SCREENREADERMOD_SPEECH_LOG_ONLY=1` and play for a few minutes; confirm speech controller logs suppression and category windows without audio output.
