#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Guidance;

/// <summary>
/// Handles audio playback for the guidance system including waypoint pings
/// and hostile tracking tones.
/// </summary>
internal sealed class GuidanceAudioPlayer
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

    private const int MaxPingDelayFrames = 54;
    private const float ArrivalTileThreshold = 4f;

    private readonly GuidanceStateManager _state;
    private SoundEffect? _hostileTone;

    public GuidanceAudioPlayer(GuidanceStateManager state)
    {
        _state = state;
    }

    public void EmitPing(Player player, Vector2 worldPosition)
    {
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        try
        {
            CleanupFinishedInstances();

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
            float configVolume = TerrariaAccessConfig.Instance?.GuidanceVolume ?? 1f;
            instance.Volume = MathHelper.Clamp(sample.Volume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);

            try
            {
                instance.Play();
                _state.ActiveWaypointInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Debug($"[WaypointPing] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[WaypointPing] Tone setup failed: {ex.Message}");
        }
    }

    public void EmitHostilePing(Player player, Vector2 worldPosition)
    {
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        try
        {
            CleanupFinishedInstances();

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
            float configVolume = TerrariaAccessConfig.Instance?.EnemySoundVolume ?? 1f;
            instance.Volume = MathHelper.Clamp(sample.Volume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);

            try
            {
                instance.Play();
                _state.ActiveWaypointInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Debug($"[HostilePing] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[HostilePing] Tone setup failed: {ex.Message}");
        }
    }

    public void EmitCurrentGuidancePing(Player player, bool isPingEnabled, Func<Player, (bool Success, Vector2 Position, string Label)> getTrackingTarget)
    {
        if (!isPingEnabled)
        {
            _state.NextPingUpdateFrame = -1;
            return;
        }

        var result = getTrackingTarget(player);
        if (result.Success)
        {
            // Use hostile ping for hostile mob tracking, waypoint ping for everything else
            if (_state.CurrentMode == SelectionMode.HostileMob)
            {
                EmitHostilePing(player, result.Position);
            }
            else
            {
                EmitPing(player, result.Position);
            }
        }
    }

    public void RescheduleGuidancePing(Player player, bool isPingEnabled, Func<Player, (bool Success, Vector2 Position, string Label)> getTrackingTarget)
    {
        if (!isPingEnabled)
        {
            _state.NextPingUpdateFrame = -1;
            _state.ArrivalAnnounced = false;
            return;
        }

        var result = getTrackingTarget(player);
        if (!result.Success)
        {
            _state.NextPingUpdateFrame = -1;
            _state.ArrivalAnnounced = false;
            return;
        }

        _state.ArrivalAnnounced = false;
        _state.NextPingUpdateFrame = ComputeNextPingFrame(player, result.Position);
    }

    public int ComputeNextPingFrame(Player player, Vector2 targetPosition)
    {
        int delay = DeterminePingDelayFrames(player, targetPosition);
        if (delay <= 0)
        {
            return -1;
        }

        return ComputeNextPingFrameFromDelay(delay);
    }

    private int DeterminePingDelayFrames(Player player, Vector2 targetPosition)
    {
        float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;
        if (distanceTiles <= ArrivalTileThreshold)
        {
            return -1;
        }

        // For hostile mob tracking, use the same interval calculation as HostileStaticEmitter
        if (_state.CurrentMode == SelectionMode.HostileMob)
        {
            return ComputeHostilePingDelayFrames(player, distanceTiles);
        }

        return MaxPingDelayFrames;
    }

    private int ComputeHostilePingDelayFrames(Player player, float distanceTiles)
    {
        // Determine if the selected hostile is a boss for range/interval calculation
        bool isBoss = false;
        float maxDistance = HostileStandardRangeTiles;

        if (_state.SelectedHostileMobIndex >= 0 && _state.SelectedHostileMobIndex < _state.NearbyHostileMobs.Count)
        {
            GuidanceEntry entry = _state.NearbyHostileMobs[_state.SelectedHostileMobIndex];
            if (entry.Index >= 0 && entry.Index < Main.maxNPCs)
            {
                NPC npc = Main.npc[entry.Index];
                if (npc.active)
                {
                    isBoss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];
                    maxDistance = isBoss ? HostileBossRangeTiles : HostileStandardRangeTiles;
                }
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

    private SoundEffect EnsureWaypointTone()
    {
        if (_state.WaypointTone is { IsDisposed: false })
        {
            return _state.WaypointTone;
        }

        _state.WaypointTone?.Dispose();
        _state.WaypointTone = CreateWaypointTone();
        return _state.WaypointTone;
    }

    private static SoundEffect CreateWaypointTone()
    {
        return SynthesizedSoundFactory.CreateSineTone(
            frequency: 720f,
            durationSeconds: 0.13f,
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WaypointPulse,
            gain: 0.45f);
    }

    private SoundEffect EnsureHostileTone()
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

    private void CleanupFinishedInstances()
    {
        for (int i = _state.ActiveWaypointInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = _state.ActiveWaypointInstances[i];
            if (instance.IsDisposed || instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                _state.ActiveWaypointInstances.RemoveAt(i);
            }
        }
    }

    public void DisposeToneResources()
    {
        foreach (SoundEffectInstance instance in _state.ActiveWaypointInstances)
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

        _state.ActiveWaypointInstances.Clear();

        if (_state.WaypointTone is not null)
        {
            if (!_state.WaypointTone.IsDisposed)
            {
                _state.WaypointTone.Dispose();
            }

            _state.WaypointTone = null;
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
}
