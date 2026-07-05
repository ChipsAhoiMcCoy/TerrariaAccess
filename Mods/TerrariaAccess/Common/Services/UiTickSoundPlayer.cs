#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Systems;
using Terraria;

namespace TerrariaAccess.Common.Services;

internal static class UiTickSoundPlayer
{
    private const int MaxActiveInstances = 8;
    private const float TickFrequency = 1200f;
    private const float TickDuration = 0.04f;
    private const float TickGain = 0.45f;
    private static readonly bool UiTickDebugEnabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_UI_TICKS")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

    private static readonly ToneEnvelope TickEnvelope = new(
        attackFraction: 0.05f,
        releaseFraction: 0.6f,
        applyHannWindow: true);

    private static readonly SpatializedSoundCache TickSounds = new();
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
            _initialized = true;
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiTickSoundPlayer] Failed to create tick sound: {ex.Message}");
        }
    }

    public static void PlaySpatialTick(float normalizedScreenX, float pitch, float volume = 1f, string? debugContext = null)
    {
        if (!SpatializedSoundEngine.CanPlay(volume))
        {
            return;
        }

        bool spatialEnabled = TerrariaAccessConfig.Instance?.SpatialInventoryAudio ?? true;
        if (!spatialEnabled)
        {
            LogTickDebug("spatial-disabled-suppressed", normalizedScreenX, pitch, volume, debugContext);
            return;
        }

        SoundEffect? tickSound = EnsureTickSound(normalizedScreenX);
        if (tickSound is null or { IsDisposed: true })
        {
            LogTickDebug("synth-unavailable-suppressed", normalizedScreenX, pitch, volume, debugContext);
            return;
        }

        try
        {
            CleanupFinishedInstances();

            if (ActiveInstances.Count >= MaxActiveInstances)
            {
                LogTickDebug("dropped-max-instances", normalizedScreenX, pitch, volume, debugContext);
                return;
            }

            SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedInterfaceCue(
                tickSound,
                volume,
                pitch);
            if (instance is not null)
            {
                ActiveInstances.Add(instance);
                LogTickDebug("play", normalizedScreenX, instance.Pitch, instance.Volume, debugContext);
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

    private static SoundEffect? EnsureTickSound(float normalizedScreenX)
    {
        Initialize();

        try
        {
            return TickSounds.GetOrCreate(normalizedScreenX, quantizedNormalizedScreenX =>
                SpatializedSoundEngine.CreateSpatialSineTone(
                TickFrequency,
                TickDuration,
                TickEnvelope,
                TickGain,
                quantizedNormalizedScreenX));
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiTickSoundPlayer] Failed to create spatial tick sound: {ex.Message}");
            return null;
        }
    }

    public static void Dispose()
    {
        SpatializedSoundEngine.StopAndDisposeAll(ActiveInstances);
        TickSounds.Dispose();
        _initialized = false;
    }

    private static void CleanupFinishedInstances()
    {
        SpatializedSoundEngine.CleanupStopped(ActiveInstances);
    }

    private static void LogTickDebug(string action, float normalizedScreenX, float pitch, float volume, string? debugContext)
    {
        if (!UiTickDebugEnabled)
        {
            return;
        }

        int linkPoint = Terraria.UI.Gamepad.UILinkPointNavigator.CurrentPoint;
        bool usingGamepad = Terraria.GameInput.PlayerInput.UsingGamepadUI;
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(
            $"[UiTickDebug] action={action} normalizedScreenX={normalizedScreenX:F3} pitch={pitch:F3} volume={volume:F3} " +
            $"activeInstances={ActiveInstances.Count} linkPoint={linkPoint} usingGamepad={usingGamepad} " +
            $"inputMode={Terraria.GameInput.PlayerInput.CurrentInputMode} context={debugContext ?? "<none>"}");
    }
}
