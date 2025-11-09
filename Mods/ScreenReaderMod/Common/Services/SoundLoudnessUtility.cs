#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace ScreenReaderMod.Common.Services;

internal static class SoundLoudnessUtility
{
    public static float ApplyDistanceFalloff(
        float baseVolume,
        float distanceTiles,
        float referenceTiles,
        float minFactor = 0.3f,
        float exponent = 1.1f)
    {
        if (baseVolume <= 0f)
        {
            return 0f;
        }

        float factor = ComputeAttenuation(distanceTiles, referenceTiles, minFactor, exponent);
        return baseVolume * factor;
    }

    public static float ComputeAttenuation(
        float distanceTiles,
        float referenceTiles,
        float minFactor = 0.3f,
        float exponent = 1.1f)
    {
        if (referenceTiles <= 0f)
        {
            return 1f;
        }

        float normalized = Math.Clamp(distanceTiles / referenceTiles, 0f, 1f);
        float shaped = MathF.Pow(1f - normalized, Math.Max(0.01f, exponent));
        float clampedMin = MathHelper.Clamp(minFactor, 0f, 1f);
        return MathHelper.Lerp(clampedMin, 1f, shaped);
    }
}
