#nullable enable
using Terraria;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Base class for audio emitters providing common validation and lifecycle patterns.
/// </summary>
internal abstract class AudioEmitterBase : IAudioEmitter
{
    /// <summary>
    /// Validates whether audio emission should be allowed for the given player.
    /// </summary>
    protected static bool CanEmitAudio(Player player)
    {
        if (player is null || !player.active || player.dead || player.ghost)
        {
            return false;
        }

        if (Main.dedServ || Main.gameMenu || Main.soundVolume <= 0f)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates whether the player can process movement-based audio (footsteps, climbing).
    /// Excludes mounted players and players on pulleys by default.
    /// </summary>
    protected static bool CanProcessMovementAudio(Player player)
    {
        if (!CanEmitAudio(player))
        {
            return false;
        }

        if (player.mount.Active || player.pulley)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Updates the emitter. Override in derived classes.
    /// </summary>
    public abstract void Update(Player player);

    /// <summary>
    /// Resets the emitter state. Override in derived classes.
    /// </summary>
    public abstract void Reset();
}
