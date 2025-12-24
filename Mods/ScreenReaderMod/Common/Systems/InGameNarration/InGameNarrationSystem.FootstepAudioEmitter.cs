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

        // Edge echo configuration
        private const int EdgeScanRangeTiles = 18;      // How far ahead to scan
        private const int MinDropHeightTiles = 3;       // Minimum drop to count as edge
        private const int EchoDelayFrames = 3;          // Frames between footstep and echo
        private const float EchoPanScalePixels = 320f;  // Pan scaling for positional audio
        private const float EchoPitchMultiplier = 1.0f; // Echo same pitch as footstep

        private Point _lastFootTile = new(-1, -1);
        private float _lastFootX = float.NaN;
        private bool _suppressNextStep = true;
        private bool _wasAirborne;
        private float _airborneStartY;
        private float _maxAirborneDisplacement;
        private SoundEffectInstance? _harmfulLoopInstance;
        private float _lastHarmfulFrequency;

        // Edge echo state
        private int _pendingEchoFrame = -1;             // Frame to play echo (-1 = none)
        private Vector2 _pendingEchoPosition;           // World position of detected edge
        private bool _pendingEchoIsPlatform;            // Whether edge is platform or ground
        private int _pendingEchoDirection;              // Direction of pending echo (1 or -1)

        private readonly record struct EdgeScanResult(Vector2 WorldPosition, bool IsPlatform);

        public void Update(Player player)
        {
            if (!CanProcess(player))
            {
                ResetState();
                return;
            }

            // Check for pending edge echoes first
            TryPlayScheduledEcho(player);

            bool grounded = IsGrounded(player);
            if (!grounded)
            {
                TrackAirborneDisplacement(player);
                _lastFootX = float.NaN;
                StopHarmfulTone();
                ClearPendingEcho();
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

            // Scan for edges ahead and schedule echo if found
            int moveDirection = Math.Sign(player.velocity.X);
            if (moveDirection != 0)
            {
                EdgeScanResult? edge = ScanForEdge(player, moveDirection);
                if (edge.HasValue)
                {
                    ScheduleEdgeEcho(edge.Value, moveDirection);
                }
            }
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
            ClearPendingEcho();
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

        private static bool HasSupportingTile(int tileX, int tileY)
        {
            Tile tile = Framing.GetTileSafely(tileX, tileY);
            if (!tile.HasTile || tile.IsActuated)
            {
                return false;
            }

            return Main.tileSolid[tile.TileType] || TileID.Sets.Platforms[tile.TileType];
        }

        private static int MeasureDropDepth(int tileX, int startY)
        {
            int depth = 0;
            for (int y = startY; y < startY + 10 && y < Main.maxTilesY; y++)
            {
                if (HasSupportingTile(tileX, y))
                {
                    break;
                }

                depth++;
            }

            return depth;
        }

        private EdgeScanResult? ScanForEdge(Player player, int direction)
        {
            if (direction == 0)
            {
                return null;
            }

            Point footTile = GetFootTile(player, out _);
            int endX = footTile.X + direction * EdgeScanRangeTiles;

            for (int scanX = footTile.X + direction;
                 direction > 0 ? scanX <= endX : scanX >= endX;
                 scanX += direction)
            {
                if (scanX < 0 || scanX >= Main.maxTilesX)
                {
                    break;
                }

                if (!HasSupportingTile(scanX, footTile.Y))
                {
                    int dropDepth = MeasureDropDepth(scanX, footTile.Y);
                    if (dropDepth >= MinDropHeightTiles)
                    {
                        int edgeTileX = scanX - direction;
                        bool isPlatform = IsPlatform(edgeTileX, footTile.Y);
                        Vector2 edgePos = new(scanX * 16f + 8f, footTile.Y * 16f + 8f);
                        return new EdgeScanResult(edgePos, isPlatform);
                    }

                    break; // Shallow drop, stop scanning
                }
            }

            return null;
        }

        private void ScheduleEdgeEcho(EdgeScanResult edge, int direction)
        {
            // If an echo is already pending in this direction, don't reschedule
            // This prevents the echo from being pushed back indefinitely as the player walks
            if (_pendingEchoFrame >= 0 && _pendingEchoDirection == direction)
            {
                return;
            }

            _pendingEchoFrame = (int)Main.GameUpdateCount + EchoDelayFrames;
            _pendingEchoPosition = edge.WorldPosition;
            _pendingEchoIsPlatform = edge.IsPlatform;
            _pendingEchoDirection = direction;
        }

        private void ClearPendingEcho()
        {
            _pendingEchoFrame = -1;
        }

        private void TryPlayScheduledEcho(Player player)
        {
            if (_pendingEchoFrame < 0)
            {
                return;
            }

            if ((int)Main.GameUpdateCount < _pendingEchoFrame)
            {
                return;
            }

            Vector2 playerCenter = player.Center;
            float offsetX = _pendingEchoPosition.X - playerCenter.X;
            float pan = MathHelper.Clamp(offsetX / EchoPanScalePixels, -1f, 1f);

            ComputeStepAudio(player, _pendingEchoIsPlatform, out float frequency, out float loudness);
            float echoFrequency = frequency * EchoPitchMultiplier;

            FootstepToneProvider.Play(echoFrequency, loudness, useTriangleWave: false, pan: pan);
            _pendingEchoFrame = -1;
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
