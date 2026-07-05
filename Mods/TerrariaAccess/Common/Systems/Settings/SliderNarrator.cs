#nullable enable
using System;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Settings;

/// <summary>
/// Reusable component for narrating slider values across different settings menus.
/// Handles audio sliders, zoom/scale sliders, and generic percentage sliders.
/// </summary>
internal sealed class SliderNarrator
{
    // State tracking per slider kind
    private float _lastMusicVolume = -1f;
    private float _lastSoundVolume = -1f;
    private float _lastAmbientVolume = -1f;
    private float _lastZoomPercent = -1f;
    private float _lastUiScalePercent = -1f;
    private float _lastParallax = -1f;
    private float _lastGenericPercent = -1f;

    // Last special feature code
    private int _lastSpecialFeature = int.MinValue;

    // Hold-to-repeat state for gamepad/keyboard slider control
    private readonly SliderRepeatState _repeatState = new();

    /// <summary>
    /// Resets all tracked slider values.
    /// </summary>
    public void Reset()
    {
        _lastMusicVolume = -1f;
        _lastSoundVolume = -1f;
        _lastAmbientVolume = -1f;
        _lastZoomPercent = -1f;
        _lastUiScalePercent = -1f;
        _lastParallax = -1f;
        _lastGenericPercent = -1f;
        _lastSpecialFeature = int.MinValue;
        _repeatState.Reset();
    }

    /// <summary>
    /// The hold-to-repeat state for slider adjustment.
    /// </summary>
    public SliderRepeatState RepeatState => _repeatState;

    /// <summary>
    /// Attempts to handle an audio slider (music, sound, ambient).
    /// </summary>
    /// <param name="specialFeature">The OPTIONS_BUTTON_SPECIALFEATURE value (2=music, 3=sound, 4=ambient).</param>
    /// <param name="label">The slider label text.</param>
    /// <param name="sliderChanged">True if the focused slider changed this frame.</param>
    /// <param name="tickKey">Optional tick key for sound.</param>
    /// <returns>True if an audio slider was handled.</returns>
    public bool TryHandleAudioSlider(int specialFeature, string label, bool sliderChanged, string? tickKey = null)
    {
        MenuSliderKind kind = specialFeature switch
        {
            2 => MenuSliderKind.Music,
            3 => MenuSliderKind.Sound,
            4 => MenuSliderKind.Ambient,
            _ => MenuSliderKind.Unknown,
        };

        if (kind == MenuSliderKind.Unknown)
        {
            return false;
        }

        float percent = ReadAudioSliderPercent(kind);
        ref float lastValue = ref GetLastAudioSliderValue(kind);

        bool featureChanged = _lastSpecialFeature != specialFeature;
        bool valueChanged = Math.Abs(percent - lastValue) >= 1f;

        _lastSpecialFeature = specialFeature;

        if (!sliderChanged && !featureChanged && !valueChanged)
        {
            return true;
        }

        string announcement = BuildSliderAnnouncement(label, kind, percent, includeLabel: sliderChanged || featureChanged);

        if (!string.IsNullOrWhiteSpace(tickKey))
        {
            PlayTickIfNew(tickKey);
        }

        ScreenReaderService.Announce(announcement, force: true);
        lastValue = percent;
        return true;
    }

    /// <summary>
    /// Handles zoom or UI scale slider announcements.
    /// </summary>
    /// <param name="specialFeature">The OPTIONS_BUTTON_SPECIALFEATURE value (10=zoom, 11=ui scale).</param>
    /// <param name="sliderChanged">True if the focused slider changed this frame.</param>
    /// <param name="lastOptionDescription">The last option description for context.</param>
    /// <returns>The announcement string, or empty if no announcement needed.</returns>
    public string HandleZoomOrUiScale(int specialFeature, bool sliderChanged, string? lastOptionDescription)
    {
        bool isZoom = specialFeature == 10;
        bool isUiScale = specialFeature == 11;

        if (!isZoom && !isUiScale)
        {
            return string.Empty;
        }

        string label = isZoom ? "Zoom" : "Interface scale";

        float percent = isZoom
            ? MathF.Round(Math.Clamp(Main.GameZoomTarget, 0.01f, 4f) * 100f)
            : MathF.Round(Math.Clamp(Main.UIScaleWanted > 0f ? Main.UIScaleWanted : Main.UIScale, 0.1f, 4f) * 100f);

        ref float lastValue = ref (isZoom ? ref _lastZoomPercent : ref _lastUiScalePercent);
        bool valueChanged = Math.Abs(percent - lastValue) >= 1f;

        if (!sliderChanged && !valueChanged)
        {
            return string.Empty;
        }

        bool includeLabel = sliderChanged || string.IsNullOrWhiteSpace(lastOptionDescription);
        lastValue = percent;

        return includeLabel ? $"{label} {percent:0} percent" : $"{percent:0} percent";
    }

    /// <summary>
    /// Handles background parallax slider announcements.
    /// </summary>
    /// <param name="specialFeature">The OPTIONS_BUTTON_SPECIALFEATURE value (1=parallax).</param>
    /// <returns>True if parallax was handled.</returns>
    public bool TryHandleParallax(int specialFeature)
    {
        if (specialFeature != 1)
        {
            bool wasParallax = _lastSpecialFeature == 1;
            _lastSpecialFeature = specialFeature;
            return wasParallax;
        }

        int parallax = Utils.Clamp(Main.bgScroll, 0, 100);
        bool changed = parallax != (int)_lastParallax || _lastSpecialFeature != specialFeature;

        _lastSpecialFeature = specialFeature;

        if (!changed)
        {
            return true;
        }

        ScreenReaderService.Announce($"Background parallax {parallax} percent");
        _lastParallax = parallax;
        return true;
    }

