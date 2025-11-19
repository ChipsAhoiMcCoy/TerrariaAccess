#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private static class FallIndicatorToneProvider
    {
        private const int SampleRate = 44100;
        private const float DurationSeconds = 0.09f;
        private const float FrequencyHz = 820f;

        private static SoundEffect? _tone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();

        public static void Play(float volume)
        {
            if (volume <= 0f || Main.soundVolume <= 0f)
            {
                return;
            }

            CleanupFinishedInstances();

            SoundEffect tone = EnsureTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume;
            instance.Pan = 0f;
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

            if (_tone is not null)
            {
                if (!_tone.IsDisposed)
                {
                    _tone.Dispose();
                }

                _tone = null;
            }
        }

        private static SoundEffect EnsureTone()
        {
            if (_tone is { IsDisposed: false })
            {
                return _tone;
            }

            _tone?.Dispose();
            _tone = SynthesizedSoundFactory.CreateSineTone(
                sampleRate: SampleRate,
                frequency: FrequencyHz,
                durationSeconds: DurationSeconds,
                envelope: SynthesizedSoundFactory.ToneEnvelopes.CursorPing,
                gain: 1f);
            return _tone;
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
