#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Emits a short tone while the player climbs ropes or chains, once per tile crossed.
    /// </summary>
    private sealed class ClimbAudioEmitter
    {
        private const int MaxTilesPerFrame = 6;

        private Point _lastRopeTile = new(-1, -1);
        private bool _wasOnRope;

        public void Update(Player player)
        {
            if (!CanProcess(player, out Point ropeTile))
            {
                ResetState();
                return;
            }

            if (!_wasOnRope || _lastRopeTile.X != ropeTile.X)
            {
                _wasOnRope = true;
                _lastRopeTile = ropeTile;
                return;
            }

            int deltaY = ropeTile.Y - _lastRopeTile.Y;
            if (deltaY == 0)
            {
                return;
            }

            int step = Math.Sign(deltaY);
            int steps = Math.Min(Math.Abs(deltaY), MaxTilesPerFrame);
            for (int i = 0; i < steps; i++)
            {
                int nextY = _lastRopeTile.Y + step;
                PlayClimbTone(player, movingUp: step < 0);
                _lastRopeTile = new Point(ropeTile.X, nextY);

                if (_lastRopeTile.Y == ropeTile.Y)
                {
                    break;
                }
            }

            if (_lastRopeTile.Y != ropeTile.Y)
            {
                _lastRopeTile = ropeTile;
            }
        }

        public void Reset()
        {
            ResetState();
        }

        private static bool CanProcess(Player player, out Point ropeTile)
        {
            ropeTile = default;
            if (!player.active || player.dead || player.ghost || player.mount.Active || !player.pulley)
            {
                return false;
            }

            ropeTile = GetRopeTile(player);
            return IsRopeTile(ropeTile);
        }

        private static Point GetRopeTile(Player player)
        {
            Vector2 center = player.Center;
            int x = Math.Clamp((int)(center.X / 16f), 0, Main.maxTilesX - 1);
            int y = Math.Clamp((int)(center.Y / 16f), 0, Main.maxTilesY - 1);
            return new Point(x, y);
        }

        private static bool IsRopeTile(Point tileCoords)
        {
            Tile tile = Framing.GetTileSafely(tileCoords.X, tileCoords.Y);
            return tile.HasTile && Main.tileRope[tile.TileType];
        }

        private static void PlayClimbTone(Player player, bool movingUp)
        {
            ComputeClimbAudio(player, movingUp, out float frequency, out float loudness);
            FootstepToneProvider.Play(frequency, loudness);
        }

        private static void ComputeClimbAudio(Player player, bool movingUp, out float frequency, out float loudness)
        {
            float verticalSpeed = Math.Abs(player.velocity.Y);
            float normalized = MathHelper.Clamp(verticalSpeed / 6f, 0f, 1f);
            frequency = movingUp
                ? MathHelper.Lerp(520f, 680f, normalized)
                : MathHelper.Lerp(420f, 560f, normalized);
            float baseVolume = MathHelper.Lerp(0.18f, 0.42f, normalized);
            loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
        }

        private void ResetState()
        {
            _wasOnRope = false;
            _lastRopeTile = new Point(-1, -1);
        }
    }
}
