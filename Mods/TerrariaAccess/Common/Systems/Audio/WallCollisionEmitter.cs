#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using static TerrariaAccess.Common.Systems.InGameNarrationSystem;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Plays a repeating tap sound when the player holds a movement key into a solid wall,
/// providing immediate feedback that they are blocked. The tap is panned slightly toward
/// the wall so the player can tell which side it is on.
/// </summary>
internal sealed class WallCollisionEmitter : AudioEmitterBase
{
    private const float VelocityStoppedThreshold = 0.5f;
    private const int TapIntervalFrames = 8; // ~7.5 taps/sec at 60 fps
    private const float TapFrequency = 300f; // Low, muffled-sounding tap
    private const float BaseVolume = 0.35f;
    private const float WallPanOffset = 0.3f; // Pan slightly toward the wall

    private int _tapTimer;
    private bool _wasBlocked;

    public override void Update(Player player)
    {
        if (!CanProcessMovementAudio(player))
        {
            Reset();
            return;
        }

        bool enabled = TerrariaAccessConfig.Instance?.WallCollisionEnabled ?? true;
        if (!enabled)
        {
            Reset();
            return;
        }

        // Determine active movement direction from control input
        int moveDirection = player.controlRight ? 1 : player.controlLeft ? -1 : 0;
        if (moveDirection == 0)
        {
            Reset();
            return;
        }

        // Must be grounded or near-grounded (not in freefall)
        if (Math.Abs(player.velocity.Y) > 1f)
        {
            Reset();
            return;
        }

        // Confirm horizontal velocity is near zero (actually blocked, not just starting to move)
        if (Math.Abs(player.velocity.X) > VelocityStoppedThreshold)
        {
            Reset();
            return;
        }

        // Get foot tile position and check one tile ahead at body height
        Point footTile = GetFootTile(player);
        int checkX = footTile.X + moveDirection;

        if (checkX < 0 || checkX >= Main.maxTilesX)
        {
            Reset();
            return;
        }

        if (!IsPathBlockedAtBodyHeight(checkX, footTile.Y))
        {
            Reset();
            return;
        }

        // Player is pressing into a wall and blocked — play repeating taps
        _tapTimer++;
        if (!_wasBlocked || _tapTimer >= TapIntervalFrames)
        {
            _tapTimer = 0;
            _wasBlocked = true;
            PlayWallTap(moveDirection);
        }
    }

    public override void Reset()
    {
        _tapTimer = 0;
        _wasBlocked = false;
    }

    public static void DisposeStaticResources()
    {
        // No static resources; uses FootstepToneProvider
    }

    private static void PlayWallTap(int direction)
    {
        float configVolume = TerrariaAccessConfig.Instance?.WallCollisionVolume ?? 1f;
        if (configVolume <= 0f)
        {
            return;
        }

        float pan = direction * WallPanOffset;
        FootstepToneProvider.Play(TapFrequency, BaseVolume * configVolume, useTriangleWave: true, pan);
    }

    private static Point GetFootTile(Player player)
    {
        Rectangle hitbox = player.Hitbox;
        int tileX = Math.Clamp(hitbox.Center.X / 16, 0, Main.maxTilesX - 1);
        int tileY = Math.Clamp(hitbox.Bottom / 16, 0, Main.maxTilesY - 1);
        return new Point(tileX, tileY);
    }

    private static bool IsSolidWall(int tileX, int tileY)
    {
        Tile tile = Framing.GetTileSafely(tileX, tileY);
        if (!tile.HasTile || tile.IsActuated)
        {
            return false;
        }

        return Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
    }

    private static bool IsPathBlockedAtBodyHeight(int tileX, int footTileY)
    {
        return IsSolidWall(tileX, footTileY - 1) || IsSolidWall(tileX, footTileY - 2);
    }
}
