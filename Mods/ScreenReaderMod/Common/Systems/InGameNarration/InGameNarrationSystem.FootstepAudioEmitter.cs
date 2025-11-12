#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class FootstepAudioEmitter
    {
        private const float MinSpeed = 0.35f;
        private const int MinFramesBetweenNotes = 3;

        private long _nextAllowedFrame;
        private int _lastFootTile = -1;
        private bool _pendingLandingStep;

        public void Update(Player player)
        {
            if (!CanProcess(player))
            {
                _pendingLandingStep = false;
                ResetTracking();
                return;
            }

            bool grounded = IsGrounded(player);
            if (!grounded)
            {
                if (!_pendingLandingStep)
                {
                    _pendingLandingStep = true;
                }

                _nextAllowedFrame = 0;
                return;
            }

            int footTile = GetFootTileColumn(player);
            bool landingStep = _pendingLandingStep;
            _pendingLandingStep = false;

            float speed = player.velocity.Length();
            if (speed < MinSpeed && !landingStep)
            {
                return;
            }

            if (!landingStep && footTile == _lastFootTile)
            {
                return;
            }

            long currentFrame = Main.GameUpdateCount;
            if (!landingStep && currentFrame < _nextAllowedFrame)
            {
                return;
            }

            _lastFootTile = footTile;
            _nextAllowedFrame = currentFrame + MinFramesBetweenNotes;

            float normalized = MathHelper.Clamp(speed / 10f, 0f, 1f);
            bool onPlatform = IsStandingOnPlatform(player);
            float frequency = onPlatform
                ? MathHelper.Lerp(360f, 420f, normalized)
                : MathHelper.Lerp(190f, 210f, normalized);
            float baseVolume = MathHelper.Lerp(0.12f, 0.32f, normalized);
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
            FootstepToneProvider.Play(frequency, loudness);
        }

        public void Reset()
        {
            _pendingLandingStep = false;
            ResetTracking();
        }

        private static bool CanProcess(Player player)
        {
            if (!player.active || player.dead || player.ghost)
            {
                return false;
            }

            if (player.mount.Active || player.pulley)
            {
                return false;
            }

            return true;
        }

        private static bool IsGrounded(Player player)
        {
            return Math.Abs(player.velocity.Y) < 0.02f;
        }

        private static int GetFootTileColumn(Player player)
        {
            float footX;
            float horizontalSpeed = player.velocity.X;
            if (Math.Abs(horizontalSpeed) < 0.05f)
            {
                footX = player.Bottom.X;
            }
            else if (horizontalSpeed > 0f)
            {
                footX = player.BottomRight.X - 1f;
            }
            else
            {
                footX = player.BottomLeft.X + 1f;
            }

            return Math.Clamp((int)(footX / 16f), 0, Main.maxTilesX - 1);
        }

        private void ResetTracking()
        {
            _lastFootTile = -1;
            _nextAllowedFrame = 0;
        }

        private static bool IsStandingOnPlatform(Player player)
        {
            Rectangle hitbox = player.Hitbox;
            int tileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
            int startX = Math.Clamp(hitbox.Left / 16 - 1, 0, Main.maxTilesX - 1);
            int endX = Math.Clamp(hitbox.Right / 16 + 1, 0, Main.maxTilesX - 1);

            for (int x = startX; x <= endX; x++)
            {
                Tile tile = Framing.GetTileSafely(x, tileY);
                if (!tile.HasTile)
                {
                    continue;
                }

                if (TileID.Sets.Platforms[tile.TileType])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
