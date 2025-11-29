#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.UI;
using Terraria.ID;

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

        if (TryHandleUiHover())
        {
            return;
        }

        if (TryHandleVolumeSlider())
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

        if (TryHandleUiHover())
        {
            return;
        }

        if (TryHandleVolumeSlider())
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

    private bool TryHandleVolumeSlider()
    {
        if (!Main.gameMenu)
        {
            return false;
        }

        int sliderId = IngameOptions.rightLock >= 0 ? IngameOptions.rightLock : IngameOptions.rightHover;
        if (sliderId is not (2 or 3 or 4))
        {
            return false;
        }

        string label;
        float value;
        ref float lastValue = ref _state.LastMusicVolume;
        switch (sliderId)
        {
            case 3:
                label = $"Music volume {Math.Round(Main.musicVolume * 100f)} percent";
                value = Main.musicVolume;
                lastValue = ref _state.LastMusicVolume;
                break;
            case 2:
                label = $"Sound volume {Math.Round(Main.soundVolume * 100f)} percent";
                value = Main.soundVolume;
                lastValue = ref _state.LastSoundVolume;
                break;
            case 4:
                label = $"Ambient volume {Math.Round(Main.ambientVolume * 100f)} percent";
                value = Main.ambientVolume;
                lastValue = ref _state.LastAmbientVolume;
                break;
            default:
                return false;
        }

        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        bool sliderChanged = sliderId != _state.LastSliderId;
        bool valueChanged = Math.Abs(value - lastValue) >= 0.01f;
        if (!sliderChanged && !valueChanged)
        {
            return true;
        }

        _state.LastSliderId = sliderId;
        lastValue = value;
        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Volume slider {sliderId} -> {label}");
        ScreenReaderService.Announce(label, force: true);
        return true;
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

        bool focusChanged = !_state.LastFocus.HasValue || _state.LastFocus.Value.Index != focus.Index;
        bool shouldAnnounce = force || focusChanged || _state.ForceNextFocus;

        string optionLabel = MenuNarrationCatalog.DescribeMenuItem(currentMode, focus.Index);
        if (shouldAnnounce)
        {
            DateTime now = DateTime.UtcNow;
            bool matchesRecentHover = !string.IsNullOrWhiteSpace(_state.LastHoverAnnouncement) &&
                string.Equals(optionLabel, _state.LastHoverAnnouncement, StringComparison.OrdinalIgnoreCase) &&
                now - _state.LastHoverAnnouncedAt < TimeSpan.FromMilliseconds(900);
            bool hasDeletionAnnouncement = MenuNarrationCatalog.TryBuildDeletionAnnouncement(currentMode, focus.Index, out string combinedLabel);
            string announcement = hasDeletionAnnouncement ? combinedLabel : optionLabel;

            bool matchesLastFocus = !string.IsNullOrWhiteSpace(_state.LastFocusAnnouncement) &&
                string.Equals(announcement, _state.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
            bool repeatedRecently = matchesLastFocus && now - _state.LastFocusAnnouncedAt < TimeSpan.FromMilliseconds(750);

            if (!force && (matchesRecentHover || repeatedRecently))
            {
                _state.ForceNextFocus = false;
                _state.LastFocus = focus;
                return true;
            }

            if (!string.IsNullOrEmpty(optionLabel))
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Focus {focus.Index} via {focus.Source} -> {optionLabel}");
                bool forceSpeech = force || _state.ForceNextFocus;
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
