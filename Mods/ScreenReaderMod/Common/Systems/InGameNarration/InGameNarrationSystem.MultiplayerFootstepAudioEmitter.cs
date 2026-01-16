#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Emits spatialized footstep sounds for other players in multiplayer.
    /// Tracks each remote player's movement and plays tones with pan/pitch
    /// based on their position relative to the local player.
    /// </summary>
    private sealed class MultiplayerFootstepAudioEmitter
    {
        private const float MinLandingDisplacement = 6f;
        private const float PitchScalePixels = 320f;
        private const float PanScalePixels = 320f;
        private const float BaseVolume = 0.45f;

        private readonly Dictionary<int, PlayerFootstepState> _playerStates = new();

        private sealed class PlayerFootstepState
        {
            public Point LastFootTile = new(-1, -1);
            public float LastFootX = float.NaN;
            public bool SuppressNextStep = true;
            public bool WasAirborne;
            public float AirborneStartY;
            public float MaxAirborneDisplacement;
        }

        public void Update(Player localPlayer)
        {
            if (localPlayer is null || !localPlayer.active)
            {
                Reset();
                return;
            }

            bool enabled = ScreenReaderModConfig.Instance?.MultiplayerFootstepsEnabled ?? true;
            if (!enabled)
            {
                Reset();
                return;
            }

            // Track which players are still active this frame
            HashSet<int> activePlayers = new();

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                // Skip the local player
                if (i == Main.myPlayer)
                {
                    continue;
                }

                Player player = Main.player[i];
                if (!CanProcess(player))
                {
                    continue;
                }

                activePlayers.Add(i);
                UpdatePlayerFootsteps(localPlayer, player, i);
            }

            // Clean up states for players who left
            List<int>? toRemove = null;
            foreach (int playerId in _playerStates.Keys)
            {
                if (!activePlayers.Contains(playerId))
                {
                    toRemove ??= new List<int>();
                    toRemove.Add(playerId);
                }
            }

            if (toRemove is not null)
            {
                foreach (int playerId in toRemove)
                {
                    _playerStates.Remove(playerId);
                }
            }
        }

        public void Reset()
        {
            _playerStates.Clear();
        }

        private void UpdatePlayerFootsteps(Player localPlayer, Player remotePlayer, int playerId)
        {
            if (!_playerStates.TryGetValue(playerId, out PlayerFootstepState? state))
            {
                state = new PlayerFootstepState();
                _playerStates[playerId] = state;
            }

            bool grounded = IsGrounded(remotePlayer);
            if (!grounded)
            {
                TrackAirborneDisplacement(remotePlayer, state);
                state.LastFootX = float.NaN;
                return;
            }

            bool landingStep = ConsumeLandingStep(state);
            Point footTile = GetFootTile(remotePlayer, out float footX);
            bool crossedTileCenter = HasCrossedTileCenter(footTile.X, footX, state);

            if (!landingStep && !crossedTileCenter)
            {
                state.LastFootX = footX;
                return;
            }

            if (state.SuppressNextStep)
            {
                state.SuppressNextStep = false;
                state.LastFootTile = footTile;
                state.LastFootX = footX;
                return;
            }

            state.LastFootTile = footTile;
            state.LastFootX = footX;

            bool onPlatform = IsPlatform(footTile.X, footTile.Y);
            PlaySpatialStep(localPlayer, remotePlayer, onPlatform);
        }

        private static bool CanProcess(Player player)
        {
            if (player is null || !player.active || player.dead || player.ghost)
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

        private static bool HasCrossedTileCenter(int tileX, float footX, PlayerFootstepState state)
        {
            if (float.IsNaN(state.LastFootX) || state.LastFootX == footX)
            {
                return false;
            }

            float tileCenterX = tileX * 16f + 8f;
            return (state.LastFootX < tileCenterX && footX >= tileCenterX) ||
                   (state.LastFootX > tileCenterX && footX <= tileCenterX);
        }

        private static Point GetFootTile(Player player, out float footX)
        {
            Rectangle hitbox = player.Hitbox;
            footX = hitbox.Center.X;
            int tileX = Math.Clamp((int)(footX / 16f), 0, Main.maxTilesX - 1);
            int tileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
            return new Point(tileX, tileY);
        }

        private static void TrackAirborneDisplacement(Player player, PlayerFootstepState state)
        {
            float currentBottom = player.Bottom.Y;
            if (!state.WasAirborne)
            {
                state.WasAirborne = true;
                state.AirborneStartY = currentBottom;
                state.MaxAirborneDisplacement = 0f;
                state.LastFootTile = new Point(-1, -1);
                return;
            }

            float displacement = Math.Abs(currentBottom - state.AirborneStartY);
            if (displacement > state.MaxAirborneDisplacement)
            {
                state.MaxAirborneDisplacement = displacement;
            }
        }

        private static bool ConsumeLandingStep(PlayerFootstepState state)
        {
            if (!state.WasAirborne)
            {
                return false;
            }

            state.WasAirborne = false;
            bool shouldPlay = state.MaxAirborneDisplacement >= MinLandingDisplacement;
            state.MaxAirborneDisplacement = 0f;
            return shouldPlay;
        }

        private static bool IsPlatform(int tileX, int tileY)
        {
            Tile tile = Framing.GetTileSafely(tileX, tileY);
            return tile.HasTile && TileID.Sets.Platforms[tile.TileType];
        }

        private static void PlaySpatialStep(Player localPlayer, Player remotePlayer, bool onPlatform)
        {
            // Calculate spatial audio properties
            SpatialAudioPanner.SpatialDirection direction = SpatialAudioPanner.ComputeDirection(
                localPlayer.Center,
                remotePlayer.Center,
                PitchScalePixels,
                PanScalePixels);

            float volume = BaseVolume;

            // Compute frequency based on remote player's speed (same logic as local footsteps)
            float horizontalSpeed = Math.Abs(remotePlayer.velocity.X);
            float normalized = MathHelper.Clamp(horizontalSpeed / 6f, 0f, 1f);

            // Apply pitch shift based on vertical position relative to local player
            // Higher pitch = above, lower pitch = below
            float baseFrequency = onPlatform
                ? MathHelper.Lerp(360f, 430f, normalized)
                : MathHelper.Lerp(190f, 220f, normalized);

            // Apply vertical pitch shift (direction.Pitch is positive when target is above)
            float pitchMultiplier = 1f + (direction.Pitch * 0.3f);
            float frequency = baseFrequency * pitchMultiplier;

            FootstepToneProvider.Play(frequency, volume, useTriangleWave: false, direction.Pan);
        }
    }
}
