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
        private const float DurationSeconds = 0.12f;

        private static readonly SoundEffect?[,] ToneCache = new SoundEffect?[2, FootstepVariantCount];
        private static readonly List<SoundEffectInstance> ActiveInstances = new();

        public static void Play(FootstepSide side, int variantIndex, float volume, float pitch, float pan)
        {
            CleanupFinishedInstances();

            SoundEffect tone = EnsureTone(side, variantIndex);
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume;
            instance.Pitch = MathHelper.Clamp(pitch, -1f, 1f);
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

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                for (int variant = 0; variant < FootstepVariantCount; variant++)
                {
                    SoundEffect? tone = ToneCache[sideIndex, variant];
                    if (tone is { IsDisposed: false })
                    {
                        tone.Dispose();
                    }

                    ToneCache[sideIndex, variant] = null;
                }
            }
        }

        private static SoundEffect EnsureTone(FootstepSide side, int variantIndex)
        {
            int sideIndex = side == FootstepSide.Left ? 0 : 1;
            int clampedVariant = Math.Clamp(variantIndex, 0, FootstepVariantCount - 1);

            SoundEffect? cached = ToneCache[sideIndex, clampedVariant];
            if (cached is { IsDisposed: false })
            {
                return cached;
            }

            cached?.Dispose();
            SoundEffect created = CreateTone(side, clampedVariant);
            ToneCache[sideIndex, clampedVariant] = created;
            return created;
        }

        private static SoundEffect CreateTone(FootstepSide side, int variantIndex)
        {
            int sampleCount = Math.Max(1, (int)(SampleRate * DurationSeconds));
            byte[] buffer = new byte[sampleCount * sizeof(short)];
            float baseFrequency = (side == FootstepSide.Left ? 178f : 204f) + variantIndex * 4f;
            float accent = side == FootstepSide.Left ? 0.92f : 1.04f;
            float phaseJitter = variantIndex * 0.0009f * (side == FootstepSide.Left ? -1f : 1f);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = GetEnvelope(t);

                float low = MathF.Sin(MathHelper.TwoPi * baseFrequency * (t + phaseJitter));
                float combined = low * envelope * accent;
                short quantized = (short)MathHelper.Clamp(combined * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(quantized & 0xFF);
                buffer[index + 1] = (byte)((quantized >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
        }

        private static float GetEnvelope(float time)
        {
            float attack = MathF.Min(0.014f, DurationSeconds * 0.3f);
            float decay = DurationSeconds - attack;
            if (time <= attack)
            {
                return MathHelper.Clamp(time / Math.Max(0.0001f, attack), 0f, 1f);
            }

            float normalized = MathHelper.Clamp((time - attack) / Math.Max(0.0001f, decay), 0f, 1f);
            return MathF.Exp(-5.2f * normalized);
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
