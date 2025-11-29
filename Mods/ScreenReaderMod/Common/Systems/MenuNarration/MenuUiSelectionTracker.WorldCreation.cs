#nullable enable
using System;
using System.Reflection;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly Type? UiWorldCreationType = Type.GetType("Terraria.GameContent.UI.States.UIWorldCreation, tModLoader");
    private static readonly FieldInfo? WorldCreationSizeButtonsField = UiWorldCreationType?.GetField("_sizeButtons", CharacterBindingFlags);
    private static readonly FieldInfo? WorldCreationDifficultyButtonsField = UiWorldCreationType?.GetField("_difficultyButtons", CharacterBindingFlags);
    private static readonly FieldInfo? WorldCreationEvilButtonsField = UiWorldCreationType?.GetField("_evilButtons", CharacterBindingFlags);

    private static UIElement? _lastWorldCreationRoot;
    private static string? _lastWorldCreationGroup;
    private static UIElement? _lastWorldCreationElement;

    private static void ResetWorldCreationContextIfNeeded(UIElement element)
    {
        if (_lastWorldCreationRoot is null)
        {
            return;
        }

        if (ReferenceEquals(element, _lastWorldCreationRoot) || IsAncestor(_lastWorldCreationRoot, element))
        {
            return;
        }

        _lastWorldCreationRoot = null;
        _lastWorldCreationGroup = null;
        _lastWorldCreationElement = null;
    }

    private static string DescribeWorldCreationElement(UIElement element)
    {
        if (UiWorldCreationType is null)
        {
            return string.Empty;
        }

        UIElement? root = FindAncestor(element, static type => UiWorldCreationType.IsAssignableFrom(type));
        if (root is null)
        {
            return string.Empty;
        }

        if (!ReferenceEquals(root, _lastWorldCreationRoot))
        {
            _lastWorldCreationGroup = null;
            _lastWorldCreationElement = null;
        }

        if (TryDescribeWorldCreationButton(root, element, WorldCreationSizeButtonsField, "World size", out string label))
        {
            return label;
        }

        if (TryDescribeWorldCreationButton(root, element, WorldCreationDifficultyButtonsField, "World difficulty", out label))
        {
            return label;
        }

        if (TryDescribeWorldCreationButton(root, element, WorldCreationEvilButtonsField, "World evil", out label))
        {
            return label;
        }

        if (TryDescribeWorldCreationInput(root, element, out label))
        {
            return label;
        }

        // Leaving grouped options inside world creation; reset so categories will be re-announced next time.
        _lastWorldCreationGroup = null;
        _lastWorldCreationElement = null;
        _lastWorldCreationRoot = root;

        return string.Empty;
    }

    private static bool TryDescribeWorldCreationButton(UIElement root, UIElement element, FieldInfo? buttonArrayField, string groupLabel, out string label)
    {
        if (buttonArrayField?.GetValue(root) is not Array buttons || buttons.Length == 0)
        {
            label = string.Empty;
            return false;
        }

        int buttonIndex = ResolveWorldCreationButtonIndex(buttons, element);
        if (buttonIndex >= 0 && buttons.GetValue(buttonIndex) is UIElement button)
        {
            label = DescribeWorldGroupOption(root, button, groupLabel, buttonIndex, buttons.Length);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static int ResolveWorldCreationButtonIndex(Array buttons, UIElement element)
    {
        int ancestorIndex = -1;
        int selectedIndex = -1;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons.GetValue(i) is not UIElement button)
            {
                continue;
            }

            if (ReferenceEquals(button, element) || IsAncestor(button, element))
            {
                return i;
            }

            if (IsAncestor(element, button) && ancestorIndex < 0)
            {
                ancestorIndex = i;
            }

            if (selectedIndex < 0 && IsGroupOptionSelected(button))
            {
                selectedIndex = i;
            }
        }

        if (ancestorIndex >= 0)
        {
            return selectedIndex >= 0 ? selectedIndex : ancestorIndex;
        }

        return -1;
    }

    private static string DescribeWorldGroupOption(UIElement root, UIElement element, string groupLabel, int index, int total)
    {
        string optionLabel = TryGetGroupOptionTitle(element);
        if (string.IsNullOrWhiteSpace(optionLabel))
        {
            optionLabel = groupLabel;
        }

        string label = optionLabel;
        if (total > 0)
        {
            label = TextSanitizer.JoinWithComma(label, $"{index + 1} of {total}");
        }

        if (IsGroupOptionSelected(element))
        {
            label = TextSanitizer.JoinWithComma("Selected", label);
        }

        _lastWorldCreationRoot = root;
        _lastWorldCreationGroup = groupLabel;
        _lastWorldCreationElement = element;

        return TextSanitizer.Clean(label);
    }

    private static bool TryDescribeWorldCreationInput(UIElement root, UIElement element, out string label)
    {
        if (TryMatchWorldCreationInput(root, element, "name", out UIElement? nameInput))
        {
            label = BuildWorldTextInputLabel("World name", nameInput!);
            return true;
        }

        if (TryMatchWorldCreationInput(root, element, "seed", out UIElement? seedInput))
        {
            label = BuildWorldTextInputLabel("Seed", seedInput!);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static bool TryMatchWorldCreationInput(UIElement root, UIElement element, string hint, out UIElement inputElement)
    {
        inputElement = null!;

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo field in root.GetType().GetFields(flags))
            {
                string name = field.Name.ToLowerInvariant();
                if (!name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (field.GetValue(root) is UIElement candidate && IsTextInputLike(candidate))
                {
                    if (ReferenceEquals(candidate, element) || IsAncestor(candidate, element) || IsAncestor(element, candidate))
                    {
                        inputElement = candidate;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore input detection failures
        }

        return false;
    }

    private static bool IsTextInputLike(UIElement element)
    {
        string typeName = element.GetType().Name;
        return typeName.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Box", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildWorldTextInputLabel(string prefix, UIElement input)
    {
        string value = TryGetInputText(input);
        if (string.IsNullOrWhiteSpace(value))
        {
            return prefix;
        }

        return TextSanitizer.JoinWithComma(prefix, value);
    }

    private static string TryGetInputText(UIElement element)
    {
        try
        {
            Type type = element.GetType();
            foreach (string propertyName in new[] { "CurrentString", "Text", "InputText", "Value" })
            {
                PropertyInfo? property = type.GetProperty(propertyName, CharacterBindingFlags);
                if (property?.GetValue(element) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return TextSanitizer.Clean(text);
                }
            }

            foreach (string fieldName in new[] { "_currentString", "_text", "_value" })
            {
                FieldInfo? field = type.GetField(fieldName, CharacterBindingFlags);
                if (field?.GetValue(element) is string fieldText && !string.IsNullOrWhiteSpace(fieldText))
                {
                    return TextSanitizer.Clean(fieldText);
                }
            }
        }
        catch
        {
            // ignore lookup failures
        }

        return string.Empty;
    }

    private static string TryGetGroupOptionTitle(UIElement element)
    {
        try
        {
            FieldInfo? titleField = element.GetType().GetField("_title", CharacterBindingFlags);
            if (titleField?.GetValue(element) is object titleObject)
            {
                PropertyInfo? textProp = titleObject.GetType().GetProperty("Text", CharacterBindingFlags);
                if (textProp?.GetValue(titleObject) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return TextSanitizer.Clean(text);
                }
            }

            PropertyInfo? optionValueProp = element.GetType().GetProperty("OptionValue", CharacterBindingFlags);
            if (optionValueProp?.GetValue(element) is object optionValue)
            {
                string text = optionValue.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return TextSanitizer.Clean(text);
                }
            }
        }
        catch
        {
            // ignore option title lookup failures
        }

        return string.Empty;
    }

    private static bool IsGroupOptionSelected(UIElement element)
    {
        try
        {
            PropertyInfo? selectedProp = element.GetType().GetProperty("IsSelected", CharacterBindingFlags);
            if (selectedProp?.GetValue(element) is bool selected)
            {
                return selected;
            }

            FieldInfo? currentOptionField = element.GetType().GetField("_currentOption", CharacterBindingFlags);
            if (currentOptionField?.GetValue(element) is bool currentOption && currentOption)
            {
                return true;
            }
        }
        catch
        {
            // ignore selection lookup failures
        }

        return false;
    }
}
