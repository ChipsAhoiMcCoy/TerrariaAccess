#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Guidance;
using Terraria;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
    // Hostile tracking constants - match HostileStaticAudioEmitter
    private const float HostileStandardRangeTiles = 52f;
    private const float HostileBossRangeTiles = 160f;
    private const int HostileMinIntervalFrames = 7;
    private const int HostileMaxIntervalFrames = 32;
    private const float HostileToneDurationSeconds = 0.045f;
    private const float HostileToneGain = 0.45f;
    private static readonly float[] HostileTonePartials = { 1.24f, 1.5f };
    private static readonly ToneEnvelope HostileToneEnvelope = new(attackFraction: 0.12f, releaseFraction: 0.55f, applyHannWindow: true);
    private static SoundEffect? _hostileTone;

    /// <summary>
    /// Returns true when the guidance system is actively tracking hostile mobs,
    /// which should suppress the normal HostileStaticAudioEmitter.
    /// </summary>
    internal static bool IsHostileMobTrackingActive =>
        _selectionMode == SelectionMode.HostileMob && NearbyHostileMobs.Count > 0;

    private static void EmitCurrentGuidancePing(Player player)
    {
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
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        try
        {
            CleanupFinishedWaypointInstances();

            SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
                player.Center,
                worldPosition,
                Main.soundVolume);
            if (sample.Volume <= 0f)
            {
                return;
            }

            SoundEffect tone = EnsureWaypointTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = sample.Pan;
            instance.Pitch = sample.Pitch;
            float configVolume = ScreenReaderModConfig.Instance?.GuidanceVolume ?? 1f;
            instance.Volume = MathHelper.Clamp(sample.Volume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);

            try
            {
                instance.Play();
                ActiveWaypointInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Debug($"[WaypointPing] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[WaypointPing] Tone setup failed: {ex.Message}");
        }
    }

    private static SoundEffect EnsureWaypointTone()
    {
        if (_waypointTone is { IsDisposed: false })
        {
            return _waypointTone;
        }

        _waypointTone?.Dispose();
        _waypointTone = CreateWaypointTone();
        return _waypointTone;
    }

    private static SoundEffect CreateWaypointTone()
    {
        return SynthesizedSoundFactory.CreateSineTone(
            frequency: 720f,
            durationSeconds: 0.13f,
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WaypointPulse,
            gain: 0.45f);
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
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        try
        {
            CleanupFinishedWaypointInstances();

            SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
                player.Center,
                worldPosition,
                Main.soundVolume);
            if (sample.Volume <= 0f)
            {
                return;
            }

            SoundEffect tone = EnsureHostileTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = sample.Pan;
            instance.Pitch = sample.Pitch;
            float configVolume = ScreenReaderModConfig.Instance?.EnemySoundVolume ?? 1f;
            instance.Volume = MathHelper.Clamp(sample.Volume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);

            try
            {
                instance.Play();
                ActiveWaypointInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Debug($"[HostilePing] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[HostilePing] Tone setup failed: {ex.Message}");
        }
    }

    private static SoundEffect EnsureHostileTone()
    {
        if (_hostileTone is { IsDisposed: false })
        {
            return _hostileTone;
        }

        _hostileTone?.Dispose();
        _hostileTone = SynthesizedSoundFactory.CreateAdditiveTone(
            fundamentalFrequency: 360f,
            partialMultipliers: HostileTonePartials,
            envelope: HostileToneEnvelope,
            durationSeconds: HostileToneDurationSeconds,
            outputGain: HostileToneGain,
            partialFalloff: 0.75f);
        return _hostileTone;
    }

    private static void CleanupFinishedWaypointInstances()
    {
        for (int i = ActiveWaypointInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = ActiveWaypointInstances[i];
            if (instance.IsDisposed || instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                ActiveWaypointInstances.RemoveAt(i);
            }
        }
    }

    private static void DisposeToneResources()
    {
        foreach (SoundEffectInstance instance in ActiveWaypointInstances)
        {
            try
            {
                if (!instance.IsDisposed)
                {
                    instance.Stop();
                }
            }
            catch
            {
            }

            instance.Dispose();
        }

        ActiveWaypointInstances.Clear();

        if (_waypointTone is not null)
        {
            if (!_waypointTone.IsDisposed)
            {
                _waypointTone.Dispose();
            }

            _waypointTone = null;
        }

        if (_hostileTone is not null)
        {
            if (!_hostileTone.IsDisposed)
            {
                _hostileTone.Dispose();
            }

            _hostileTone = null;
        }
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

        // For hostile mob tracking, use the same interval calculation as HostileStaticAudioEmitter
        if (_selectionMode == SelectionMode.HostileMob)
        {
            return ComputeHostilePingDelayFrames(player, distanceTiles);
        }

        return MaxPingDelayFrames;
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

        return Math.Max(1, (int)MathF.Round(frames));
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
