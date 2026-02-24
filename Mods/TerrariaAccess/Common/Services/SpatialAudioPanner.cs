#nullable enable
using Microsoft.Xna.Framework;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Provides spatial audio calculations matching Terraria's native system, with pitch added for vertical positioning.
/// </summary>
/// <remarks>
/// <para>
/// Pan and volume use Terraria's exact constants from <c>ActiveSound.Update()</c>:
/// <list type="bullet">
///   <item>Pan: <c>offsetX / 960f</c> clamped to [-1, 1]</item>
///   <item>Volume: <c>1 - distance / 2500f</c> clamped to [0, 1]</item>
/// </list>
/// </para>
/// <para>
/// Pitch mirrors the pan calculation but for vertical positioning:
/// <list type="bullet">
///   <item>Pitch: <c>-offsetY / 960f</c> clamped to [-0.7, 0.7]</item>
///   <item>Negative Y offset (target above) = positive pitch (higher tone)</item>
///   <item>Positive Y offset (target below) = negative pitch (lower tone)</item>
/// </list>
/// </para>
/// </remarks>
internal static class SpatialAudioPanner
{
    /// <summary>
    /// Terraria's pan scale: 960 pixels (~60 tiles) for full left/right panning.
    /// </summary>
    private const float PanScalePixels = 960f;

    /// <summary>
    /// Pitch scale matches pan scale for consistent spatial perception.
    /// </summary>
    private const float PitchScalePixels = 960f;

    /// <summary>
    /// Terraria's volume falloff distance: 2500 pixels (~156 tiles) for silence.
    /// </summary>
    private const float VolumeFalloffPixels = 2500f;

    /// <summary>
    /// Maximum pitch deviation to avoid extreme/unpleasant shifts.
    /// </summary>
    private const float MaxPitchClamp = 0.7f;

    /// <summary>
    /// Spatial audio sample containing pan, pitch, volume, and distance.
    /// </summary>
    internal readonly record struct SpatialAudioSample(float Pan, float Pitch, float Volume, float DistanceTiles);

    /// <summary>
    /// Computes spatial audio parameters for a sound at the target position relative to the listener.
    /// </summary>
    /// <param name="listener">The listener's world position (typically player center).</param>
    /// <param name="target">The sound source's world position.</param>
    /// <param name="baseVolume">Base volume before distance falloff (typically Main.soundVolume).</param>
    /// <returns>A sample containing pan, pitch, volume, and distance in tiles.</returns>
    public static SpatialAudioSample Compute(Vector2 listener, Vector2 target, float baseVolume = 1f)
    {
        Vector2 offset = target - listener;

        // Pan: horizontal positioning (Terraria's exact formula)
        float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);

        // Pitch: vertical positioning (mirrors pan formula)
        // Negative because Terraria's Y increases downward, but we want "above = higher pitch"
        float pitch = MathHelper.Clamp(-offset.Y / PitchScalePixels, -MaxPitchClamp, MaxPitchClamp);

        // Volume: distance-based falloff (Terraria's exact formula)
        float distance = offset.Length();
        float distanceFalloff = MathHelper.Clamp(1f - distance / VolumeFalloffPixels, 0f, 1f);
        float volume = MathHelper.Clamp(distanceFalloff * baseVolume, 0f, 1f);

        float distanceTiles = distance / 16f;

        return new SpatialAudioSample(pan, pitch, volume, distanceTiles);
    }
}
