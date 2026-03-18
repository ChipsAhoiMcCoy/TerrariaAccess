#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.Map;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems.Settings;

/// <summary>
/// Narrates the in-game settings/options menu (accessed via Escape key during gameplay).
/// Refactored to use SettingsNarratorBase and SliderNarrator for shared functionality.
/// </summary>
internal sealed class IngameSettingsNarrator : SettingsNarratorBase
{
    // Reflection fields for IngameOptions state
    private FieldInfo? _leftHoverField;
    private FieldInfo? _leftLockField;
    private FieldInfo? _rightHoverField;
    private FieldInfo? _rightLockField;
    private FieldInfo? _categoryField;
    private FieldInfo? _mouseOverTextField;
    private FieldInfo? _leftSideCategoryMappingField;
    private FieldInfo? _skipRightSlotField;
    private bool _fieldsResolved;
    private static bool _loggedFieldCatalog;

    // State tracking
    private int _lastLeftHover = int.MinValue;
    private int _lastSelectedLeftIndex = int.MinValue;
    private int _lastCategory = int.MinValue;
    private int _lastRawCategory = int.MinValue;
    private int _lastRightHover = int.MinValue;
    private int _lastRightLock = int.MinValue;
    private bool _wasOptionFocused;

    // Slider handling
    private readonly SliderNarrator _sliderNarrator = new();

    // Static category data
    private static readonly string[] DefaultCategoryLabels = BuildDefaultCategoryLabels();
    private static readonly string[] CategoryLabelOverrides = BuildCategoryLabelOverrides();
    private static readonly Dictionary<int, string> CategoryFallbackLabels = BuildCategoryFallbackLabels();
    private static readonly Dictionary<string, int> CategoryLabelLookup = BuildCategoryLookup();

    /// <inheritdoc/>
    public override bool IsActive => Main.ingameOptionsWindow;

    /// <summary>
    /// Primes reflection for accessing IngameOptions state.
    /// Called from the draw hook to ensure fields are resolved before Update.
    /// </summary>
    public void PrimeReflection()
    {
        EnsureReflection();
    }

    /// <inheritdoc/>
    public override void Update()
    {
        EnsureReflection();

        int leftHover = ReadInt(_leftHoverField);
        int leftLock = ReadInt(_leftLockField);
        int rightHover = ReadInt(_rightHoverField);
        int rightLock = ReadInt(_rightLockField);
        int rawCategory = ReadInt(_categoryField);
        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;

        bool leftHoverChanged = leftHover >= 0 && leftHover != _lastLeftHover;
        if (leftHoverChanged)
        {
            _lastLeftHover = leftHover;
        }

        int selectedLeftIndex = leftLock >= 0 ? leftLock : leftHover;
        int categoryId = ResolveCategoryId(rawCategory, selectedLeftIndex);

        // Detect category/hover desync during navigation
        bool rawCategoryChanged = rawCategory != _lastRawCategory;
        bool categoryAndHoverOutOfSync = rawCategoryChanged && !leftHoverChanged && leftHover >= 0;
        _lastRawCategory = rawCategory;

        string? categoryLabel = GetCategoryLabelById(categoryId, selectedLeftIndex, leftHover);

        // Handle returning to category list from options
        bool noOptionFocused = rightHover < 0 && rightLock < 0;
        bool returnedToCategoryList = noOptionFocused && _wasOptionFocused && selectedLeftIndex >= 0;
        _wasOptionFocused = !noOptionFocused;

        if (returnedToCategoryList && !string.IsNullOrWhiteSpace(categoryLabel))
        {
            ForceCategoryAnnouncement = true;
        }

        // Announce category changes
        if (!string.IsNullOrWhiteSpace(categoryLabel))
        {
            bool categoryChanged = categoryId != _lastCategory || !string.Equals(categoryLabel, LastCategoryAnnouncement, StringComparison.Ordinal);

            if ((categoryChanged || ForceCategoryAnnouncement) && (noOptionFocused || ForceCategoryAnnouncement) && !categoryAndHoverOutOfSync)
            {
                TryAnnounceCategory(categoryLabel, $"cat-{categoryId}");
                _lastCategory = categoryId;
            }
        }

        _lastSelectedLeftIndex = selectedLeftIndex;

        // Allow UI to settle after menu opens
        if (IsSettling())
        {
            return;
        }

        if (rightHover < 0)
        {
            rightHover = rightLock;
        }

        bool hasFocus = selectedLeftIndex >= 0 || rightHover >= 0 || rightLock >= 0;
        HandleNoFocusAnnouncement(hasFocus, categoryLabel, $"cat-stale-{categoryId}");

        bool optionIndicesChanged = rightHover != _lastRightHover || categoryId != _lastRightLock;
        bool optionActive = categoryId >= 0 && rightHover >= 0 && !IsOptionSkipped(rightHover);

        // Try slider handling first
        bool handledSlider = optionActive && TryHandleSlider(categoryId, rightHover, special, categoryLabel, optionIndicesChanged);

        if (optionActive && !handledSlider)
        {
            HandleRegularOption(categoryId, rightHover, special, categoryLabel, optionIndicesChanged);
        }

        if (!optionActive && optionIndicesChanged)
        {
            LastOptionAnnouncement = null;
            _lastRightHover = rightHover;
            _lastRightLock = categoryId;
        }

        if (!handledSlider)
        {
            HandleSpecialFeature(special);
        }
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        _lastLeftHover = int.MinValue;
        _lastSelectedLeftIndex = int.MinValue;
        _lastCategory = int.MinValue;
        _lastRawCategory = int.MinValue;
        _lastRightHover = int.MinValue;
        _lastRightLock = int.MinValue;
        _wasOptionFocused = false;
        _sliderNarrator.Reset();
    }

