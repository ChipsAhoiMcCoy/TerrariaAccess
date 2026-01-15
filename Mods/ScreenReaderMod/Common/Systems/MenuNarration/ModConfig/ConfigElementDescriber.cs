#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ScreenReaderMod.Common.Utilities;
using Terraria.Localization;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Provides description/narration text for config elements.
/// Extracts labels, values, and tooltips using reflection.
/// </summary>
internal static class ConfigElementDescriber
{
    private static readonly Dictionary<Type, ConfigElementAccessors> AccessorCache = new();

    /// <summary>
    /// Gets a full description of a config element (label + value).
    /// </summary>
    public static string DescribeConfigElement(UIElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        Type type = element.GetType();
        string? typeName = type.FullName;

        // Skip header elements - they are non-interactive section titles
        if (typeName is not null && typeName.Contains("HeaderElement", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Check if this is an action button (UITextPanel used for Back, Save, etc.)
        if (typeName is not null && typeName.Contains("UITextPanel", StringComparison.Ordinal))
        {
            string buttonText = ExtractTextFromTypeHierarchy(element);
            if (!string.IsNullOrWhiteSpace(buttonText))
            {
                string cleanButtonText = TextSanitizer.Clean(buttonText);
                return $"{cleanButtonText} button";
            }
        }

        ConfigElementAccessors accessors = GetAccessors(type);

        // Try to get label (primary text from TextDisplayFunction)
        string label = TryExtractConfigLabel(element, accessors);

        // Try to get value
        string value = TryExtractConfigValue(element, accessors);

        // Clean up the label and value
        string cleanLabel = !string.IsNullOrWhiteSpace(label) ? TextSanitizer.Clean(label) : string.Empty;
        string cleanValue = !string.IsNullOrWhiteSpace(value) ? TextSanitizer.Clean(value) : string.Empty;

        // Check if we should skip adding the value to avoid duplication
        bool labelAlreadyHasValue = cleanLabel.Contains(':');
        bool labelEndsWithValue = !string.IsNullOrWhiteSpace(cleanValue) &&
                                   !string.IsNullOrWhiteSpace(cleanLabel) &&
                                   labelAlreadyHasValue &&
                                   cleanLabel.EndsWith(cleanValue, StringComparison.OrdinalIgnoreCase);
        bool valueContainsLabel = !string.IsNullOrWhiteSpace(cleanValue) &&
                                   !string.IsNullOrWhiteSpace(cleanLabel) &&
                                   cleanValue.Contains(cleanLabel, StringComparison.OrdinalIgnoreCase);

        // Build description - don't duplicate value if already in label
        if (!string.IsNullOrWhiteSpace(cleanLabel))
        {
            if (labelAlreadyHasValue || labelEndsWithValue || valueContainsLabel || string.IsNullOrWhiteSpace(cleanValue))
            {
                return cleanLabel;
            }
            else
            {
                return $"{cleanLabel}: {cleanValue}";
            }
        }

        // Fallback to generic extraction
        string extracted = ExtractElementText(element);
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            return TextSanitizer.Clean(extracted);
        }

        return type.Name;
    }

    /// <summary>
    /// Gets description text for a mod list element.
    /// </summary>
    public static string DescribeModListElement(UIElement element)
    {
        string text = ExtractTextFromTypeHierarchy(element);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return TextSanitizer.Clean(text);
        }

        return ExtractElementText(element);
    }

