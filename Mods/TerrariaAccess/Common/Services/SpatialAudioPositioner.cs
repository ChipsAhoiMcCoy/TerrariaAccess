#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Provides 2D spatial audio calculations for accessibility cues.
/// </summary>
/// <remarks>
/// <para>
/// Horizontal position is normalized against the visible viewport:
/// <list type="bullet">
///   <item>Left edge of the visible screen = <c>-1</c></item>
///   <item>Center of the visible screen = <c>0</c></item>
///   <item>Right edge of the visible screen = <c>1</c></item>
/// </list>
/// </para>
/// <para>
/// The normalized horizontal value feeds custom interaural time delay synthesis through
/// <see cref="SpatializedSoundEngine"/>; callers should not reinterpret it as a legacy XNA
/// <c>SoundEffectInstance.Pan</c> value.
/// </para>
/// </remarks>
[Obsolete("Use SpatializedSoundEngine for spatial audio calculations so production callers stay behind the custom 2D ITD facade.")]
internal static class SpatialAudioPositioner
{
    /// <summary>
    /// Fallback horizontal scale used only when the viewport dimensions are unavailable.
    /// </summary>
    private const float FallbackHorizontalScalePixels = 960f;

    /// <summary>
    /// Fallback vertical scale used only when the viewport dimensions are unavailable.
    /// </summary>
    private const float FallbackPitchScalePixels = 960f;

    private const float FullVolumeRadiusPixels = 96f;

    /// <summary>
    /// Maximum distance at which accessibility world cues fade to silence.
    /// </summary>
    private const float VolumeFalloffPixels = 2500f;

    /// <summary>
    /// Maximum pitch deviation to avoid extreme/unpleasant shifts.
    /// </summary>
    private const float MaxPitchClamp = 0.65f;

    /// <summary>
    /// Maximum ITD at full left/right. 0.8ms is intentionally a little stronger than the
    /// average human maximum so game cues remain legible over Terraria's dense audio mix.
    /// </summary>
    private const float MaxInterauralDelaySeconds = 0.0008f;

    /// <summary>
    /// Minimum far-ear gain at full left/right. This adds a controlled level cue without
    /// fully muting the delayed ear, preserving the ITD effect for headphones.
    /// </summary>
    private const float MinDelayedEarGainAtEdge = 0.3f;

    /// <summary>
    /// Keeps center and near-center cues mostly timing-based while strengthening hard edges.
    /// </summary>
    private const float DelayedEarGainCurvePower = 1.5f;

    /// <summary>
    /// Default cache resolution for synthesized ITD buffers. 256 steps per side keeps
    /// generated cue positions close to the requested visible-screen X while still allowing reuse.
    /// </summary>
    private const int DefaultNormalizedScreenXStepsPerSide = 256;

    /// <summary>
    /// Spatial audio sample containing normalized horizontal position, pitch, volume, and distance.
    /// </summary>
    internal readonly record struct SpatialAudioSample(float NormalizedScreenX, float Pitch, float Volume, float DistanceTiles);

    internal readonly record struct InterauralParameters(
        int LeftDelaySamples,
        int RightDelaySamples,
        float LeftDelayFraction,
        float RightDelayFraction,
        float LeftGain,
        float RightGain);

    /// <summary>
    /// Computes spatial audio parameters for a sound at the target position relative to the listener.
    /// </summary>
    /// <param name="listener">The listener's world position (typically player center).</param>
    /// <param name="target">The sound source's world position.</param>
    /// <param name="baseVolume">Cue-local base volume before distance falloff. Global Terraria volume is applied by the playback boundary.</param>
    /// <returns>A sample containing normalized visible-screen X, pitch, volume, and distance in tiles.</returns>
    [Obsolete("Use SpatializedSoundEngine.Compute so callers stay behind the custom spatial audio facade.")]
    public static SpatialAudioSample Compute(Vector2 listener, Vector2 target, float baseVolume = 1f)
    {
        Vector2 offset = target - listener;

        float normalizedScreenX = ComputeWorldNormalizedScreenX(target.X, listener.X);

        // Terraria's Y increases downward, so targets above the center produce a higher pitch.
        float pitch = ComputeWorldPitch(target.Y, listener.Y);

        float distance = offset.Length();
        if (!float.IsFinite(distance))
        {
            distance = VolumeFalloffPixels;
        }

        float distanceFalloff = ComputeDistanceFalloff(distance);
        float safeBaseVolume = float.IsFinite(baseVolume) ? baseVolume : 0f;
        float volume = MathHelper.Clamp(distanceFalloff * safeBaseVolume, 0f, 1f);

        float distanceTiles = distance / 16f;

        return new SpatialAudioSample(normalizedScreenX, pitch, volume, distanceTiles);
    }

    public static float ComputeScreenNormalizedX(float screenX)
    {
        if (!float.IsFinite(screenX))
        {
            return 0f;
        }

        int screenWidth = Main.screenWidth;
        if (screenWidth <= 0)
        {
            return 0f;
        }

        float normalizedX = MathHelper.Clamp(screenX / screenWidth, 0f, 1f);
        return MathHelper.Clamp((normalizedX * 2f) - 1f, -1f, 1f);
    }

    public static float ComputeScreenPitch(float screenY)
    {
        int screenHeight = Main.screenHeight;
        if (screenHeight <= 0)
        {
            return 0f;
        }

        float normalizedY = MathHelper.Clamp(screenY / screenHeight, 0f, 1f);
        return ComputeNormalizedScreenPitch(normalizedY);
    }

