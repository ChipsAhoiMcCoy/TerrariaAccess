#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ReLogic.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Plays delayed centered reference cues for distance-cadenced locator tones.
/// </summary>
internal static class ReferenceToneCuePlayer
{
    private const int MaxPendingCues = 32;
    private const float ReferenceVolumeScale = 0.85f;

    private static readonly List<PendingGeneratedCue> PendingGeneratedCues = new();
    private static readonly List<PendingNativeCue> PendingNativeCues = new();
    private static readonly List<SoundEffectInstance> ActiveGeneratedInstances = new();
    private static readonly List<SlotId> ActiveNativeCueSlots = new();

    public static void QueueGeneratedCue(SoundEffect? centeredTone, float effectiveVolume, int sourceIntervalFrames)
    {
        if (sourceIntervalFrames <= 0)
        {
            return;
        }

        float volume = SanitizeReferenceVolume(effectiveVolume);
        if (centeredTone is null || volume <= 0f || !SpatializedSoundEngine.CanPlay(volume))
        {
            return;
        }

        try
        {
            if (centeredTone.IsDisposed)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        TrimPendingQueue(PendingGeneratedCues);
        PendingGeneratedCues.Add(new PendingGeneratedCue(centeredTone, volume, ComputePlayFrame(sourceIntervalFrames)));
    }

    public static void QueueNativeCue(SoundStyle style, float effectiveVolume, int sourceIntervalFrames, float pitch = 0f)
    {
        if (sourceIntervalFrames <= 0)
        {
            return;
        }

        float volume = SanitizeReferenceVolume(effectiveVolume);
        if (volume <= 0f || !SpatializedSoundEngine.CanPlay(volume))
        {
            return;
        }

        TrimPendingQueue(PendingNativeCues);
        PendingNativeCues.Add(new PendingNativeCue(
            style,
            volume,
            float.IsFinite(pitch) ? MathHelper.Clamp(pitch, -1f, 1f) : 0f,
            ComputePlayFrame(sourceIntervalFrames)));
    }

    public static void Update()
    {
        CleanupFinishedInstances();

        if (Main.dedServ || !SpatializedSoundEngine.CanPlay())
        {
            ClearPending();
            return;
        }

        ulong currentFrame = Main.GameUpdateCount;
        PlayDueGeneratedCues(currentFrame);
        PlayDueNativeCues(currentFrame);
    }

    public static void Reset()
    {
        ClearPending();
        SpatializedSoundEngine.StopAndDisposeAll(ActiveGeneratedInstances);
        StopAllNativeCues();
    }

    private static void PlayDueGeneratedCues(ulong currentFrame)
    {
        for (int i = PendingGeneratedCues.Count - 1; i >= 0; i--)
        {
            PendingGeneratedCue cue = PendingGeneratedCues[i];
            if (currentFrame < cue.PlayFrame)
            {
                continue;
            }

            PendingGeneratedCues.RemoveAt(i);

            SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                cue.CenteredTone,
                cue.Volume,
                pitch: 0f);
            if (instance is not null)
            {
                ActiveGeneratedInstances.Add(instance);
            }
        }
    }

    private static void PlayDueNativeCues(ulong currentFrame)
    {
        for (int i = PendingNativeCues.Count - 1; i >= 0; i--)
        {
            PendingNativeCue cue = PendingNativeCues[i];
            if (currentFrame < cue.PlayFrame)
            {
                continue;
            }

            PendingNativeCues.RemoveAt(i);
            SlotId slot = SoundEngine.PlaySound(
                cue.Style with { MaxInstances = 0 },
                position: null,
                sound =>
                {
                    sound.Position = null;
                    sound.Volume = cue.Volume * AudioVolumeDefaults.WorldCueVolumeScale;
                    sound.Pitch = cue.Pitch;
                    if (sound.Sound is not null && !sound.Sound.IsDisposed)
                    {
                        sound.Sound.Pan = 0f;
                    }

                    return true;
                });

            if (slot.IsValid)
            {
                ActiveNativeCueSlots.Add(slot);
            }
        }
    }

    private static ulong ComputePlayFrame(int sourceIntervalFrames) =>
        Main.GameUpdateCount + (ulong)ComputeReferenceDelayFrames(sourceIntervalFrames);

    private static int ComputeReferenceDelayFrames(int sourceIntervalFrames)
    {
        int safeInterval = Math.Max(1, sourceIntervalFrames);
        return Math.Max(1, (int)MathF.Round(safeInterval * 0.5f));
    }

    private static float SanitizeReferenceVolume(float effectiveVolume) =>
        float.IsFinite(effectiveVolume)
            ? MathHelper.Clamp(effectiveVolume * ReferenceVolumeScale, 0f, 1f)
            : 0f;

    private static void ClearPending()
    {
        PendingGeneratedCues.Clear();
        PendingNativeCues.Clear();
    }

    private static void CleanupFinishedInstances()
    {
        SpatializedSoundEngine.CleanupStopped(ActiveGeneratedInstances);
        CleanupFinishedNativeCueSlots();
    }

    private static void CleanupFinishedNativeCueSlots()
    {
        for (int i = ActiveNativeCueSlots.Count - 1; i >= 0; i--)
        {
            if (!SoundEngine.TryGetActiveSound(ActiveNativeCueSlots[i], out ActiveSound? activeSound) ||
                !activeSound.IsPlayingOrPaused)
            {
                ActiveNativeCueSlots.RemoveAt(i);
            }
        }
    }

    private static void StopAllNativeCues()
    {
        for (int i = ActiveNativeCueSlots.Count - 1; i >= 0; i--)
        {
            if (SoundEngine.TryGetActiveSound(ActiveNativeCueSlots[i], out ActiveSound? activeSound))
            {
                activeSound.Stop();
            }
        }

        ActiveNativeCueSlots.Clear();
    }

    private static void TrimPendingQueue<T>(List<T> queue)
    {
        if (queue.Count < MaxPendingCues)
        {
            return;
        }

        queue.RemoveAt(0);
    }

    private readonly record struct PendingGeneratedCue(SoundEffect CenteredTone, float Volume, ulong PlayFrame);

    private readonly record struct PendingNativeCue(SoundStyle Style, float Volume, float Pitch, ulong PlayFrame);
}

public sealed class ReferenceToneCueSystem : ModSystem
{
    public override void PostUpdateEverything()
    {
        ReferenceToneCuePlayer.Update();
    }

    public override void OnWorldUnload()
    {
        ReferenceToneCuePlayer.Reset();
    }

    public override void Unload()
    {
        ReferenceToneCuePlayer.Reset();
    }
}
