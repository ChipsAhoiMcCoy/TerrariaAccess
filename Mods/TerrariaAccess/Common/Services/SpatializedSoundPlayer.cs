#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Systems;
using Terraria;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Plays sounds that already contain their spatial information in the rendered stereo buffer.
/// Do not set <see cref="SoundEffectInstance.Pan"/> for these instances; horizontal direction
/// is encoded by interaural time delay during synthesis.
/// </summary>
[Obsolete("Use SpatializedSoundEngine for custom sound playback so ITD, pitch, volume, and lifecycle stay behind one facade.")]
internal static class SpatializedSoundPlayer
{
    private static SoundEffectInstance? Play(
        SoundEffect? sound,
        float volume,
        float pitch = 0f,
        bool looped = false)
    {
        float safeVolume = ClampVolume(volume);
        if (Main.dedServ || GetMasterSoundVolume() <= 0f || !IsSoundUsable(sound) || safeVolume <= 0f)
        {
            return null;
        }

        SoundEffectInstance? instance = null;
        try
        {
            instance = sound!.CreateInstance();
            instance.IsLooped = looped;
            instance.Pan = 0f;
            instance.Pitch = ClampPitch(pitch);
            instance.Volume = safeVolume;
            instance.Play();
            return instance;
        }
        catch
        {
            DisposeInstanceQuietly(instance);
            return null;
        }
    }

    internal static SoundEffectInstance? PlayAlreadySpatializedWorldCue(
        SoundEffect? sound,
        float volume,
        float pitch = 0f,
        bool looped = false)
    {
        return Play(
            sound,
            ClampVolume(volume) * GetMasterSoundVolume() * AudioVolumeDefaults.WorldCueVolumeScale,
            pitch,
            looped);
    }

    internal static SoundEffectInstance? PlayInterfaceCue(
        SoundEffect? sound,
        float volume,
        float pitch = 0f,
        bool looped = false)
    {
        return Play(
            sound,
            ClampVolume(volume) * GetMasterSoundVolume() * AudioVolumeDefaults.InterfaceCueVolumeScale,
            pitch,
            looped);
    }

    internal static SoundEffectInstance? PlayWorldCue(
        SoundEffect? sound,
        SpatializedSoundEngine.SpatialAudioSample sample,
        float localVolumeScale,
        bool looped = false)
    {
        return PlayAlreadySpatializedWorldCue(
            sound,
            sample.ScaleVolume(localVolumeScale),
            sample.Pitch,
            looped);
    }

    internal static void StopAndDispose(SoundEffectInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            if (!instance.IsDisposed)
            {
                instance.Stop();
            }
        }
        catch
        {
            // Ignore backend stop failures during unload/reset.
        }

        DisposeInstanceQuietly(instance);
    }

    internal static void StopInstanceAndDisposeEffect(SoundEffectInstance? instance, SoundEffect? effect)
    {
        StopAndDispose(instance);
        if (IsSoundUsable(effect))
        {
            try
            {
                effect.Dispose();
            }
            catch
            {
                // Ignore backend dispose failures during unload/reset.
            }
        }
    }

    private static void SetVolume(SoundEffectInstance? instance, float volume)
    {
        if (!IsInstanceUsable(instance))
        {
            return;
        }

        try
        {
            instance.Volume = ClampVolume(volume);
        }
        catch
        {
            // Ignore backend volume-update failures for stale instances.
        }
    }

    internal static void SetWorldCueVolume(SoundEffectInstance? instance, float volume)
    {
        SetVolume(
            instance,
            ClampVolume(volume) * GetMasterSoundVolume() * AudioVolumeDefaults.WorldCueVolumeScale);
    }

    internal static void CleanupStopped(IList<SoundEffectInstance>? instances)
    {
        if (instances is null)
        {
            return;
        }

        for (int i = instances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance? instance = instances[i];
            if (instance is null)
            {
                instances.RemoveAt(i);
                continue;
            }

            bool shouldRemove;
            try
            {
                shouldRemove = instance.IsDisposed || instance.State == SoundState.Stopped;
            }
            catch
            {
                shouldRemove = true;
            }

            if (shouldRemove)
            {
                StopAndDispose(instance);
                instances.RemoveAt(i);
            }
        }
    }

    internal static void StopAndDisposeAll(IList<SoundEffectInstance>? instances)
    {
        if (instances is null)
        {
            return;
        }

        for (int i = instances.Count - 1; i >= 0; i--)
        {
            StopAndDispose(instances[i]);
        }

        instances.Clear();
    }

    private static float GetMasterSoundVolume() => ClampVolume(NativeSoundSuppression.GetEffectiveSoundVolume());

    private static bool IsInstanceUsable([NotNullWhen(true)] SoundEffectInstance? instance)
    {
        if (instance is null)
        {
            return false;
        }

        try
        {
            return !instance.IsDisposed;
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeInstanceQuietly(SoundEffectInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            instance.Dispose();
        }
        catch
        {
            // Ignore backend dispose failures during unload/reset.
        }
    }

    private static bool IsSoundUsable([NotNullWhen(true)] SoundEffect? sound)
    {
        if (sound is null)
        {
            return false;
        }

        try
        {
            return !sound.IsDisposed;
        }
        catch
        {
            return false;
        }
    }

    private static float ClampVolume(float volume) =>
        float.IsFinite(volume) ? MathHelper.Clamp(volume, 0f, 1f) : 0f;

    private static float ClampPitch(float pitch) =>
        float.IsFinite(pitch) ? MathHelper.Clamp(pitch, -1f, 1f) : 0f;
}
