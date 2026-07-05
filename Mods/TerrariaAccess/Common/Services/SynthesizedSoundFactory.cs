#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

#pragma warning disable CS0618 // Low-level factory methods call each other; production code should use SpatializedSoundEngine.

namespace TerrariaAccess.Common.Services;

internal static class SynthesizedSoundFactory
{
    private const int DefaultSampleRate = 44100;
    private const int MaxSampleRate = 192000;
    private const float MaxGeneratedDurationSeconds = 5f;
    private const float MaxToneFrequencyHz = 20000f;
    private const float MaxPartialMultiplier = 64f;

    public static class ToneEnvelopes
    {
        public static ToneEnvelope CursorPing { get; } = new(attackFraction: 0.1f, releaseFraction: 0.35f, applyHannWindow: true);
        public static ToneEnvelope WaypointPulse { get; } = new(attackFraction: 0.3f, releaseFraction: 1f, applyHannWindow: true);
        public static ToneEnvelope WorldCue { get; } = new(attackFraction: 0.18f, releaseFraction: 0.4f, applyHannWindow: true);
    }

    /// <summary>
    /// Creates a stereo sine effect whose horizontal position is encoded with interaural time delay.
    /// The <paramref name="normalizedScreenX"/> value is the normalized visible-screen X position from
    /// <see cref="SpatialAudioPositioner"/>; it is encoded as ITD, not as a legacy XNA
    /// <c>Pan</c> value.
    /// </summary>
    [Obsolete("Use SpatializedSoundEngine.CreateSpatialSineTone so custom sounds stay behind the 2D ITD facade.")]
    public static SoundEffect CreateSpatialSineTone(
        float frequency,
        float durationSeconds,
        ToneEnvelope envelope,
        float gain,
        float normalizedScreenX,
        int sampleRate = DefaultSampleRate)
    {
        float safeFrequency = SanitizeFrequency(frequency);
        float safeGain = SanitizeGain(gain);
        return CreateSpatialTone(sampleRate, durationSeconds, envelope, safeGain, normalizedScreenX, time => MathF.Sin(MathHelper.TwoPi * safeFrequency * time));
    }

    /// <summary>
    /// Creates a stereo additive tone whose horizontal position is encoded with interaural time delay.
    /// The <paramref name="normalizedScreenX"/> value is the normalized visible-screen X position from
    /// <see cref="SpatialAudioPositioner"/>; it is encoded as ITD, not as a legacy XNA
    /// <c>Pan</c> value.
    /// </summary>
    [Obsolete("Use SpatializedSoundEngine.CreateSpatialAdditiveTone so custom sounds stay behind the 2D ITD facade.")]
    public static SoundEffect CreateSpatialAdditiveTone(
        float fundamentalFrequency,
        float[] partialMultipliers,
        ToneEnvelope envelope,
        float durationSeconds,
        float outputGain,
        float normalizedScreenX,
        float partialFalloff = 0.6f,
        int sampleRate = DefaultSampleRate)
    {
        float safeFundamentalFrequency = SanitizeFrequency(fundamentalFrequency);
        float safeOutputGain = SanitizeGain(outputGain);
        float safePartialFalloff = SanitizeGain(partialFalloff);
        float[] partials = SanitizePartialMultipliers(partialMultipliers);

        return CreateSpatialTone(
            sampleRate,
            durationSeconds,
            envelope,
            safeOutputGain,
            normalizedScreenX,
            time =>
            {
                float sample = MathF.Sin(MathHelper.TwoPi * safeFundamentalFrequency * time);
                for (int i = 0; i < partials.Length; i++)
                {
                    float multiplier = partials[i];
                    float amplitude = safePartialFalloff / (i + 1f);
                    sample += MathF.Sin(MathHelper.TwoPi * safeFundamentalFrequency * multiplier * time) * amplitude;
                }

                return sample;
            });
    }

