#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Detects sustained falls and reuses the edge-detection beep from the landing point
/// below the player's feet as the ground approaches.
/// </summary>
internal sealed class FallDetectionEmitter : AudioEmitterBase
{
    private enum FallState { Grounded, Debouncing, Falling }

    // Thresholds
    private const float FallingVelocityThreshold = 0.75f;
    private const float GroundedVelocityThreshold = 0.02f;
    private const int DebounceFrames = 8;
    private const int MinFallingFrames = 15;
    private const float MinFallenDistanceTiles = 8f;
    private const int MaxGroundScanTiles = 200;
    private const int GroundScanInterval = 3;
    private const int LandingWarningStartTiles = 15;

    // State
    private FallState _state = FallState.Grounded;
    private int _debounceCounter;
    private int _fallingFrames;
    private float _fallStartFootY;
    private int _groundScanTimer;
    private int _cachedGroundDistance = MaxGroundScanTiles;
    private int _landingBeepTimer = EdgeBeepCue.IntervalFrames - 1;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            ResetToGrounded();
            return;
        }

        bool enabled = TerrariaAccessConfig.Instance?.FallDetectionEnabled ?? true;
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
        _fallingFrames = 0;
        _fallStartFootY = GetFootWorldY(player);
        _cachedGroundDistance = ScanGroundDistance(player);
        _groundScanTimer = 0;
        _landingBeepTimer = EdgeBeepCue.IntervalFrames - 1;
    }

    private void UpdateFalling(Player player)
    {
        float configVolume = TerrariaAccessConfig.Instance?.FallDetectionVolume ?? 1f;
        if (configVolume <= 0f)
        {
            ResetLandingBeepCadence();
            return;
        }

        _fallingFrames++;

        // Scan for ground distance periodically
        _groundScanTimer++;
        if (_groundScanTimer >= GroundScanInterval)
        {
            _groundScanTimer = 0;
            _cachedGroundDistance = ScanGroundDistance(player);
        }

        if (_cachedGroundDistance > LandingWarningStartTiles || !HasFallenLongEnough(player))
        {
            ResetLandingBeepCadence();
            return;
        }

        if (EdgeBeepCue.TickCadence(ref _landingBeepTimer))
        {
            Vector2 landingPosition = GetLandingWarningPosition(player, _cachedGroundDistance);
            EdgeBeepCue.Play(player, landingPosition, configVolume);
        }
    }

    private bool HasFallenLongEnough(Player player)
    {
        float fallenDistanceTiles = (GetFootWorldY(player) - _fallStartFootY) * player.gravDir / 16f;
        return _fallingFrames >= MinFallingFrames && fallenDistanceTiles >= MinFallenDistanceTiles;
    }

    private static Vector2 GetLandingWarningPosition(Player player, int groundDistanceTiles)
    {
        float landingWorldY = player.gravDir >= 0f
            ? player.Bottom.Y + groundDistanceTiles * 16f
            : player.Top.Y - groundDistanceTiles * 16f;

        return new Vector2(player.Center.X, landingWorldY);
    }

    private static float GetFootWorldY(Player player)
    {
        return player.gravDir >= 0f ? player.Bottom.Y : player.Top.Y;
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

    private void ResetToGrounded()
    {
        _state = FallState.Grounded;
        _debounceCounter = 0;
        _fallingFrames = 0;
        _fallStartFootY = 0f;
        _groundScanTimer = 0;
        _cachedGroundDistance = MaxGroundScanTiles;
        ResetLandingBeepCadence();
    }

    private void ResetLandingBeepCadence()
    {
        _landingBeepTimer = EdgeBeepCue.IntervalFrames - 1;
    }
}
