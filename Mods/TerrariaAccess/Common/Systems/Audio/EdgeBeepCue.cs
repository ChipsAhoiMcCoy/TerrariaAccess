#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaAccess.Common.Services;
using static TerrariaAccess.Common.Systems.InGameNarrationSystem;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Shared high-pitched beep used for drop-off and landing warnings.
/// </summary>
internal static class EdgeBeepCue
{
    public const float Volume = 0.35f;
    public const float Frequency = 3000f;
    public const int IntervalFrames = 6;

    public static bool TickCadence(ref int timer)
    {
        timer++;
        if (timer < IntervalFrames)
        {
            return false;
        }

        timer = 0;
        return true;
    }

    public static void Play(Player player, Vector2 worldPosition, float localVolumeScale)
    {
        SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
            player.Center,
            worldPosition,
            Volume);

        FootstepToneProvider.PlaySpatial(sample, Frequency, localVolumeScale, useTriangleWave: false);
    }
}
