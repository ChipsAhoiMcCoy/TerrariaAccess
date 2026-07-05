#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    internal static class FootstepToneProvider
    {
        private const int SampleRate = 44100;
        private const float DurationSeconds = 0.08f;
        private const float WhiteNoiseDurationSeconds = 0.5f;
        private const int ToneCacheFrequencyStepHz = 10;

        private static readonly SpatializedSoundCache<(int CacheKey, bool Triangle, bool Looped)> ToneCache = new();
        private static readonly SpatializedSoundCache WhiteNoiseCache = new();
        private static readonly List<SoundEffectInstance> ActiveInstances = new();
        private static readonly Random NoiseRandom = new();

        /// <summary>
        /// Plays a short custom tone with horizontal direction encoded by interaural time delay.
        /// <paramref name="normalizedScreenX"/> is visible-screen X: left edge -1, center 0, right edge 1.
        /// </summary>
        public static void Play(float frequencyHz, float volume, bool useTriangleWave, float normalizedScreenX, float pitch = 0f)
        {
            if (frequencyHz <= 0f || !SpatializedSoundEngine.CanPlay(volume))
            {
                return;
            }

            try
            {
                CleanupFinishedInstances();

                SoundEffect tone = EnsureTone(frequencyHz, useTriangleWave, normalizedScreenX, looped: false);
                SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                    tone,
                    volume,
                    pitch);
                if (instance is null)
                {
                    return;
                }

                ActiveInstances.Add(instance);
            }
            catch (Exception ex)
            {
                LogPlaybackFailure("tone", ex);
            }
        }

        /// <summary>
        /// Plays an intentionally centered self/status cue. World-positioned sounds should use
        /// <see cref="PlaySpatial"/> so visible-screen-X ITD, elevation pitch, and distance volume
        /// come from the shared spatial engine.
        /// </summary>
        public static void PlayCentered(float frequencyHz, float volume, bool useTriangleWave, float pitch = 0f)
        {
            Play(
                frequencyHz,
                volume,
                useTriangleWave,
                SpatializedSoundEngine.CenterNormalizedScreenX,
                pitch);
        }

        /// <summary>
        /// Plays a short custom tone from a spatial sample, applying the sample's distance attenuation
        /// and normalized-screen-X ITD position in one place.
        /// </summary>
        public static void PlaySpatial(
            SpatializedSoundEngine.SpatialAudioSample sample,
            float frequencyHz,
            float localVolumeScale,
            bool useTriangleWave = false)
        {
            Play(
                frequencyHz,
                sample.ScaleVolume(localVolumeScale),
                useTriangleWave,
                sample.NormalizedScreenX,
                sample.Pitch);
        }

        /// <summary>
        /// Plays a world-positioned cue using the shared visible-screen-X ITD and distance volume,
        /// but leaves pitch under caller control. Use this when the cue already encodes elevation
        /// as frequency, such as sonar scan rows.
        /// </summary>
        public static void PlaySpatialHorizontalAndDistance(
            SpatializedSoundEngine.SpatialAudioSample sample,
            float frequencyHz,
            float localVolumeScale,
            bool useTriangleWave = false,
            float pitch = 0f)
        {
            Play(
                frequencyHz,
                sample.ScaleVolume(localVolumeScale),
                useTriangleWave,
                sample.NormalizedScreenX,
                pitch);
        }

        public static void DisposeStaticResources()
        {
            SpatializedSoundEngine.StopAndDisposeAll(ActiveInstances);

            ToneCache.Dispose();
            WhiteNoiseCache.Dispose();
        }

        private static SoundEffect EnsureTone(float frequencyHz, bool useTriangleWave, float normalizedScreenX, bool looped)
        {
            int cacheKey = Math.Clamp((int)MathF.Round(frequencyHz / ToneCacheFrequencyStepHz) * ToneCacheFrequencyStepHz, 50, 12000);
            var key = (cacheKey, useTriangleWave, looped);
            return ToneCache.GetOrCreate(
                key,
                normalizedScreenX,
                quantizedNormalizedScreenX => CreateTone(
                    MathF.Max(40f, cacheKey),
                    useTriangleWave,
                    quantizedNormalizedScreenX,
                    looped));
        }

        /// <summary>
        /// Plays a looping triangle tone with wrapped ITD delay so loop seams remain continuous.
        /// <paramref name="normalizedScreenX"/> is visible-screen X: left edge -1, center 0, right edge 1.
        /// </summary>
        public static SoundEffectInstance? PlayLoopingTriangle(float frequencyHz, float volume, float normalizedScreenX)
        {
            if (frequencyHz <= 0f || !SpatializedSoundEngine.CanPlay(volume))
            {
                return null;
            }

            try
            {
                CleanupFinishedInstances();

                SoundEffect tone = EnsureTone(frequencyHz, useTriangleWave: true, normalizedScreenX, looped: true);
                SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                    tone,
                    volume,
                    looped: true);
                if (instance is not null)
                {
                    ActiveInstances.Add(instance);
                }

                return instance;
            }
            catch (Exception ex)
            {
                LogPlaybackFailure("looping triangle", ex);
                return null;
            }
        }

        /// <summary>
        /// Plays an intentionally centered looping self/status cue.
        /// </summary>
        public static SoundEffectInstance? PlayLoopingTriangleCentered(float frequencyHz, float volume)
        {
            return PlayLoopingTriangle(frequencyHz, volume, SpatializedSoundEngine.CenterNormalizedScreenX);
        }

        public static void StopInstance(SoundEffectInstance instance)
        {
            if (instance is null)
            {
                return;
            }

            SpatializedSoundEngine.StopAndDispose(instance);
            ActiveInstances.Remove(instance);
        }

        /// <summary>
        /// Plays looping white noise with wrapped ITD delay so loop seams remain continuous.
        /// <paramref name="normalizedScreenX"/> is visible-screen X: left edge -1, center 0, right edge 1.
        /// </summary>
        public static SoundEffectInstance? PlayLoopingWhiteNoise(float volume, float normalizedScreenX, float pitch = 0f)
        {
            if (!SpatializedSoundEngine.CanPlay(volume))
            {
                return null;
            }

            try
            {
                CleanupFinishedInstances();

                SoundEffect noise = EnsureWhiteNoise(normalizedScreenX);
                SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                    noise,
                    volume,
                    pitch,
                    looped: true);
                if (instance is not null)
                {
                    ActiveInstances.Add(instance);
                }

                return instance;
            }
            catch (Exception ex)
            {
                LogPlaybackFailure("looping white noise", ex);
                return null;
            }
        }

        /// <summary>
        /// Plays looping white noise from a spatial sample, applying distance attenuation and
        /// normalized-screen-X ITD position in one place.
        /// </summary>
        public static SoundEffectInstance? PlayLoopingWhiteNoiseSpatial(
            SpatializedSoundEngine.SpatialAudioSample sample,
            float localVolumeScale)
        {
            return PlayLoopingWhiteNoise(
                sample.ScaleVolume(localVolumeScale),
                sample.NormalizedScreenX,
                sample.Pitch);
        }

        private static SoundEffect EnsureWhiteNoise(float normalizedScreenX)
        {
            return WhiteNoiseCache.GetOrCreate(normalizedScreenX, CreateWhiteNoise);
        }

        private static SoundEffect CreateWhiteNoise(float normalizedScreenX)
        {
            int sampleCount = Math.Max(1, (int)(SampleRate * WhiteNoiseDurationSeconds));
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                // Generate white noise: random values between -1 and 1
                float sample = (float)(NoiseRandom.NextDouble() * 2.0 - 1.0);
                // Apply a gentle low-pass filter by averaging with previous sample for softer static
                samples[i] = sample * 0.3f;
            }

            return SpatializedSoundEngine.CreateSpatialFromSamples(samples, SampleRate, normalizedScreenX, wrapDelay: true);
        }

        private static SoundEffect CreateTone(float frequencyHz, bool useTriangleWave, float normalizedScreenX, bool looped)
        {
            int sampleCount = Math.Max(1, (int)(SampleRate * DurationSeconds));
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = GetEnvelope(t);

                float basePhase = MathHelper.TwoPi * frequencyHz * t;
                float waveform = useTriangleWave ? GetTriangleWave(basePhase) : MathF.Sin(basePhase);
                samples[i] = waveform * envelope;
            }

            return SpatializedSoundEngine.CreateSpatialFromSamples(samples, SampleRate, normalizedScreenX, wrapDelay: looped);
        }

        private static float GetEnvelope(float time)
        {
            float attack = MathF.Min(0.02f, DurationSeconds * 0.35f);
            float decay = Math.Max(DurationSeconds - attack, 0.01f);
            if (time <= attack)
            {
                return MathHelper.Clamp(time / Math.Max(attack, 0.0001f), 0f, 1f);
            }

            float normalized = MathHelper.Clamp((time - attack) / Math.Max(decay, 0.0001f), 0f, 1f);
            return MathF.Exp(-4.5f * normalized);
        }

        private static float GetTriangleWave(float phase)
        {
            float normalized = (phase / MathHelper.TwoPi) % 1f;
            if (normalized < 0f)
            {
                normalized += 1f;
            }

            return 4f * MathF.Abs(normalized - 0.5f) - 1f;
        }

        private static void CleanupFinishedInstances()
        {
            SpatializedSoundEngine.CleanupStopped(ActiveInstances);
        }

        private static void LogPlaybackFailure(string cueKind, Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn(
                $"[FootstepToneProvider] Failed to play {cueKind} cue: {ex.Message}");
        }
    }
}