    private bool TryHandleSlider(int categoryId, int rightHover, int specialFeature, string? categoryLabel, bool optionIndicesChanged)
    {
        // Audio sliders (music=2, sound=3, ambient=4)
        if (specialFeature is 2 or 3 or 4)
        {
            string label = ResolveSliderLabel(categoryId, rightHover, categoryLabel, (MenuSliderKind)specialFeature);
            bool handled = _sliderNarrator.TryHandleAudioSlider(specialFeature, label, optionIndicesChanged, $"opt-{categoryId}-{rightHover}");

            if (handled)
            {
                _lastRightHover = rightHover;
                _lastRightLock = categoryId;
            }

            return handled;
        }

        // Zoom/UI Scale sliders (zoom=10, scale=11)
        if (specialFeature is 10 or 11)
        {
            string description = _sliderNarrator.HandleZoomOrUiScale(specialFeature, optionIndicesChanged, LastOptionAnnouncement);
            if (!string.IsNullOrWhiteSpace(description))
            {
                TryAnnounceOption(description, $"opt-{categoryId}-{rightHover}", force: true);
                _lastRightHover = rightHover;
                _lastRightLock = categoryId;
                return true;
            }
        }

        return false;
    }

    private void HandleRegularOption(int categoryId, int rightHover, int specialFeature, string? categoryLabel, bool optionIndicesChanged)
    {
        string? description = null;
        bool shouldAnnounce = optionIndicesChanged;

        if (specialFeature is 10 or 11)
        {
            description = _sliderNarrator.HandleZoomOrUiScale(specialFeature, optionIndicesChanged, LastOptionAnnouncement);
            shouldAnnounce = !string.IsNullOrWhiteSpace(description);
        }
        else if (!shouldAnnounce && !string.IsNullOrWhiteSpace(LastOptionAnnouncement))
        {
            description = DescribeOption(categoryId, rightHover, categoryLabel, optionIndicesChanged);
            shouldAnnounce = !string.IsNullOrWhiteSpace(description) &&
                !string.Equals(description, LastOptionAnnouncement, StringComparison.OrdinalIgnoreCase);
        }
        else if (shouldAnnounce)
        {
            description = DescribeOption(categoryId, rightHover, categoryLabel, optionIndicesChanged);
        }

        if (shouldAnnounce && !string.IsNullOrWhiteSpace(description))
        {
            TryAnnounceOption(description, $"opt-{categoryId}-{rightHover}");
            _lastRightHover = rightHover;
            _lastRightLock = categoryId;
        }
        else if (optionIndicesChanged)
        {
            LastOptionAnnouncement = description;
            _lastRightHover = rightHover;
            _lastRightLock = categoryId;
        }
    }

