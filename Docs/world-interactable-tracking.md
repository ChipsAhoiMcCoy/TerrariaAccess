# World Interactable Tracking

The in-game narration stack now owns a reusable tracker (`WorldInteractableTracker`) that can discover world objects, schedule guidance pings, and render positional tones that mimic the waypoint panner. Players opt-in by cycling past `None` to the new **Exploration/Gathering** slot in the waypoint selector; switching back to `None` or a specific waypoint silences the tracker. Once enabled, it runs inside `InGameNarrationSystem` every frame (unless the game is paused) and refreshes its scanned candidates roughly every 18 ticks so positional and timing data stay up to date without exhausting the tile array.

## Core building blocks

- **`InteractableCueProfile`** describes the audio personality for an interactable (fundamental frequency, additive partials, gain, audible radius, and cadence range). The static properties at the bottom of `InGameNarrationSystem.WorldInteractableTracker.cs` collect the presets we ship today, and adding a new one is as simple as creating another property that calls the private constructor.
- **`TileInteractableDefinition`** binds a cue profile to one or more tile IDs, including anchor metadata (frame width/height, tile dimensions, optional tile predicates). `TileInteractableSource` walks tiles near the player, identifies valid anchors, and emits `Candidate` entries for the tracker to consider.
- **`WorldInteractableSource`** is the base for different world scanners (tile-based, item-based, etc.). Sources can override `PrepareFrame` to capture transient state before the shared tracker asks them to `Collect` nearby candidates.
- **`WorldInteractableTracker`** keeps the latest candidates, sorts them by player distance when it is time to emit sounds, and manages cadence/panning/pitch so cues feel consistent with the waypoint tone.

## Adding a tile-based interactable

1. Choose or create an `InteractableCueProfile` that matches the sound you want (fundamental frequency, min/max volume, cadence range, audible radius).
2. Add a new `TileInteractableDefinition` to the appropriate tile source inside `WorldInteractableTracker` (the constructor currently registers a `ChestInteractableSource` plus a `StaticTileInteractableSource` for hearts/altars/orbs/larva).
    - Specify the tile IDs, sprite frame width/height, tile footprint, and any optional predicate (e.g., `tilePredicate: static _ => !WorldGen.crimson`).
    - If the definition needs extra filtering, derive a custom `TileInteractableSource` and override `ShouldIncludeAnchor`.
3. Rebuild. The new definition will be discovered on the next scan and will inherit the positional audio behavior automatically.

## Adding an item-based interactable

1. Decide whether the existing `ItemInteractableSource` is enough. It accepts a cue profile plus one or more item IDs and emits candidates for any active item within the configured scan radius.
2. Register a new instance in `WorldInteractableTracker`'s constructor, passing the scan radius, cue profile, and item IDs.

If you need something more elaborate (NPCs, projectiles, etc.), derive a new `WorldInteractableSource`, override `Collect`, and register it through `RegisterSource`. As long as the source supplies a `TrackedInteractableKey`, world position, and cue profile, the shared tracker will handle cadence, audio synthesis, and lifecycle management.

## Audio and cadence tuning

- Volume and cadence scale with distance: closer objects ping more frequently and with higher volume.
- Pitch and pan come from the player's offset relative to the interactable (`PitchScalePixels` and `PanScalePixels` in the profile).
- Tones are synthesized through `SynthesizedSoundFactory.CreateAdditiveTone`, so we do not rely on external assets.

This scaffold lets us grow a catalog of interactables quickly—adding new entries is isolated to data tables (definitions + profiles), while the playback logic, NVDA alignment, and cleanup stay centralized. Use this pattern as the starting point for chest tracking (now continuous even after opening) or any future interactable lists that should surface through positional audio.