    /// <summary>
    /// Creates stereo noise whose horizontal position is encoded with interaural time delay.
    /// The <paramref name="normalizedScreenX"/> value is the normalized visible-screen X position from
    /// <see cref="SpatialAudioPositioner"/>; it is encoded as ITD, not as a legacy XNA
    /// <c>Pan</c> value.
    /// </summary>
    [Obsolete("Use SpatializedSoundEngine.CreateSpatialNoise so custom sounds stay behind the 2D ITD facade.")]
    public static SoundEffect CreateSpatialNoise(
        float durationSeconds,
        ToneEnvelope envelope,
        float gain,
        float normalizedScreenX,
        int sampleRate = DefaultSampleRate,
        int? seed = null)
    {
        int safeSampleRate = SanitizeSampleRate(sampleRate);
        float safeGain = SanitizeGain(gain);
        int sampleCount = ComputeGeneratedSampleCount(safeSampleRate, durationSeconds);
        float[] samples = new float[sampleCount];
        float denominator = Math.Max(1, sampleCount - 1);
        Random random = seed.HasValue ? new Random(seed.Value) : new Random(unchecked((int)DateTime.UtcNow.Ticks));

        for (int i = 0; i < sampleCount; i++)
        {
            float normalizedIndex = i / denominator;
            float envelopeValue = envelope.Evaluate(normalizedIndex);
            float centered = (float)(random.NextDouble() * 2d - 1d);
            samples[i] = centered * safeGain * envelopeValue;
        }

        return CreateSpatialFromSamples(samples, safeSampleRate, normalizedScreenX);
    }

    /// <summary>
    /// Converts mono source samples into a stereo ITD buffer.
    /// <paramref name="normalizedScreenX"/> is visible-screen X: left edge -1, center 0, right edge 1.
    /// Use <paramref name="wrapDelay"/> for looping effects so the delayed ear wraps instead of ending with a silent tail.
    /// </summary>
    [Obsolete("Use SpatializedSoundEngine.CreateSpatialFromSamples so custom sounds stay behind the 2D ITD facade.")]
    public static SoundEffect CreateSpatialFromSamples(
        float[] sourceSamples,
        int sampleRate,
        float normalizedScreenX,
        bool wrapDelay = false)
    {
        int safeSampleRate = SanitizeSampleRate(sampleRate);
        byte[] buffer = CreateSpatialPcm16(sourceSamples, safeSampleRate, normalizedScreenX, wrapDelay);
        return new SoundEffect(buffer, safeSampleRate, AudioChannels.Stereo);
    }

    internal static byte[] CreateSpatialPcm16(
        float[] sourceSamples,
        int sampleRate,
        float normalizedScreenX,
        bool wrapDelay = false)
    {
        int safeSampleRate = SanitizeSampleRate(sampleRate);
        if (sourceSamples is null || sourceSamples.Length == 0)
        {
            sourceSamples = new[] { 0f };
        }

        SpatialAudioPositioner.InterauralParameters ears = SpatialAudioPositioner.ComputeInterauralParameters(normalizedScreenX, safeSampleRate);
        int maxDelay = Math.Max(
            ears.LeftDelaySamples + (ears.LeftDelayFraction > 0f ? 1 : 0),
            ears.RightDelaySamples + (ears.RightDelayFraction > 0f ? 1 : 0));
        int outputSampleCount = Math.Max(1, sourceSamples.Length + (wrapDelay ? 0 : maxDelay));
        byte[] buffer = new byte[outputSampleCount * sizeof(short) * 2];

        for (int i = 0; i < outputSampleCount; i++)
        {
            short left = Quantize(ReadDelayedSample(sourceSamples, i - ears.LeftDelaySamples, ears.LeftDelayFraction, wrapDelay) * ears.LeftGain);
            short right = Quantize(ReadDelayedSample(sourceSamples, i - ears.RightDelaySamples, ears.RightDelayFraction, wrapDelay) * ears.RightGain);
            int index = i * sizeof(short) * 2;
            WriteInt16(buffer, index, left);
            WriteInt16(buffer, index + sizeof(short), right);
        }

        return buffer;
    }

    private static SoundEffect CreateSpatialTone(
        int sampleRate,
        float durationSeconds,
        ToneEnvelope envelope,
        float outputGain,
        float normalizedScreenX,
        Func<float, float> waveform)
    {
        int safeSampleRate = SanitizeSampleRate(sampleRate);
        int sampleCount = ComputeGeneratedSampleCount(safeSampleRate, durationSeconds);
        float[] samples = new float[sampleCount];
        float denominator = Math.Max(1, sampleCount - 1);

        for (int i = 0; i < sampleCount; i++)
        {
            float normalizedIndex = i / denominator;
            float time = i / (float)safeSampleRate;
            samples[i] = waveform(time) * outputGain * envelope.Evaluate(normalizedIndex);
        }

        return CreateSpatialFromSamples(samples, safeSampleRate, normalizedScreenX);
    }