    private void HandleSpecialFeature(int specialFeature)
    {
        if (_sliderNarrator.TryHandleParallax(specialFeature))
        {
            return;
        }
    }

    private string ResolveSliderLabel(int categoryId, int optionIndex, string? categoryLabel, MenuSliderKind kind)
    {
        if (TryGetOptionLabel(categoryId, optionIndex, out string label) && !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        string? fallback = DescribeFallback(categoryId, optionIndex, categoryLabel);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return SliderNarrationHelper.GetDefaultSliderLabel(kind);
    }

    #region Reflection Helpers

    private void EnsureReflection()
    {
        if (_fieldsResolved)
        {
            return;
        }

        try
        {
            Type optionsType = typeof(IngameOptions);
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            if (!_loggedFieldCatalog)
            {
                foreach (FieldInfo field in optionsType.GetFields(flags).OrderBy(f => f.Name))
                {
                    LogDebug($"field: {field.FieldType.FullName} {field.Name}");
                }
                _loggedFieldCatalog = true;
            }

            FieldInfo[] fields = optionsType.GetFields(flags);
            _leftHoverField = FindIntField(fields, "left", "hover");
            _leftLockField = FindIntField(fields, "left", "lock");
            _rightHoverField = FindIntField(fields, "right", "hover");
            _rightLockField = FindIntField(fields, "right", "lock");
            _categoryField = FindIntField(fields, "category");
            _mouseOverTextField = FindStringField(fields, "mouse", "over", "text");
            _leftSideCategoryMappingField ??= optionsType.GetField("_leftSideCategoryMapping", flags);
            _skipRightSlotField ??= optionsType.GetField("skipRightSlot", flags);

            _fieldsResolved = true;

            LogDebug($"Reflection resolved: leftHover={_leftHoverField?.Name ?? "null"}, " +
                $"leftLock={_leftLockField?.Name ?? "null"}, rightHover={_rightHoverField?.Name ?? "null"}, " +
                $"rightLock={_rightLockField?.Name ?? "null"}, category={_categoryField?.Name ?? "null"}");
        }
        catch (Exception ex)
        {
            LogDebug($"Reflection resolution failed: {ex.Message}");
            _fieldsResolved = true;
        }
    }

    private static FieldInfo? FindIntField(IEnumerable<FieldInfo> fields, params string[] keywords)
    {
        return fields.FirstOrDefault(field =>
            field.FieldType == typeof(int) &&
            keywords.All(k => field.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    private static FieldInfo? FindStringField(IEnumerable<FieldInfo> fields, params string[] keywords)
    {
        return fields.FirstOrDefault(field =>
            field.FieldType == typeof(string) &&
            keywords.All(k => field.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    private static int ReadInt(FieldInfo? field)
    {
        try
        {
            if (field is not null && field.GetValue(null) is int value)
            {
                return value;
            }
        }
        catch
        {
            // Ignore read failures
        }
        return -1;
    }

    private string ReadString(FieldInfo? field)
    {
        try
        {
            if (field is not null && field.GetValue(null) is string value)
            {
                return value;
            }
        }
        catch
        {
            // Ignore read failures
        }
        return string.Empty;
    }

    #endregion

    #region Category Resolution

    private int ResolveCategoryId(int rawCategory, int selectedLeftIndex)
    {
        if (selectedLeftIndex >= 0 && TryMapLeftToCategory(selectedLeftIndex, out int mappedCategory))
        {
            return mappedCategory;
        }

        if (rawCategory >= 0)
        {
            return rawCategory;
        }

        return selectedLeftIndex;
    }

    private string? GetCategoryLabelById(int categoryId, int selectedLeftIndex, int leftHover)
    {
        _ = leftHover; // Previously used for mouseOverText fallback

        if (selectedLeftIndex >= 0 && TryGetLeftLabel(selectedLeftIndex, out string leftLabel) && !string.IsNullOrWhiteSpace(leftLabel))
        {
            return leftLabel;
        }

        if (categoryId >= 0 && TryGetCategoryLabel(categoryId, out string label) && !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        if (categoryId >= 0 && CategoryFallbackLabels.TryGetValue(categoryId, out string? fallbackLabel) && !string.IsNullOrWhiteSpace(fallbackLabel))
        {
            return fallbackLabel;
        }

        if (selectedLeftIndex >= 0)
        {
            return GetLeftCategoryLabel(selectedLeftIndex);
        }

        return null;
    }

    private static string? GetLeftCategoryLabel(int leftIndex)
    {
        if (leftIndex < 0)
        {
            return null;
        }

        if ((uint)leftIndex < (uint)CategoryLabelOverrides.Length)
        {
            string overrideLabel = CategoryLabelOverrides[leftIndex];
            if (!string.IsNullOrWhiteSpace(overrideLabel))
            {
                return overrideLabel;
            }
        }

        if ((uint)leftIndex < (uint)DefaultCategoryLabels.Length)
        {
            string defaultLabel = DefaultCategoryLabels[leftIndex];
            if (!string.IsNullOrWhiteSpace(defaultLabel))
            {
                return defaultLabel;
            }
        }

        return null;
    }

    // These methods would integrate with IngameOptionsLabelTracker if the code is part of InGameNarrationSystem
    // For now, provide stub implementations that can be connected later
    private static bool TryGetLeftLabel(int index, out string label)
    {
        // This would normally call InGameNarrationSystem.IngameOptionsLabelTracker.TryGetLeftLabel
        label = string.Empty;
        return false;
    }

    private static bool TryGetCategoryLabel(int category, out string label)
    {
        // This would normally call InGameNarrationSystem.IngameOptionsLabelTracker.TryGetCategoryLabel
        label = string.Empty;
        return false;
    }

    private static bool TryMapLeftToCategory(int leftIndex, out int category)
    {
        // This would normally call InGameNarrationSystem.IngameOptionsLabelTracker.TryMapLeftToCategory
        category = -1;
        return false;
    }

    private static bool TryGetOptionLabel(int category, int optionIndex, out string label)
    {
        // This would normally call InGameNarrationSystem.IngameOptionsLabelTracker.TryGetOptionLabel
        label = string.Empty;
        return false;
    }

    private static bool IsOptionSkipped(int optionIndex)
    {
        // This would normally call InGameNarrationSystem.IngameOptionsLabelTracker.IsOptionSkipped
        return false;
    }

    #endregion

    #region Option Description

    private string? DescribeOption(int category, int option, string? categoryLabel, bool optionIndicesChanged)
    {
        if (category < 0 || option < 0)
        {
            return null;
        }

        if (TryGetOptionLabel(category, option, out string label) && !string.IsNullOrWhiteSpace(label))
        {
            return BuildScaleAwareLabel(label, optionIndicesChanged, category, option, categoryLabel);
        }

        string mouseText = ReadString(_mouseOverTextField);
        if (!string.IsNullOrWhiteSpace(mouseText))
        {
            return BuildScaleAwareLabel(mouseText, optionIndicesChanged, category, option, categoryLabel);
        }

        string? fallback = DescribeFallback(category, option, categoryLabel);
        return BuildScaleAwareLabel(fallback, optionIndicesChanged, category, option, categoryLabel);
    }

    private string BuildScaleAwareLabel(string? label, bool optionIndicesChanged, int categoryId, int optionIndex, string? categoryLabel)
    {
        string sanitized = TextSanitizer.Clean(label ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        string lower = sanitized.ToLowerInvariant();
        bool isZoom = lower.Contains("zoom", StringComparison.Ordinal);
        bool isUiScale = lower.Contains("ui scale", StringComparison.Ordinal) ||
            lower.Contains("ui-scale", StringComparison.Ordinal) ||
            lower.Contains("interface scale", StringComparison.Ordinal);

        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        if (special == 10) isZoom = true;
        else if (special == 11) isUiScale = true;

        if (!isZoom && !isUiScale)
        {
            bool looksLikePercentOnly = sanitized.All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch) || ch == '%');
            if (!looksLikePercentOnly)
            {
                return sanitized;
            }

            if (TryMapPercentOnlySlider(categoryId, optionIndex, categoryLabel, out string mapped))
            {
                isZoom = mapped.Equals("Zoom", StringComparison.OrdinalIgnoreCase);
                isUiScale = mapped.Contains("scale", StringComparison.OrdinalIgnoreCase);
                sanitized = mapped;
            }
            else
            {
                return sanitized;
            }
        }

        if (isZoom || isUiScale)
        {
            string fixedLabel = isZoom ? "Zoom" : "Interface scale";
            float percent = isZoom
                ? MathF.Round(Math.Clamp(Main.GameZoomTarget, 0.01f, 4f) * 100f)
                : MathF.Round(Math.Clamp(Main.UIScaleWanted > 0f ? Main.UIScaleWanted : Main.UIScale, 0.1f, 4f) * 100f);

            bool includeLabel = optionIndicesChanged || string.IsNullOrWhiteSpace(LastOptionAnnouncement);
            return includeLabel ? $"{fixedLabel} {percent:0} percent" : $"{percent:0} percent";
        }

        return sanitized;
    }

    private static bool TryMapPercentOnlySlider(int categoryId, int optionIndex, string? categoryLabel, out string label)
    {
        string sanitizedCategory = TextSanitizer.Clean(categoryLabel ?? string.Empty);
        string lowerCategory = sanitizedCategory.ToLowerInvariant();

        bool looksLikeInterface = categoryId == 1 ||
            string.Equals(sanitizedCategory, TextSanitizer.Clean(Lang.menu[210].Value), StringComparison.OrdinalIgnoreCase) ||
            lowerCategory.Contains("interface", StringComparison.Ordinal);

        if (looksLikeInterface)
        {
            label = "Interface scale";
            return true;
        }

        bool looksLikeZoom = categoryId == 2 ||
            string.Equals(sanitizedCategory, TextSanitizer.Clean(Lang.menu[63].Value), StringComparison.OrdinalIgnoreCase) ||
            lowerCategory.Contains("zoom", StringComparison.Ordinal) ||
            lowerCategory.Contains("display", StringComparison.Ordinal) ||
            lowerCategory.Contains("video", StringComparison.Ordinal);

        int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
        if (special == 11 && !looksLikeInterface) looksLikeInterface = true;
        else if (special == 10 && !looksLikeZoom) looksLikeZoom = true;

        if (looksLikeZoom)
        {
            label = "Zoom";
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static string? DescribeFallback(int category, int option, string? categoryLabel)
    {
        int normalizedCategory = NormalizeCategoryId(category, categoryLabel);

        try
        {
            return normalizedCategory switch
            {
                0 => DescribeGeneral(option),
                1 => DescribeInterface(option),
                2 => DescribeVideo(option),
                3 => DescribeAudio(option),
                4 => DescribeCursor(option),
                5 => DescribeGameplay(option),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static int NormalizeCategoryId(int category, string? categoryLabel)
    {
        if (!string.IsNullOrWhiteSpace(categoryLabel) && CategoryLabelLookup.TryGetValue(categoryLabel, out int mapped))
        {
            return mapped;
        }
        return category >= 0 ? category : -1;
    }

    #endregion

    #region Fallback Descriptions

    private static string DescribeGeneral(int option)
    {
        string result = option switch
        {
            0 => Main.autoSave ? Lang.menu[67].Value : Lang.menu[68].Value,
            1 => Main.autoPause ? Lang.menu[69].Value : Lang.menu[70].Value,
            2 => Main.mapEnabled ? Lang.menu[112].Value : Lang.menu[113].Value,
            3 => Main.HidePassword ? Lang.menu[212].Value : Lang.menu[211].Value,
            4 => Lang.menu[5].Value,
            _ => $"General option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    private static string DescribeAudio(int option)
    {
        string result = option switch
        {
            0 => $"{Lang.menu[98].Value}: {MathF.Round(Main.musicVolume * 100f):0}%",
            1 => $"{Lang.menu[99].Value}: {MathF.Round(Main.soundVolume * 100f):0}%",
            2 => $"{Lang.menu[119].Value}: {MathF.Round(Main.ambientVolume * 100f):0}%",
            3 => Lang.menu[5].Value,
            _ => $"Audio option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    private static string DescribeInterface(int option)
    {
        string mapBorder = string.Empty;
        try
        {
            string key = Main.MinimapFrameManagerInstance?.ActiveSelectionKeyName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                mapBorder = Language.GetTextValue("UI.MinimapFrame_" + key);
            }
        }
        catch
        {
            mapBorder = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(mapBorder))
        {
            mapBorder = Language.GetTextValue("UI.MinimapFrame_Classic");
        }

        string result = option switch
        {
            0 => Main.showItemText ? Lang.menu[71].Value : Lang.menu[72].Value,
            1 => $"{Lang.menu[123].Value} {Lang.menu[124 + Utils.Clamp(Main.invasionProgressMode, 0, 2)].Value}",
            2 => Main.placementPreview ? Lang.menu[128].Value : Lang.menu[129].Value,
            3 => ItemSlot.Options.HighlightNewItems ? Lang.inter[117].Value : Lang.inter[116].Value,
            4 => Main.MouseShowBuildingGrid ? Lang.menu[229].Value : Lang.menu[230].Value,
            5 => Main.GamepadDisableInstructionsDisplay ? Lang.menu[241].Value : Lang.menu[242].Value,
            6 => Language.GetTextValue("UI.SelectMapBorder", mapBorder),
            7 => Language.GetTextValue("UI.SelectHealthStyle", Main.ResourceSetsManager?.ActiveSet.DisplayedName ?? string.Empty),
            8 => Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff"),
            9 => Language.GetTextValue("tModLoader.BossBarStyle", Terraria.ModLoader.BossBarLoader.CurrentStyle?.DisplayName ?? string.Empty),
            10 => Lang.menu[5].Value,
            _ => $"Interface option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    private static string DescribeVideo(int option)
    {
        int frameSkipIndex = (int)Main.FrameSkipMode;
        string result = option switch
        {
            0 => Lang.menu[51].Value,
            1 => Lang.menu[52].Value,
            2 => Lang.menu[247 + Utils.Clamp(frameSkipIndex, 0, 3)].Value,
            3 => Language.GetTextValue("UI.LightMode_" + Lighting.Mode),
            4 => Main.qaStyle switch
            {
                0 => Lang.menu[59].Value,
                1 => Lang.menu[60].Value,
                2 => Lang.menu[61].Value,
                _ => Lang.menu[62].Value,
            },
            5 => Main.BackgroundEnabled ? Lang.menu[100].Value : Lang.menu[101].Value,
            6 => ChildSafety.Disabled ? Lang.menu[132].Value : Lang.menu[133].Value,
            7 => Main.SettingsEnabled_MinersWobble ? Lang.menu[250].Value : Lang.menu[251].Value,
            8 => Main.SettingsEnabled_TilesSwayInWind ? Language.GetTextValue("UI.TilesSwayInWindOn") : Language.GetTextValue("UI.TilesSwayInWindOff"),
            9 => Language.GetTextValue("UI.Effects"),
            10 => Lang.menu[5].Value,
            _ => $"Video option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    private static string DescribeCursor(int option)
    {
        string lockOn = LockOnHelper.UseMode switch
        {
            LockOnHelper.LockOnMode.FocusTarget => Lang.menu[232].Value,
            LockOnHelper.LockOnMode.TargetClosest => Lang.menu[233].Value,
            LockOnHelper.LockOnMode.ThreeDS => Lang.menu[234].Value,
            _ => string.Empty,
        };

        string result = option switch
        {
            0 => Lang.menu[64].Value,
            1 => Language.GetTextValue("UI.Red"),
            2 => Language.GetTextValue("UI.Green"),
            3 => Language.GetTextValue("UI.Blue"),
            4 => Language.GetTextValue("UI.Brightness"),
            5 => Lang.menu[5].Value,
            6 => Lang.menu[217].Value,
            7 => Language.GetTextValue("UI.Red"),
            8 => Language.GetTextValue("UI.Green"),
            9 => Language.GetTextValue("UI.Blue"),
            10 => Language.GetTextValue("UI.Brightness"),
            11 => Lang.menu[5].Value,
            12 => lockOn,
            13 => Player.SmartCursorSettings.SmartBlocksEnabled ? Lang.menu[215].Value : Lang.menu[216].Value,
            14 => Main.cSmartCursorModeIsToggleAndNotHold ? Lang.menu[121].Value : Lang.menu[122].Value,
            15 => Player.SmartCursorSettings.SmartAxeAfterPickaxe ? Lang.menu[214].Value : Lang.menu[213].Value,
            16 => Lang.menu[5].Value,
            _ => $"Cursor option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    private static string DescribeGameplay(int option)
    {
        string result = option switch
        {
            0 => Lang.menu[220].Value,
            1 => Lang.menu[221].Value,
            2 => Lang.menu[222].Value,
            3 => Lang.menu[5].Value,
            _ => $"Gameplay option {option + 1}",
        };
        return TextSanitizer.Clean(result);
    }

    #endregion

    #region Static Category Data

    private static string[] BuildDefaultCategoryLabels()
    {
        return new[]
        {
            TextSanitizer.Clean(Lang.menu[114].Value),
            TextSanitizer.Clean(Lang.menu[210].Value),
            TextSanitizer.Clean(Lang.menu[63].Value),
            TextSanitizer.Clean(Lang.menu[65].Value),
            TextSanitizer.Clean(Lang.menu[218].Value),
            TextSanitizer.Clean(Lang.menu[219].Value),
            TextSanitizer.Clean(Lang.menu[103].Value),
        };
    }

    private static string[] BuildCategoryLabelOverrides()
    {
        return new[]
        {
            TextSanitizer.Clean(Lang.menu[114].Value),
            TextSanitizer.Clean(Lang.menu[218].Value),
            TextSanitizer.Clean(Lang.menu[219].Value),
            ResolveModConfigurationLabel(),
            TextSanitizer.Clean(Lang.menu[131].Value),
            LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.IngameOptions.CloseMenu", "Close Menu"),
            LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.IngameOptions.SaveAndExit", "Save & Exit"),
        };
    }

    private static string ResolveModConfigurationLabel()
    {
        string[] candidates =
        {
            "tModLoader.ModConfiguration",
            "tModLoader.MenuModConfiguration",
            "ModConfiguration",
        };

        foreach (string key in candidates)
        {
            try
            {
                LocalizedText text = Language.GetText(key);
                string value = text?.Value ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
                {
                    return TextSanitizer.Clean(value);
                }
            }
            catch
            {
                // Ignore
            }
        }

        return LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.IngameOptions.ModConfiguration", "Mod Configuration");
    }

    private static Dictionary<int, string> BuildCategoryFallbackLabels()
    {
        return new Dictionary<int, string>
        {
            [0] = TextSanitizer.Clean(Lang.menu[114].Value),
            [1] = TextSanitizer.Clean(Lang.menu[210].Value),
            [2] = TextSanitizer.Clean(Lang.menu[63].Value),
            [3] = TextSanitizer.Clean(Lang.menu[65].Value),
            [4] = TextSanitizer.Clean(Lang.menu[218].Value),
            [5] = TextSanitizer.Clean(Lang.menu[219].Value),
        };
    }

    private static Dictionary<string, int> BuildCategoryLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddMapping(int id, string? label)
        {
            string sanitized = TextSanitizer.Clean(label ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized) && !lookup.ContainsKey(sanitized))
            {
                lookup[sanitized] = id;
            }
        }

        foreach ((int id, string label) in CategoryFallbackLabels)
        {
            AddMapping(id, label);
        }

        for (int i = 0; i < DefaultCategoryLabels.Length; i++)
        {
            AddMapping(i, DefaultCategoryLabels[i]);
        }

        for (int i = 0; i < CategoryLabelOverrides.Length; i++)
        {
            AddMapping(i, CategoryLabelOverrides[i]);
        }

        return lookup;
    }

    #endregion
}
