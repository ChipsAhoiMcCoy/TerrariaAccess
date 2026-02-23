#nullable enable
using System;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;
using static ScreenReaderMod.Common.Systems.InGameNarrationSystem;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// Detects when the player is falling and provides spatialized audio feedback.
/// Emits one tone per tile crossed, positioned at the ground below the player
/// so that pitch naturally lowers when far from ground and rises when approaching it.
/// Also provides a fall damage warning past the 25-tile threshold.
/// </summary>
internal sealed class FallDetectionEmitter : AudioEmitterBase
{
    private enum FallState { Grounded, Debouncing, Falling }

    // Thresholds
    private const float FallingVelocityThreshold = 2.0f;
    private const float GroundedVelocityThreshold = 0.02f;
    private const int DebounceFrames = 5;
    private const int MaxGroundScanTiles = 200;
    private const int GroundScanInterval = 3;
    private const int ProximityToneStartTiles = 30; // Only play tones when ground is within this range
    private const float FallDamageTileThreshold = 25f;

    // Spatial tone parameters — matches local footstep frequencies exactly
    private const float GroundFrequencyMin = 190f;
    private const float GroundFrequencyMax = 220f;
    private const float PitchShiftFactor = 0.3f; // Same factor as multiplayer footsteps

    // Fall damage warning parameters
    private const float DamageWarningBeepFrequency = 880f;
    private const int DamageWarningBeepInterval = 20;

    // Volume — matches local footstep base volume
    private const float BaseVolume = 0.45f;
    private const float DamageWarningBeepVolume = 0.5f;

