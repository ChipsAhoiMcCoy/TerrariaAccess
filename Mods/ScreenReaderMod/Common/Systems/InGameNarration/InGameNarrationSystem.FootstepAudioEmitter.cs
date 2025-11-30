#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Emits a single tone whenever the player’s footprint crosses into a new tile or finishes a landing.
    /// Keeps light-weight state so narration stays in sync with movement without allocating per frame.
    /// </summary>
    private sealed class FootstepAudioEmitter
    {
        private const float MinLandingDisplacement = 6f;

        // Tracks the last tile column/row we acknowledged so each tile triggers at most one tone.
        private Point _lastFootTile = new(-1, -1);
        private bool _suppressNextStep = true;
        private bool _wasAirborne;
        private float _airborneStartY;
        private float _maxAirborneDisplacement;

        public void Update(Player player)
        {
            if (!CanProcess(player))
            {
                ResetState();
                return;
            }

            bool grounded = IsGrounded(player);
            if (!grounded)
            {
                TrackAirborneDisplacement(player);
                return;
            }

            bool landingStep = ConsumeLandingStep();
            Point footTile = GetFootTile(player);
            bool movedToNewTile = footTile != _lastFootTile;
            if (!landingStep && !movedToNewTile)
            {
                return;
            }

            if (_suppressNextStep)
            {
                _suppressNextStep = false;
                _lastFootTile = footTile;
                return;
            }

            _lastFootTile = footTile;
            bool onPlatform = IsPlatform(footTile.X, footTile.Y);
            PlayStep(player, onPlatform);
        }

        public void Reset()
        {
            ResetState();
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

        private static Point GetFootTile(Player player)
        {
            Rectangle hitbox = player.Hitbox;
            float footX = hitbox.Center.X;
            int tileX = Math.Clamp((int)(footX / 16f), 0, Main.maxTilesX - 1);
            int tileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
            return new Point(tileX, tileY);
        }

        private void PlayStep(Player player, bool onPlatform)
        {
            float horizontalSpeed = Math.Abs(player.velocity.X);
            float normalized = MathHelper.Clamp(horizontalSpeed / 6f, 0f, 1f);
            float frequency = onPlatform
                ? MathHelper.Lerp(360f, 430f, normalized)
                : MathHelper.Lerp(190f, 220f, normalized);
            float baseVolume = MathHelper.Lerp(0.18f, 0.35f, normalized);
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
            FootstepToneProvider.Play(frequency, loudness);
        }

        private void ResetState()
        {
            _suppressNextStep = true;
            _lastFootTile = new Point(-1, -1);
            _wasAirborne = false;
            _airborneStartY = 0f;
            _maxAirborneDisplacement = 0f;
        }

        private void TrackAirborneDisplacement(Player player)
        {
            float currentBottom = player.Bottom.Y;
            if (!_wasAirborne)
            {
                _wasAirborne = true;
                _airborneStartY = currentBottom;
                _maxAirborneDisplacement = 0f;
                _lastFootTile = new Point(-1, -1);
                return;
            }

            float displacement = Math.Abs(currentBottom - _airborneStartY);
            if (displacement > _maxAirborneDisplacement)
            {
                _maxAirborneDisplacement = displacement;
            }
        }

        private bool ConsumeLandingStep()
        {
            if (!_wasAirborne)
            {
                return false;
            }

            _wasAirborne = false;
            bool shouldPlay = _maxAirborneDisplacement >= MinLandingDisplacement;
            _maxAirborneDisplacement = 0f;
            return shouldPlay;
        }

        private static bool IsPlatform(int tileX, int tileY)
        {
            Tile tile = Framing.GetTileSafely(tileX, tileY);
            return tile.HasTile && TileID.Sets.Platforms[tile.TileType];
        }
    }
}
