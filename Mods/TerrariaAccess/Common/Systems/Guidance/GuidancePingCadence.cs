#nullable enable
using System;

namespace TerrariaAccess.Common.Systems.Guidance;

internal static class GuidancePingCadence
{
    private const float DistanceCueDelayMultiplier = 1.35f;

    public static int ComputeDistanceDelayFrames(
        float distanceTiles,
        float arrivalThresholdTiles,
        int minDelayFrames,
        int maxDelayFrames,
        float maxDistanceTiles)
    {
        int safeMin = Math.Max(1, minDelayFrames);
        int safeMax = Math.Max(safeMin, maxDelayFrames);
        if (!float.IsFinite(distanceTiles))
        {
            return ApplyDistanceCueRateReduction(safeMax);
        }

        if (distanceTiles <= arrivalThresholdTiles)
        {
            return -1;
        }

        float range = Math.Max(1f, maxDistanceTiles - arrivalThresholdTiles);
        float normalized = Math.Clamp((distanceTiles - arrivalThresholdTiles) / range, 0f, 1f);
        float frames = safeMin + ((safeMax - safeMin) * normalized);
        return ApplyDistanceCueRateReduction(Math.Max(1, (int)MathF.Round(frames)));
    }

    public static int ApplyDistanceCueRateReduction(int delayFrames)
    {
        int safeDelay = Math.Max(1, delayFrames);
        float slowedDelay = safeDelay * DistanceCueDelayMultiplier;
        return Math.Max(safeDelay, (int)MathF.Ceiling(slowedDelay));
    }

    public static float ComputeDistanceVolumeScale(
        float distanceTiles,
        float arrivalThresholdTiles,
        float maxDistanceTiles,
        float minVolumeScale)
    {
        float safeMinVolume = float.IsFinite(minVolumeScale)
            ? Math.Clamp(minVolumeScale, 0f, 1f)
            : 0f;

        if (!float.IsFinite(distanceTiles))
        {
            return safeMinVolume;
        }

        if (distanceTiles <= arrivalThresholdTiles)
        {
            return 1f;
        }

        float range = Math.Max(1f, maxDistanceTiles - arrivalThresholdTiles);
        float normalized = Math.Clamp((distanceTiles - arrivalThresholdTiles) / range, 0f, 1f);
        return 1f - ((1f - safeMinVolume) * normalized);
    }
}
