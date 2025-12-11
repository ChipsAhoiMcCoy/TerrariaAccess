#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private static class FootstepToneProvider
    {
        private const int SampleRate = 44100;
        private const float DurationSeconds = 0.08f;

        private static readonly Dictionary<(int CacheKey, bool Triangle), SoundEffect?> ToneCache = new();
        private static readonly List<SoundEffectInstance> ActiveInstances = new();

        public static void Play(float frequencyHz, float volume, bool useTriangleWave = false, float pan = 0f)
        {
            if (frequencyHz <= 0f || volume <= 0f || Main.soundVolume <= 0f)
            {
                return;
            }

            CleanupFinishedInstances();

            SoundEffect tone = EnsureTone(frequencyHz, useTriangleWave);
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
            instance.Pan = MathHelper.Clamp(pan, -1f, 1f);
            instance.Play();
            ActiveInstances.Add(instance);
        }

        public static void DisposeStaticResources()
        {
            foreach (SoundEffectInstance instance in ActiveInstances)
            {
                try
                {
                    instance.Stop();
                }
                catch
                {
                    // ignore audio backend failures
                }

                instance.Dispose();
            }

            ActiveInstances.Clear();

            foreach (KeyValuePair<(int CacheKey, bool Triangle), SoundEffect?> kvp in ToneCache)
            {
                kvp.Value?.Dispose();
            }

            ToneCache.Clear();
        }

        private static SoundEffect EnsureTone(float frequencyHz, bool useTriangleWave)
        {
            int cacheKey = Math.Clamp((int)MathF.Round(frequencyHz), 50, 2000);
            var key = (cacheKey, useTriangleWave);
            if (ToneCache.TryGetValue(key, out SoundEffect? cached) && cached is { IsDisposed: false })
            {
                return cached;
            }

            cached?.Dispose();
            SoundEffect created = CreateTone(MathF.Max(40f, frequencyHz), useTriangleWave);
            ToneCache[key] = created;
            return created;
        }

        public static SoundEffectInstance? PlayLoopingTriangle(float frequencyHz, float volume, float pan = 0f)
        {
            if (frequencyHz <= 0f || volume <= 0f || Main.soundVolume <= 0f)
            {
                return null;
            }

            CleanupFinishedInstances();

            SoundEffect tone = EnsureTone(frequencyHz, useTriangleWave: true);
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = true;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
            instance.Pan = MathHelper.Clamp(pan, -1f, 1f);
            instance.Play();
            ActiveInstances.Add(instance);
            return instance;
        }

        public static void StopInstance(SoundEffectInstance instance)
        {
            if (instance is null)
            {
                return;
            }

            try
            {
                instance.Stop();
            }
            catch
            {
                // ignore audio backend failures
            }

            instance.Dispose();
            ActiveInstances.Remove(instance);
        }

        private static SoundEffect CreateTone(float frequencyHz, bool useTriangleWave)
        {
            int sampleCount = Math.Max(1, (int)(SampleRate * DurationSeconds));
            byte[] buffer = new byte[sampleCount * sizeof(short)];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = GetEnvelope(t);

                float basePhase = MathHelper.TwoPi * frequencyHz * t;
                float waveform = useTriangleWave ? GetTriangleWave(basePhase) : MathF.Sin(basePhase);
                float sample = waveform * envelope;
                short quantized = (short)MathHelper.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(quantized & 0xFF);
                buffer[index + 1] = (byte)((quantized >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
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
            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = ActiveInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    ActiveInstances.RemoveAt(i);
                }
            }
        }
    }
}
