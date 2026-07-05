#nullable enable
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace TerrariaAccess.Common.Services;

internal enum UiSoundCue
{
    Tick,
    Open,
    Close,
}

/// <summary>
/// Central path for native Terraria UI feedback cues.
/// </summary>
internal static class UiSoundCuePlayer
{
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
        PlayNativeCue(UiSoundCue.Tick, volume);
    }

    public static void PlayOpen(float volume = 1f) =>
        PlayNativeCue(UiSoundCue.Open, volume);

    public static void PlayClose(float volume = 1f) =>
        PlayNativeCue(UiSoundCue.Close, volume);

    public static void PlayCloseOrTick(bool close, float volume = 1f)
    {
        if (close)
        {
            PlayClose(volume);
            return;
        }

        PlayTick(volume);
    }

    private static void PlayNativeCue(UiSoundCue cue, float volume)
    {
        if (Main.dedServ || volume <= 0f)
        {
            return;
        }

        try
        {
            SoundStyle style = cue switch
            {
                UiSoundCue.Open => SoundID.MenuOpen,
                UiSoundCue.Close => SoundID.MenuClose,
                _ => SoundID.MenuTick,
            };

            SoundEngine.PlaySound(volume == 1f ? style : style.WithVolumeScale(volume));
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[UiSoundCuePlayer] Failed to play {cue} cue: {ex.Message}");
        }
    }

    public static void Dispose()
    {
    }
}
