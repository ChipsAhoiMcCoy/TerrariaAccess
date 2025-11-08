using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace ScreenReaderMod.Common.Services;

internal static class SynthesizedSoundFactory
{
    private const int DefaultSampleRate = 44100;

    public static class ToneEnvelopes
    {
        public static ToneEnvelope CursorPing { get; } = new(attackFraction: 0.1f, releaseFraction: 0.35f, applyHannWindow: true);
        public static ToneEnvelope WaypointPulse { get; } = new(attackFraction: 0.3f, releaseFraction: 1f, applyHannWindow: true);
        public static ToneEnvelope WorldCue { get; } = new(attackFraction: 0.18f, releaseFraction: 0.4f, applyHannWindow: true);
    }

    public static SoundEffect CreateSineTone(
        float frequency,
        float durationSeconds,
        ToneEnvelope envelope,
        float gain = 1f,
        int sampleRate = DefaultSampleRate)
    {
        return CreateTone(sampleRate, durationSeconds, envelope, gain, time => MathF.Sin(MathHelper.TwoPi * frequency * time));
    }

    public static SoundEffect CreateAdditiveTone(
        float fundamentalFrequency,
        float[] partialMultipliers,
        ToneEnvelope envelope,
        float durationSeconds,
        float outputGain = 0.35f,
        float partialFalloff = 0.6f,
        int sampleRate = DefaultSampleRate)
    {
        float[] partials = partialMultipliers?.Length > 0 ? partialMultipliers : Array.Empty<float>();

        return CreateTone(
            sampleRate,
            durationSeconds,
            envelope,
            outputGain,
            time =>
            {
                float sample = MathF.Sin(MathHelper.TwoPi * fundamentalFrequency * time);
                for (int i = 0; i < partials.Length; i++)
                {
                    float multiplier = partials[i];
                    float amplitude = partialFalloff / (i + 1f);
                    sample += MathF.Sin(MathHelper.TwoPi * fundamentalFrequency * multiplier * time) * amplitude;
                }

                return sample;
            });
    }

    private static SoundEffect CreateTone(
        int sampleRate,
        float durationSeconds,
        ToneEnvelope envelope,
        float outputGain,
        Func<float, float> waveform)
    {
        int sampleCount = Math.Max(1, (int)(sampleRate * Math.Max(durationSeconds, 0f)));
        byte[] buffer = new byte[sampleCount * sizeof(short)];
        float denominator = Math.Max(1, sampleCount - 1);

        for (int i = 0; i < sampleCount; i++)
        {
            float normalizedIndex = i / denominator;
            float time = i / (float)sampleRate;
            float sample = waveform(time) * outputGain * envelope.Evaluate(normalizedIndex);

            short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            int index = i * 2;
            buffer[index] = (byte)(value & 0xFF);
            buffer[index + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
    }
}

internal readonly struct ToneEnvelope
{
    private const float MinPortion = 0.0001f;

    public ToneEnvelope(float attackFraction, float releaseFraction, bool applyHannWindow)
    {
        AttackFraction = Math.Clamp(attackFraction, 0f, 1f);
        ReleaseFraction = Math.Clamp(releaseFraction, 0f, 1f);
        ApplyHannWindow = applyHannWindow;
    }

    public float AttackFraction { get; }
    public float ReleaseFraction { get; }
    public bool ApplyHannWindow { get; }

    public float Evaluate(float normalizedIndex)
    {
        float envelope = 1f;
        float clampedIndex = Math.Clamp(normalizedIndex, 0f, 1f);

        if (ApplyHannWindow)
        {
            envelope *= 0.5f - 0.5f * MathF.Cos(MathHelper.TwoPi * clampedIndex);
        }

        if (AttackFraction > 0f)
        {
            float attackProgress = Math.Clamp(clampedIndex / Math.Max(AttackFraction, MinPortion), 0f, 1f);
            envelope *= attackProgress;
        }

        if (ReleaseFraction > 0f)
        {
            float releaseStart = Math.Clamp(1f - ReleaseFraction, 0f, 1f);
            if (clampedIndex >= releaseStart)
            {
                float releaseProgress = Math.Clamp((1f - clampedIndex) / Math.Max(ReleaseFraction, MinPortion), 0f, 1f);
                envelope *= releaseProgress;
            }
        }

        return envelope;
    }
}
