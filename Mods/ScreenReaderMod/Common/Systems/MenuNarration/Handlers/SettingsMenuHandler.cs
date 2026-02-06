#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles all settings menus including audio, video, interface, cursor, and gameplay settings.
/// Includes slider handling for volume and other adjustable settings.
/// </summary>
internal sealed class SettingsMenuHandler : MenuHandlerBase
{
    // Settings menu modes
    private const int AudioMenuMode = 26;
    private const int GeneralSettingsMode = 112;
    private const int InterfaceSettingsMode = 1112;
    private const int VideoSettingsMode = 1111;
    private const int EffectsMenuMode = 2008;
    private const int ResolutionMenuMode = 111;
    private const int CursorSettingsMode = 1125;
    private const int GameplaySettingsMode = 1127;
    private const int TmlSettingsMode = 10017;
    private const int MainSettingsMode = 11;

    public override int Priority => 60;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        return IsSettingsMenuMode(menuMode);
    }

    private static bool IsSettingsMenuMode(int menuMode)
    {
        return menuMode is AudioMenuMode or GeneralSettingsMode or InterfaceSettingsMode or
            VideoSettingsMode or EffectsMenuMode or ResolutionMenuMode or CursorSettingsMode or
            GameplaySettingsMode or TmlSettingsMode or MainSettingsMode;
    }

    public override void OnEntered(MenuNarrationContext context)
    {
        base.OnEntered(context);
        ResetSliderTracking();
    }

    public override void OnLeft()
    {
        base.OnLeft();
        ResetSliderTracking();
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            return events;
        }

        int currentMode = context.MenuMode;
        DateTime now = context.Timestamp;

        if (ModeJustEntered)
        {
            HandleMenuModeChanged(context, now, events);
            ModeJustEntered = false;
            return events;
        }

        // Handle hover events
        if (TryHandleSettingsHover(context, events))
        {
            return events;
        }

        // Handle slider adjustments
        if (TryHandleSettingsSlider(currentMode, events))
        {
            return events;
        }

        // Handle focus changes
        if (!TryHandleFocus(context, false, events))
        {
            // No fallback for settings menus
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        // Handle hover if available
        if (TryHandleSettingsHover(context, events))
        {
            return;
        }

        // Handle slider if on a slider
        if (TryHandleSettingsSlider(context.MenuMode, events))
        {
            return;
        }

        // Force focus announcement
        TryHandleFocus(context, true, events);
    }

    private bool TryHandleSettingsHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        if (!UiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover))
        {
            return false;
        }

        if (!hover.IsNew)
        {
            return true;
        }

        string cleaned = TextSanitizer.Clean(hover.Text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        // Filter out headers and volume labels
        string lower = cleaned.ToLowerInvariant();
        if (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("sound"))
        {
            return false;
        }

        // Filter out mode titles
        if (!base.IsAllowedHover(context.MenuMode, cleaned))
        {
            return false;
        }

        // Special filter for tModLoader settings title
        if (context.MenuMode == TmlSettingsMode && lower.Contains("tmodloader"))
        {
            return false;
        }

        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }

    #region Slider Handling

    private void ResetSliderTracking()
    {
        State.ResetSliderTracking();
    }

    private bool TryHandleSettingsSlider(int currentMode, List<MenuNarrationEvent> events)
    {
        if (!Main.gameMenu)
        {
            return false;
        }

        bool audioMenu = currentMode == AudioMenuMode;
        int sliderIndex = IngameOptions.rightLock >= 0 ? IngameOptions.rightLock : IngameOptions.rightHover;
        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        bool hasSpecialSlider = special == 1;
        bool hasSliderIndex = sliderIndex >= 0;

        // For audio menu, check focusMenu first to detect back button regardless of slider state.
        if (audioMenu)
        {
            int focusMenu = GetFocusMenu();
            bool isOnBackButton = focusMenu == 2;

            if (isOnBackButton)
            {
                if (!State.WasOnAudioMenuBackButton)
                {
                    State.WasOnAudioMenuBackButton = true;
                    State.LastSliderId = -1;
                    State.LastSliderKind = MenuSliderKind.Unknown;

                    string backLabel = GetBackLabel();
                    ScreenReaderMod.Instance?.Logger.Info($"[SettingsHandler] Audio back button: {backLabel}");
                    events.Add(new MenuNarrationEvent(backLabel, true, MenuNarrationEventKind.Focus));
                    State.LastFocusAnnouncement = backLabel;
                    State.LastFocusAnnouncedAt = DateTime.UtcNow;
                }
                return true;
            }
            else if (State.WasOnAudioMenuBackButton)
            {
                State.WasOnAudioMenuBackButton = false;
                ResetSliderTracking();
            }
        }

        if (!hasSliderIndex && !hasSpecialSlider)
        {
            if (audioMenu)
            {
                // Wait for slider links to appear before narrating
                return true;
            }

            ResetSliderTracking();
            return false;
        }

        int categoryId = InGameNarrationSystem.OptionsTracker?.GetCurrentCategory() ?? -1;
        if (categoryId != State.LastCategoryId)
        {
            ResetSliderTracking();
            State.LastCategoryId = categoryId;
        }

        string sliderLabel = hasSliderIndex ? GetSliderLabel(sliderIndex) : string.Empty;
        MenuSliderKind kind = DetectSliderKind(sliderIndex, sliderLabel, special, categoryId, currentMode);

        if (hasSpecialSlider && kind == MenuSliderKind.Unknown)
        {
            kind = MenuSliderKind.Parallax;
        }

        if (audioMenu)
        {
            kind = ResolveAudioSliderKind(sliderIndex, special, kind);
        }

        if (kind == MenuSliderKind.Unknown)
        {
            ResetSliderTracking();
            return false;
        }

        if (string.IsNullOrWhiteSpace(sliderLabel))
        {
            sliderLabel = SliderNarrationHelper.GetDefaultSliderLabel(kind);
        }

        if (audioMenu)
        {
            sliderLabel = SliderNarrationHelper.GetDefaultSliderLabel(kind);
        }

        float percent = ReadSliderPercent(kind);
        ref float lastValue = ref GetLastSliderValue(kind);

        int sliderId = hasSliderIndex ? sliderIndex : 1000 + special;
        bool sliderChanged = sliderId != State.LastSliderId || kind != State.LastSliderKind;
        bool valueChanged = Math.Abs(percent - lastValue) >= 1f;

        if (!sliderChanged && !valueChanged)
        {
            return true;
        }

        // Build slider announcement directly (no EnqueuePrefix) for fluid speech without pauses
        string announcement = SliderNarrationHelper.BuildSliderAnnouncement(sliderLabel, kind, percent, includeLabel: sliderChanged);

        State.LastSliderId = sliderId;
        State.LastSliderKind = kind;
        lastValue = percent;
        State.LastFocus = null;

        ScreenReaderMod.Instance?.Logger.Info($"[SettingsHandler] Slider {sliderId} ({kind}) -> {announcement}");
        events.Add(new MenuNarrationEvent(announcement, true, MenuNarrationEventKind.Slider));
        return true;
    }

    private static MenuSliderKind ResolveAudioSliderKind(int sliderIndex, int special, MenuSliderKind current)
    {
        if (special is 2 or 3 or 4)
        {
            return special switch
            {
                2 => MenuSliderKind.Music,
                3 => MenuSliderKind.Sound,
                4 => MenuSliderKind.Ambient,
                _ => current,
            };
        }

        if (current == MenuSliderKind.Unknown)
        {
            return sliderIndex switch
            {
                3 => MenuSliderKind.Music,
                2 => MenuSliderKind.Sound,
                4 => MenuSliderKind.Ambient,
                0 => MenuSliderKind.Music,
                1 => MenuSliderKind.Sound,
                5 => MenuSliderKind.Ambient,
                _ => MenuSliderKind.Unknown,
            };
        }

        return current;
    }

    private static string GetSliderLabel(int sliderIndex)
    {
        if (InGameNarrationSystem.OptionsTracker?.TryGetCurrentOptionLabel(sliderIndex, out string label) == true &&
            !string.IsNullOrWhiteSpace(label))
        {
            return TextSanitizer.Clean(label);
        }

        return string.Empty;
    }

    private static MenuSliderKind DetectSliderKind(int sliderIndex, string label, int specialFeature, int categoryId, int menuMode)
    {
        string sanitized = TextSanitizer.Clean(label);
        string lower = sanitized.ToLowerInvariant();

        if (lower.Contains("back", StringComparison.Ordinal))
        {
            return MenuSliderKind.Unknown;
        }

        MenuSliderKind kind = MenuSliderKind.Unknown;
        if (lower.Contains("music", StringComparison.Ordinal))
        {
            kind = MenuSliderKind.Music;
        }
        else if (lower.Contains("sound", StringComparison.Ordinal))
        {
            kind = MenuSliderKind.Sound;
        }
        else if (lower.Contains("ambient", StringComparison.Ordinal) ||
            lower.Contains("ambience", StringComparison.Ordinal))
        {
            kind = MenuSliderKind.Ambient;
        }
        else if (lower.Contains("zoom", StringComparison.Ordinal))
        {
            kind = MenuSliderKind.Zoom;
        }
        else if (lower.Contains("ui scale", StringComparison.Ordinal) ||
            lower.Contains("ui-scale", StringComparison.Ordinal) ||
            lower.Contains("interface scale", StringComparison.Ordinal))
        {
            kind = MenuSliderKind.InterfaceScale;
        }

        if (menuMode == AudioMenuMode)
        {
            MenuSliderKind indexed = sliderIndex switch
            {
                0 => MenuSliderKind.Music,
                1 => MenuSliderKind.Sound,
                2 => MenuSliderKind.Ambient,
                3 => MenuSliderKind.Music,
                4 => MenuSliderKind.Sound,
                5 => MenuSliderKind.Ambient,
                _ => MenuSliderKind.Unknown,
            };

            if (indexed != MenuSliderKind.Unknown)
            {
                return indexed;
            }
        }

        if (kind != MenuSliderKind.Unknown)
        {
            return kind;
        }

        switch (specialFeature)
        {
            case 1:
                return MenuSliderKind.Parallax;
            case 2:
                return MenuSliderKind.Music;
            case 3:
                return MenuSliderKind.Sound;
            case 4:
                return MenuSliderKind.Ambient;
        }

        if (categoryId == 2)
        {
            return MenuSliderKind.Zoom;
        }

        if (categoryId == 1)
        {
            return MenuSliderKind.InterfaceScale;
        }

        if (categoryId == 3)
        {
            return sliderIndex switch
            {
                0 => MenuSliderKind.Music,
                1 => MenuSliderKind.Sound,
                2 => MenuSliderKind.Ambient,
                _ => MenuSliderKind.Unknown,
            };
        }

        return MenuSliderKind.Unknown;
    }

    private static float ReadSliderPercent(MenuSliderKind kind)
    {
        return kind switch
        {
            MenuSliderKind.Music => MathF.Round(Math.Clamp(Main.musicVolume, 0f, 1f) * 100f),
            MenuSliderKind.Sound => MathF.Round(Math.Clamp(Main.soundVolume, 0f, 1f) * 100f),
            MenuSliderKind.Ambient => MathF.Round(Math.Clamp(Main.ambientVolume, 0f, 1f) * 100f),
            MenuSliderKind.Zoom => MathF.Round(Math.Clamp(Main.GameZoomTarget, 0.01f, 4f) * 100f),
            MenuSliderKind.InterfaceScale => MathF.Round(Math.Clamp(Main.UIScaleWanted > 0f ? Main.UIScaleWanted : Main.UIScale, 0.1f, 4f) * 100f),
            MenuSliderKind.Parallax => Utils.Clamp(Main.bgScroll, 0, 100),
            _ => 0f,
        };
    }

    private ref float GetLastSliderValue(MenuSliderKind kind)
    {
        switch (kind)
        {
            case MenuSliderKind.Sound:
                return ref State.LastSoundVolume;
            case MenuSliderKind.Ambient:
                return ref State.LastAmbientVolume;
            case MenuSliderKind.Zoom:
                return ref State.LastZoom;
            case MenuSliderKind.InterfaceScale:
                return ref State.LastInterfaceScale;
            case MenuSliderKind.Parallax:
                return ref State.LastParallax;
            case MenuSliderKind.Music:
            default:
                return ref State.LastMusicVolume;
        }
    }

    #endregion

    protected override bool IsAllowedHover(int menuMode, string cleanedLabel)
    {
        if (string.IsNullOrWhiteSpace(cleanedLabel))
        {
            return false;
        }

        string lower = cleanedLabel.ToLowerInvariant();

        // Filter out header-like audio text and menu titles.
        if (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("sound"))
        {
            return false;
        }

        if (menuMode == TmlSettingsMode && lower.Contains("tmodloader"))
        {
            return false;
        }

        return base.IsAllowedHover(menuMode, cleanedLabel);
    }
}
