#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Emits short pulses for nearby solid collision geometry.
/// </summary>
internal sealed class WallToneEmitter : AudioEmitterBase
{
    private const int ScanIntervalFrames = 3;
    private const int MinPulseSeparationFrames = 8;
    private const int MinSidePulseIntervalFrames = 8;
    private const int MaxSidePulseIntervalFrames = 24;
    private const int MinCeilingPulseIntervalFrames = 10;
    private const int MaxCeilingPulseIntervalFrames = 30;

    private const float SideBaseVolume = 0.30f;
    private const float CeilingBaseVolume = 0.24f;
    private const float FarDistanceVolumeScale = 0.35f;
    private const float SidePitchDrop = 0.16f;
    private const float MaxSidePitch = -0.06f;
    private const float CeilingPitchLift = 0.34f;
    private const float MinCeilingPitch = 0.28f;

    private int _scanTimer;
    private WallToneScanResult _lastScan;
    private long _nextLeftPulseFrame;
    private long _nextRightPulseFrame;
    private long _nextCeilingPulseFrame;
    private long _nextAnyPulseFrame;
    private WallToneCueKind _nextCueKind = WallToneCueKind.Left;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player) || ShouldSuppress(player))
        {
            Reset();
            return;
        }

        TerrariaAccessConfig? config = TerrariaAccessConfig.Instance;
        bool enabled = config?.WallTonesEnabled ?? true;
        float configVolume = config?.WallToneVolume ?? 1f;
        if (!enabled || configVolume <= 0f)
        {
            Reset();
            return;
        }

        UpdateScan(player);
        EmitDuePulse(player, configVolume);
    }

    public override void Reset()
    {
        _scanTimer = 0;
        _lastScan = default;
        _nextLeftPulseFrame = 0;
        _nextRightPulseFrame = 0;
        _nextCeilingPulseFrame = 0;
        _nextAnyPulseFrame = 0;
        _nextCueKind = WallToneCueKind.Left;
    }

    public void DisposeStaticResources()
    {
    }

    private static bool ShouldSuppress(Player player)
    {
        if (Main.playerInventory ||
            Main.ingameOptionsWindow ||
            Main.inFancyUI ||
            Main.gameMenu ||
            Main.InGuideCraftMenu ||
            Main.InReforgeMenu ||
            Main.CreativeMenu.Enabled ||
            Main.hairWindow ||
            Main.clothesWindow ||
            Main.drawingPlayerChat ||
            Main.editChest ||
            Main.editSign ||
            player.talkNPC != -1 ||
            player.sign != -1 ||
            player.chest != -1 ||
            Main.npcShop != 0 ||
            player.tileEntityAnchor.InUse)
        {
            return true;
        }

        return Main.InGameUI?.CurrentState is not null;
    }

    private void UpdateScan(Player player)
    {
        if (_scanTimer <= 0)
        {
            _lastScan = WallToneGeometry.Scan(
                player.Hitbox,
                player.gravDir,
                IsBlockingWallToneTile,
                Main.maxTilesX,
                Main.maxTilesY);
            _scanTimer = ScanIntervalFrames;
            return;
        }

        _scanTimer--;
    }

    private void EmitDuePulse(Player player, float configVolume)
    {
        long currentFrame = (long)Main.GameUpdateCount;
        ResetUnavailableCueTimers();

        if (currentFrame < _nextAnyPulseFrame)
        {
            return;
        }

        if (!TrySelectDuePulse(currentFrame, out WallToneCueKind cueKind, out WallToneContact contact))
        {
            return;
        }

        switch (cueKind)
        {
            case WallToneCueKind.Left:
                PlaySidePulse(player, contact, configVolume);
                _nextLeftPulseFrame = currentFrame + ComputeIntervalFrames(
                    contact.DistanceTiles,
                    WallToneGeometry.SideProbeRangeTiles,
                    MinSidePulseIntervalFrames,
                    MaxSidePulseIntervalFrames);
                break;

            case WallToneCueKind.Right:
                PlaySidePulse(player, contact, configVolume);
                _nextRightPulseFrame = currentFrame + ComputeIntervalFrames(
                    contact.DistanceTiles,
                    WallToneGeometry.SideProbeRangeTiles,
                    MinSidePulseIntervalFrames,
                    MaxSidePulseIntervalFrames);
                break;

            case WallToneCueKind.Ceiling:
                PlayCeilingPulse(player, contact, configVolume);
                _nextCeilingPulseFrame = currentFrame + ComputeIntervalFrames(
                    contact.DistanceTiles,
                    WallToneGeometry.CeilingProbeRangeTiles,
                    MinCeilingPulseIntervalFrames,
                    MaxCeilingPulseIntervalFrames);
                break;
        }

        _nextAnyPulseFrame = currentFrame + MinPulseSeparationFrames;
        _nextCueKind = GetNextCueKind(cueKind);
    }

    private void ResetUnavailableCueTimers()
    {
        if (!_lastScan.HasLeftWall)
        {
            _nextLeftPulseFrame = 0;
        }

        if (!_lastScan.HasRightWall)
        {
            _nextRightPulseFrame = 0;
        }

        if (!_lastScan.HasCeiling)
        {
            _nextCeilingPulseFrame = 0;
        }
    }

    private bool TrySelectDuePulse(long currentFrame, out WallToneCueKind cueKind, out WallToneContact contact)
    {
        for (int offset = 0; offset < 3; offset++)
        {
            WallToneCueKind candidate = GetCueKindOffset(_nextCueKind, offset);
            if (TryGetCueContact(candidate, out WallToneContact candidateContact) &&
                currentFrame >= GetNextPulseFrame(candidate))
            {
                cueKind = candidate;
                contact = candidateContact;
                return true;
            }
        }

        cueKind = default;
        contact = default;
        return false;
    }

    private bool TryGetCueContact(WallToneCueKind cueKind, out WallToneContact contact)
    {
        WallToneContact? candidate = cueKind switch
        {
            WallToneCueKind.Left => _lastScan.Left,
            WallToneCueKind.Right => _lastScan.Right,
            WallToneCueKind.Ceiling => _lastScan.Ceiling,
            _ => null
        };

        contact = candidate.GetValueOrDefault();
        return candidate.HasValue && contact.DistanceTiles > 0;
    }

    private long GetNextPulseFrame(WallToneCueKind cueKind)
    {
        return cueKind switch
        {
            WallToneCueKind.Left => _nextLeftPulseFrame,
            WallToneCueKind.Right => _nextRightPulseFrame,
            WallToneCueKind.Ceiling => _nextCeilingPulseFrame,
            _ => long.MaxValue
        };
    }

    private static WallToneCueKind GetCueKindOffset(WallToneCueKind cueKind, int offset)
    {
        int index = ((int)cueKind + offset) % 3;
        return (WallToneCueKind)index;
    }

    private static WallToneCueKind GetNextCueKind(WallToneCueKind cueKind) =>
        GetCueKindOffset(cueKind, 1);

    private static void PlaySidePulse(Player player, WallToneContact contact, float configVolume)
    {
        float distanceVolume = ComputeDistanceVolumeScale(contact.DistanceTiles, WallToneGeometry.SideProbeRangeTiles);
        Vector2 targetPosition = GetTileCenter(contact.Tile);
        SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
            player.Center,
            targetPosition,
            SideBaseVolume * distanceVolume);
        sample = sample with
        {
            Pitch = MathHelper.Clamp(Math.Min(sample.Pitch - SidePitchDrop, MaxSidePitch), -1f, 1f)
        };

        AccessibilityCueSoundPlayer.PlaySpatialInteraural(AccessibilityCueAssets.WallTone, sample, configVolume);
    }

    private static void PlayCeilingPulse(Player player, WallToneContact contact, float configVolume)
    {
        float distanceVolume = ComputeDistanceVolumeScale(contact.DistanceTiles, WallToneGeometry.CeilingProbeRangeTiles);
        Vector2 targetPosition = GetTileCenter(contact.Tile);
        SpatializedSoundEngine.SpatialAudioSample computed = SpatializedSoundEngine.Compute(
            player.Center,
            targetPosition,
            CeilingBaseVolume * distanceVolume);
        SpatializedSoundEngine.SpatialAudioSample sample = computed with
        {
            NormalizedScreenX = SpatializedSoundEngine.CenterNormalizedScreenX,
            Pitch = MathHelper.Clamp(Math.Max(computed.Pitch + CeilingPitchLift, MinCeilingPitch), -1f, 1f)
        };

        AccessibilityCueSoundPlayer.PlaySpatialInteraural(AccessibilityCueAssets.WallTone, sample, configVolume);
    }

    private static Vector2 GetTileCenter(Point tile) =>
        new(tile.X * 16f + 8f, tile.Y * 16f + 8f);

    private static float ComputeDistanceVolumeScale(int distanceTiles, int maxDistanceTiles)
    {
        float closeness = ComputeCloseness(distanceTiles, maxDistanceTiles);
        return MathHelper.Lerp(FarDistanceVolumeScale, 1f, closeness * closeness);
    }

    private static int ComputeIntervalFrames(
        int distanceTiles,
        int maxDistanceTiles,
        int minIntervalFrames,
        int maxIntervalFrames)
    {
        float closeness = ComputeCloseness(distanceTiles, maxDistanceTiles);
        float frames = MathHelper.Lerp(maxIntervalFrames, minIntervalFrames, closeness);
        return Math.Max(1, (int)MathF.Round(frames));
    }

    private static float ComputeCloseness(int distanceTiles, int maxDistanceTiles)
    {
        if (distanceTiles <= 0 || maxDistanceTiles <= 1)
        {
            return 0f;
        }

        float normalizedDistance = MathHelper.Clamp(
            (distanceTiles - 1f) / (maxDistanceTiles - 1f),
            0f,
            1f);
        return 1f - normalizedDistance;
    }

    private static bool IsBlockingWallToneTile(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= Main.maxTilesX || tileY >= Main.maxTilesY)
        {
            return false;
        }

        Tile tile = Framing.GetTileSafely(tileX, tileY);
        if (!tile.HasTile || tile.IsActuated)
        {
            return false;
        }

        int tileType = tile.TileType;
        if (IsSetMember(Main.tileRope, tileType) ||
            IsSetMember(TileID.Sets.Platforms, tileType) ||
            tileType == TileID.MinecartTrack)
        {
            return false;
        }

        return IsSetMember(Main.tileSolid, tileType) && !IsSetMember(Main.tileSolidTop, tileType);
    }

    private static bool IsSetMember(bool[] values, int index) =>
        index >= 0 && index < values.Length && values[index];

    private enum WallToneCueKind
    {
        Left,
        Right,
        Ceiling
    }
}
