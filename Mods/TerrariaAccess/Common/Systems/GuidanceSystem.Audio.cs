#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Audio;
using TerrariaAccess.Common.Systems.Guidance;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem
{
    // Hostile tracking constants - match HostileStaticEmitter
    private const float HostileStandardRangeTiles = 52f;
    private const float HostileBossRangeTiles = 160f;
    private const int HostileMinIntervalFrames = 7;
    private const int HostileMaxIntervalFrames = 32;
    private const float HostileToneDurationSeconds = 0.045f;
    private const float HostileToneGain = 0.45f;
    private static readonly float[] HostileTonePartials = { 1.24f, 1.5f };
    private static readonly ToneEnvelope HostileToneEnvelope = new(attackFraction: 0.12f, releaseFraction: 0.55f, applyHannWindow: true);
    private static readonly SpatializedSoundCache _hostileToneCache = new();

    /// <summary>
    /// Returns true when the guidance system is actively tracking hostile mobs,
    /// which should suppress the normal HostileStaticEmitter.
    /// </summary>
    internal static bool IsHostileMobTrackingActive =>
        _selectionMode == SelectionMode.HostileMob && NearbyHostileMobs.Count > 0;

    private static void EmitCurrentGuidancePing(Player player)
    {
        if (_selectionMode == SelectionMode.Exploration)
        {
            ExplorationTargetRegistry.RequestSelectedTargetCue();
            _nextPingUpdateFrame = -1;
            return;
        }

        if (!IsPingEnabledForCurrentSelection())
        {
            _nextPingUpdateFrame = -1;
            return;
        }

        if (TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out _))
        {
            // Use hostile ping for hostile mob tracking, waypoint ping for everything else
            if (_selectionMode == SelectionMode.HostileMob)
            {
                EmitHostilePing(player, targetPosition);
            }
            else
            {
                EmitPing(player, targetPosition);
            }
        }
    }

    private static void RescheduleGuidancePing(Player player)
    {
        if (!IsPingEnabledForCurrentSelection())
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            return;
        }

        if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out _))
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            return;
        }

        _arrivalAnnounced = false;
        _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
    }

    private static void EmitPing(Player player, Vector2 worldPosition)
    {
        float configVolume = TerrariaAccessConfig.Instance?.GuidanceVolume ?? 1f;
        if (!SpatializedSoundEngine.CanPlay(configVolume))
        {
            return;
        }

        try
        {
            CleanupFinishedWaypointInstances();

            SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
                player.Center,
                worldPosition,
                1f);
            float distanceTiles = Vector2.Distance(player.Center, worldPosition) / 16f;
            float distanceVolumeScale = ComputeGuidancePingVolumeScale(distanceTiles);
            float localVolumeScale = configVolume * distanceVolumeScale;
            int intervalFrames = ComputeGuidancePingDelayFrames(distanceTiles);
            if (!sample.IsAudible(localVolumeScale))
            {
                return;
            }

            SoundEffect tone = EnsureWaypointTone(sample.NormalizedScreenX);
            SoundEffectInstance? instance = SpatializedSoundEngine.PlayWorldCue(
                tone,
                sample,
                localVolumeScale);
            if (instance is not null)
            {
                ActiveWaypointInstances.Add(instance);
                ReferenceToneCuePlayer.QueueGeneratedCue(
                    EnsureWaypointTone(SpatializedSoundEngine.CenterNormalizedScreenX),
                    sample.ScaleVolume(localVolumeScale),
                    intervalFrames);
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[WaypointPing] Tone setup failed: {ex.Message}");
        }
    }

    private static SoundEffect EnsureWaypointTone(float normalizedScreenX)
    {
        return _waypointToneCache.GetOrCreate(normalizedScreenX, CreateWaypointTone);
    }

    private static SoundEffect CreateWaypointTone(float normalizedScreenX)
    {
        return SpatializedSoundEngine.CreateSpatialSineTone(
            frequency: 720f,
            durationSeconds: 0.13f,
            envelope: SpatializedSoundEngine.ToneEnvelopes.WaypointPulse,
            gain: 0.45f,
            normalizedScreenX: normalizedScreenX);
    }

    private static void EmitHostileMobSelectionPing(Player player)
    {
        if (_selectionMode != SelectionMode.HostileMob)
        {
            return;
        }

        if (!TryGetSelectedHostileMob(player, out GuidanceEntry entry))
        {
            return;
        }

        EmitHostilePing(player, entry.WorldPosition);
    }

    private static void EmitHostilePing(Player player, Vector2 worldPosition)
    {
        float configVolume = TerrariaAccessConfig.Instance?.EnemySoundVolume ?? 1f;
        if (!SpatializedSoundEngine.CanPlay(configVolume))
        {
            return;
        }

        try
        {
            CleanupFinishedWaypointInstances();

            SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
                player.Center,
                worldPosition,
                1f);
            float distanceTiles = Vector2.Distance(player.Center, worldPosition) / 16f;
            int intervalFrames = ComputeHostilePingDelayFrames(player, distanceTiles);
            if (!sample.IsAudible(configVolume))
            {
                return;
            }

            SoundEffect tone = EnsureHostileTone(sample.NormalizedScreenX);
            SoundEffectInstance? instance = SpatializedSoundEngine.PlayWorldCue(
                tone,
                sample,
                configVolume);
            if (instance is not null)
            {
                ActiveWaypointInstances.Add(instance);
                ReferenceToneCuePlayer.QueueGeneratedCue(
                    EnsureHostileTone(SpatializedSoundEngine.CenterNormalizedScreenX),
                    sample.ScaleVolume(configVolume),
                    intervalFrames);
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[HostilePing] Tone setup failed: {ex.Message}");
        }
    }

    private static SoundEffect EnsureHostileTone(float normalizedScreenX)
    {
        return _hostileToneCache.GetOrCreate(normalizedScreenX, quantizedNormalizedScreenX =>
            SpatializedSoundEngine.CreateSpatialAdditiveTone(
            fundamentalFrequency: 360f,
            partialMultipliers: HostileTonePartials,
            envelope: HostileToneEnvelope,
            durationSeconds: HostileToneDurationSeconds,
            outputGain: HostileToneGain,
            normalizedScreenX: quantizedNormalizedScreenX,
            partialFalloff: 0.75f));
    }

    private static void CleanupFinishedWaypointInstances()
    {
        SpatializedSoundEngine.CleanupStopped(ActiveWaypointInstances);
    }

    private static void DisposeToneResources()
    {
        SpatializedSoundEngine.StopAndDisposeAll(ActiveWaypointInstances);

        _waypointToneCache.Dispose();
        _hostileToneCache.Dispose();
    }

    private static int ComputeNextPingFrame(Player player, Vector2 targetPosition)
    {
        int delay = DeterminePingDelayFrames(player, targetPosition);
        if (delay <= 0)
        {
            return -1;
        }

        return ComputeNextPingFrameFromDelay(delay);
    }

    private static int DeterminePingDelayFrames(Player player, Vector2 targetPosition)
    {
        float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;
        if (distanceTiles <= ArrivalTileThreshold)
        {
            return -1;
        }

        // For hostile mob tracking, use the same interval calculation as HostileStaticEmitter
        if (_selectionMode == SelectionMode.HostileMob)
        {
            return ComputeHostilePingDelayFrames(player, distanceTiles);
        }

        return ComputeGuidancePingDelayFrames(distanceTiles);
    }

    private static int ComputeGuidancePingDelayFrames(float distanceTiles)
    {
        return GuidancePingCadence.ComputeDistanceDelayFrames(
            distanceTiles,
            ArrivalTileThreshold,
            MinGuidancePingDelayFrames,
            MaxPingDelayFrames,
            GuidancePingCadenceRangeTiles);
    }

    private static float ComputeGuidancePingVolumeScale(float distanceTiles)
    {
        return GuidancePingCadence.ComputeDistanceVolumeScale(
            distanceTiles,
            ArrivalTileThreshold,
            GuidancePingCadenceRangeTiles,
            MinGuidancePingVolumeScale);
    }

    private static int ComputeHostilePingDelayFrames(Player player, float distanceTiles)
    {
        // Determine if the selected hostile is a boss for range/interval calculation
        bool isBoss = false;
        float maxDistance = HostileStandardRangeTiles;

        if (TryGetSelectedHostileMob(player, out GuidanceEntry entry) &&
            entry.Index >= 0 && entry.Index < Main.maxNPCs)
        {
            NPC npc = Main.npc[entry.Index];
            if (npc.active)
            {
                isBoss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];
                maxDistance = isBoss ? HostileBossRangeTiles : HostileStandardRangeTiles;
            }
        }

        // Calculate normalized distance and lerp between min/max interval
        float normalized = maxDistance <= 0f
            ? 0f
            : Math.Clamp(distanceTiles / maxDistance, 0f, 1f);

        float frames = MathHelper.Lerp(HostileMinIntervalFrames, HostileMaxIntervalFrames, normalized);

        // Bosses ping faster
        if (isBoss)
        {
            frames *= 0.65f;
        }

        return GuidancePingCadence.ApplyDistanceCueRateReduction(Math.Max(1, (int)MathF.Round(frames)));
    }

    private static int ComputeNextPingFrameFromDelay(int delayFrames)
    {
        int safeDelay = Math.Max(1, delayFrames);
        ulong current = Main.GameUpdateCount;
        ulong target = current + (ulong)safeDelay;
        if (target > int.MaxValue)
        {
            target = (ulong)int.MaxValue;
        }

        return (int)target;
    }
}
