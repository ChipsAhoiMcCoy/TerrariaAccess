#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuNarrationController
{
    private readonly MenuFocusResolver _focusResolver = new();
    private readonly MenuUiSelectionTracker _uiSelectionTracker = new();

    private int _lastMenuMode = -1;
    private MenuFocus? _lastFocus;
    private bool _announcedFallback;
    private int _focusFailureCount;
    private bool _forceNextFocus;
    private int _lastSliderId = -1;
    private float _lastMusicVolume = -1f;
    private float _lastSoundVolume = -1f;
    private float _lastAmbientVolume = -1f;

    public void Process(Main main)
    {
        if (!Main.gameMenu)
        {
            ResetState();
            return;
        }

        int currentMode = Main.menuMode;
        if (currentMode != _lastMenuMode)
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

        if (!TryHandleFocus(main, currentMode, force: false))
        {
            AnnounceFallback(currentMode);
        }
    }

    private void HandleMenuModeChanged(Main main, int currentMode)
    {
        _lastMenuMode = currentMode;
        _lastFocus = null;
        _announcedFallback = false;
        _focusFailureCount = 0;
        _forceNextFocus = true;
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        ResetSliderTracking();

        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(currentMode);
        ScreenReaderService.Announce($"{modeLabel}.", force: true);
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

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] UI hover -> {hover.Text}");
        ScreenReaderService.Announce(hover.Text);
        return true;
    }

    private void ResetSliderTracking()
    {
        _lastSliderId = -1;
        _lastMusicVolume = -1f;
        _lastSoundVolume = -1f;
        _lastAmbientVolume = -1f;
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
        ref float lastValue = ref _lastMusicVolume;
        switch (sliderId)
        {
            case 3:
                label = $"Music volume {Math.Round(Main.musicVolume * 100f)} percent";
                value = Main.musicVolume;
                lastValue = ref _lastMusicVolume;
                break;
            case 2:
                label = $"Sound volume {Math.Round(Main.soundVolume * 100f)} percent";
                value = Main.soundVolume;
                lastValue = ref _lastSoundVolume;
                break;
            case 4:
                label = $"Ambient volume {Math.Round(Main.ambientVolume * 100f)} percent";
                value = Main.ambientVolume;
                lastValue = ref _lastAmbientVolume;
                break;
            default:
                return false;
        }

        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        bool sliderChanged = sliderId != _lastSliderId;
        bool valueChanged = Math.Abs(value - lastValue) >= 0.01f;
        if (!sliderChanged && !valueChanged)
        {
            return true;
        }

        _lastSliderId = sliderId;
        lastValue = value;
        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Volume slider {sliderId} -> {label}");
        ScreenReaderService.Announce(label, force: true);
        return true;
    }

    private bool TryHandleFocus(Main main, int currentMode, bool force)
    {
        if (!_focusResolver.TryGetFocus(main, out MenuFocus focus))
        {
            if (_focusFailureCount++ < 5)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Unable to determine focus for menu mode {currentMode} (attempt {_focusFailureCount}).");
            }

            return false;
        }

        _focusFailureCount = 0;

        bool focusChanged = !_lastFocus.HasValue || _lastFocus.Value.Index != focus.Index;
        bool shouldAnnounce = force || focusChanged || _forceNextFocus;

        string optionLabel = MenuNarrationCatalog.DescribeMenuItem(currentMode, focus.Index);
        if (shouldAnnounce)
        {
            if (!string.IsNullOrEmpty(optionLabel))
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Focus {focus.Index} via {focus.Source} -> {optionLabel}");
                bool forceSpeech = force || _forceNextFocus;
                ScreenReaderService.Announce(optionLabel, forceSpeech);
                _forceNextFocus = false;
            }
            else
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Missing label for focus {focus.Index} (source {focus.Source}) in menu mode {currentMode}.");
                MenuNarrationCatalog.LogMenuSnapshot(currentMode, allowRepeat: true);
            }
        }
        else if (_lastFocus.HasValue && !_lastFocus.Value.Source.Equals(focus.Source, StringComparison.Ordinal))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Focus source switched to {focus.Source} for index {focus.Index}.");
        }

        _lastFocus = focus;
        _announcedFallback = false;
        return true;
    }

    private void AnnounceFallback(int currentMode)
    {
        if (_announcedFallback)
        {
            return;
        }

        string fallback = MenuNarrationCatalog.DescribeMenuItem(currentMode, 0);
        if (string.IsNullOrEmpty(fallback))
        {
            return;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] Fallback focus -> {fallback}");
        ScreenReaderService.Announce(fallback, force: true);
        _announcedFallback = true;
        _forceNextFocus = true;
    }

    private void ResetState()
    {
        _focusResolver.Reset();
        _uiSelectionTracker.Reset();
        _lastMenuMode = -1;
        _lastFocus = null;
        _announcedFallback = false;
        _focusFailureCount = 0;
        _forceNextFocus = false;
        ResetSliderTracking();
    }
}
