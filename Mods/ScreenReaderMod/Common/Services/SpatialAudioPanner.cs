#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace ScreenReaderMod.Common.Services;

/// <summary>
/// Provides shared stereo pan/pitch calculations for positional cues so guidance and interactable
/// beacons stay consistent.
/// </summary>
internal static class SpatialAudioPanner
{
    internal readonly record struct SpatialDirection(float Pitch, float Pan, float DistanceTiles);

    internal readonly record struct SpatialAudioProfile(
        float PitchScalePixels,
        float PanScalePixels,
        float DistanceReferenceTiles,
        float MinVolume,
        float VolumeScale = 0.85f,
        float PitchClamp = 0.8f);

    internal readonly record struct SpatialAudioSample(float Pitch, float Pan, float Volume);

    public static SpatialDirection ComputeDirection(Vector2 listener, Vector2 target, float pitchScalePixels, float panScalePixels, float pitchClamp = 0.8f)
    {
        Vector2 offset = target - listener;
        float pitch = MathHelper.Clamp(-offset.Y / Math.Max(0.001f, pitchScalePixels), -pitchClamp, pitchClamp);
        float pan = MathHelper.Clamp(offset.X / Math.Max(0.001f, panScalePixels), -1f, 1f);
        float distanceTiles = offset.Length() / 16f;
        return new SpatialDirection(pitch, pan, distanceTiles);
    }

    public static SpatialAudioSample ComputeSample(Vector2 listener, Vector2 target, SpatialAudioProfile profile, float soundVolume)
    {
        SpatialDirection direction = ComputeDirection(listener, target, profile.PitchScalePixels, profile.PanScalePixels, profile.PitchClamp);
        float distanceFactor = 1f / (1f + (direction.DistanceTiles / Math.Max(1f, profile.DistanceReferenceTiles)));
        float volume = MathHelper.Clamp(profile.MinVolume + distanceFactor * profile.VolumeScale, 0f, 1f) * soundVolume;
        return new SpatialAudioSample(direction.Pitch, direction.Pan, volume);
    }
}