    public static float ComputeNormalizedScreenPitch(float normalizedY)
    {
        if (!float.IsFinite(normalizedY))
        {
            return 0f;
        }

        float clampedY = MathHelper.Clamp(normalizedY, 0f, 1f);
        float centeredY = (clampedY * 2f) - 1f;
        return MathHelper.Clamp(-centeredY * MaxPitchClamp, -MaxPitchClamp, MaxPitchClamp);
    }

    public static InterauralParameters ComputeInterauralParameters(float normalizedScreenX, int sampleRate)
    {
        int safeSampleRate = Math.Max(1, sampleRate);
        float clampedScreenX = ClampNormalizedScreenX(normalizedScreenX);
        float magnitude = MathF.Abs(clampedScreenX);
        float totalDelaySamples = Math.Max(0f, magnitude * MaxInterauralDelaySeconds * safeSampleRate);
        float delayedEarGain = ComputeDelayedEarGain(magnitude);
        int delaySamples = (int)MathF.Floor(totalDelaySamples);
        float delayFraction = totalDelaySamples - delaySamples;
        if (delayFraction >= 0.9999f)
        {
            delaySamples++;
            delayFraction = 0f;
        }
        else if (delayFraction <= 0.0001f)
        {
            delayFraction = 0f;
        }

        if (clampedScreenX < 0f)
        {
            return new InterauralParameters(
                LeftDelaySamples: 0,
                RightDelaySamples: delaySamples,
                LeftDelayFraction: 0f,
                RightDelayFraction: delayFraction,
                LeftGain: 1f,
                RightGain: delayedEarGain);
        }

        if (clampedScreenX > 0f)
        {
            return new InterauralParameters(
                LeftDelaySamples: delaySamples,
                RightDelaySamples: 0,
                LeftDelayFraction: delayFraction,
                RightDelayFraction: 0f,
                LeftGain: delayedEarGain,
                RightGain: 1f);
        }

        return new InterauralParameters(0, 0, 0f, 0f, 1f, 1f);
    }

    private static float ComputeDelayedEarGain(float normalizedScreenMagnitude)
    {
        float clampedMagnitude = MathHelper.Clamp(normalizedScreenMagnitude, 0f, 1f);
        float shapedMagnitude = MathF.Pow(clampedMagnitude, DelayedEarGainCurvePower);
        return MathHelper.Lerp(1f, MinDelayedEarGainAtEdge, shapedMagnitude);
    }

    public static int QuantizeNormalizedScreenX(float normalizedScreenX, int stepsPerSide = DefaultNormalizedScreenXStepsPerSide)
    {
        int safeSteps = Math.Max(1, stepsPerSide);
        float clamped = ClampNormalizedScreenX(normalizedScreenX);
        return Math.Clamp((int)MathF.Round(clamped * safeSteps), -safeSteps, safeSteps);
    }

    public static float DequantizeNormalizedScreenX(int normalizedScreenXKey, int stepsPerSide = DefaultNormalizedScreenXStepsPerSide)
    {
        int safeSteps = Math.Max(1, stepsPerSide);
        return MathHelper.Clamp(normalizedScreenXKey / (float)safeSteps, -1f, 1f);
    }

    private static float ComputeWorldNormalizedScreenX(float targetWorldX, float listenerWorldX)
    {
        float visibleWidth = Main.ViewSize.X;
        if (IsFinitePositive(visibleWidth) && float.IsFinite(Main.ViewPosition.X))
        {
            float normalizedVisibleX = (targetWorldX - Main.ViewPosition.X) / visibleWidth;
            return ClampNormalizedScreenX((normalizedVisibleX * 2f) - 1f);
        }

        return ClampNormalizedScreenX((targetWorldX - listenerWorldX) / FallbackHorizontalScalePixels);
    }

    private static float ComputeWorldPitch(float targetWorldY, float listenerWorldY)
    {
        float visibleHeight = Main.ViewSize.Y;
        if (IsFinitePositive(visibleHeight) && float.IsFinite(Main.ViewPosition.Y))
        {
            float normalizedVisibleY = (targetWorldY - Main.ViewPosition.Y) / visibleHeight;
            return ComputeNormalizedScreenPitch(normalizedVisibleY);
        }

        float fallbackPitch = -(targetWorldY - listenerWorldY) / FallbackPitchScalePixels;
        if (!float.IsFinite(fallbackPitch))
        {
            return 0f;
        }

        return MathHelper.Clamp(fallbackPitch, -MaxPitchClamp, MaxPitchClamp);
    }

    private static bool IsFinitePositive(float value) => float.IsFinite(value) && value > 0f;

    private static float ClampNormalizedScreenX(float normalizedScreenX)
    {
        if (!float.IsFinite(normalizedScreenX))
        {
            return 0f;
        }

        return MathHelper.Clamp(normalizedScreenX, -1f, 1f);
    }

    private static float ComputeDistanceFalloff(float distancePixels)
    {
        if (distancePixels <= FullVolumeRadiusPixels)
        {
            return 1f;
        }

        float range = Math.Max(1f, VolumeFalloffPixels - FullVolumeRadiusPixels);
        float normalized = MathHelper.Clamp((distancePixels - FullVolumeRadiusPixels) / range, 0f, 1f);
        return MathHelper.Clamp(1f - MathF.Pow(normalized, 1.25f), 0f, 1f);
    }
}