    /// <summary>
    /// Handles generic percentage slider announcements.
    /// </summary>
    /// <param name="percent">The current percentage value.</param>
    /// <param name="label">The slider label.</param>
    /// <param name="sliderChanged">True if the focused slider changed this frame.</param>
    /// <param name="lastOptionDescription">The last option description for context.</param>
    /// <returns>The announcement string, or empty if no announcement needed.</returns>
    public string HandleGenericPercentSlider(float percent, string label, bool sliderChanged, string? lastOptionDescription)
    {
        bool valueChanged = Math.Abs(percent - _lastGenericPercent) >= 1f;

        if (!sliderChanged && !valueChanged)
        {
            return string.Empty;
        }

        bool includeLabel = sliderChanged || string.IsNullOrWhiteSpace(lastOptionDescription);
        _lastGenericPercent = percent;

        return includeLabel ? $"{label} {percent:0} percent" : $"{percent:0} percent";
    }

    /// <summary>
    /// Reads the current audio slider percentage for the given kind.
    /// </summary>
    public static float ReadAudioSliderPercent(MenuSliderKind kind)
    {
        return kind switch
        {
            MenuSliderKind.Music => MathF.Round(Math.Clamp(Main.musicVolume, 0f, 1f) * 100f),
            MenuSliderKind.Sound => MathF.Round(Math.Clamp(Main.soundVolume, 0f, 1f) * 100f),
            MenuSliderKind.Ambient => MathF.Round(Math.Clamp(Main.ambientVolume, 0f, 1f) * 100f),
            _ => 0f,
        };
    }

    /// <summary>
    /// Builds a slider announcement using the shared helper.
    /// </summary>
    public static string BuildSliderAnnouncement(string rawLabel, MenuSliderKind kind, float percent, bool includeLabel)
    {
        return SliderNarrationHelper.BuildSliderAnnouncement(rawLabel, kind, percent, includeLabel);
    }

    private ref float GetLastAudioSliderValue(MenuSliderKind kind)
    {
        switch (kind)
        {
            case MenuSliderKind.Sound:
                return ref _lastSoundVolume;
            case MenuSliderKind.Ambient:
                return ref _lastAmbientVolume;
            case MenuSliderKind.Music:
            default:
                return ref _lastMusicVolume;
        }
    }

    // Tick sound state
    private string? _lastTickKey;
    private uint _lastTickFrame;

    private void PlayTickIfNew(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastTickKey, StringComparison.Ordinal))
        {
            return;
        }

        uint frame = Main.GameUpdateCount;
        uint age = frame >= _lastTickFrame ? frame - _lastTickFrame : uint.MaxValue - _lastTickFrame + frame + 1;
        if (age < 5)
        {
            return;
        }

        _lastTickKey = key;
        _lastTickFrame = frame;
        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
    }
}

/// <summary>
/// Tracks slider hold-to-repeat state for continuous value adjustment.
/// Shared implementation used by both in-game settings and mod config.
/// </summary>
internal sealed class SliderRepeatState
{
    /// <summary>Frames before repeat starts (~0.4 sec at 60fps).</summary>
    public const int RepeatDelay = 25;

    /// <summary>Frames between repeats (~20 adjustments/sec).</summary>
    public const int RepeatRate = 3;

    private int _repeatDirection; // -1 = left, 0 = none, 1 = right
    private int _repeatTimer;
    private int _lastElementIndex = -1;
    private float _lastValue = -1f;

    /// <summary>Current repeat direction: -1 (left), 0 (none), or 1 (right).</summary>
    public int RepeatDirection => _repeatDirection;

    /// <summary>Frames the direction has been held.</summary>
    public int RepeatTimer => _repeatTimer;

    /// <summary>Last element index that was being adjusted.</summary>
    public int LastElementIndex => _lastElementIndex;

    /// <summary>Last slider value that was announced.</summary>
    public float LastValue => _lastValue;

    /// <summary>True if currently in an active repeat cycle.</summary>
    public bool IsRepeating => _repeatDirection != 0 && _repeatTimer >= RepeatDelay;

    /// <summary>
    /// Updates repeat state and returns true if an adjustment should be made this frame.
    /// </summary>
    /// <param name="currentDirection">Current held direction: -1, 0, or 1.</param>
    /// <returns>True if a slider adjustment should occur this frame.</returns>
    public bool UpdateAndCheckShouldAdjust(int currentDirection)
    {
        if (currentDirection == 0)
        {
            // No direction held - reset
            _repeatDirection = 0;
            _repeatTimer = 0;
            return false;
        }

        if (currentDirection != _repeatDirection)
        {
            // Direction changed - immediate first adjustment
            _repeatDirection = currentDirection;
            _repeatTimer = 0;
            return true;
        }

        // Same direction held - check repeat timing
        _repeatTimer++;

        if (_repeatTimer >= RepeatDelay)
        {
            // Past initial delay - repeat at fast rate
            int framesSinceDelay = _repeatTimer - RepeatDelay;
            return framesSinceDelay % RepeatRate == 0;
        }

        return false;
    }

    /// <summary>Records slider element state for value change tracking.</summary>
    public void TrackElement(int elementIndex, float value)
    {
        _lastElementIndex = elementIndex;
        _lastValue = value;
    }

    /// <summary>Clears element tracking when navigating away.</summary>
    public void ClearElementTracking()
    {
        _lastElementIndex = -1;
        _lastValue = -1f;
    }

    /// <summary>Resets all repeat state.</summary>
    public void Reset()
    {
        _repeatDirection = 0;
        _repeatTimer = 0;
        _lastElementIndex = -1;
        _lastValue = -1f;
    }
}
