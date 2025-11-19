#nullable enable
using System;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Plays a metronome-style tick while the player is falling so they can anticipate upcoming landings.
    /// The tick interval shrinks as solid ground approaches.
    /// </summary>
    private sealed class FallProximityAudioEmitter
    {
        private const int MaxScanDistanceTiles = 140;
        private const float MinFallingSpeed = 1.4f;
        private const int MinIntervalFrames = 4;
        private const int MaxIntervalFrames = 120;
        private const float RampBeginDistanceTiles = 30f;

        private float _tickTimerFrames;

        public void Update(Player player)
        {
            if (!ShouldProcess(player))
            {
                Reset();
                return;
            }

            bool foundSurface = TryMeasureDistanceToSurface(player, out float distanceTiles);
            distanceTiles = foundSurface ? distanceTiles : MaxScanDistanceTiles;

            int intervalFrames = Math.Max(1, ComputeIntervalFrames(distanceTiles));
            if (_tickTimerFrames > intervalFrames)
            {
                _tickTimerFrames = intervalFrames;
            }

            _tickTimerFrames -= 1f;
            if (_tickTimerFrames > 0f)
            {
                return;
            }

            PlayTick(player, distanceTiles);
            _tickTimerFrames = intervalFrames;
        }

        public void Reset()
        {
            _tickTimerFrames = 0f;
        }

        private static bool ShouldProcess(Player player)
        {
            if (!player.active || player.dead || player.ghost)
            {
                return false;
            }

            if (player.mount.Active || player.pulley)
            {
                return false;
            }

            float gravityDir = MathF.Sign(player.gravDir);
            if (gravityDir == 0f)
            {
                gravityDir = 1f;
            }

            float fallingSpeed = player.velocity.Y * gravityDir;
            return fallingSpeed > MinFallingSpeed;
        }

        private static bool TryMeasureDistanceToSurface(Player player, out float distanceTiles)
        {
            Rectangle hitbox = player.Hitbox;
            float gravityDir = player.gravDir >= 0f ? 1f : -1f;
            float originWorldY = gravityDir > 0f ? hitbox.Bottom : hitbox.Top;
            int startTileY = Math.Clamp((int)(originWorldY / 16f), 0, Main.maxTilesY - 1);
            int tileLeft = Math.Clamp(hitbox.Left / 16 - 1, 0, Main.maxTilesX - 1);
            int tileRight = Math.Clamp(hitbox.Right / 16 + 1, 0, Main.maxTilesX - 1);

            for (int offset = 0; offset < MaxScanDistanceTiles; offset++)
            {
                int tileY = startTileY + (int)(gravityDir * offset);
                if (tileY < 0 || tileY >= Main.maxTilesY)
                {
                    break;
                }

                for (int tileX = tileLeft; tileX <= tileRight; tileX++)
                {
                    Tile tile = Framing.GetTileSafely(tileX, tileY);
                    if (!TryResolveSurfaceBoundary(tile, tileY, gravityDir, out float boundaryWorldY))
                    {
                        continue;
                    }

                    float distancePixels = gravityDir > 0f
                        ? boundaryWorldY - originWorldY
                        : originWorldY - boundaryWorldY;

                    if (distancePixels < 0f)
                    {
                        continue;
                    }

                    distanceTiles = distancePixels / 16f;
                    return true;
                }
            }

            distanceTiles = MaxScanDistanceTiles;
            return false;
        }

        private static int ComputeIntervalFrames(float distanceTiles)
        {
            if (distanceTiles >= RampBeginDistanceTiles)
            {
                return MaxIntervalFrames;
            }

            float clamped = MathHelper.Clamp(distanceTiles, 0f, RampBeginDistanceTiles);
            float normalized = 1f - (clamped / Math.Max(1f, RampBeginDistanceTiles));
            float eased = MathF.Pow(normalized, 1.8f);
            float interpolated = MathHelper.Lerp(MaxIntervalFrames, MinIntervalFrames, eased);
            return (int)MathF.Round(Math.Max(1f, interpolated));
        }

        private static void PlayTick(Player player, float distanceTiles)
        {
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(0.45f, distanceTiles: 0f, referenceTiles: 2f);
            FallIndicatorToneProvider.Play(loudness);
        }

        private static bool IsValidSurface(Tile tile)
        {
            if (!tile.HasTile || tile.IsActuated)
            {
                return false;
            }

            ushort type = tile.TileType;
            if (Main.tileSolid[type] && !Main.tileSolidTop[type])
            {
                return true;
            }

            if (Main.tileSolidTop[type] || TileID.Sets.Platforms[type])
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveSurfaceBoundary(Tile tile, int tileY, float gravityDir, out float boundaryWorldY)
        {
            if (IsValidSurface(tile))
            {
                boundaryWorldY = gravityDir > 0f ? tileY * 16f : (tileY + 1) * 16f;
                return true;
            }

            if (tile.LiquidAmount <= 0)
            {
                boundaryWorldY = 0f;
                return false;
            }

            float fillPixels = MathHelper.Clamp(tile.LiquidAmount / 255f * 16f, 0f, 16f);
            if (gravityDir > 0f)
            {
                boundaryWorldY = tileY * 16f + (16f - fillPixels);
                return true;
            }

            boundaryWorldY = tileY * 16f + fillPixels;
            return true;
        }
    }
}
