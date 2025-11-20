#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
    private static void EmitCurrentGuidancePing(Player player)
    {
        if (TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out _))
        {
            EmitPing(player, targetPosition);
        }
    }

    private static void RescheduleGuidancePing(Player player)
    {
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

            Vector2 offset = worldPosition - player.Center;
            float pitch = MathHelper.Clamp(-offset.Y / PitchScale, -0.7f, 0.7f);
            float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);

            float distanceTiles = offset.Length() / 16f;
            float distanceFactor = 1f / (1f + (distanceTiles / Math.Max(1f, DistanceReferenceTiles)));
            float volume = MathHelper.Clamp(MinVolume + distanceFactor * 0.85f, 0f, 1f) * Main.soundVolume;

            SoundEffect tone = EnsureWaypointTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = pan;
            instance.Pitch = pitch;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f);

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
            gain: 0.75f);
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
    }

    private static int ComputeNextPingFrame(Player player, Vector2 waypointPosition)
    {
        int delay = DeterminePingDelayFrames(player, waypointPosition);
        if (delay <= 0)
        {
            return -1;
        }

        return ComputeNextPingFrameFromDelay(delay);
    }

    private static int DeterminePingDelayFrames(Player player, Vector2 waypointPosition)
    {
        float distanceTiles = Vector2.Distance(player.Center, waypointPosition) / 16f;
        if (distanceTiles <= ArrivalTileThreshold)
        {
            return -1;
        }

        float frames = MathHelper.Clamp(distanceTiles * PingDelayScale, MinPingDelayFrames, MaxPingDelayFrames);
        return (int)MathF.Round(frames);
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
