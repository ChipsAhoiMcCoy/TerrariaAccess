#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly Type? UiWorldCreationType = ResolveWorldCreationType();
    private static readonly FieldInfo? WorldCreationSizeButtonsField = ResolveWorldCreationField("_sizeButtons");
    private static readonly FieldInfo? WorldCreationDifficultyButtonsField = ResolveWorldCreationField("_difficultyButtons");
    private static readonly FieldInfo? WorldCreationEvilButtonsField = ResolveWorldCreationField("_evilButtons");

    private static UIElement? _lastWorldCreationRoot;
    private static string? _lastWorldCreationGroup;
    private static UIElement? _lastWorldCreationElement;

    private static Type? ResolveWorldCreationType()
    {
        return Type.GetType("Terraria.GameContent.UI.States.UIWorldCreation, tModLoader")
            ?? Type.GetType("Terraria.GameContent.UI.States.UIWorldCreation, Terraria")
            ?? typeof(Main).Assembly.GetType("Terraria.GameContent.UI.States.UIWorldCreation")
            ?? typeof(UIElement).Assembly.GetType("Terraria.GameContent.UI.States.UIWorldCreation");
    }

    private static FieldInfo? ResolveWorldCreationField(string name)
    {
        return UiWorldCreationType?.GetField(name, CharacterBindingFlags);
    }

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

        if (TryDescribeWorldCreationButton(root, element, WorldCreationSizeButtonsField, "World size", "size", out string label))
        {
            return label;
        }

        if (TryDescribeWorldCreationButton(root, element, WorldCreationDifficultyButtonsField, "World difficulty", "difficulty", out label))
        {
            return label;
        }

        if (TryDescribeWorldCreationButton(root, element, WorldCreationEvilButtonsField, "World evil", "evil", out label))
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

    private static bool TryDescribeWorldCreationButton(UIElement root, UIElement element, FieldInfo? buttonArrayField, string groupLabel, string hint, out string label)
    {
        UIElement[] buttons = TryGetWorldCreationButtons(root, buttonArrayField, hint);
        if (buttons.Length == 0)
        {
            label = string.Empty;
            return false;
        }

        int buttonIndex = ResolveWorldCreationButtonIndex(buttons, element);
        if (buttonIndex >= 0 && buttonIndex < buttons.Length && buttons[buttonIndex] is UIElement button)
        {
            label = DescribeWorldGroupOption(root, button, groupLabel, buttonIndex, buttons.Length);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static UIElement[] TryGetWorldCreationButtons(UIElement root, FieldInfo? directField, string nameHint)
    {
        if (directField?.GetValue(root) is Array directArray)
        {
            UIElement[] directButtons = ExtractButtons(directArray);
            if (directButtons.Length > 0)
            {
                return directButtons;
            }
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            foreach (FieldInfo field in root.GetType().GetFields(flags))
            {
                string lowered = field.Name.ToLowerInvariant();
                if (!lowered.Contains(nameHint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object? value = field.GetValue(root);
                if (value is Array array)
                {
                    UIElement[] buttons = ExtractButtons(array);
                    if (buttons.Length > 0)
                    {
                        return buttons;
                    }
                }
                else if (value is IEnumerable enumerable)
                {
                    var collected = new List<UIElement>();
                    foreach (object? item in enumerable)
                    {
                        if (item is UIElement uiElement)
                        {
                            collected.Add(uiElement);
                        }
                    }

                    if (collected.Count > 0)
                    {
                        return collected.ToArray();
                    }
                }
            }
        }
        catch
        {
            // ignore reflection failures while probing alternate field names
        }

        return Array.Empty<UIElement>();
    }

    private static UIElement[] ExtractButtons(Array array)
    {
        var result = new List<UIElement>(array.Length);
        foreach (object? entry in array)
        {
            if (entry is UIElement button)
            {
                result.Add(button);
            }
        }

        return result.ToArray();
    }

    private static int ResolveWorldCreationButtonIndex(IReadOnlyList<UIElement> buttons, UIElement element)
    {
        int ancestorIndex = -1;
        int selectedIndex = -1;

        for (int i = 0; i < buttons.Count; i++)
        {
            UIElement button = buttons[i];

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
        bool sameGroup = ReferenceEquals(_lastWorldCreationRoot, root) &&
            string.Equals(_lastWorldCreationGroup, groupLabel, StringComparison.OrdinalIgnoreCase);
        bool includeGroup = !sameGroup || string.IsNullOrWhiteSpace(_lastWorldCreationGroup);
        bool includeCount = includeGroup;

        string baseLabel = string.IsNullOrWhiteSpace(optionLabel) ? groupLabel : optionLabel;
        string label;
        if (includeGroup)
        {
            label = string.IsNullOrWhiteSpace(optionLabel)
                ? groupLabel
                : TextSanitizer.JoinWithComma(groupLabel, baseLabel);
        }
        else
        {
            label = baseLabel;
        }

        if (includeCount && total > 0)
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

        if (TryFindDescendantInput(root, hint, out inputElement))
        {
            return true;
        }

        return false;
    }

    private static bool TryFindDescendantInput(UIElement element, string hint, out UIElement inputElement)
    {
        inputElement = null!;
        if (element.Children is null)
        {
            return false;
        }

        foreach (UIElement child in element.Children)
        {
            string name = child.GetType().Name.ToLowerInvariant();
            if (IsTextInputLike(child) && name.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                inputElement = child;
                return true;
            }

            if (TryFindDescendantInput(child, hint, out inputElement))
            {
                return true;
            }
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

    private static WorldCreationInput BuildWorldTextInputValue(string prefix, UIElement input)
    {
        string value = TryGetInputText(input);
        return new WorldCreationInput(prefix, value);
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

    internal static bool IsWorldCreationElement(UIElement element)
    {
        if (UiWorldCreationType is null)
        {
            return false;
        }

        return FindAncestor(element, static type => UiWorldCreationType.IsAssignableFrom(type)) is not null;
    }

    internal static bool TryBuildWorldCreationSnapshot(UIState? state, UIElement? hovered, out WorldCreationSnapshot snapshot)
    {
        snapshot = default;
        UIElement? root = FindWorldCreationRoot(state);
        if (root is null)
        {
            return false;
        }

        WorldCreationSelection size = DescribeWorldCreationGroupSelection(root, WorldCreationSizeButtonsField, "World size", "size", hovered, out bool sizeFocused);
        WorldCreationSelection difficulty = DescribeWorldCreationGroupSelection(root, WorldCreationDifficultyButtonsField, "World difficulty", "difficulty", hovered, out bool difficultyFocused);
        WorldCreationSelection evil = DescribeWorldCreationGroupSelection(root, WorldCreationEvilButtonsField, "World evil", "evil", hovered, out bool evilFocused);
        WorldCreationInput name = DescribeWorldCreationInputValue(root, "name", "World name", hovered, out bool nameFocused);
        WorldCreationInput seed = DescribeWorldCreationInputValue(root, "seed", "Seed", hovered, out bool seedFocused);

        snapshot = new WorldCreationSnapshot(size, difficulty, evil, name, seed, sizeFocused, difficultyFocused, evilFocused, nameFocused, seedFocused);
        return true;
    }

    private static WorldCreationSelection DescribeWorldCreationGroupSelection(UIElement root, FieldInfo? buttonArrayField, string groupLabel, string fieldHint, UIElement? hovered, out bool isFocused)
    {
        UIElement[] buttons = TryGetWorldCreationButtons(root, buttonArrayField, fieldHint);
        isFocused = false;
        if (buttons.Length == 0)
        {
            return new WorldCreationSelection(groupLabel, string.Empty, 0, 0, false);
        }

        int selectedIndex = hovered is not null ? ResolveWorldCreationButtonIndex(buttons, hovered) : -1;
        isFocused = selectedIndex >= 0;

        if (selectedIndex < 0)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (IsGroupOptionSelected(buttons[i]))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        UIElement selected = buttons[selectedIndex];
        string optionLabel = TryGetGroupOptionTitle(selected);
        return new WorldCreationSelection(groupLabel, optionLabel, selectedIndex, buttons.Length, IsGroupOptionSelected(selected));
    }

    private static WorldCreationInput DescribeWorldCreationInputValue(UIElement root, string hint, string prefix, UIElement? hovered, out bool isFocused)
    {
        isFocused = false;
        if (hovered is not null && TryMatchWorldCreationInput(root, hovered, hint, out UIElement? hoveredInput))
        {
            isFocused = true;
            return BuildWorldTextInputValue(prefix, hoveredInput);
        }

        if (TryMatchWorldCreationInput(root, root, hint, out UIElement? input))
        {
            return BuildWorldTextInputValue(prefix, input);
        }

        return new WorldCreationInput(prefix, string.Empty);
    }

    private static UIElement? FindWorldCreationRoot(UIState? state)
    {
        if (UiWorldCreationType is null || state is null)
        {
            return null;
        }

        if (UiWorldCreationType.IsAssignableFrom(state.GetType()))
        {
            return state;
        }

        return FindDescendant(state, static type => UiWorldCreationType.IsAssignableFrom(type));
    }

    private static UIElement? FindDescendant(UIElement root, Func<Type, bool> predicate)
    {
        if (predicate(root.GetType()))
        {
            return root;
        }

        if (root.Children is null)
        {
            return null;
        }

        foreach (UIElement child in root.Children)
        {
            UIElement? match = FindDescendant(child, predicate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    internal readonly record struct WorldCreationSnapshot(
        WorldCreationSelection Size,
        WorldCreationSelection Difficulty,
        WorldCreationSelection Evil,
        WorldCreationInput Name,
        WorldCreationInput Seed,
        bool SizeFocused,
        bool DifficultyFocused,
        bool EvilFocused,
        bool NameFocused,
        bool SeedFocused)
    {
        public bool IsEmpty => Size.IsEmpty
            && Difficulty.IsEmpty
            && Evil.IsEmpty
            && Name.IsEmpty
            && Seed.IsEmpty;
    }

    internal readonly record struct WorldCreationSelection(string Group, string Option, int Index, int Total, bool Selected)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Group) && string.IsNullOrWhiteSpace(Option);

        public string Describe(bool includeGroup)
        {
            string option = Option ?? string.Empty;
            string group = Group ?? string.Empty;
            string baseLabel = string.IsNullOrWhiteSpace(option) ? group : option;
            string label;
            if (includeGroup)
            {
                label = string.IsNullOrWhiteSpace(option) ? group : TextSanitizer.JoinWithComma(group, baseLabel);
            }
            else
            {
                label = baseLabel;
            }
            if (includeGroup && Total > 0)
            {
                label = TextSanitizer.JoinWithComma(label, $"{Index + 1} of {Total}");
            }

            if (Selected)
            {
                label = TextSanitizer.JoinWithComma("Selected", label);
            }

            return TextSanitizer.Clean(label);
        }
    }

    internal readonly record struct WorldCreationInput(string Prefix, string Value)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Prefix) && string.IsNullOrWhiteSpace(Value);

        public string Describe(bool includePrefix)
        {
            string prefix = Prefix ?? string.Empty;
            string value = Value ?? string.Empty;
            string baseLabel = string.IsNullOrWhiteSpace(value) ? prefix : value;
            if (includePrefix)
            {
                return TextSanitizer.Clean(TextSanitizer.JoinWithComma(prefix, baseLabel));
            }

            return TextSanitizer.Clean(baseLabel);
        }
    }

    internal static bool IsTrackedWorldCreationElement(UIElement element)
    {
        if (UiWorldCreationType is null)
        {
            return false;
        }

        UIElement? root = FindAncestor(element, static type => UiWorldCreationType.IsAssignableFrom(type));
        if (root is null)
        {
            return false;
        }

        if (MatchesWorldCreationGroup(root, element, WorldCreationSizeButtonsField, "size"))
        {
            return true;
        }

        if (MatchesWorldCreationGroup(root, element, WorldCreationDifficultyButtonsField, "difficulty"))
        {
            return true;
        }

        if (MatchesWorldCreationGroup(root, element, WorldCreationEvilButtonsField, "evil"))
        {
            return true;
        }

        if (TryMatchWorldCreationInput(root, element, "name", out _))
        {
            return true;
        }

        if (TryMatchWorldCreationInput(root, element, "seed", out _))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesWorldCreationGroup(UIElement root, UIElement element, FieldInfo? field, string hint)
    {
        UIElement[] buttons = TryGetWorldCreationButtons(root, field, hint);
        if (buttons.Length == 0)
        {
            return false;
        }

        return ResolveWorldCreationButtonIndex(buttons, element) >= 0;
    }
}
