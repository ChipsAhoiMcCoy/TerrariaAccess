#nullable enable
using System;
using System.Globalization;
using System.Reflection;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class ModConfigMenuNarrator
{
    private readonly MenuUiSelectionTracker _uiTracker = new();
    private static readonly Type? ModConfigListType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfigList, tModLoader");
    private static readonly Type? ModConfigStateType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfig, tModLoader");
    private static readonly Type? ModType = Type.GetType("Terraria.ModLoader.Mod, tModLoader");
    private static readonly Type? ModConfigType = Type.GetType("Terraria.ModLoader.Config.ModConfig, tModLoader");

    private static readonly FieldInfo? SelectedModField = ModConfigListType?.GetField("selectedMod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? EditingModField = ModConfigStateType?.GetField("mod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ActiveConfigField = ModConfigStateType?.GetField("modConfig", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? ModDisplayNameProperty = ModType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModInternalNameProperty = ModType?.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);

    private static readonly PropertyInfo? ModConfigDisplayNameProperty = ModConfigType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModConfigFullNameProperty = ModConfigType?.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);

    private static readonly PropertyInfo? LocalizedValueProperty = typeof(LocalizedText).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

    private UIState? _lastState;
    private string? _lastModName;
    private string? _lastConfigLabel;
    private bool _listIntroAnnounced;
    private bool _detailIntroAnnounced;
    private string? _lastHoverAnnouncement;

    public void Reset()
    {
        _lastState = null;
        _lastModName = null;
        _lastConfigLabel = null;
        _listIntroAnnounced = false;
        _detailIntroAnnounced = false;
        _lastHoverAnnouncement = null;
        _uiTracker.Reset();
    }

    public bool TryHandleFancyUi(int menuMode, UserInterface? menuUi)
    {
        if (menuMode != MenuID.FancyUI)
        {
            Reset();
            return false;
        }

        return TryHandleState(menuUi?.CurrentState, menuUi, alignCursor: false, enableHover: false);
    }

    public bool TryHandleIngameUi(UserInterface? inGameUi, bool requiresPause)
    {
        if (!requiresPause)
        {
            Reset();
            return false;
        }

        return TryHandleState(inGameUi?.CurrentState, inGameUi, alignCursor: true, enableHover: true);
    }

    private bool TryHandleState(UIState? state, UserInterface? uiContext, bool alignCursor, bool enableHover)
    {
        if (state is null)
        {
            Reset();
            return false;
        }

        Type stateType = state.GetType();

        if (ModConfigListType is not null && ModConfigListType.IsAssignableFrom(stateType))
        {
            PrepareForState(state, alignCursor);
            HandleListState(state);
            _lastState = state;
            return true;
        }

        if (ModConfigStateType is not null && ModConfigStateType.IsAssignableFrom(stateType))
        {
            PrepareForState(state, alignCursor);
            HandleConfigState(state);

            if (enableHover)
            {
                TryAnnounceHover(uiContext);
            }

            _lastState = state;
            return true;
        }

        Reset();
        return false;
    }

    private void PrepareForState(UIState state, bool alignCursor)
    {
        if (!ReferenceEquals(_lastState, state))
        {
            _uiTracker.Reset();
            _lastHoverAnnouncement = null;

            if (alignCursor)
            {
                PositionCursorAtStateCenter(state);
            }
        }
    }

    private void HandleListState(UIState state)
    {
        if (!_listIntroAnnounced || !ReferenceEquals(_lastState, state))
        {
            string intro = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ListIntro", "Mod configuration list.");
            ScreenReaderService.Announce(intro, force: true);
            _listIntroAnnounced = true;
            _detailIntroAnnounced = false;
        }

        object? selectedMod = SelectedModField?.GetValue(state);
        string modName = DescribeMod(selectedMod);
        if (string.IsNullOrWhiteSpace(modName))
        {
            return;
        }

        if (string.Equals(modName, _lastModName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string template = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.SelectedMod", "Selected mod: {0}");
        ScreenReaderService.Announce(string.Format(template, modName), force: true);
        _lastModName = modName;
    }

    private void HandleConfigState(UIState state)
    {
        object? mod = EditingModField?.GetValue(state);
        object? config = ActiveConfigField?.GetValue(state);

        string modName = DescribeMod(mod);
        string configName = DescribeConfig(config);

        if (!_detailIntroAnnounced || !ReferenceEquals(_lastState, state) || !string.Equals(configName, _lastConfigLabel, StringComparison.OrdinalIgnoreCase))
        {
            string message = ComposeConfigAnnouncement(configName, modName);
            ScreenReaderService.Announce(message, force: true);
            _detailIntroAnnounced = true;
            _lastConfigLabel = configName;
        }

        _listIntroAnnounced = false;
    }

    private void TryAnnounceHover(UserInterface? uiContext)
    {
        if (uiContext is null)
        {
            return;
        }

        if (!_uiTracker.TryGetHoverLabel(uiContext, out MenuUiLabel hover))
        {
            return;
        }

        if (!hover.IsNew)
        {
            return;
        }

        string announcement = BuildHoverAnnouncement(hover);
        if (string.IsNullOrWhiteSpace(announcement) || string.Equals(announcement, _lastHoverAnnouncement, StringComparison.Ordinal))
        {
            return;
        }

        _lastHoverAnnouncement = announcement;
        ScreenReaderService.Announce(announcement);
    }

    private static string BuildHoverAnnouncement(MenuUiLabel hover)
    {
        string cleaned = TextSanitizer.Clean(hover.Text);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        if (hover.Element is null)
        {
            return string.Empty;
        }

        return DescribeConfigElement(hover.Element);
    }

    private static string ComposeConfigAnnouncement(string configName, string modName)
    {
        if (string.IsNullOrWhiteSpace(configName))
        {
            configName = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigFallback", "Configuration");
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            string template = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigIntroNoMod", "{0} configuration.");
            return string.Format(template, configName);
        }

        string combinedTemplate = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigIntro", "{0} from {1}");
        return string.Format(combinedTemplate, configName, modName);
    }

    private static void PositionCursorAtStateCenter(UIState state)
    {
        CalculatedStyle dims;
        try
        {
            dims = state.GetInnerDimensions();
            if (dims.Width <= 0f || dims.Height <= 0f)
            {
                dims = state.GetDimensions();
            }
        }
        catch
        {
            dims = new CalculatedStyle(0f, 0f, Main.screenWidth, Main.screenHeight);
        }

        float width = dims.Width > 0f ? dims.Width : Main.screenWidth;
        float height = dims.Height > 0f ? dims.Height : Main.screenHeight;
        float x = dims.Width > 0f ? dims.X : 0f;
        float y = dims.Height > 0f ? dims.Y : 0f;

        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);

        int clampedX = (int)Math.Clamp(centerX, 0f, Main.screenWidth - 1);
        int clampedY = (int)Math.Clamp(centerY, 0f, Main.screenHeight - 1);

        Main.mouseX = clampedX;
        Main.mouseY = clampedY;
        PlayerInput.MouseX = clampedX;
        PlayerInput.MouseY = clampedY;
    }

    private static string DescribeMod(object? mod)
    {
        if (mod is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ModDisplayNameProperty?.GetValue(mod);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ModInternalNameProperty?.GetValue(mod) is string name && !string.IsNullOrWhiteSpace(name))
            {
                return TextSanitizer.Clean(name);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to describe mod: {ex.Message}");
        }

        return string.Empty;
    }

    private static string DescribeConfig(object? config)
    {
        if (config is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ModConfigDisplayNameProperty?.GetValue(config);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ModConfigFullNameProperty?.GetValue(config) is string fullName && !string.IsNullOrWhiteSpace(fullName))
            {
                return TextSanitizer.Clean(fullName);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to describe config: {ex.Message}");
        }

        return string.Empty;
    }

    private static string ExtractLocalized(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is LocalizedText localized)
        {
            return TextSanitizer.Clean(localized.Value ?? string.Empty);
        }

        if (LocalizedValueProperty is not null && value.GetType() == LocalizedTextType)
        {
            try
            {
                object? textValue = LocalizedValueProperty.GetValue(value);
                if (textValue is string str)
                {
                    return TextSanitizer.Clean(str);
                }
            }
            catch
            {
                // ignored
            }
        }

        return TextSanitizer.Clean(value.ToString() ?? string.Empty);
    }

    private static Type LocalizedTextType => typeof(LocalizedText);

    private const int MaxReflectionDepth = 4;

    private static string DescribeConfigElement(UIElement element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        string label = ExtractConfigLabel(element, 0);
        string value = ExtractConfigValue(element, 0);

        if (string.IsNullOrWhiteSpace(label))
        {
            label = element.GetType().Name ?? string.Empty;
        }

        label = TextSanitizer.Clean(label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return label;
        }

        return TextSanitizer.Clean($"{label}: {value}");
    }

    private static string ExtractConfigLabel(object? element, int depth)
    {
        if (element is null || depth > MaxReflectionDepth)
        {
            return string.Empty;
        }

        string direct = ExtractFromMembers(element, LabelMemberCandidates, treatBoolAsToggle: false);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string hinted = ExtractFromHints(element, LabelHintKeywords, treatBoolAsToggle: false);
        if (!string.IsNullOrWhiteSpace(hinted))
        {
            return hinted;
        }

        foreach (string nestedName in NestedLabelSources)
        {
            if (TryReadMember(element, nestedName, out object? nested) && nested is not null)
            {
                if (ReferenceEquals(nested, element))
                {
                    continue;
                }

                string nestedLabel = ExtractConfigLabel(nested, depth + 1);
                if (!string.IsNullOrWhiteSpace(nestedLabel))
                {
                    return nestedLabel;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractConfigValue(object? element, int depth)
    {
        if (element is null || depth > MaxReflectionDepth)
        {
            return string.Empty;
        }

        string direct = ExtractFromMembers(element, ValueMemberCandidates, treatBoolAsToggle: true);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        string hinted = ExtractFromHints(element, ValueHintKeywords, treatBoolAsToggle: true);
        if (!string.IsNullOrWhiteSpace(hinted))
        {
            return hinted;
        }

        foreach (string nestedName in NestedValueSources)
        {
            if (TryReadMember(element, nestedName, out object? nested) && nested is not null)
            {
                if (ReferenceEquals(nested, element))
                {
                    continue;
                }

                string nestedValue = ExtractConfigValue(nested, depth + 1);
                if (!string.IsNullOrWhiteSpace(nestedValue))
                {
                    return nestedValue;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractFromMembers(object instance, string[] candidates, bool treatBoolAsToggle)
    {
        Type type = instance.GetType();
        foreach (string memberName in candidates)
        {
            if (TryReadMember(type, instance, memberName, out object? value))
            {
                string converted = ConvertToText(value, treatBoolAsToggle);
                if (!string.IsNullOrWhiteSpace(converted))
                {
                    return converted;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractFromHints(object instance, string[] hints, bool treatBoolAsToggle)
    {
        Type type = instance.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (!MatchesHints(property.Name, hints))
            {
                continue;
            }

            if (!TryGetPropertyValue(property, instance, out object? value))
            {
                continue;
            }

            string converted = ConvertToText(value, treatBoolAsToggle);
            if (!string.IsNullOrWhiteSpace(converted))
            {
                return converted;
            }
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (!MatchesHints(field.Name, hints))
            {
                continue;
            }

            if (!TryGetFieldValue(field, instance, out object? value))
            {
                continue;
            }

            string converted = ConvertToText(value, treatBoolAsToggle);
            if (!string.IsNullOrWhiteSpace(converted))
            {
                return converted;
            }
        }

        return string.Empty;
    }

    private static bool TryReadMember(object instance, string memberName, out object? value)
    {
        return TryReadMember(instance.GetType(), instance, memberName, out value);
    }

    private static bool TryReadMember(Type type, object instance, string memberName, out object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property is not null && TryGetPropertyValue(property, instance, out value))
        {
            return true;
        }

        FieldInfo? field = type.GetField(memberName, flags);
        if (field is not null && TryGetFieldValue(field, instance, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetPropertyValue(PropertyInfo property, object instance, out object? value)
    {
        value = null;

        if (property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        try
        {
            value = property.GetValue(instance, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetFieldValue(FieldInfo field, object instance, out object? value)
    {
        try
        {
            value = field.GetValue(instance);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool MatchesHints(string memberName, string[] hints)
    {
        foreach (string hint in hints)
        {
            if (memberName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string ConvertToText(object? value, bool treatBoolAsToggle)
    {
        if (value is null)
        {
            return string.Empty;
        }

        switch (value)
        {
            case string s:
                return TextSanitizer.Clean(s);
            case LocalizedText localized:
                return TextSanitizer.Clean(localized.Value ?? string.Empty);
            case bool b:
                return treatBoolAsToggle ? DescribeToggle(b) : string.Empty;
            case Enum enumValue:
                return TextSanitizer.Clean(enumValue.ToString());
            case float floatValue:
                return floatValue.ToString("0.##", CultureInfo.CurrentCulture);
            case double doubleValue:
                return doubleValue.ToString("0.##", CultureInfo.CurrentCulture);
            case decimal decimalValue:
                return decimalValue.ToString("0.##", CultureInfo.CurrentCulture);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.CurrentCulture);
            default:
                return TextSanitizer.Clean(value.ToString() ?? string.Empty);
        }
    }

    private static string DescribeToggle(bool value)
    {
        return value
            ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "On")
            : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOff", "Off");
    }

    private static readonly string[] LabelMemberCandidates =
    {
        "Label",
        "_label",
        "DisplayName",
        "_displayName",
        "Header",
        "Caption",
        "Title",
        "Text",
    };

    private static readonly string[] ValueMemberCandidates =
    {
        "CurrentValue",
        "Value",
        "_value",
        "Selection",
        "SelectedValue",
        "State",
        "Toggle",
        "Text",
    };

    private static readonly string[] LabelHintKeywords = { "label", "display", "header", "caption", "title", "text", "name" };
    private static readonly string[] ValueHintKeywords = { "value", "current", "selected", "state", "text", "option", "input" };

    private static readonly string[] NestedLabelSources = { "Property", "_property", "Field", "_field", "Wrapper", "_wrapper" };
    private static readonly string[] NestedValueSources = { "Property", "_property", "Field", "_field", "Wrapper", "_wrapper" };
}
