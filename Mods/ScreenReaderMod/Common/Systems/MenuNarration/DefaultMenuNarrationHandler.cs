#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.UI;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class DefaultMenuNarrationHandler : IMenuNarrationHandler
{
    private readonly MenuFocusResolver _focusResolver = new();
    private readonly MenuUiSelectionTracker _uiSelectionTracker = new();
    private readonly ModConfigMenuNarrator _modConfigNarrator = new();
    private readonly MenuNarrationState _state = new();
    private MenuUiSelectionTracker.WorldCreationSnapshot _lastWorldCreationSnapshot;
    private bool _modeJustEntered;

    public bool CanHandle(MenuNarrationContext context)
    {
        return context.IsMenuActive;
    }

    public void OnMenuEntered(MenuNarrationContext context)
    {
        _modeJustEntered = true;
        _state.ResetForMode(context.MenuMode);
        _state.ModeEnteredAt = context.Timestamp;
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        _modConfigNarrator.Reset();
        _lastWorldCreationSnapshot = default;
    }

    public void OnMenuLeft()
    {
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        _modConfigNarrator.Reset();
        _state.ResetAll();
        _lastWorldCreationSnapshot = default;
        _modeJustEntered = false;
    }

    public IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        List<MenuNarrationEvent> events = new();
        if (!context.IsMenuActive)
        {
            return events;
        }

        int currentMode = context.MenuMode;
        DateTime now = context.Timestamp;

        if (_modeJustEntered)
        {
            HandleMenuModeChanged(context, currentMode, now, events);
            _modeJustEntered = false;
            return events;
        }

        bool hoverHandled = TryHandleUiHover(context, now, events);
        bool worldCreationHandled = TryHandleWorldCreationSnapshot(context, events);
        if (hoverHandled || worldCreationHandled)
        {
            return events;
        }

        if (TryHandleSettingsSlider(currentMode, events))
        {
            return events;
        }

        if (_modConfigNarrator.TryBuildMenuEvents(context, events))
        {
            return events;
        }

        if (!TryHandleFocus(context, currentMode, force: false, now, events))
        {
            AnnounceFallback(context, now, events);
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, int currentMode, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(currentMode, context.UiState);
        bool modeRepeat = !string.IsNullOrWhiteSpace(_state.LastModeAnnouncement) &&
            string.Equals(modeLabel, _state.LastModeAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            timestamp - _state.LastModeAnnouncedAt < TimeSpan.FromSeconds(1);
        // Suppress explicit menu title announcements; focus/hover events will provide context.
        _state.LastModeAnnouncement = modeLabel;
        _state.LastModeAnnouncedAt = timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(currentMode);

        if (context.UiState is not null)
        {
            ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI state: {context.UiState.GetType().FullName}");
        }
        else
        {
            ScreenReaderMod.Instance?.Logger.Info("[MenuNarration] UI state: <null>");
        }

        bool hoverHandled = TryHandleUiHover(context, timestamp, events);
        bool worldCreationHandled = TryHandleWorldCreationSnapshot(context, events);
        if (hoverHandled || worldCreationHandled)
        {
            return;
        }

        if (TryHandleSettingsSlider(currentMode, events))
        {
            return;
        }

        if (!TryHandleFocus(context, currentMode, force: true, timestamp, events))
        {
            AnnounceFallback(context, timestamp, events);
        }
    }

    private bool TryHandleUiHover(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        // Suppress hover announcements when Workshop Hub is handling gamepad navigation
        // to avoid conflicting announcements
        if (WorkshopHubAccessibilitySystem.IsHandlingGamepadInput)
        {
            return false;
        }

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

        if (!IsAllowedHover(context.MenuMode, cleaned))
        {
            ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI hover suppressed -> {cleaned}");
            return false;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI hover -> {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        _state.LastHoverAnnouncement = cleaned;
        _state.LastHoverAnnouncedAt = timestamp;
        _state.SawHoverThisMode = true;
        return true;
    }

    private void ResetSliderTracking()
    {
        _state.ResetSliderTracking();
    }

    private bool TryHandleSettingsSlider(int currentMode, List<MenuNarrationEvent> events)
    {
        if (!Main.gameMenu)
        {
            return false;
        }

        if (!IsSettingsMenuMode(currentMode))
        {
            return false;
        }

        bool audioMenu = currentMode == 26;
        int sliderIndex = IngameOptions.rightLock >= 0 ? IngameOptions.rightLock : IngameOptions.rightHover;
        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        bool hasSpecialSlider = special == 1;
        bool hasSliderIndex = sliderIndex >= 0;

        if (!hasSliderIndex && !hasSpecialSlider)
        {
            ResetSliderTracking();

            if (audioMenu)
            {
                // Wait for the slider links to appear before narrating to avoid duplicate preamble lines.
                return true;
            }

            return false;
        }

        int categoryId = InGameNarrationSystem.IngameOptionsLabelTracker.GetCurrentCategory();
        if (categoryId != _state.LastCategoryId)
        {
            ResetSliderTracking();
            _state.LastCategoryId = categoryId;
        }

        string sliderLabel = hasSliderIndex ? GetSliderLabel(sliderIndex) : string.Empty;
        MenuSliderKind kind = DetectSliderKind(sliderIndex, sliderLabel, special, categoryId, currentMode);

        if (hasSpecialSlider && kind == MenuSliderKind.Unknown)
        {
            kind = MenuSliderKind.Parallax;
        }
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
            sliderLabel = SliderNarrationHelper.GetDefaultSliderLabel(kind);
        }

        if (audioMenu)
        {
            sliderLabel = SliderNarrationHelper.GetDefaultSliderLabel(kind);
        }

        float percent = ReadSliderPercent(kind);
        ref float lastValue = ref GetLastSliderValue(kind);

        int sliderId = hasSliderIndex ? sliderIndex : 1000 + special;
        bool sliderChanged = sliderId != _state.LastSliderId || kind != _state.LastSliderKind;
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
        _state.LastSliderId = sliderId;
        _state.LastSliderKind = kind;
        lastValue = percent;

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Slider {sliderId} ({kind}) -> {announcement}");
        events.Add(new MenuNarrationEvent(announcement, true, MenuNarrationEventKind.Slider));
        return true;
    }

    private bool IsAllowedHover(int menuMode, string cleanedLabel)
    {
        if (string.IsNullOrWhiteSpace(cleanedLabel))
        {
            return false;
        }

        string lower = cleanedLabel.ToLowerInvariant();

        if (menuMode == MenuID.Title)
        {
            // Only allow known main menu entries; suppress stray tooltips like Steam join messages or migration prompts.
            string[] allowed =
            {
                TextSanitizer.Clean(Lang.menu[12].Value), // Single Player
                TextSanitizer.Clean(Lang.menu[13].Value), // Multiplayer
                TextSanitizer.Clean(Lang.menu[131].Value), // Achievements
                TextSanitizer.Clean(Language.GetTextValue("UI.Workshop")),
                TextSanitizer.Clean(Lang.menu[14].Value), // Settings
                TextSanitizer.Clean(Language.GetTextValue("UI.Credits")),
                TextSanitizer.Clean(Lang.menu[15].Value), // Exit
            };

            foreach (string allowedLabel in allowed)
            {
                if (!string.IsNullOrWhiteSpace(allowedLabel) &&
                    string.Equals(cleanedLabel, allowedLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        if (IsSettingsMenuMode(menuMode))
        {
            // Filter out header-like audio text and menu titles.
            if (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("sound"))
            {
                return false;
            }
        }

        if (menuMode == 10017 && lower.Contains("tmodloader"))
        {
            return false;
        }

        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(menuMode);
        if (!string.IsNullOrWhiteSpace(modeLabel) &&
            string.Equals(cleanedLabel, TextSanitizer.Clean(modeLabel), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool ShouldSuppressHover(int menuMode, string cleanedLabel)
    {
        if (string.IsNullOrWhiteSpace(cleanedLabel))
        {
            return true;
        }

        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(menuMode);
        if (!string.IsNullOrWhiteSpace(modeLabel) &&
            string.Equals(cleanedLabel, TextSanitizer.Clean(modeLabel), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string lower = cleanedLabel.ToLowerInvariant();
        if (IsSettingsMenuMode(menuMode) && (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("sound")))
        {
            return true;
        }

        if (menuMode == 10017 && lower.Contains("tmodloader"))
        {
            return true;
        }

        return false;
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
                return ref _state.LastSoundVolume;
            case MenuSliderKind.Ambient:
                return ref _state.LastAmbientVolume;
            case MenuSliderKind.Zoom:
                return ref _state.LastZoom;
            case MenuSliderKind.InterfaceScale:
                return ref _state.LastInterfaceScale;
            case MenuSliderKind.Parallax:
                return ref _state.LastParallax;
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
        return SliderNarrationHelper.BuildSliderAnnouncement(rawLabel, kind, percent, includeLabel);
    }

    private bool TryHandleFocus(MenuNarrationContext context, int currentMode, bool force, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        // Suppress focus announcements when Workshop Hub is handling gamepad navigation
        if (WorkshopHubAccessibilitySystem.IsHandlingGamepadInput)
        {
            return false;
        }

        if (!_focusResolver.TryGetFocus(context.Main, out MenuFocus focus))
        {
            if (_state.FocusFailureCount++ < 5)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Unable to determine focus for menu mode {currentMode} (attempt {_state.FocusFailureCount}).");
            }

            return false;
        }

        _state.FocusFailureCount = 0;

        UIState? uiState = context.UiState;
        if (uiState is not null && !_state.SawHoverThisMode && timestamp - _state.ModeEnteredAt < TimeSpan.FromMilliseconds(250))
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
            bool matchesRecentHover = !string.IsNullOrWhiteSpace(_state.LastHoverAnnouncement) &&
                string.Equals(optionLabel, _state.LastHoverAnnouncement, StringComparison.OrdinalIgnoreCase) &&
                timestamp - _state.LastHoverAnnouncedAt < TimeSpan.FromMilliseconds(900);
            bool matchesLastFocus = !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
                string.Equals(announcement, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
            bool repeatedRecently = matchesLastFocus && timestamp - _state.LastFocusAnnouncedAt < TimeSpan.FromMilliseconds(900);

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
                events.Add(new MenuNarrationEvent(announcement, forceSpeech, MenuNarrationEventKind.Focus));
                _state.LastFocusAnnouncement = announcement;
                _state.LastFocusAnnouncedAt = timestamp;

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

    private void AnnounceFallback(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        int currentMode = context.MenuMode;

        if (IsSettingsMenuMode(currentMode) || currentMode == MenuID.Title)
        {
            return;
        }

        if (context.UiState is not null && !_state.SawHoverThisMode)
        {
            return;
        }

        if (_state.AnnouncedFallback)
        {
            return;
        }

        string fallback = MenuNarrationCatalog.DescribeMenuItem(currentMode, 0);
        if (string.IsNullOrEmpty(fallback))
        {
            return;
        }

        bool sameAsLastFocus = !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
            string.Equals(fallback, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            timestamp - _state.LastFocusAnnouncedAt < TimeSpan.FromSeconds(1);
        if (sameAsLastFocus)
        {
            _state.AnnouncedFallback = true;
            _state.ForceNextFocus = true;
            return;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Fallback focus -> {fallback}");
        events.Add(new MenuNarrationEvent(fallback, true, MenuNarrationEventKind.Focus));
        _state.AnnouncedFallback = true;
        _state.ForceNextFocus = true;
    }

    private bool TryHandleWorldCreationSnapshot(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        UIElement? hovered = null;
        if (_uiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover) &&
            MenuUiSelectionTracker.IsWorldCreationElement(hover.Element))
        {
            hovered = hover.Element;
        }

        if (!MenuUiSelectionTracker.TryBuildWorldCreationSnapshot(context.UiState, hovered, out MenuUiSelectionTracker.WorldCreationSnapshot snapshot))
        {
            _lastWorldCreationSnapshot = default;
            return false;
        }

        if (snapshot.IsEmpty)
        {
            _lastWorldCreationSnapshot = snapshot;
            return false;
        }

        var changes = new List<(string Text, bool Focused)>(5);

        static void AddSelectionChange(MenuUiSelectionTracker.WorldCreationSelection current, MenuUiSelectionTracker.WorldCreationSelection previous, List<(string Text, bool Focused)> buffer, bool isFocused, bool wasFocused)
        {
            if (!isFocused && !previous.IsEmpty)
            {
                return;
            }

            if (current.IsEmpty)
            {
                return;
            }

            bool unchanged = !previous.IsEmpty &&
                string.Equals(current.Option ?? string.Empty, previous.Option ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                current.Index == previous.Index &&
                current.Total == previous.Total &&
                current.Selected == previous.Selected;
            bool focusChanged = isFocused != wasFocused;
            if (unchanged && !focusChanged)
            {
                return;
            }

            bool includeGroup = previous.IsEmpty ||
                (isFocused && !wasFocused) ||
                !string.Equals(current.Group ?? string.Empty, previous.Group ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            string option = string.IsNullOrWhiteSpace(current.Option) ? current.Group ?? string.Empty : current.Option ?? string.Empty;
            string group = current.Group ?? string.Empty;
            string description;
            if (includeGroup)
            {
                if (string.IsNullOrWhiteSpace(group))
                {
                    description = current.Describe(includeGroup: true);
                }
                else
                {
                    description = current.Selected
                        ? TextSanitizer.JoinWithComma(group, $"Selected {option}")
                        : TextSanitizer.JoinWithComma(group, option);
                }
            }
            else
            {
                description = TextSanitizer.Clean(option ?? string.Empty);
                if (current.Selected)
                {
                    description = TextSanitizer.JoinWithComma("Selected", description);
                }
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            buffer.Add((description, isFocused));
        }

        static void AddInputChange(MenuUiSelectionTracker.WorldCreationInput current, MenuUiSelectionTracker.WorldCreationInput previous, List<(string Text, bool Focused)> buffer, bool isFocused, bool wasFocused)
        {
            if (!isFocused && !previous.IsEmpty)
            {
                return;
            }

            if (current.IsEmpty)
            {
                return;
            }

            bool unchanged = !previous.IsEmpty &&
                string.Equals(current.Value ?? string.Empty, previous.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.Prefix ?? string.Empty, previous.Prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            bool focusChanged = isFocused != wasFocused;
            if (unchanged && !focusChanged)
            {
                return;
            }

            bool includePrefix = previous.IsEmpty || (isFocused && !wasFocused);
            string description = current.Describe(includePrefix);
            if (!includePrefix && !string.IsNullOrWhiteSpace(current.Value))
            {
                description = TextSanitizer.Clean(current.Value);
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            buffer.Add((description, isFocused));
        }

        AddSelectionChange(snapshot.Size, _lastWorldCreationSnapshot.Size, changes, snapshot.SizeFocused, _lastWorldCreationSnapshot.SizeFocused);
        AddSelectionChange(snapshot.Difficulty, _lastWorldCreationSnapshot.Difficulty, changes, snapshot.DifficultyFocused, _lastWorldCreationSnapshot.DifficultyFocused);
        AddSelectionChange(snapshot.Evil, _lastWorldCreationSnapshot.Evil, changes, snapshot.EvilFocused, _lastWorldCreationSnapshot.EvilFocused);
        AddInputChange(snapshot.Name, _lastWorldCreationSnapshot.Name, changes, snapshot.NameFocused, _lastWorldCreationSnapshot.NameFocused);
        AddInputChange(snapshot.Seed, _lastWorldCreationSnapshot.Seed, changes, snapshot.SeedFocused, _lastWorldCreationSnapshot.SeedFocused);

        _lastWorldCreationSnapshot = snapshot;

        if (changes.Count == 0)
        {
            return false;
        }

        (string Text, bool Focused) announcement = changes.Find(change => change.Focused);
        if (string.IsNullOrWhiteSpace(announcement.Text))
        {
            announcement = changes[0];
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] World creation -> {announcement.Text}");
        events.Add(new MenuNarrationEvent(announcement.Text, true, MenuNarrationEventKind.WorldCreation));
        return true;
    }
}
