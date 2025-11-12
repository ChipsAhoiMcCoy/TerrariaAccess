#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class FootstepAudioEmitter
    {
        private const float MinSpeed = 0.35f;
        private const int MinFramesBetweenNotes = 3;

        private long _nextAllowedFrame;
        private Point _lastTile = new(-1, -1);

        public void Update(Player player)
        {
            bool grounded = IsGrounded(player);
            if (!ShouldEmit(player, grounded))
            {
                ResetTracking();
                return;
            }

            float speed = player.velocity.Length();
            if (speed < MinSpeed)
            {
                return;
            }

            Point tile = GetPlayerTile(player);
            if (tile == _lastTile)
            {
                return;
            }

            long currentFrame = Main.GameUpdateCount;
            if (currentFrame < _nextAllowedFrame)
            {
                return;
            }

            _lastTile = tile;
            _nextAllowedFrame = currentFrame + MinFramesBetweenNotes;

            float normalized = MathHelper.Clamp(speed / 10f, 0f, 1f);
            float frequency = MathHelper.Lerp(185f, 215f, normalized);
            float baseVolume = MathHelper.Lerp(0.25f, 0.55f, normalized);
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
            FootstepToneProvider.Play(frequency, loudness);
        }

        public void Reset()
        {
            ResetTracking();
        }

        private static bool ShouldEmit(Player player, bool grounded)
        {
            if (!player.active || player.dead || player.ghost)
            {
                return false;
            }

            if (player.mount.Active || player.pulley)
            {
                return false;
            }

            if (!grounded)
            {
                return false;
            }

            return true;
        }

        private static bool IsGrounded(Player player)
        {
            return Math.Abs(player.velocity.Y) < 0.02f;
        }

        private static Point GetPlayerTile(Player player)
        {
            Vector2 center = player.Center;
            int tileX = Math.Clamp((int)(center.X / 16f), 0, Main.maxTilesX - 1);
            int tileY = Math.Clamp((int)(center.Y / 16f), 0, Main.maxTilesY - 1);
            return new Point(tileX, tileY);
        }

        private void ResetTracking()
        {
            _lastTile = new Point(-1, -1);
            _nextAllowedFrame = 0;
        }
    }
}