    // State
    private FallState _state = FallState.Grounded;
    private int _debounceCounter;
    private float _fallStartY;
    private int _groundScanTimer;
    private int _cachedGroundDistance = MaxGroundScanTiles;
    private int _lastFootTileY = -1;
    private int _damageWarningBeepTimer;
    private bool _damageWarningAnnounced;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            ResetToGrounded();
            return;
        }

        bool enabled = ScreenReaderModConfig.Instance?.FallDetectionEnabled ?? true;
        if (!enabled)
        {
            ResetToGrounded();
            return;
        }

        // Check suppression conditions that immediately cancel fall detection
        if (ShouldSuppress(player))
        {
            ResetToGrounded();
            return;
        }

        float gravDir = player.gravDir;
        float directionalVelocity = player.velocity.Y * gravDir;

        switch (_state)
        {
            case FallState.Grounded:
                if (directionalVelocity > FallingVelocityThreshold)
                {
                    _state = FallState.Debouncing;
                    _debounceCounter = 1;
                }
                break;

            case FallState.Debouncing:
                if (directionalVelocity > FallingVelocityThreshold)
                {
                    _debounceCounter++;
                    if (_debounceCounter >= DebounceFrames)
                    {
                        EnterFallingState(player);
                    }
                }
                else
                {
                    _state = FallState.Grounded;
                    _debounceCounter = 0;
                }
                break;

            case FallState.Falling:
                if (IsGrounded(player))
                {
                    ResetToGrounded();
                    return;
                }

                UpdateFalling(player);
                break;
        }
    }

    public override void Reset()
    {
        ResetToGrounded();
    }

    public static void DisposeStaticResources()
    {
        // No static sound effects to dispose; we use FootstepToneProvider's cache
    }

    private static bool ShouldSuppress(Player player)
    {
        if (player.mount.Active) return true;
        if (player.pulley) return true;
        if (player.jump > 0) return true;
        if (player.wet) return true;
        if (player.grappling[0] >= 0) return true;
        return false;
    }

    private static bool IsGrounded(Player player)
    {
        return Math.Abs(player.velocity.Y) < GroundedVelocityThreshold;
    }

    private void EnterFallingState(Player player)
    {
        _state = FallState.Falling;
        _fallStartY = player.Bottom.Y;
        _cachedGroundDistance = MaxGroundScanTiles;
        _groundScanTimer = 0;
        _lastFootTileY = GetFootTileY(player);
        _damageWarningBeepTimer = 0;
        _damageWarningAnnounced = false;
    }

    private void UpdateFalling(Player player)
    {
        float configVolume = ScreenReaderModConfig.Instance?.FallDetectionVolume ?? 1f;
        if (configVolume <= 0f)
        {
            return;
        }

        // Scan for ground distance periodically
        _groundScanTimer++;
        if (_groundScanTimer >= GroundScanInterval)
        {
            _groundScanTimer = 0;
            _cachedGroundDistance = ScanGroundDistance(player);
        }

        // Check if we crossed into a new tile row
        int currentTileY = GetFootTileY(player);
        bool crossedTile = currentTileY != _lastFootTileY && _lastFootTileY >= 0;
        _lastFootTileY = currentTileY;

        if (crossedTile && _cachedGroundDistance <= ProximityToneStartTiles)
        {
            PlaySpatialFallTone(player, configVolume);
        }

        // Check fall damage warning
        UpdateFallDamageWarning(player, configVolume);
    }

    private void PlaySpatialFallTone(Player player, float configVolume)
    {
        // Position the sound at the ground below (or above in inverted gravity) the player
        Vector2 playerCenter = player.Center;
        float groundWorldY = player.gravDir >= 0
            ? player.Bottom.Y + _cachedGroundDistance * 16f
            : player.Top.Y - _cachedGroundDistance * 16f;
        Vector2 groundPos = new(playerCenter.X, groundWorldY);

        // Compute spatial parameters: pitch encodes vertical distance, volume fades with range
        SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(playerCenter, groundPos, BaseVolume);

        // Use the same base frequency range as local footsteps (190-220 Hz ground tones).
        // Fall speed maps to the same range that horizontal walk speed uses for regular steps.
        float fallSpeed = Math.Abs(player.velocity.Y);
        float normalized = MathHelper.Clamp(fallSpeed / 6f, 0f, 1f);
        float baseFrequency = MathHelper.Lerp(GroundFrequencyMin, GroundFrequencyMax, normalized);

        // Apply vertical pitch shift identical to multiplayer footsteps
        float frequency = baseFrequency * (1f + sample.Pitch * PitchShiftFactor);
        frequency = Math.Max(frequency, 80f);

        // Sine wave (useTriangleWave: false) to match normal footstep sound
        FootstepToneProvider.Play(frequency, sample.Volume * configVolume, useTriangleWave: false, sample.Pan);
    }

    private void UpdateFallDamageWarning(Player player, float configVolume)
    {
        // Don't warn if player has fall damage immunity or slow fall
        if (player.noFallDmg || player.slowFall)
        {
            return;
        }

        float fallDistance = Math.Abs(player.Bottom.Y - _fallStartY) / 16f;
        if (fallDistance < FallDamageTileThreshold)
        {
            return;
        }

        // One-shot speech announcement
        if (!_damageWarningAnnounced)
        {
            _damageWarningAnnounced = true;
            ScreenReaderService.Announce("Fall damage warning", force: true);
        }

        // Periodic warning beeps
        _damageWarningBeepTimer++;
        if (_damageWarningBeepTimer >= DamageWarningBeepInterval)
        {
            _damageWarningBeepTimer = 0;
            float volume = DamageWarningBeepVolume * configVolume;
            FootstepToneProvider.Play(DamageWarningBeepFrequency, volume, useTriangleWave: false);
        }
    }

    private static int ScanGroundDistance(Player player)
    {
        float gravDir = player.gravDir;
        int scanDirection = gravDir >= 0 ? 1 : -1; // 1 = downward (normal), -1 = upward (inverted)

        // Get feet position in tile coordinates using the player's actual hitbox,
        // so we only scan columns the player occupies (not adjacent wall tiles).
        Rectangle hitbox = player.Hitbox;
        int footTileY;
        if (gravDir >= 0)
        {
            footTileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
        }
        else
        {
            footTileY = Math.Clamp(hitbox.Top / 16, 0, Main.maxTilesY - 1);
        }

        int leftTileX = Math.Clamp(hitbox.Left / 16, 0, Main.maxTilesX - 1);
        int rightTileX = Math.Clamp((hitbox.Right - 1) / 16, 0, Main.maxTilesX - 1);

        int bestDistance = MaxGroundScanTiles;
        for (int x = leftTileX; x <= rightTileX; x++)
        {
            bestDistance = Math.Min(bestDistance, ScanColumn(x, footTileY, scanDirection));
        }

        return bestDistance;
    }

    private static int ScanColumn(int tileX, int startTileY, int direction)
    {
        for (int i = 0; i < MaxGroundScanTiles; i++)
        {
            int y = startTileY + i * direction;
            if (y < 0 || y >= Main.maxTilesY)
            {
                return MaxGroundScanTiles;
            }

            Tile tile = Framing.GetTileSafely(tileX, y);
            if (tile.HasTile && !tile.IsActuated &&
                (Main.tileSolid[tile.TileType] || TileID.Sets.Platforms[tile.TileType]))
            {
                return i;
            }
        }

        return MaxGroundScanTiles;
    }

    private static int GetFootTileY(Player player)
    {
        float footY = player.gravDir >= 0 ? player.Bottom.Y : player.Top.Y;
        return Math.Clamp((int)(footY / 16f), 0, Main.maxTilesY - 1);
    }

    private void ResetToGrounded()
    {
        _state = FallState.Grounded;
        _debounceCounter = 0;
        _groundScanTimer = 0;
        _cachedGroundDistance = MaxGroundScanTiles;
        _lastFootTileY = -1;
        _damageWarningBeepTimer = 0;
        _damageWarningAnnounced = false;
    }
}
