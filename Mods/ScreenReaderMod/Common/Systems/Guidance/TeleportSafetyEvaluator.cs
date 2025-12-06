#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace ScreenReaderMod.Common.Systems.Guidance;

/// <summary>
/// Evaluates teleport destinations near a target point and reports why a search failed.
/// </summary>
internal sealed class TeleportSafetyEvaluator
{
    private readonly int _searchRadiusTiles;
    private readonly int _verticalSearchTiles;

    public TeleportSafetyEvaluator(int searchRadiusTiles, int verticalSearchTiles)
    {
        _searchRadiusTiles = searchRadiusTiles;
        _verticalSearchTiles = verticalSearchTiles;
    }

    public bool TryFindSafeDestination(Player player, Vector2 targetCenter, out Vector2 destination, out string failureReason)
    {
        int width = player.width;
        int height = player.height;
        Vector2 baseTopLeft = targetCenter - new Vector2(width * 0.5f, height);

        int outOfBounds = 0;
        int blocked = 0;
        int candidates = 0;

        for (int radius = 0; radius <= _searchRadiusTiles; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    Vector2 candidateBase = baseTopLeft + new Vector2(dx * 16f, dy * 16f);
                    if (!IsWithinWorld(candidateBase, width, height))
                    {
                        outOfBounds++;
                        continue;
                    }

                    foreach (int vertical in EnumerateVerticalOffsets())
                    {
                        Vector2 candidate = candidateBase + new Vector2(0f, vertical * 16f);
                        if (!IsWithinWorld(candidate, width, height))
                        {
                            outOfBounds++;
                            continue;
                        }

                        candidates++;
                        if (Collision.SolidCollision(candidate, width, height))
                        {
                            blocked++;
                            continue;
                        }

                        destination = candidate;
                        failureReason = string.Empty;
                        return true;
                    }
                }
            }
        }

        destination = default;

        if (candidates == 0 && outOfBounds > 0)
        {
            failureReason = "Target is too close to the world edge.";
            return false;
        }

        failureReason = blocked > 0 ? "All nearby positions are blocked." : "No valid teleport locations were found.";
        return false;
    }

    private IEnumerable<int> EnumerateVerticalOffsets()
    {
        yield return 0;

        for (int step = 1; step <= _verticalSearchTiles; step++)
        {
            yield return step;   // prefer landing slightly below the target if there's space (ground bias)
            yield return -step;  // then search upward
        }
    }

    private static bool IsWithinWorld(Vector2 topLeft, int width, int height)
    {
        float minX = 16f;
        float minY = 16f;
        float maxX = (Main.maxTilesX - 2) * 16f - width;
        float maxY = (Main.maxTilesY - 2) * 16f - height;

        return topLeft.X >= minX &&
               topLeft.X <= maxX &&
               topLeft.Y >= minY &&
               topLeft.Y <= maxY;
    }
}
