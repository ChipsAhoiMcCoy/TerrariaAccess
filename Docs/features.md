# Feature Overview

This mod wires Terraria into a speech-first experience for blind and low-vision players. It layers narration over menus, the in-game UI, navigation helpers, and synthesized audio cues so you can keep situational awareness without visuals.

## Speech pipeline
- Uses NVDA through `nvdaControllerClient64.dll`; place the DLL beside `tModLoader.exe` or in `Mods/ScreenReaderMod/Libraries/`.
- Falls back to Windows SAPI for world announcements while still respecting the mute/interrupt toggles.
- Speech repeats are rate-limited and can be interrupted with the **Speech Interrupt** keybind (default `F2`).

## Menu narration
- Narrates the title screen, player and world creation/deletion flows, language picker, credits, and all settings submenus (audio, video, interface, gameplay, cursor, effects, resolution).
- Covers multiplayer entry, host & play settings, join by IP, tModLoader settings, mod browser, workshop publishing/downloading, and mod configuration menus.
- Follows keyboard and gamepad focus, announcing both the active menu mode and the focused option text.

## In-game narration
- **Inventory, storage, and shops:** Announces slot location (hotbar, inventory, coins, ammo, armor, dyes, accessories, misc equipment, trash, chest/bank/safe/void vault, shops) plus item names, prefixes, stack counts, price to buy, and sell price when applicable. Empty slots and tooltips are read, and crafting requirements are surfaced when hovering recipes.
- **Crafting and guide/reforge menus:** Captures recipe focus (mouse or gamepad grid), reads ingredient needs, and echoes guide/reforge slots.
- **Hotbar and cursor:** Hotbar selection changes are announced; smart cursor targets and cursor narration describe the tile/wall or entity under focus so you know what you will interact with.
- **NPC dialogue:** Buttons and dialogue options are narrated; typed dialogue responses are echoed through the input tracker.
- **In-game settings and controls:** Sliders, toggles, and control bindings speak their labels while you adjust them. Mod configuration menus are covered as well.
- **Lock-on, treasure, and combat awareness:** Lock-on targets are called out, treasure bags emit a beacon tone when dropped, and hostile NPCs generate positional “static” pings that prioritize bosses and closer threats.
- **Footsteps and biome changes:** Footstep tones add spatial cues while moving; biome transitions announce once the player has stabilized in the new area.

## Navigation and guidance
- **Waypoints:** Create, cycle, and delete named waypoints; each emits a positional ping that scales pitch, pan, and cadence with distance. Teleport to the active waypoint/target with the guidance teleport keybind when a safe landing spot is found.
- **Exploration/Gathering mode:** Cycle the guidance category to **Exploration** to follow the nearest tracked interactable (chests, hearts, altars, orbs, larva, etc.) discovered by the world interactable tracker.
- **NPC/Player/Interactable tracking:** Guidance categories include nearby town NPCs, other players (in multiplayer), and tracked interactables so you can lock onto what matters without visuals.
- **World interactable tracker:** Emits positional tones for key world objects using synthesized audio (no external assets). See `Docs/world-interactable-tracking.md` for tuning details and extension points.

## Build Mode
- Toggle build mode, mark two corners, then hold your use button to clear or place tiles/walls across the rectangle under the cursor. The held tool determines whether you clear (pick/axe/hammer) or place tiles/walls; consumables are used automatically.
- Build mode expands placement range to the current viewport, prevents accidental quick-mount while active, and auto-swaps to the best axe when clearing trees.
- Completion announcements summarize how many tiles or walls were placed/removed, and directional prompts describe the cursor offset while selecting an area.

## Logging and diagnostics
- Speech and narration breadcrumbs are written to `tModLoader-Logs/client.log` with `[Narration]`, `[WorldNarration]`, `[MenuNarration]`, and `[NVDA]` tags.
- Set `SRM_DEBUG_NARRATION=1` before launching tModLoader to dump extra inventory narration debug lines when investigating focus issues.
