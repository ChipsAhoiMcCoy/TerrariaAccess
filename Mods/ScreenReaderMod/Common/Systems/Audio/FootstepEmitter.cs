#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;
using static ScreenReaderMod.Common.Systems.InGameNarrationSystem;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// Emits a single tone whenever the player's footprint crosses into a new tile or finishes a landing.
/// Keeps light-weight state so narration stays in sync with movement without allocating per frame.
/// </summary>
internal sealed class FootstepEmitter : AudioEmitterBase
{
    private const float MinLandingDisplacement = 6f;

    // Edge detection configuration
    private const int EdgeScanRangeTiles = 18;      // How far ahead to scan
    private const int MinDropHeightTiles = 3;       // Minimum drop to count as edge

    private Point _lastFootTile = new(-1, -1);
    private float _lastFootX = float.NaN;
    private bool _suppressNextStep = true;
    private bool _wasAirborne;
    private float _airborneStartY;
    private float _maxAirborneDisplacement;
    private SoundEffectInstance? _harmfulLoopInstance;
    private float _lastHarmfulFrequency;

    // Edge beep state
    private const float EdgeBeepVolume = 0.35f;       // Volume for edge beeps
    private const float EdgeBeepFrequency = 3000f;    // High-pitched pure tone
    private const int EdgeBeepIntervalFrames = 6;     // Consistent beep rate (~10 beeps/sec)
    private int _edgeBeepTimer;

    // Edge static (white noise) state
    private const float EdgeStaticMaxVolume = 0.35f;  // Maximum volume when very close to edge
    private const float EdgeStaticMinVolume = 0.05f;  // Minimum volume when far from edge
    private SoundEffectInstance? _edgeStaticInstance;
    private float _lastEdgeStaticVolume;
    private float _lastEdgeStaticPan;

    private readonly record struct EdgeScanResult(Vector2 WorldPosition, bool IsPlatform, float DistancePixels);

