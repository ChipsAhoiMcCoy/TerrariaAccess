#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;

namespace TerrariaAccess.Common.Services;

internal enum UiSoundCue
{
    Tick,
    Open,
    Close,
}

/// <summary>
/// Central custom-audio path for non-world UI feedback cues.
/// UI cues are intentionally centered unless a caller has concrete screen-position data,
/// in which case it should use <see cref="UiTickSoundPlayer.PlaySpatialTick"/>.
/// </summary>
internal static class UiSoundCuePlayer
{
    private const int MaxActiveInstances = 16;
    private static readonly SpatializedSoundCache<UiSoundCue> CueCache = new();
    private static readonly List<SoundEffectInstance> ActiveInstances = new();

    public static void Play(UiSoundCue cue, float volume = 1f)
    {
        switch (cue)
        {
            case UiSoundCue.Open:
                PlayOpen(volume);
                break;
            case UiSoundCue.Close:
                PlayClose(volume);
                break;
            default:
                PlayTick(volume);
                break;
        }
    }

    public static void PlayTick(float volume = 1f)
    {
        PlayAlreadySpatializedCue(UiSoundCue.Tick, volume);
    }

    public static void PlayOpen(float volume = 1f) =>
        PlayAlreadySpatializedCue(UiSoundCue.Open, volume);

    public static void PlayClose(float volume = 1f) =>
        PlayAlreadySpatializedCue(UiSoundCue.Close, volume);

    public static void PlayCloseOrTick(bool close, float volume = 1f)
    {
        if (close)
        {
            PlayClose(volume);
            return;
        }

        PlayTick(volume);
    }

    private static void PlayAlreadySpatializedCue(UiSoundCue cue, float volume)
    {
        if (!SpatializedSoundEngine.CanPlay(volume))
        {
            return;
        }

        try
        {
            SpatializedSoundEngine.CleanupStopped(ActiveInstances);
            if (ActiveInstances.Count >= MaxActiveInstances)
            {
                return;
            }

            SoundEffect sound = CueCache.GetOrCreate(cue, SpatializedSoundEngine.CenterNormalizedScreenX, normalizedScreenX => CreateCue(cue, normalizedScreenX));
            SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedInterfaceCue(sound, volume);
            if (instance is not null)
            {
                ActiveInstances.Add(instance);
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiSoundCuePlayer] Failed to play {cue} cue: {ex.Message}");
        }
    }

    public static void Dispose()
    {
        SpatializedSoundEngine.StopAndDisposeAll(ActiveInstances);
        CueCache.Dispose();
    }

    private static SoundEffect CreateCue(UiSoundCue cue, float normalizedScreenX)
    {
        return cue switch
        {
            UiSoundCue.Open => SpatializedSoundEngine.CreateSpatialAdditiveTone(
                fundamentalFrequency: 660f,
                partialMultipliers: new[] { 2f, 3f },
                envelope: SpatializedSoundEngine.ToneEnvelopes.CursorPing,
                durationSeconds: 0.065f,
                outputGain: 0.22f,
                normalizedScreenX: normalizedScreenX),
            UiSoundCue.Close => SpatializedSoundEngine.CreateSpatialAdditiveTone(
                fundamentalFrequency: 420f,
                partialMultipliers: new[] { 2f },
                envelope: SpatializedSoundEngine.ToneEnvelopes.CursorPing,
                durationSeconds: 0.07f,
                outputGain: 0.24f,
                normalizedScreenX: normalizedScreenX),
            _ => SpatializedSoundEngine.CreateSpatialSineTone(
                frequency: 880f,
                durationSeconds: 0.035f,
                envelope: SpatializedSoundEngine.ToneEnvelopes.CursorPing,
                gain: 0.2f,
                normalizedScreenX: normalizedScreenX),
        };
    }
}
