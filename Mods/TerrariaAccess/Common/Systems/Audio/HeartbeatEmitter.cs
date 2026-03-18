#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Emits a rhythmic "lub-dub" heartbeat tone when the player's health drops below 50%.
/// The beat rate increases as health decreases, providing an audio indicator of danger level.
/// At 50% health: 1 beat per second. Near death: ~5 beats per second.
/// </summary>
internal sealed class HeartbeatEmitter : AudioEmitterBase
{
    /// <summary>Health fraction at which the heartbeat begins (50%).</summary>
    private const float ActivationThreshold = 0.5f;

    /// <summary>Interval in frames at the activation threshold (1 beat per second at 60fps).</summary>
    private const int MaxIntervalFrames = 60;

    /// <summary>Fastest interval in frames at near-zero health.</summary>
    private const int MinIntervalFrames = 12;

    /// <summary>Frame delay between the "lub" and "dub" of each beat.</summary>
    private const int DubDelayFrames = 8;

    // "Lub" tone: low, percussive thump
    private const float LubFundamental = 70f;
    private const float LubDurationSeconds = 0.1f;
    private const float LubGain = 0.4f;

    // "Dub" tone: slightly higher, softer
    private const float DubFundamental = 90f;
    private const float DubDurationSeconds = 0.1f;
    private const float DubGain = 0.28f;

    private static readonly float[] HeartbeatPartials = { 2.0f, 3.0f };
    private static readonly ToneEnvelope HeartbeatEnvelope = new(
        attackFraction: 0.05f,
        releaseFraction: 0.6f,
        applyHannWindow: true);

    private static SoundEffect? s_lubTone;
    private static SoundEffect? s_dubTone;

    private long _nextBeatFrame;
    private long _pendingDubFrame;
    private bool _dubPending;
    private readonly List<SoundEffectInstance> _liveInstances = new();

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            Reset();
            return;
        }

        if (!(TerrariaAccessConfig.Instance?.HeartbeatEnabled ?? true))
        {
            CleanupFinishedInstances();
            return;
        }

        float healthFraction = player.statLifeMax2 > 0
            ? (float)player.statLife / player.statLifeMax2
            : 1f;

        // Only active below activation threshold
        if (healthFraction > ActivationThreshold)
        {
            CleanupFinishedInstances();
            return;
        }

        long currentFrame = (long)Main.GameUpdateCount;

        // Check if dub beat is due
        if (_dubPending && currentFrame >= _pendingDubFrame)
        {
            _dubPending = false;
            PlayBeat(isDub: true);
        }

        // Check if main lub beat is due
        if (currentFrame >= _nextBeatFrame)
        {
            PlayBeat(isDub: false);

            // Schedule the dub
            _pendingDubFrame = currentFrame + DubDelayFrames;
            _dubPending = true;

            // Schedule next beat cycle
            int interval = ComputeIntervalFrames(healthFraction);
            _nextBeatFrame = currentFrame + Math.Max(1, interval);
        }

        CleanupFinishedInstances();
    }

    public override void Reset()
    {
        _nextBeatFrame = 0;
        _dubPending = false;
        StopAllInstances();
    }

    public static void DisposeStaticResources()
    {
        s_lubTone?.Dispose();
        s_lubTone = null;
        s_dubTone?.Dispose();
        s_dubTone = null;
    }

    private static int ComputeIntervalFrames(float healthFraction)
    {
        // Map health fraction from [0, ActivationThreshold] to [MinInterval, MaxInterval]
        float normalized = Math.Clamp(healthFraction / ActivationThreshold, 0f, 1f);
        float frames = MathHelper.Lerp(MinIntervalFrames, MaxIntervalFrames, normalized);
        return Math.Max(MinIntervalFrames, (int)MathF.Round(frames));
    }

    private void PlayBeat(bool isDub)
    {
        SoundEffect tone = isDub ? EnsureDubTone() : EnsureLubTone();
        if (tone.IsDisposed)
        {
            return;
        }

        float configVolume = TerrariaAccessConfig.Instance?.HeartbeatVolume ?? 1f;
        float volume = MathHelper.Clamp(
            Main.soundVolume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale,
            0f, 1f);

        if (volume <= 0f)
        {
            return;
        }

        SoundEffectInstance instance = tone.CreateInstance();
        instance.IsLooped = false;
        instance.Volume = volume;

        try
        {
            instance.Play();
            _liveInstances.Add(instance);
        }
        catch
        {
            instance.Dispose();
        }
    }

    private void CleanupFinishedInstances()
    {
        for (int i = _liveInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = _liveInstances[i];
            if (instance.IsDisposed || instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                _liveInstances.RemoveAt(i);
            }
        }
    }

    private void StopAllInstances()
    {
        for (int i = _liveInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = _liveInstances[i];
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

        _liveInstances.Clear();
    }

    private static SoundEffect EnsureLubTone()
    {
        if (s_lubTone is { IsDisposed: false })
        {
            return s_lubTone;
        }

        s_lubTone?.Dispose();
        s_lubTone = SynthesizedSoundFactory.CreateAdditiveTone(
            fundamentalFrequency: LubFundamental,
            partialMultipliers: HeartbeatPartials,
            envelope: HeartbeatEnvelope,
            durationSeconds: LubDurationSeconds,
            outputGain: LubGain,
            partialFalloff: 0.5f);
        return s_lubTone;
    }

    private static SoundEffect EnsureDubTone()
    {
        if (s_dubTone is { IsDisposed: false })
        {
            return s_dubTone;
        }

        s_dubTone?.Dispose();
        s_dubTone = SynthesizedSoundFactory.CreateAdditiveTone(
            fundamentalFrequency: DubFundamental,
            partialMultipliers: HeartbeatPartials,
            envelope: HeartbeatEnvelope,
            durationSeconds: DubDurationSeconds,
            outputGain: DubGain,
            partialFalloff: 0.5f);
        return s_dubTone;
    }
}