    public override void Update(Player player)
    {
        if (!CanProcessMovementAudio(player))
        {
            ResetState();
            return;
        }

        EdgeDetectionMode edgeMode = ScreenReaderModConfig.Instance?.EdgeDetectionMode ?? EdgeDetectionMode.Static;

        // Handle edge detection based on mode
        switch (edgeMode)
        {
            case EdgeDetectionMode.Beeps:
                StopEdgeStatic();
                UpdateEdgeBeep(player);
                break;
            case EdgeDetectionMode.Static:
                ResetEdgeBeep();
                UpdateEdgeStatic(player);
                break;
            default:
                ResetEdgeBeep();
                StopEdgeStatic();
                break;
        }

        bool grounded = IsGrounded(player);
        if (!grounded)
        {
            TrackAirborneDisplacement(player);
            _lastFootX = float.NaN;
            StopHarmfulTone();
            ResetEdgeBeep();
            StopEdgeStatic();
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

    public override void Reset()
    {
        ResetState();
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
        float configVolume = ScreenReaderModConfig.Instance?.FootstepVolume ?? 1f;
        FootstepToneProvider.Play(frequency, loudness * configVolume, useTriangleWave: onHarmfulTile);
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
        ResetEdgeBeep();
        StopEdgeStatic();
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

    private static bool IsSolidWall(int tileX, int tileY)
    {
        Tile tile = Framing.GetTileSafely(tileX, tileY);
        if (!tile.HasTile || tile.IsActuated)
        {
            return false;
        }

        // A solid wall blocks horizontal movement (not platforms, not top-solid-only)
        return Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
    }

    private static bool IsPathBlockedAtBodyHeight(int tileX, int footTileY)
    {
        // Player is about 3 tiles tall; check the 2 tiles above foot level for walls
        // If there's a solid wall at body/head height, the player can't walk through
        return IsSolidWall(tileX, footTileY - 1) || IsSolidWall(tileX, footTileY - 2);
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

        Point footTile = GetFootTile(player, out float footX);
        int endX = footTile.X + direction * EdgeScanRangeTiles;

        for (int scanX = footTile.X + direction;
             direction > 0 ? scanX <= endX : scanX >= endX;
             scanX += direction)
        {
            if (scanX < 0 || scanX >= Main.maxTilesX)
            {
                break;
            }

            // Stop scanning if there's a wall blocking the path at body height
            // (e.g., house wall, building structure) - player can't reach edges beyond this
            if (IsPathBlockedAtBodyHeight(scanX, footTile.Y))
            {
                break;
            }

            if (!HasSupportingTile(scanX, footTile.Y))
            {
                // Check if gap is at least 2 tiles wide (player width)
                // A 1-tile gap can be walked over, so ignore it
                int nextX = scanX + direction;
                bool isWideGap = nextX >= 0 && nextX < Main.maxTilesX &&
                                 !HasSupportingTile(nextX, footTile.Y);

                if (!isWideGap)
                {
                    // 1-tile gap, skip and continue scanning beyond it
                    continue;
                }

                int dropDepth = MeasureDropDepth(scanX, footTile.Y);
                if (dropDepth >= MinDropHeightTiles)
                {
                    int edgeTileX = scanX - direction;
                    bool isPlatform = IsPlatform(edgeTileX, footTile.Y);
                    Vector2 edgePos = new(scanX * 16f + 8f, footTile.Y * 16f + 8f);
                    float distancePixels = Math.Abs(edgePos.X - footX);
                    return new EdgeScanResult(edgePos, isPlatform, distancePixels);
                }

                // Shallow drop - continue scanning for deeper edges ahead
            }
        }

        return null;
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
        float baseVolume = 0.45f;
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

        float configVolume = ScreenReaderModConfig.Instance?.FootstepVolume ?? 1f;
        _harmfulLoopInstance.Volume = MathHelper.Clamp(loudness * configVolume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
    }

    private static void ComputeHarmfulToneAudio(Player player, out float frequency, out float loudness)
    {
        float verticalSpeed = Math.Abs(player.velocity.Y);
        float horizontalSpeed = Math.Abs(player.velocity.X);
        float normalized = MathHelper.Clamp((verticalSpeed + horizontalSpeed) / 8f, 0f, 1f);
        frequency = MathHelper.Lerp(520f, 640f, normalized);
        float baseVolume = 0.22f;
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

    private void UpdateEdgeBeep(Player player)
    {
        if (!IsGrounded(player))
        {
            ResetEdgeBeep();
            return;
        }

        // Use control inputs for immediate responsiveness (velocity has acceleration lag)
        int moveDirection = player.controlRight ? 1 : player.controlLeft ? -1 : 0;
        if (moveDirection == 0)
        {
            // Not pressing movement keys - stop edge warning immediately
            ResetEdgeBeep();
            return;
        }

        // Scan only in movement direction
        EdgeScanResult? edge = ScanForEdge(player, moveDirection);

        if (!edge.HasValue)
        {
            ResetEdgeBeep();
            return;
        }

        EdgeScanResult nearestEdge = edge.Value;

        // Increment timer and check if it's time to beep
        _edgeBeepTimer++;
        if (_edgeBeepTimer >= EdgeBeepIntervalFrames)
        {
            _edgeBeepTimer = 0;

            // Compute spatial audio with distance-based volume falloff
            SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
                player.Center,
                nearestEdge.WorldPosition,
                EdgeBeepVolume);

            // Play a beep (pure sine wave) with spatial pan and volume
            float configVolume = ScreenReaderModConfig.Instance?.FootstepVolume ?? 1f;
            FootstepToneProvider.Play(EdgeBeepFrequency, sample.Volume * configVolume, useTriangleWave: false, sample.Pan);
        }
    }

    private void ResetEdgeBeep()
    {
        _edgeBeepTimer = 0;
    }

    private void UpdateEdgeStatic(Player player)
    {
        if (!IsGrounded(player))
        {
            StopEdgeStatic();
            return;
        }

        // Use control inputs for immediate responsiveness (velocity has acceleration lag)
        int moveDirection = player.controlRight ? 1 : player.controlLeft ? -1 : 0;
        if (moveDirection == 0)
        {
            // Not pressing movement keys - stop edge warning immediately
            StopEdgeStatic();
            return;
        }

        // Scan only in movement direction
        EdgeScanResult? edge = ScanForEdge(player, moveDirection);

        if (!edge.HasValue)
        {
            StopEdgeStatic();
            return;
        }

        EdgeScanResult nearestEdge = edge.Value;

        // Calculate volume based on distance - louder when closer
        float maxDistancePixels = EdgeScanRangeTiles * 16f;
        float normalizedDistance = MathHelper.Clamp(nearestEdge.DistancePixels / maxDistancePixels, 0f, 1f);
        // Invert so closer = louder, use exponential curve for more dramatic increase when close
        float volumeScale = 1f - normalizedDistance;
        volumeScale = volumeScale * volumeScale; // Quadratic curve for more dramatic near-edge effect
        float targetVolume = MathHelper.Lerp(EdgeStaticMinVolume, EdgeStaticMaxVolume, volumeScale);

        // Calculate pan based on edge position
        SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
            player.Center,
            nearestEdge.WorldPosition,
            targetVolume);

        float configVolume = ScreenReaderModConfig.Instance?.FootstepVolume ?? 1f;
        float finalVolume = sample.Volume * configVolume;

        // Start or update the white noise instance
        if (_edgeStaticInstance is null || _edgeStaticInstance.IsDisposed || _edgeStaticInstance.State == SoundState.Stopped)
        {
            _edgeStaticInstance = FootstepToneProvider.PlayLoopingWhiteNoise(finalVolume, sample.Pan);
            _lastEdgeStaticVolume = finalVolume;
            _lastEdgeStaticPan = sample.Pan;
        }
        else
        {
            // Update volume and pan if changed significantly
            if (Math.Abs(finalVolume - _lastEdgeStaticVolume) > 0.01f || Math.Abs(sample.Pan - _lastEdgeStaticPan) > 0.05f)
            {
                _edgeStaticInstance.Volume = MathHelper.Clamp(finalVolume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
                _edgeStaticInstance.Pan = MathHelper.Clamp(sample.Pan, -1f, 1f);
                _lastEdgeStaticVolume = finalVolume;
                _lastEdgeStaticPan = sample.Pan;
            }
        }
    }

    private void StopEdgeStatic()
    {
        if (_edgeStaticInstance is null)
        {
            return;
        }

        FootstepToneProvider.StopInstance(_edgeStaticInstance);
        _edgeStaticInstance = null;
        _lastEdgeStaticVolume = 0f;
        _lastEdgeStaticPan = 0f;
    }
}