    private static float ReadDelayedSample(float[] samples, int index, float delayFraction, bool wrap)
    {
        float clampedFraction = Math.Clamp(delayFraction, 0f, 1f);
        if (clampedFraction > 0f)
        {
            float current = ReadSampleAt(samples, index, wrap);
            float previous = ReadSampleAt(samples, index - 1, wrap);
            return MathHelper.Lerp(current, previous, clampedFraction);
        }

        return ReadSampleAt(samples, index, wrap);
    }

    private static float ReadSampleAt(float[] samples, int index, bool wrap)
    {
        if (wrap && samples.Length > 0)
        {
            int wrappedIndex = index % samples.Length;
            if (wrappedIndex < 0)
            {
                wrappedIndex += samples.Length;
            }

            return samples[wrappedIndex];
        }

        if (index < 0 || index >= samples.Length)
        {
            return 0f;
        }

        return samples[index];
    }

    private static int SanitizeSampleRate(int sampleRate) =>
        Math.Clamp(sampleRate > 0 ? sampleRate : DefaultSampleRate, 1, MaxSampleRate);

    internal static float SanitizeFrequency(float frequency) =>
        float.IsFinite(frequency) && frequency > 0f ? Math.Min(frequency, MaxToneFrequencyHz) : 0f;

    internal static float SanitizeGain(float gain) =>
        float.IsFinite(gain) ? gain : 0f;

    internal static float[] SanitizePartialMultipliers(float[]? partialMultipliers)
    {
        if (partialMultipliers is null || partialMultipliers.Length == 0)
        {
            return Array.Empty<float>();
        }

        float[] sanitized = new float[partialMultipliers.Length];
        int count = 0;
        for (int i = 0; i < partialMultipliers.Length; i++)
        {
            float multiplier = partialMultipliers[i];
            if (float.IsFinite(multiplier) && multiplier > 0f)
            {
                sanitized[count++] = Math.Min(multiplier, MaxPartialMultiplier);
            }
        }

        if (count == 0)
        {
            return Array.Empty<float>();
        }

        if (count == sanitized.Length)
        {
            return sanitized;
        }

        Array.Resize(ref sanitized, count);
        return sanitized;
    }

    internal static int ComputeGeneratedSampleCount(int sampleRate, float durationSeconds)
    {
        int safeSampleRate = SanitizeSampleRate(sampleRate);
        float safeDuration = float.IsFinite(durationSeconds)
            ? Math.Clamp(durationSeconds, 0f, MaxGeneratedDurationSeconds)
            : 0f;
        return Math.Max(1, (int)MathF.Ceiling(safeSampleRate * safeDuration));
    }

    private static short Quantize(float sample)
    {
        if (!float.IsFinite(sample))
        {
            return 0;
        }

        float scaled = sample * short.MaxValue;
        if (!float.IsFinite(scaled))
        {
            return sample > 0f ? short.MaxValue : short.MinValue;
        }

        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    private static void WriteInt16(byte[] buffer, int index, short value)
    {
        buffer[index] = (byte)(value & 0xFF);
        buffer[index + 1] = (byte)((value >> 8) & 0xFF);
    }
}

internal readonly struct ToneEnvelope
{
    private const float MinPortion = 0.0001f;

    public ToneEnvelope(float attackFraction, float releaseFraction, bool applyHannWindow)
    {
        AttackFraction = SanitizeFraction(attackFraction);
        ReleaseFraction = SanitizeFraction(releaseFraction);
        ApplyHannWindow = applyHannWindow;
    }

    public float AttackFraction { get; }
    public float ReleaseFraction { get; }
    public bool ApplyHannWindow { get; }

    public float Evaluate(float normalizedIndex)
    {
        if (!float.IsFinite(normalizedIndex))
        {
            return 0f;
        }

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

        return float.IsFinite(envelope) ? envelope : 0f;
    }

    private static float SanitizeFraction(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}
