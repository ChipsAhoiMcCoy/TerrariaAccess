#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuNarrationController
{
    private readonly MenuFocusResolver _focusResolver = new();
    private readonly MenuUiSelectionTracker _uiSelectionTracker = new();
    private readonly ModConfigMenuNarrator _modConfigNarrator = new();
    private readonly MenuNarrationState _state = new();

    public void Process(Main main)
    {
        if (!Main.gameMenu)
        {
            ResetState();
            return;
        }

        int currentMode = Main.menuMode;
        if (currentMode != _state.LastMenuMode)
        {
            HandleMenuModeChanged(main, currentMode);
            return;
        }

        if (TryHandleSettingsSpecialFeature())
        {
            return;
        }

        if (TryHandleUiHover())
        {
            return;
        }

        if (TryHandleSettingsSlider())
        {
            return;
        }

        if (_modConfigNarrator.TryHandleFancyUi(Main.menuMode, Main.MenuUI))
        {
            return;
        }

        if (!TryHandleFocus(main, currentMode, force: false))
        {
            AnnounceFallback(currentMode);
        }
    }

    private void HandleMenuModeChanged(Main main, int currentMode)
    {
        _state.ResetForMode(currentMode);
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        _modConfigNarrator.Reset();

        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(currentMode, Main.MenuUI?.CurrentState);
        DateTime now = DateTime.UtcNow;
        bool modeRepeat = !string.IsNullOrWhiteSpace(_state.LastModeAnnouncement) &&
            string.Equals(modeLabel, _state.LastModeAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            now - _state.LastModeAnnouncedAt < TimeSpan.FromSeconds(1);
        if (!modeRepeat)
        {
            ScreenReaderService.Announce($"{modeLabel}.", force: true);
            _state.LastModeAnnouncement = modeLabel;
            _state.LastModeAnnouncedAt = now;
        }
        MenuNarrationCatalog.LogMenuSnapshot(currentMode);

        UIState? uiState = Main.MenuUI?.CurrentState;
        if (uiState is not null)
        {
            ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI state: {uiState.GetType().FullName}");
        }
        else
        {
            ScreenReaderMod.Instance?.Logger.Info("[MenuNarration] UI state: <null>");
        }

        if (TryHandleSettingsSpecialFeature())
        {
            return;
        }

        if (TryHandleUiHover())
        {
            return;
        }

        if (TryHandleSettingsSlider())
        {
            return;
        }

        if (!TryHandleFocus(main, currentMode, force: true))
        {
            AnnounceFallback(currentMode);
        }
    }

    private bool TryHandleUiHover()
    {
        if (!_uiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover))
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

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI hover -> {cleaned}");
        ScreenReaderService.Announce(cleaned);
        _state.LastHoverAnnouncement = cleaned;
        _state.LastHoverAnnouncedAt = DateTime.UtcNow;
        _state.SawHoverThisMode = true;
        return true;
    }

    private void ResetSliderTracking()
    {
        _state.ResetSliderTracking();
    }

    private bool TryHandleSettingsSpecialFeature()
    {
        if (!Main.gameMenu)
        {
            return false;
        }

        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        if (special <= 0)
        {
            _state.LastSpecialFeature = special;
            return false;
        }

        int currentMode = Main.menuMode;
        bool inSettingsMenu = currentMode is 26 or 112 or 1112 or 1111 or 2008 or 111 or 1125 or 1127 or 10017;
        if (!inSettingsMenu)
        {
            _state.LastSpecialFeature = special;
            return false;
        }

        bool handled = false;
        switch (special)
        {
            case 1:
            {
                int parallax = Utils.Clamp(Main.bgScroll, 0, 100);
                bool parallaxChanged = parallax != _state.LastParallax || _state.LastSpecialFeature != special;
                if (parallaxChanged)
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Parallax slider -> {parallax}%");
                    ScreenReaderService.Announce($"Background parallax {parallax} percent", force: true);
                    _state.LastParallax = parallax;
                    handled = true;
                }

                _state.LastParallax = parallax;
                break;
            }
            case 2:
            case 3:
            case 4:
                _state.LastSpecialFeature = special;
                return handled;
        }

        _state.LastSpecialFeature = special;
        return handled;
    }

    private bool TryHandleSettingsSlider()
    {
        if (!Main.gameMenu)
        {
            return false;
        }

        int currentMode = Main.menuMode;
        if (!IsSettingsMenuMode(currentMode))
        {
            return false;
        }

        bool audioMenu = currentMode == 26;
        int sliderIndex = IngameOptions.rightLock >= 0 ? IngameOptions.rightLock : IngameOptions.rightHover;
        if (sliderIndex < 0)
        {
            ResetSliderTracking();
            if (audioMenu)
            {
                _state.ForceNextFocus = true;
            }
            return false;
        }

        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        int categoryId = InGameNarrationSystem.IngameOptionsLabelTracker.GetCurrentCategory();
        if (categoryId != _state.LastCategoryId)
        {
            ResetSliderTracking();
            _state.LastCategoryId = categoryId;
        }
        string sliderLabel = GetSliderLabel(sliderIndex);
        MenuSliderKind kind = DetectSliderKind(sliderIndex, sliderLabel, special, categoryId, currentMode);
        if (audioMenu)
        {
            if (special is 2 or 3 or 4)
            {
                kind = special switch
                {
                    2 => MenuSliderKind.Music,
                    3 => MenuSliderKind.Sound,
                    4 => MenuSliderKind.Ambient,
                    _ => kind,
                };
            }

            if (kind == MenuSliderKind.Unknown)
            {
                kind = sliderIndex switch
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
        }

        if (audioMenu && IsBackLabel(sliderLabel))
        {
            LogAudioMenuDebug(sliderIndex, sliderLabel, kind, percent: 0f, categoryId, special, note: "back label detected");
            ResetSliderTracking();
            return false;
        }

        if (kind == MenuSliderKind.Unknown)
        {
            LogAudioMenuDebug(sliderIndex, sliderLabel, kind, percent: 0f, categoryId, special, note: "unknown kind");
            ResetSliderTracking();
            return false;
        }

        if (string.IsNullOrWhiteSpace(sliderLabel))
        {
            if (audioMenu)
            {
                sliderLabel = kind switch
                {
                    MenuSliderKind.Music => "Music",
                    MenuSliderKind.Sound => "Sound",
                    MenuSliderKind.Ambient => "Ambient",
                    _ => sliderLabel,
                };
            }
        }

        if (string.IsNullOrWhiteSpace(sliderLabel))
        {
            sliderLabel = GetDefaultSliderLabel(kind);
        }

        if (audioMenu)
        {
            // Normalize audio labels to avoid stale or mismatched text after returning from gameplay.
            sliderLabel = GetDefaultSliderLabel(kind);
        }

        float percent = ReadSliderPercent(kind);
        ref float lastValue = ref GetLastSliderValue(kind);

        bool sliderChanged = sliderIndex != _state.LastSliderId || kind != _state.LastSliderKind;
        bool valueChanged = Math.Abs(percent - lastValue) >= 1f;
        if (!sliderChanged && !valueChanged)
        {
            return true;
        }

        if (audioMenu)
        {
            LogAudioMenuDebug(sliderIndex, sliderLabel, kind, percent, categoryId, special, note: sliderChanged ? "audio slider focus changed" : "audio slider value changed");
        }

        string announcement = BuildSliderAnnouncement(sliderLabel, kind, percent, includeLabel: sliderChanged);
        _state.LastSliderId = sliderIndex;
        _state.LastSliderKind = kind;
        lastValue = percent;

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Slider {sliderIndex} ({kind}) -> {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
        return true;
    }

    private static bool IsSettingsMenuMode(int menuMode)
    {
        return menuMode is 26 or 112 or 1112 or 1111 or 2008 or 111 or 1125 or 1127 or 10017;
    }

    private string GetSliderLabel(int sliderIndex)
    {
        if (InGameNarrationSystem.IngameOptionsLabelTracker.TryGetCurrentOptionLabel(sliderIndex, out string label) &&
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

        if (menuMode == 26)
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
            case 2:
                return MenuSliderKind.Music;
            case 3:
                return MenuSliderKind.Sound;
            case 4:
                return MenuSliderKind.Ambient;
        }

        // Fall back to category-based detection when labels are just percentages.
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
            _ => 0f,
        };
    }

    private ref float GetLastSliderValue(MenuSliderKind kind)
    {
        switch (kind)
        {
            case MenuSliderKind.Sound:
                return ref _state.LastSoundVolume;
            case MenuSliderKind.Ambient:
                return ref _state.LastAmbientVolume;
            case MenuSliderKind.Zoom:
                return ref _state.LastZoom;
            case MenuSliderKind.InterfaceScale:
                return ref _state.LastInterfaceScale;
            case MenuSliderKind.Music:
            default:
                return ref _state.LastMusicVolume;
        }
    }

    private static bool IsBackLabel(string label)
    {
        string cleaned = TextSanitizer.Clean(label);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        string backLabel = TextSanitizer.Clean(Language.GetTextValue("UI.Back"));
        if (!string.IsNullOrWhiteSpace(backLabel) && string.Equals(cleaned, backLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string langBack = TextSanitizer.Clean(Lang.menu[5].Value);
        if (!string.IsNullOrWhiteSpace(langBack) && string.Equals(cleaned, langBack, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return cleaned.Contains("back", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("close", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogAudioMenuDebug(int sliderIndex, string sliderLabel, MenuSliderKind kind, float percent, int categoryId, int special, string note)
    {
        try
        {
            string label = TextSanitizer.Clean(sliderLabel);
            string snapshot = $"[MenuNarration][AudioDebug] idx={sliderIndex} label='{label}' kind={kind} percent={percent:0} category={categoryId} special={special} rightHover={IngameOptions.rightHover} rightLock={IngameOptions.rightLock} menuItems={MenuNarrationCatalog.DescribeMenuMode(Main.menuMode)} note={note}";
            ScreenReaderMod.Instance?.Logger.Info(snapshot);
            MenuNarrationCatalog.LogMenuSnapshot(Main.menuMode, allowRepeat: true);
        }
        catch
        {
            // best-effort debug logging
        }
    }

    private static string BuildSliderAnnouncement(string rawLabel, MenuSliderKind kind, float percent, bool includeLabel)
    {
        string baseLabel = ExtractBaseLabel(rawLabel, kind);
        if (includeLabel)
        {
            return $"{baseLabel} {percent:0} percent";
        }

        return $"{percent:0} percent";
    }

    private static string ExtractBaseLabel(string rawLabel, MenuSliderKind kind)
    {
        string sanitized = TextSanitizer.Clean(rawLabel);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            int percentIndex = sanitized.IndexOf('%');
            if (percentIndex >= 0)
            {
                sanitized = sanitized[..percentIndex];
            }

            int percentWord = sanitized.IndexOf("percent", StringComparison.OrdinalIgnoreCase);
            if (percentWord >= 0)
            {
                sanitized = sanitized[..percentWord];
            }

            sanitized = sanitized.Trim().TrimEnd(':').Trim();
            sanitized = TrimTrailingNumber(sanitized);
        }

        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        return GetDefaultSliderLabel(kind);
    }

    private static string GetDefaultSliderLabel(MenuSliderKind kind)
    {
        return kind switch
        {
            MenuSliderKind.Music => "Music volume",
            MenuSliderKind.Sound => "Sound volume",
            MenuSliderKind.Ambient => "Ambient volume",
            MenuSliderKind.Zoom => "Zoom",
            MenuSliderKind.InterfaceScale => "Interface scale",
            _ => "Slider",
        };
    }

    private static string TrimTrailingNumber(string value)
    {
        int end = value.Length;
        while (end > 0 && (char.IsWhiteSpace(value[end - 1]) || char.IsDigit(value[end - 1]) || value[end - 1] == ':' || value[end - 1] == '.'))
        {
            end--;
        }

        if (end < value.Length)
        {
            return value[..end].TrimEnd();
        }

        return value;
    }

    private bool TryHandleFocus(Main main, int currentMode, bool force)
    {
        if (!_focusResolver.TryGetFocus(main, out MenuFocus focus))
        {
            if (_state.FocusFailureCount++ < 5)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Unable to determine focus for menu mode {currentMode} (attempt {_state.FocusFailureCount}).");
            }

            return false;
        }

        _state.FocusFailureCount = 0;

        UIState? uiState = Main.MenuUI?.CurrentState;
        if (uiState is not null && !_state.SawHoverThisMode && DateTime.UtcNow - _state.ModeEnteredAt < TimeSpan.FromMilliseconds(250))
        {
            return false;
        }

        string optionLabel = MenuNarrationCatalog.DescribeMenuItem(currentMode, focus.Index);
        bool hasDeletionAnnouncement = MenuNarrationCatalog.TryBuildDeletionAnnouncement(currentMode, focus.Index, out string combinedLabel);
        string announcement = hasDeletionAnnouncement ? combinedLabel : optionLabel;

        bool focusChanged = !_state.LastFocus.HasValue || _state.LastFocus.Value.Index != focus.Index;
        bool announcementChanged = !focusChanged &&
            _state.LastFocus.HasValue &&
            _state.LastFocus.Value.Index == focus.Index &&
            !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
            !string.IsNullOrWhiteSpace(announcement) &&
            !string.Equals(announcement, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
        bool shouldAnnounce = force || focusChanged || _state.ForceNextFocus || announcementChanged;

        if (shouldAnnounce)
        {
            DateTime now = DateTime.UtcNow;
            bool matchesRecentHover = !string.IsNullOrWhiteSpace(_state.LastHoverAnnouncement) &&
                string.Equals(optionLabel, _state.LastHoverAnnouncement, StringComparison.OrdinalIgnoreCase) &&
                now - _state.LastHoverAnnouncedAt < TimeSpan.FromMilliseconds(900);
            bool matchesLastFocus = !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
                string.Equals(announcement, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
            bool repeatedRecently = matchesLastFocus && now - _state.LastFocusAnnouncedAt < TimeSpan.FromMilliseconds(900);

            bool shouldThrottleBack = currentMode == 26 && IsBackLabel(announcement) && repeatedRecently;

            if (!force && !announcementChanged && (matchesRecentHover || repeatedRecently || shouldThrottleBack))
            {
                _state.ForceNextFocus = false;
                _state.LastFocus = focus;
                return true;
            }

            if (!string.IsNullOrEmpty(optionLabel))
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Focus {focus.Index} via {focus.Source} -> {optionLabel}");
                bool forceSpeech = force || _state.ForceNextFocus || announcementChanged;
                ScreenReaderService.Announce(announcement, forceSpeech);
                _state.LastFocusAnnouncement = announcement;
                _state.LastFocusAnnouncedAt = now;

                _state.ForceNextFocus = false;
            }
            else
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Missing label for focus {focus.Index} (source {focus.Source}) in menu mode {currentMode}.");
                MenuNarrationCatalog.LogMenuSnapshot(currentMode, allowRepeat: true);
            }
        }
        else if (_state.LastFocus.HasValue && !_state.LastFocus.Value.Source.Equals(focus.Source, StringComparison.Ordinal))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Focus source switched to {focus.Source} for index {focus.Index}.");
        }

        _state.LastFocus = focus;
        _state.AnnouncedFallback = false;
        return true;
    }

    private void AnnounceFallback(int currentMode)
    {
        if (_state.AnnouncedFallback)
        {
            return;
        }

        string fallback = MenuNarrationCatalog.DescribeMenuItem(currentMode, 0);
        if (string.IsNullOrEmpty(fallback))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        bool sameAsLastFocus = !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
            string.Equals(fallback, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            now - _state.LastFocusAnnouncedAt < TimeSpan.FromSeconds(1);
        if (sameAsLastFocus)
        {
            _state.AnnouncedFallback = true;
            _state.ForceNextFocus = true;
            return;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Fallback focus -> {fallback}");
        ScreenReaderService.Announce(fallback, force: true);
        _state.AnnouncedFallback = true;
        _state.ForceNextFocus = true;
    }

    private void ResetState()
    {
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        _modConfigNarrator.Reset();
        _state.ResetAll();
    }
}