    /// <summary>
    /// Gets the cached accessors for a config element type.
    /// </summary>
    public static ConfigElementAccessors GetAccessors(Type type)
    {
        if (AccessorCache.TryGetValue(type, out ConfigElementAccessors? cached))
        {
            return cached;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        var accessors = new ConfigElementAccessors
        {
            LabelProperty = FindProperty(type, flags, "Label", "DisplayName", "Name"),
            LabelField = FindField(type, flags, "Label", "_label", "_text"),
            ValueProperty = FindProperty(type, flags, "Value", "CurrentValue"),
            ValueField = FindField(type, flags, "_value", "value"),
            TooltipProperty = FindProperty(type, flags, "Tooltip", "TooltipText"),
            TooltipField = FindField(type, flags, "_tooltip", "tooltip"),
            TextDisplayFunctionProperty = FindProperty(type, flags, "TextDisplayFunction"),
            TooltipFunctionProperty = FindProperty(type, flags, "TooltipFunction"),
            GetObjectMethod = FindMethod(type, flags, "GetObject"),
            ProportionProperty = FindProperty(type, flags, "Proportion"),
            MinProperty = FindProperty(type, flags, "Min"),
            MaxProperty = FindProperty(type, flags, "Max"),
            IncrementProperty = FindProperty(type, flags, "Increment"),
            TickIncrementProperty = FindProperty(type, flags, "TickIncrement"),
        };

        AccessorCache[type] = accessors;
        return accessors;
    }

    private static string TryExtractConfigLabel(UIElement element, ConfigElementAccessors accessors)
    {
        // Try TextDisplayFunction property first (Func<string> delegate in ConfigElement)
        if (accessors.TextDisplayFunctionProperty?.GetValue(element) is Delegate textFunc)
        {
            try
            {
                object? result = textFunc.DynamicInvoke();
                string text = ConvertToText(result);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
                // Ignore delegate invocation failures
            }
        }

        // Try Label field
        if (accessors.LabelField?.GetValue(element) is object labelFieldValue)
        {
            string text = ConvertToText(labelFieldValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Try Label property
        if (accessors.LabelProperty?.GetValue(element) is object labelValue)
        {
            string text = ConvertToText(labelValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string TryExtractConfigValue(UIElement element, ConfigElementAccessors accessors)
    {
        // Try GetObject method first
        if (accessors.GetObjectMethod is not null)
        {
            try
            {
                object? result = accessors.GetObjectMethod.Invoke(element, Array.Empty<object>());
                return FormatConfigValue(result);
            }
            catch
            {
                // Ignore method invocation failures
            }
        }

        // Try Value property
        if (accessors.ValueProperty?.GetValue(element) is object valueObj)
        {
            return FormatConfigValue(valueObj);
        }

        if (accessors.ValueField?.GetValue(element) is object valueFieldObj)
        {
            return FormatConfigValue(valueFieldObj);
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts tooltip text for a config element.
    /// </summary>
    public static string TryExtractTooltip(UIElement element, ConfigElementAccessors accessors)
    {
        if (accessors.TooltipFunctionProperty?.GetValue(element) is Delegate tooltipFunc)
        {
            try
            {
                object? result = tooltipFunc.DynamicInvoke();
                string text = ConvertToText(result);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
                // Ignore
            }
        }

        if (accessors.TooltipProperty?.GetValue(element) is object tooltipValue)
        {
            return ConvertToText(tooltipValue);
        }

        if (accessors.TooltipField?.GetValue(element) is object tooltipFieldValue)
        {
            return ConvertToText(tooltipFieldValue);
        }

        return string.Empty;
    }

    private static string FormatConfigValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        // Handle collection types
        if (value is System.Collections.ICollection collection)
        {
            int count = collection.Count;
            return count == 1
                ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.OneItem", "1 item")
                : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ItemCount", "{0} items").Replace("{0}", count.ToString());
        }

        // Handle IEnumerable (but not string)
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            string typeName = value.GetType().Name;
            if (typeName.Contains("List", StringComparison.Ordinal) ||
                typeName.Contains("Collection", StringComparison.Ordinal) ||
                typeName.Contains("Array", StringComparison.Ordinal) ||
                typeName.Contains("Set", StringComparison.Ordinal))
            {
                int count = 0;
                foreach (object? _ in enumerable)
                {
                    count++;
                    if (count > 100) break;
                }
                return count == 1
                    ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.OneItem", "1 item")
                    : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ItemCount", "{0} items").Replace("{0}", count.ToString());
            }
        }

        return value switch
        {
            bool b => b
                ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "On")
                : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOff", "Off"),
            float f => f.ToString("0.##", CultureInfo.CurrentCulture),
            double d => d.ToString("0.##", CultureInfo.CurrentCulture),
            Enum e => e.ToString(),
            _ => ConvertToText(value),
        };
    }

    private static string ExtractTextFromTypeHierarchy(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type? type = element.GetType();

        while (type is not null && type != typeof(object))
        {
            PropertyInfo? textProp = type.GetProperty("Text", flags | BindingFlags.DeclaredOnly);
            if (textProp is not null)
            {
                try
                {
                    object? value = textProp.GetValue(element);
                    if (value is string str && !string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            FieldInfo? textField = type.GetField("_text", flags | BindingFlags.DeclaredOnly);
            if (textField is not null)
            {
                try
                {
                    object? value = textField.GetValue(element);
                    string? str = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            type = type.BaseType;
        }

        return string.Empty;
    }

    private static string ExtractElementText(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = element.GetType();

        string[] textMembers = { "Text", "_text", "Label", "_label", "DisplayName", "Name" };

        foreach (string memberName in textMembers)
        {
            PropertyInfo? prop = type.GetProperty(memberName, flags);
            if (prop?.GetValue(element) is object propValue)
            {
                string text = ConvertToText(propValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            FieldInfo? field = type.GetField(memberName, flags);
            if (field?.GetValue(element) is object fieldValue)
            {
                string text = ConvertToText(fieldValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        // Recurse into first child with text
        foreach (UIElement child in element.Children)
        {
            string childText = ExtractElementText(child);
            if (!string.IsNullOrWhiteSpace(childText))
            {
                return childText;
            }
        }

        return string.Empty;
    }

    private static string ConvertToText(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string s)
        {
            return TextSanitizer.Clean(s);
        }

        if (value is LocalizedText localized)
        {
            return TextSanitizer.Clean(localized.Value ?? string.Empty);
        }

        return TextSanitizer.Clean(value.ToString() ?? string.Empty);
    }

    private static MethodInfo? FindMethod(Type type, BindingFlags flags, string name)
    {
        Type? currentType = type;
        while (currentType is not null && currentType != typeof(object))
        {
            MethodInfo? method = currentType.GetMethod(name, flags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method is not null)
            {
                return method;
            }
            currentType = currentType.BaseType;
        }
        return null;
    }

    private static PropertyInfo? FindProperty(Type type, BindingFlags flags, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? prop = type.GetProperty(name, flags);
            if (prop is not null)
            {
                return prop;
            }
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, BindingFlags flags, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo? field = type.GetField(name, flags);
            if (field is not null)
            {
                return field;
            }
        }
        return null;
    }

    /// <summary>
    /// Describes a mod object.
    /// </summary>
    public static string DescribeMod(object? mod)
    {
        if (mod is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ReflectionCache.Mod.DisplayName?.GetValue(mod);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ReflectionCache.Mod.Name?.GetValue(mod) is string name && !string.IsNullOrWhiteSpace(name))
            {
                return TextSanitizer.Clean(name);
            }
        }
        catch
        {
            // Ignore
        }

        return string.Empty;
    }

    /// <summary>
    /// Describes a config object.
    /// </summary>
    public static string DescribeConfig(object? config)
    {
        if (config is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ReflectionCache.ModConfigDef.DisplayName?.GetValue(config);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ReflectionCache.ModConfigDef.FullName?.GetValue(config) is string fullName && !string.IsNullOrWhiteSpace(fullName))
            {
                return TextSanitizer.Clean(fullName);
            }
        }
        catch
        {
            // Ignore
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

        return TextSanitizer.Clean(value.ToString() ?? string.Empty);
    }
}

/// <summary>
/// Cached reflection accessors for config element properties.
/// </summary>
internal sealed class ConfigElementAccessors
{
    public PropertyInfo? LabelProperty { get; init; }
    public FieldInfo? LabelField { get; init; }
    public PropertyInfo? ValueProperty { get; init; }
    public FieldInfo? ValueField { get; init; }
    public PropertyInfo? TooltipProperty { get; init; }
    public FieldInfo? TooltipField { get; init; }
    public PropertyInfo? TextDisplayFunctionProperty { get; init; }
    public PropertyInfo? TooltipFunctionProperty { get; init; }
    public MethodInfo? GetObjectMethod { get; init; }
    public PropertyInfo? ProportionProperty { get; init; }
    public PropertyInfo? MinProperty { get; init; }
    public PropertyInfo? MaxProperty { get; init; }
    public PropertyInfo? IncrementProperty { get; init; }
    public PropertyInfo? TickIncrementProperty { get; init; }
}
