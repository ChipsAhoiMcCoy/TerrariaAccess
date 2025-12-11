#nullable enable
using System;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuNarrationState
{
    internal int LastMenuMode = -1;
    internal MenuFocus? LastFocus;
    internal bool AnnouncedFallback;
    internal int FocusFailureCount;
    internal bool ForceNextFocus;
    internal string? LastHoverAnnouncement;
    internal string? LastFocusAnnouncement;
    internal DateTime LastFocusAnnouncedAt = DateTime.MinValue;
    internal DateTime LastHoverAnnouncedAt = DateTime.MinValue;
    internal bool SawHoverThisMode;
    internal DateTime ModeEnteredAt = DateTime.MinValue;
    internal string? LastModeAnnouncement;
    internal DateTime LastModeAnnouncedAt = DateTime.MinValue;
    internal int LastSliderId = -1;
    internal MenuSliderKind LastSliderKind = MenuSliderKind.Unknown;
    internal float LastMusicVolume = -1f;
    internal float LastSoundVolume = -1f;
    internal float LastAmbientVolume = -1f;
    internal float LastZoom = -1f;
    internal float LastInterfaceScale = -1f;
    internal float LastParallax = -1f;
    internal int LastCategoryId = -1;

    internal void ResetForMode(int mode)
    {
        LastMenuMode = mode;
        LastFocus = null;
        AnnouncedFallback = false;
        FocusFailureCount = 0;
        ForceNextFocus = true;
        LastHoverAnnouncement = null;
        LastHoverAnnouncedAt = DateTime.MinValue;
        SawHoverThisMode = false;
        ModeEnteredAt = DateTime.UtcNow;
        ResetSliderTracking();
    }

    internal void ResetAll()
    {
        ResetForMode(-1);
        ForceNextFocus = false;
        ModeEnteredAt = DateTime.MinValue;
        LastFocusAnnouncement = null;
        LastFocusAnnouncedAt = DateTime.MinValue;
        LastModeAnnouncement = null;
        LastModeAnnouncedAt = DateTime.MinValue;
    }

    internal void ResetSliderTracking()
    {
        LastSliderId = -1;
        LastSliderKind = MenuSliderKind.Unknown;
        LastMusicVolume = -1f;
        LastSoundVolume = -1f;
        LastAmbientVolume = -1f;
        LastZoom = -1f;
        LastInterfaceScale = -1f;
        LastParallax = -1f;
        LastCategoryId = -1;
    }
}

internal enum MenuSliderKind
{
    Unknown = 0,
    Music = 1,
    Sound = 2,
    Ambient = 3,
    Zoom = 4,
    InterfaceScale = 5,
    Parallax = 6,
}
