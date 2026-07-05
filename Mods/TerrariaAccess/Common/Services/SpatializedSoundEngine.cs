#nullable enable
#pragma warning disable CS0618 // This facade is the permitted production boundary for low-level spatial helpers.

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Facade for Terraria Access custom spatial audio.
/// Horizontal position is always normalized visible-screen X: left edge -1, center 0, right edge 1.
/// The engine encodes that value as interaural time delay in synthesized stereo buffers.
/// </summary>
internal static class SpatializedSoundEngine
{
    public const float CenterNormalizedScreenX = 0f;

    internal readonly record struct SpatialAudioSample(float NormalizedScreenX, float Pitch, float Volume, float DistanceTiles)
    {
        /// <summary>
        /// Applies a local cue/config volume scale to the distance-attenuated spatial volume.
        /// Global Terraria volume and shared cue ceilings are applied later by the playback boundary.
        /// </summary>
        public float ScaleVolume(float localVolumeScale) => SpatializedSoundEngine.ClampVolume(Volume * localVolumeScale);

        public bool IsAudible(float localVolumeScale) => ScaleVolume(localVolumeScale) > 0f;
    }

    public static class ToneEnvelopes
    {
        public static ToneEnvelope CursorPing => SynthesizedSoundFactory.ToneEnvelopes.CursorPing;
        public static ToneEnvelope WaypointPulse => SynthesizedSoundFactory.ToneEnvelopes.WaypointPulse;
        public static ToneEnvelope WorldCue => SynthesizedSoundFactory.ToneEnvelopes.WorldCue;
    }

    private static float ClampVolume(float volume) =>
        float.IsFinite(volume) ? MathHelper.Clamp(volume, 0f, 1f) : 0f;

    public static float NormalizeVolume(float volume) => ClampVolume(volume);

    public static bool CanPlay(float volume = 1f) =>
        !Main.dedServ && ClampVolume(volume) > 0f && ClampVolume(NativeSoundSuppression.GetEffectiveSoundVolume()) > 0f;

    public static SpatialAudioSample Compute(Vector2 listener, Vector2 target, float baseVolume = 1f)
    {
        SpatialAudioPositioner.SpatialAudioSample sample = SpatialAudioPositioner.Compute(listener, target, baseVolume);
        return new SpatialAudioSample(sample.NormalizedScreenX, sample.Pitch, sample.Volume, sample.DistanceTiles);
    }

    public static float ComputeScreenNormalizedX(float screenX) =>
        SpatialAudioPositioner.ComputeScreenNormalizedX(screenX);

    public static float ComputeScreenPitch(float screenY) =>
        SpatialAudioPositioner.ComputeScreenPitch(screenY);

    public static float ComputeNormalizedScreenPitch(float normalizedY) =>
        SpatialAudioPositioner.ComputeNormalizedScreenPitch(normalizedY);

    public static int QuantizeNormalizedScreenX(float normalizedScreenX) =>
        SpatialAudioPositioner.QuantizeNormalizedScreenX(normalizedScreenX);

    public static float DequantizeNormalizedScreenX(int normalizedScreenXKey) =>
        SpatialAudioPositioner.DequantizeNormalizedScreenX(normalizedScreenXKey);

    public static SoundEffect CreateSpatialSineTone(
        float frequency,
        float durationSeconds,
        ToneEnvelope envelope,
        float gain,
        float normalizedScreenX) =>
        SynthesizedSoundFactory.CreateSpatialSineTone(
            frequency,
            durationSeconds,
            envelope,
            gain,
            normalizedScreenX);

    public static SoundEffect CreateSpatialAdditiveTone(
        float fundamentalFrequency,
        float[] partialMultipliers,
        ToneEnvelope envelope,
        float durationSeconds,
        float outputGain,
        float normalizedScreenX,
        float partialFalloff = 0.6f) =>
        SynthesizedSoundFactory.CreateSpatialAdditiveTone(
            fundamentalFrequency,
            partialMultipliers,
            envelope,
            durationSeconds,
            outputGain,
            normalizedScreenX,
            partialFalloff);

    public static SoundEffect CreateSpatialNoise(
        float durationSeconds,
        ToneEnvelope envelope,
        float gain,
        float normalizedScreenX,
        int? seed = null) =>
        SynthesizedSoundFactory.CreateSpatialNoise(
            durationSeconds,
            envelope,
            gain,
            normalizedScreenX,
            seed: seed);

    public static SoundEffect CreateSpatialFromSamples(
        float[] sourceSamples,
        int sampleRate,
        float normalizedScreenX,
        bool wrapDelay = false) =>
        SynthesizedSoundFactory.CreateSpatialFromSamples(
            sourceSamples,
            sampleRate,
            normalizedScreenX,
            wrapDelay);

    public static SoundEffectInstance? PlayWorldCue(
        SoundEffect? sound,
        SpatialAudioSample sample,
        float localVolumeScale,
        bool looped = false) =>
        SpatializedSoundPlayer.PlayWorldCue(sound, sample, localVolumeScale, looped);

    /// <summary>
    /// Plays a generated stereo buffer whose ITD position was already encoded during synthesis.
    /// Use <see cref="PlayWorldCue"/> for ordinary world-positioned sounds so distance volume
    /// and elevation pitch come from the shared spatial sample.
    /// </summary>
    public static SoundEffectInstance? PlayAlreadySpatializedWorldCue(
        SoundEffect? sound,
        float volume,
        float pitch = 0f,
        bool looped = false) =>
        SpatializedSoundPlayer.PlayAlreadySpatializedWorldCue(sound, volume, pitch, looped);

    /// <summary>
    /// Plays a generated stereo UI buffer whose ITD position was already encoded during synthesis.
    /// UI callers should usually compute normalized screen X and pitch through this facade first.
    /// </summary>
    public static SoundEffectInstance? PlayAlreadySpatializedInterfaceCue(
        SoundEffect? sound,
        float volume,
        float pitch = 0f,
        bool looped = false) =>
        SpatializedSoundPlayer.PlayInterfaceCue(sound, volume, pitch, looped);

    public static void SetWorldCueVolume(SoundEffectInstance? instance, float volume) =>
        SpatializedSoundPlayer.SetWorldCueVolume(instance, volume);

    public static void CleanupStopped(IList<SoundEffectInstance>? instances) =>
        SpatializedSoundPlayer.CleanupStopped(instances);

    public static void StopAndDispose(SoundEffectInstance? instance) =>
        SpatializedSoundPlayer.StopAndDispose(instance);

    public static void StopInstanceAndDisposeEffect(SoundEffectInstance? instance, SoundEffect? effect) =>
        SpatializedSoundPlayer.StopInstanceAndDisposeEffect(instance, effect);

    public static void StopAndDisposeAll(IList<SoundEffectInstance>? instances) =>
        SpatializedSoundPlayer.StopAndDisposeAll(instances);
}
