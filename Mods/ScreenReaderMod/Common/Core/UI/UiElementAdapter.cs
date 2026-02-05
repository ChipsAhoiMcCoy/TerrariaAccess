#nullable enable
using System;
using System.Reflection;
using Terraria.UI;

namespace ScreenReaderMod.Common.Core.UI;

/// <summary>
/// Base adapter that wraps Terraria's UIElement class to provide the IFocusable interface.
/// This serves as the foundation for adapting Terraria's various UI element types to
/// the unified accessibility abstraction layer.
/// </summary>
/// <remarks>
/// Terraria's UIElement hierarchy is used extensively in:
/// - Mod menus (UIModBrowser, UIMods, etc.)
/// - Character/world creation
/// - Achievements
/// - Settings panels
///
/// This adapter extracts label information via reflection, similar to how
/// MenuUiSelectionTracker handles UI elements.
/// </remarks>
public class UiElementAdapter : IFocusable
{
    private static readonly BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Creates a new adapter wrapping the specified UIElement.
    /// </summary>
    /// <param name="element">The Terraria UIElement to wrap.</param>
    /// <param name="region">The name of the region containing this element.</param>
    /// <exception cref="ArgumentNullException">Thrown if element is null.</exception>
    public UiElementAdapter(UIElement element, string region)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        Region = region ?? "Unknown";
    }

    /// <summary>
    /// The wrapped Terraria UIElement.
    /// </summary>
    protected UIElement Element { get; }

    /// <summary>
    /// The region name for focus context.
    /// </summary>
    protected string Region { get; }

    /// <inheritdoc/>
    public virtual bool IsFocused => IsElementHovered();

    /// <inheritdoc/>
    public virtual FocusableType ElementType => FocusableType.Generic;

    /// <inheritdoc/>
    public virtual bool IsEnabled => true;

    /// <inheritdoc/>
    public virtual string GetNarrationLabel()
    {
        // Try common label extraction patterns
        string? label = TryExtractText() ?? TryExtractValue() ?? TryInvokeTextFunction();

        if (string.IsNullOrWhiteSpace(label))
        {
            // Fallback to type name
            return Element.GetType().Name;
        }

        return label.Trim();
    }

    /// <inheritdoc/>
    public virtual string? GetDetailedDescription()
    {
        // Try to get tooltip or description text
        return TryExtractTooltip();
    }

    /// <inheritdoc/>
    public virtual FocusContext? GetFocusContext()
    {
        if (!IsFocused)
        {
            return null;
        }

        return new FocusContext(Region, GetIdentifier());
    }

    /// <inheritdoc/>
    public virtual string GetIdentifier()
    {
        // Use element's unique ID if available, otherwise fallback to type + position
        Type type = Element.GetType();

        // Try to get UniqueId property
        PropertyInfo? uniqueIdProp = type.GetProperty("UniqueId", MemberFlags);
        if (uniqueIdProp?.GetValue(Element) is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            return uniqueId;
        }

        // Fallback: use type name + hashcode
        return $"{type.Name}_{Element.GetHashCode()}";
    }

    /// <summary>
    /// Checks if the element is currently being hovered/focused.
    /// </summary>
    protected virtual bool IsElementHovered()
    {
        // UIElement tracks hover via IsMouseHovering
        return Element.IsMouseHovering;
    }

    /// <summary>
    /// Tries to extract text from common Text/text properties or fields.
    /// </summary>
    protected virtual string? TryExtractText()
    {
        Type type = Element.GetType();

        // Try Text property (UIText, etc.)
        PropertyInfo? textProp = type.GetProperty("Text", MemberFlags);
        if (textProp is not null)
        {
            object? value = textProp.GetValue(Element);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // Might be a LocalizedText
            if (value is not null)
            {
                string? converted = ConvertToString(value);
                if (!string.IsNullOrWhiteSpace(converted))
                {
                    return converted;
                }
            }
        }

        // Try _text field
        FieldInfo? textField = type.GetField("_text", MemberFlags);
        if (textField?.GetValue(Element) is string fieldText && !string.IsNullOrWhiteSpace(fieldText))
        {
            return fieldText;
        }

        return null;
    }

    /// <summary>
    /// Tries to extract a value from common Value/value properties or fields.
    /// </summary>
    protected virtual string? TryExtractValue()
    {
        Type type = Element.GetType();

        // Try Value property
        PropertyInfo? valueProp = type.GetProperty("Value", MemberFlags);
        if (valueProp is not null)
        {
            object? value = valueProp.GetValue(Element);
            return ConvertToString(value);
        }

        // Try _value field
        FieldInfo? valueField = type.GetField("_value", MemberFlags);
        if (valueField is not null)
        {
            object? value = valueField.GetValue(Element);
            return ConvertToString(value);
        }

        return null;
    }

    /// <summary>
    /// Tries to invoke a text display function delegate.
    /// </summary>
    protected virtual string? TryInvokeTextFunction()
    {
        Type type = Element.GetType();

        // Common function field names
        string[] funcNames = { "_TextDisplayFunction", "TextDisplayFunction", "_GetTextFunction" };

        foreach (string funcName in funcNames)
        {
            FieldInfo? field = type.GetField(funcName, MemberFlags);
            if (field?.GetValue(Element) is Delegate del)
            {
                try
                {
                    object? result = del.DynamicInvoke();
                    return ConvertToString(result);
                }
                catch
                {
                    // Ignore delegate invocation failures
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to extract tooltip text from the element.
    /// </summary>
    protected virtual string? TryExtractTooltip()
    {
        Type type = Element.GetType();

        // Try HoverText property
        PropertyInfo? hoverProp = type.GetProperty("HoverText", MemberFlags);
        if (hoverProp is not null)
        {
            object? value = hoverProp.GetValue(Element);
            string? text = ConvertToString(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Try _hoverText field
        FieldInfo? hoverField = type.GetField("_hoverText", MemberFlags);
        if (hoverField is not null)
        {
            object? value = hoverField.GetValue(Element);
            return ConvertToString(value);
        }

        return null;
    }

    /// <summary>
    /// Converts an object value to a string, handling LocalizedText and other types.
    /// </summary>
    protected static string? ConvertToString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        // Check for LocalizedText (Terraria.Localization.LocalizedText)
        Type type = value.GetType();
        if (type.Name == "LocalizedText" || type.FullName?.Contains("LocalizedText") == true)
        {
            PropertyInfo? valueProp = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valueProp?.GetValue(value) is string localizedValue)
            {
                return localizedValue;
            }
        }

        return value.ToString();
    }

    /// <summary>
    /// Creates the appropriate adapter type for a given UIElement.
    /// </summary>
    /// <param name="element">The UIElement to adapt.</param>
    /// <param name="region">The region name.</param>
    /// <returns>An appropriate IFocusable adapter for the element.</returns>
    public static IFocusable CreateFor(UIElement element, string region)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        string? typeName = element.GetType().FullName;

        // Check for button types
        if (typeName?.Contains("Button", StringComparison.Ordinal) == true)
        {
            return new UiButtonAdapter(element, region);
        }

        // Check for toggle types
        if (typeName?.Contains("Toggle", StringComparison.Ordinal) == true)
        {
            return new UiToggleAdapter(element, region);
        }

        // Default adapter
        return new UiElementAdapter(element, region);
    }
}

/// <summary>
/// Adapter for button-type UIElements.
/// </summary>
public class UiButtonAdapter : UiElementAdapter
{
    public UiButtonAdapter(UIElement element, string region) : base(element, region)
    {
    }

    public override FocusableType ElementType => FocusableType.Button;
}

/// <summary>
/// Adapter for toggle/checkbox UIElements.
/// </summary>
public class UiToggleAdapter : UiElementAdapter
{
    private static readonly BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public UiToggleAdapter(UIElement element, string region) : base(element, region)
    {
    }

    public override FocusableType ElementType => FocusableType.Toggle;

    public override string GetNarrationLabel()
    {
        string baseLabel = base.GetNarrationLabel();
        string state = GetToggleState() ? "On" : "Off";
        return $"{baseLabel}: {state}";
    }

    /// <summary>
    /// Gets the current toggle state.
    /// </summary>
    protected bool GetToggleState()
    {
        Type type = Element.GetType();

        // Try IsOn property
        PropertyInfo? isOnProp = type.GetProperty("IsOn", MemberFlags);
        if (isOnProp?.GetValue(Element) is bool isOn)
        {
            return isOn;
        }

        // Try Checked property
        PropertyInfo? checkedProp = type.GetProperty("Checked", MemberFlags);
        if (checkedProp?.GetValue(Element) is bool isChecked)
        {
            return isChecked;
        }

        // Try _isOn field
        FieldInfo? isOnField = type.GetField("_isOn", MemberFlags);
        if (isOnField?.GetValue(Element) is bool fieldValue)
        {
            return fieldValue;
        }

        return false;
    }
}
