#nullable enable
using Terraria;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// Interface for audio emitters that produce sound cues in response to game state.
/// Emitters are updated each frame and can be reset when changing contexts.
/// </summary>
internal interface IAudioEmitter
{
    /// <summary>
    /// Updates the emitter state and emits audio cues as appropriate.
    /// Called each frame when the player is active in-game.
    /// </summary>
    /// <param name="player">The local player to emit audio relative to.</param>
    void Update(Player player);

    /// <summary>
    /// Resets the emitter to its initial state.
    /// Called when entering/exiting worlds, changing contexts, or when the player becomes inactive.
    /// </summary>
    void Reset();
}
