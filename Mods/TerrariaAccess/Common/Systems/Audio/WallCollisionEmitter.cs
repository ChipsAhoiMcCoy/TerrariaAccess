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
        if (!CanEmitAudio(player) || player.pulley)
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

        if (!IsPressingIntoSolidWall(player, moveDirection))
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

    private static bool IsPressingIntoSolidWall(Player player, int moveDirection)
    {
        Rectangle hitbox = player.Hitbox;
        Vector2 probePosition = new(hitbox.X + moveDirection * 2f, hitbox.Y + 2f);
        int probeHeight = Math.Max(1, hitbox.Height - 4);
        return Collision.SolidCollision(probePosition, hitbox.Width, probeHeight);
    }
}
