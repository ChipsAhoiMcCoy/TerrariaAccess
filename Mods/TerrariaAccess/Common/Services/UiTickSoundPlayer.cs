#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Systems;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace TerrariaAccess.Common.Services;

internal static class UiTickSoundPlayer
{
    private const int MaxActiveInstances = 8;
    private const float TickFrequency = 1200f;
    private const float TickDuration = 0.04f;
    private const float TickGain = 0.45f;

    private static readonly ToneEnvelope TickEnvelope = new(
        attackFraction: 0.05f,
        releaseFraction: 0.6f,
        applyHannWindow: true);

    private static SoundEffect? _tickSound;
    private static readonly List<SoundEffectInstance> ActiveInstances = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            _tickSound = SynthesizedSoundFactory.CreateSineTone(
                TickFrequency,
                TickDuration,
                TickEnvelope,
                TickGain);
            _initialized = true;
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiTickSoundPlayer] Failed to create tick sound: {ex.Message}");
        }
    }

    public static void PlaySpatialTick(float pan, float pitch, float volume = 1f)
    {
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        bool spatialEnabled = TerrariaAccessConfig.Instance?.SpatialInventoryAudio ?? true;
        if (!spatialEnabled)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        if (_tickSound is null or { IsDisposed: true })
        {
            Initialize();
            if (_tickSound is null or { IsDisposed: true })
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }
        }

        try
        {
            CleanupFinishedInstances();

            if (ActiveInstances.Count >= MaxActiveInstances)
            {
                return;
            }

            SoundEffectInstance instance = _tickSound.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = MathHelper.Clamp(pan, -1f, 1f);
            instance.Pitch = MathHelper.Clamp(pitch, -1f, 1f);
            instance.Volume = MathHelper.Clamp(volume * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);

            try
            {
                instance.Play();
                ActiveInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Debug($"[UiTickSoundPlayer] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiTickSoundPlayer] Tick playback failed: {ex.Message}");
        }
    }

    public static void Cleanup()
    {
        CleanupFinishedInstances();
    }

    public static void Dispose()
    {
        foreach (SoundEffectInstance instance in ActiveInstances)
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

        ActiveInstances.Clear();

        if (_tickSound is { IsDisposed: false })
        {
            _tickSound.Dispose();
        }

        _tickSound = null;
        _initialized = false;
    }

    private static void CleanupFinishedInstances()
    {
        for (int i = ActiveInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = ActiveInstances[i];
            if (instance.IsDisposed || instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                ActiveInstances.RemoveAt(i);
            }
        }
    }
}
