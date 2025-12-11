#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.ID;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Emits a single tone whenever the player's footprint crosses into a new tile or finishes a landing.
    /// Keeps light-weight state so narration stays in sync with movement without allocating per frame.
    /// </summary>
    private sealed class FootstepAudioEmitter
    {
        private const float MinLandingDisplacement = 6f;

        private Point _lastFootTile = new(-1, -1);
        private float _lastFootX = float.NaN;
        private bool _suppressNextStep = true;
        private bool _wasAirborne;
        private float _airborneStartY;
        private float _maxAirborneDisplacement;
        private SoundEffectInstance? _harmfulLoopInstance;
        private float _lastHarmfulFrequency;

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
                _lastFootX = float.NaN;
                StopHarmfulTone();
                return;
            }

            bool landingStep = ConsumeLandingStep();
            Point footTile = GetFootTile(player, out float footX);
            bool crossedTileCenter = HasCrossedTileCenter(footTile.X, footX);
            if (!landingStep && !crossedTileCenter)
            {
                _lastFootX = footX;
                UpdateHarmfulTone(player, footTile);
                return;
            }

            UpdateHarmfulTone(player, footTile);

            if (_suppressNextStep)
            {
                _suppressNextStep = false;
                _lastFootTile = footTile;
                _lastFootX = footX;
                return;
            }

            _lastFootTile = footTile;
            _lastFootX = footX;
            bool onPlatform = IsPlatform(footTile.X, footTile.Y);
            bool onHarmfulTile = IsHarmfulTile(player, footTile);
            PlayStep(player, onPlatform, onHarmfulTile);
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

        private bool HasCrossedTileCenter(int tileX, float footX)
        {
            if (float.IsNaN(_lastFootX) || _lastFootX == footX)
            {
                return false;
            }

            float tileCenterX = tileX * 16f + 8f;
            return (_lastFootX < tileCenterX && footX >= tileCenterX) ||
                   (_lastFootX > tileCenterX && footX <= tileCenterX);
        }

        private static Point GetFootTile(Player player, out float footX)
        {
            Rectangle hitbox = player.Hitbox;
            footX = hitbox.Center.X;
            int tileX = Math.Clamp((int)(footX / 16f), 0, Main.maxTilesX - 1);
            int tileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
            return new Point(tileX, tileY);
        }

        private void PlayStep(Player player, bool onPlatform, bool onHarmfulTile)
        {
            ComputeStepAudio(player, onPlatform, out float frequency, out float loudness);
            FootstepToneProvider.Play(frequency, loudness, useTriangleWave: onHarmfulTile);
        }

        private void ResetState()
        {
            _suppressNextStep = true;
            _lastFootTile = new Point(-1, -1);
            _lastFootX = float.NaN;
            _wasAirborne = false;
            _airborneStartY = 0f;
            _maxAirborneDisplacement = 0f;
            StopHarmfulTone();
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

        private static bool IsHarmfulTile(Player player, Point tileCoords)
        {
            Tile tile = Framing.GetTileSafely(tileCoords.X, tileCoords.Y);
            if (!tile.HasTile)
            {
                return false;
            }

            ushort type = tile.TileType;
            if (TileID.Sets.Falling[type])
            {
                return false;
            }

            if (TileID.Sets.TouchDamageBleeding[type] || TileID.Sets.Suffocate[type] || TileID.Sets.TouchDamageImmediate[type] > 0)
            {
                return true;
            }

            if (TileID.Sets.TouchDamageHot[type] && !player.fireWalk)
            {
                return true;
            }

            return false;
        }

        private static void ComputeStepAudio(Player player, bool onPlatform, out float frequency, out float loudness)
        {
            float horizontalSpeed = Math.Abs(player.velocity.X);
            float normalized = MathHelper.Clamp(horizontalSpeed / 6f, 0f, 1f);
            frequency = onPlatform
                ? MathHelper.Lerp(360f, 430f, normalized)
                : MathHelper.Lerp(190f, 220f, normalized);
            float baseVolume = MathHelper.Lerp(0.225f, 0.4375f, normalized);
            loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
        }

        private void UpdateHarmfulTone(Player player, Point tileCoords)
        {
            bool onHarmfulTile = IsHarmfulTile(player, tileCoords);
            if (!onHarmfulTile)
            {
                StopHarmfulTone();
                return;
            }

            ComputeHarmfulToneAudio(player, out float frequency, out float loudness);
            if (_harmfulLoopInstance is null || _harmfulLoopInstance.IsDisposed || _harmfulLoopInstance.State == SoundState.Stopped)
            {
                _harmfulLoopInstance = FootstepToneProvider.PlayLoopingTriangle(frequency, loudness);
                _lastHarmfulFrequency = frequency;
                return;
            }

            if (Math.Abs(frequency - _lastHarmfulFrequency) > 1f)
            {
                StopHarmfulTone();
                _harmfulLoopInstance = FootstepToneProvider.PlayLoopingTriangle(frequency, loudness);
                _lastHarmfulFrequency = frequency;
                return;
            }

            _harmfulLoopInstance.Volume = MathHelper.Clamp(loudness, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
        }

        private static void ComputeHarmfulToneAudio(Player player, out float frequency, out float loudness)
        {
            float verticalSpeed = Math.Abs(player.velocity.Y);
            float horizontalSpeed = Math.Abs(player.velocity.X);
            float normalized = MathHelper.Clamp((verticalSpeed + horizontalSpeed) / 8f, 0f, 1f);
            frequency = MathHelper.Lerp(520f, 640f, normalized);
            float baseVolume = MathHelper.Lerp(0.22f, 0.48f, normalized);
            loudness = SoundLoudnessUtility.ApplyDistanceFalloff(baseVolume, distanceTiles: 0f, referenceTiles: 1f);
        }

        private void StopHarmfulTone()
        {
            if (_harmfulLoopInstance is null)
            {
                return;
            }

            FootstepToneProvider.StopInstance(_harmfulLoopInstance);
            _harmfulLoopInstance = null;
        }
    }
}
