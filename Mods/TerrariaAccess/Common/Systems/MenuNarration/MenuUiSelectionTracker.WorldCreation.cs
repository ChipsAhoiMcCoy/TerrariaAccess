#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.UI;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly Type? UiWorldCreationType = ResolveWorldCreationType();
    private static readonly FieldInfo? WorldCreationSizeButtonsField = ResolveWorldCreationField("_sizeButtons");
    private static readonly FieldInfo? WorldCreationDifficultyButtonsField = ResolveWorldCreationField("_difficultyButtons");
    private static readonly FieldInfo? WorldCreationEvilButtonsField = ResolveWorldCreationField("_evilButtons");
    private static readonly FieldInfo? WorldCreationNamePlateField = ResolveWorldCreationField("_namePlate");
    private static readonly FieldInfo? WorldCreationSeedPlateField = ResolveWorldCreationField("_seedPlate");

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

        // Update tracking state first
        _lastWorldCreationRoot = root;
        _lastWorldCreationGroup = groupLabel;
        _lastWorldCreationElement = element;

        // Build the label with inline group concatenation
        string baseLabel = string.IsNullOrWhiteSpace(optionLabel) ? groupLabel : optionLabel;
        string label;

        if (includeGroup && !string.IsNullOrWhiteSpace(groupLabel) && !string.IsNullOrWhiteSpace(optionLabel))
        {
            label = TextSanitizer.JoinWithComma(groupLabel, baseLabel);
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

        return TextSanitizer.Clean(label);
    }

    private static bool TryDescribeWorldCreationInput(UIElement root, UIElement element, out string label)
    {
        // Try explicit field references first (UICharacterNameButton types)
        if (TryMatchExplicitWorldCreationInput(root, element, WorldCreationNamePlateField, out UIElement? nameInput))
        {
            label = BuildWorldTextInputLabel("World name", nameInput!);
            return true;
        }

        if (TryMatchExplicitWorldCreationInput(root, element, WorldCreationSeedPlateField, out UIElement? seedInput))
        {
            label = BuildWorldTextInputLabel("Seed", seedInput!);
            return true;
        }

        // Fall back to dynamic matching for compatibility
        if (TryMatchWorldCreationInput(root, element, "name", out nameInput))
        {
            label = BuildWorldTextInputLabel("World name", nameInput!);
            return true;
        }

        if (TryMatchWorldCreationInput(root, element, "seed", out seedInput))
        {
            label = BuildWorldTextInputLabel("Seed", seedInput!);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static bool TryMatchExplicitWorldCreationInput(UIElement root, UIElement element, FieldInfo? field, out UIElement inputElement)
    {
        inputElement = null!;

        if (field is null)
        {
            return false;
        }

        try
        {
            if (field.GetValue(root) is UIElement candidate)
            {
                if (ReferenceEquals(candidate, element) || IsAncestor(candidate, element) || IsAncestor(element, candidate))
                {
                    inputElement = candidate;
                    return true;
                }
            }
        }
        catch
        {
            // ignore reflection failures
        }

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

        // Get placeholder for empty fields
        string placeholder = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldCreation.EmptyInputPlaceholder",
            "empty, type to enter");

        // If no value, return prefix with placeholder
        if (string.IsNullOrWhiteSpace(value))
        {
            return TextSanitizer.JoinWithComma(prefix, placeholder);
        }

        // Return prefix with value
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

            // Check for UICharacterNameButton's actualContents field first
            // This is used by world name and seed plates in world creation
            FieldInfo? actualContentsField = type.GetField("actualContents", CharacterBindingFlags);
            if (actualContentsField?.GetValue(element) is string actualContents && !string.IsNullOrWhiteSpace(actualContents))
            {
                return TextSanitizer.Clean(actualContents);
            }

            // Also handle empty actualContents explicitly - if the field exists but is empty,
            // return empty string to indicate the field was found but has no content
            if (actualContentsField is not null)
            {
                // Field exists, return whatever it contains (including empty)
                object? value = actualContentsField.GetValue(element);
                if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                {
                    return string.Empty;
                }
            }

            foreach (string propertyName in new[] { "CurrentString", "Text", "InputText", "Value" })
            {
                PropertyInfo? property = type.GetProperty(propertyName, CharacterBindingFlags);
                if (property?.GetValue(element) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return TextSanitizer.Clean(text);
                }
            }

            foreach (string fieldName in new[] { "_currentString", "_value" })
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
        WorldCreationInput name = DescribeWorldCreationInputValue(root, "name", "World name", hovered, WorldCreationNamePlateField, out bool nameFocused);
        WorldCreationInput seed = DescribeWorldCreationInputValue(root, "seed", "Seed", hovered, WorldCreationSeedPlateField, out bool seedFocused);

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

    private static WorldCreationInput DescribeWorldCreationInputValue(UIElement root, string hint, string prefix, UIElement? hovered, FieldInfo? explicitField, out bool isFocused)
    {
        isFocused = false;

        // Try explicit field reference first
        if (explicitField is not null)
        {
            try
            {
                if (explicitField.GetValue(root) is UIElement plateElement)
                {
                    // Check if hovered element matches this plate
                    if (hovered is not null &&
                        (ReferenceEquals(plateElement, hovered) ||
                         IsAncestor(plateElement, hovered) ||
                         IsAncestor(hovered, plateElement)))
                    {
                        isFocused = true;
                    }

                    return BuildWorldTextInputValue(prefix, plateElement);
                }
            }
            catch
            {
                // ignore reflection failures
            }
        }

        // Fall back to dynamic matching
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

            // Build the value part (option name + optional count + selected state)
            string baseLabel = string.IsNullOrWhiteSpace(option) ? group : option;
            string label;

            if (includeGroup && !string.IsNullOrWhiteSpace(group))
            {
                label = string.IsNullOrWhiteSpace(option) ? group : TextSanitizer.JoinWithComma(group, baseLabel);
            }
            else
            {
                label = baseLabel;
            }

            // Always include position context for better navigation awareness
            if (Total > 0)
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
            bool valueIsEmpty = string.IsNullOrWhiteSpace(value);

            // Get placeholder for empty fields
            string placeholder = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldCreation.EmptyInputPlaceholder",
                "empty, type to enter");

            string valueOrPlaceholder = valueIsEmpty ? placeholder : value;

            if (includePrefix && !string.IsNullOrWhiteSpace(prefix))
            {
                return TextSanitizer.Clean(TextSanitizer.JoinWithComma(prefix, valueOrPlaceholder));
            }

            return TextSanitizer.Clean(valueOrPlaceholder);
        }
    }

    // World creation link point IDs (assigned in UIWorldCreation.SetupGamepadPoints, base 3000)
    internal const int WcLinkBack = 3000;
    internal const int WcLinkCreate = 3001;
    internal const int WcLinkRandomizeName = 3002;
    internal const int WcLinkName = 3003;
    internal const int WcLinkRandomizeSeed = 3004;
    internal const int WcLinkSeed = 3005;
    internal const int WcLinkSizeBase = 3006;
    internal const int WcLinkDifficultyBase = 3009;
    internal const int WcLinkEvilBase = 3013;
    internal const int WcLinkMax = 3015;

    /// <summary>
    /// Returns true if the link point ID is within the world creation range (3000-3015).
    /// </summary>
    internal static bool IsWorldCreationLinkPoint(int linkPointId)
    {
        return linkPointId >= WcLinkBack && linkPointId <= WcLinkMax;
    }

    /// <summary>
    /// Returns the group name for a world creation link point (e.g., "size", "difficulty", "evil", "input", "bottom").
    /// </summary>
    internal static string GetWorldCreationLinkPointGroup(int linkPointId)
    {
        return linkPointId switch
        {
            >= WcLinkEvilBase => "evil",
            >= WcLinkDifficultyBase => "difficulty",
            >= WcLinkSizeBase => "size",
            >= WcLinkRandomizeName => "input",
            _ => "bottom",
        };
    }

    /// <summary>
    /// Describes a world creation element at the given link point ID.
    /// </summary>
    internal static string DescribeWorldCreationLinkPoint(UIState? state, int linkPointId, bool includeGroupName)
    {
        if (linkPointId == WcLinkBack)
        {
            string label = TextSanitizer.Clean(Language.GetTextValue("UI.Back"));
            if (string.IsNullOrWhiteSpace(label)) label = "Back";
            return $"{label}, 1 of 2";
        }

        if (linkPointId == WcLinkCreate)
        {
            string label = TextSanitizer.Clean(Language.GetTextValue("UI.Create"));
            if (string.IsNullOrWhiteSpace(label)) label = "Create";
            return $"{label}, 2 of 2";
        }

        if (linkPointId == WcLinkRandomizeName)
        {
            return "Randomize name";
        }

        if (linkPointId == WcLinkRandomizeSeed)
        {
            return "Randomize seed";
        }

        UIElement? root = FindWorldCreationRoot(state);
        if (root is null) return string.Empty;

        if (linkPointId == WcLinkName)
        {
            return DescribeNamePlate(root);
        }

        if (linkPointId == WcLinkSeed)
        {
            return DescribeSeedPlate(root);
        }

        // Group buttons (size, difficulty, evil)
        if (linkPointId >= WcLinkSizeBase && linkPointId <= WcLinkSizeBase + 2)
        {
            return DescribeGroupButtonByIndex(root, WorldCreationSizeButtonsField, "World size", "size",
                linkPointId - WcLinkSizeBase, includeGroupName);
        }

        if (linkPointId >= WcLinkDifficultyBase && linkPointId <= WcLinkDifficultyBase + 3)
        {
            return DescribeGroupButtonByIndex(root, WorldCreationDifficultyButtonsField, "World difficulty", "difficulty",
                linkPointId - WcLinkDifficultyBase, includeGroupName);
        }

        if (linkPointId >= WcLinkEvilBase && linkPointId <= WcLinkEvilBase + 2)
        {
            return DescribeGroupButtonByIndex(root, WorldCreationEvilButtonsField, "World evil", "evil",
                linkPointId - WcLinkEvilBase, includeGroupName);
        }

        return string.Empty;
    }

    /// <summary>
    /// Checks if the group button at the given link point is currently selected.
    /// </summary>
    internal static bool IsWorldCreationLinkPointSelected(UIState? state, int linkPointId)
    {
        UIElement? root = FindWorldCreationRoot(state);
        if (root is null) return false;

        FieldInfo? field;
        int index;
        string hint;

        if (linkPointId >= WcLinkSizeBase && linkPointId <= WcLinkSizeBase + 2)
        {
            field = WorldCreationSizeButtonsField; index = linkPointId - WcLinkSizeBase; hint = "size";
        }
        else if (linkPointId >= WcLinkDifficultyBase && linkPointId <= WcLinkDifficultyBase + 3)
        {
            field = WorldCreationDifficultyButtonsField; index = linkPointId - WcLinkDifficultyBase; hint = "difficulty";
        }
        else if (linkPointId >= WcLinkEvilBase && linkPointId <= WcLinkEvilBase + 2)
        {
            field = WorldCreationEvilButtonsField; index = linkPointId - WcLinkEvilBase; hint = "evil";
        }
        else
        {
            return false;
        }

        UIElement[] buttons = TryGetWorldCreationButtons(root, field, hint);
        if (buttons.Length == 0 || index >= buttons.Length) return false;

        return IsGroupOptionSelected(buttons[index]);
    }

    private static string DescribeGroupButtonByIndex(UIElement root, FieldInfo? field, string groupLabel, string hint,
        int index, bool includeGroup)
    {
        UIElement[] buttons = TryGetWorldCreationButtons(root, field, hint);
        if (buttons.Length == 0 || index >= buttons.Length) return string.Empty;

        UIElement button = buttons[index];
        string optionLabel = TryGetGroupOptionTitle(button);
        bool selected = IsGroupOptionSelected(button);

        string label = string.IsNullOrWhiteSpace(optionLabel) ? groupLabel : optionLabel;

        if (includeGroup && !string.IsNullOrWhiteSpace(groupLabel) && !string.IsNullOrWhiteSpace(optionLabel))
        {
            label = TextSanitizer.JoinWithComma(groupLabel, label);
        }

        label = TextSanitizer.JoinWithComma(label, $"{index + 1} of {buttons.Length}");

        if (selected)
        {
            label = TextSanitizer.JoinWithComma("Selected", label);
        }

        return TextSanitizer.Clean(label);
    }

    private static string DescribeNamePlate(UIElement root)
    {
        try
        {
            if (WorldCreationNamePlateField?.GetValue(root) is UIElement plate)
            {
                return BuildWorldTextInputLabel("World name", plate);
            }
        }
        catch
        {
            // ignore reflection failures
        }
        return "World name";
    }

    private static string DescribeSeedPlate(UIElement root)
    {
        try
        {
            if (WorldCreationSeedPlateField?.GetValue(root) is UIElement plate)
            {
                return BuildWorldTextInputLabel("Seed", plate);
            }
        }
        catch
        {
            // ignore reflection failures
        }
        return "Seed";
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

        // Check explicit field references for name/seed plates first
        if (TryMatchExplicitWorldCreationInput(root, element, WorldCreationNamePlateField, out _))
        {
            return true;
        }

        if (TryMatchExplicitWorldCreationInput(root, element, WorldCreationSeedPlateField, out _))
        {
            return true;
        }

        // Fall back to dynamic matching
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

    /// <summary>
    /// Tries to describe a world creation bottom button (Create or Back) if the element matches.
    /// These buttons are UITextPanel&lt;LocalizedText&gt; positioned at the bottom of the screen.
    /// </summary>
    internal static bool TryDescribeWorldCreationBottomButton(UIElement element, out string description)
    {
        description = string.Empty;

        if (UiWorldCreationType is null)
        {
            TerrariaAccess.Instance?.Logger.Debug("[WorldCreation] UiWorldCreationType is null - type resolution failed");
            return false;
        }

        UIElement? root = FindAncestor(element, static type => UiWorldCreationType.IsAssignableFrom(type));
        if (root is null)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[WorldCreation] Element {element.GetType().Name} has no UIWorldCreation ancestor");
            return false;
        }

        // Collect bottom buttons from the world creation screen
        var bottomButtons = CollectWorldCreationBottomButtons(root);
        if (bottomButtons.Count == 0)
        {
            TerrariaAccess.Instance?.Logger.Debug("[WorldCreation] No bottom buttons found in UI tree");
            return false;
        }

        TerrariaAccess.Instance?.Logger.Debug($"[WorldCreation] Found {bottomButtons.Count} bottom buttons, checking for match with {element.GetType().Name}");

        // Check if the element is one of the bottom buttons (or a child/ancestor of one)
        int buttonIndex = -1;
        UIElement? matchedButton = null;
        for (int i = 0; i < bottomButtons.Count; i++)
        {
            UIElement button = bottomButtons[i];
            bool isExact = ReferenceEquals(button, element);
            bool isChild = IsAncestor(button, element);
            bool isParent = IsAncestor(element, button);

            if (isExact || isChild || isParent)
            {
                buttonIndex = i;
                matchedButton = button;
                TerrariaAccess.Instance?.Logger.Debug(
                    $"[WorldCreation] Matched button {i} ({button.GetType().Name}): exact={isExact}, child={isChild}, parent={isParent}");
                break;
            }
        }

        if (buttonIndex < 0 || matchedButton is null)
        {
            // Log what we couldn't match for debugging
            TerrariaAccess.Instance?.Logger.Debug(
                $"[WorldCreation] No button match for element {element.GetType().FullName}");
            for (int i = 0; i < bottomButtons.Count; i++)
            {
                TerrariaAccess.Instance?.Logger.Debug(
                    $"[WorldCreation] Button {i}: {bottomButtons[i].GetType().FullName}");
            }
            return false;
        }

        // Try to extract text from the matched button
        string buttonText = ExtractButtonText(matchedButton);
        TerrariaAccess.Instance?.Logger.Debug($"[WorldCreation] ExtractButtonText returned: '{buttonText}'");

        // If text extraction fails, use position-based fallback
        // In world creation, Back is always on the left (index 0), Create on the right
        if (string.IsNullOrWhiteSpace(buttonText))
        {
            buttonText = buttonIndex == 0
                ? TextSanitizer.Clean(Language.GetTextValue("UI.Back"))
                : TextSanitizer.Clean(Language.GetTextValue("UI.Create"));

            // Final fallback to English if localization fails
            if (string.IsNullOrWhiteSpace(buttonText))
            {
                buttonText = buttonIndex == 0 ? "Back" : "Create";
            }

            TerrariaAccess.Instance?.Logger.Debug(
                $"[WorldCreation] Using position fallback for button {buttonIndex}: {buttonText}");
        }

        description = $"{buttonText}, {buttonIndex + 1} of {bottomButtons.Count}";
        TerrariaAccess.Instance?.Logger.Debug($"[WorldCreation] Final description: {description}");
        return true;
    }

    private static List<UIElement> CollectWorldCreationBottomButtons(UIElement root)
    {
        var buttons = new List<UIElement>();
        CollectBottomButtonsRecursive(root, buttons);

        if (buttons.Count == 0)
        {
            return buttons;
        }

        // Filter to only include buttons at the bottom of the screen (VAlign close to 1f or high Y position)
        // The bottom buttons in world creation have VAlign = 1f and Top = -45f
        float maxY = buttons.Max(static b => b.GetDimensions().Y);
        const float rowTolerance = 50f; // Allow some tolerance for bottom row detection
        buttons = buttons.Where(b => maxY - b.GetDimensions().Y < rowTolerance).ToList();

        // Sort left-to-right by X position
        buttons.Sort(static (a, b) => a.GetDimensions().X.CompareTo(b.GetDimensions().X));

        return buttons;
    }

    private static void CollectBottomButtonsRecursive(UIElement current, List<UIElement> buttons)
    {
        // Check if this is a UITextPanel type (used for Back/Create buttons)
        string? typeName = current.GetType().FullName;
        if (typeName is not null && typeName.Contains("UITextPanel", StringComparison.Ordinal))
        {
            // Only include if it looks like a button (has reasonable dimensions and is clickable)
            var dims = current.GetDimensions();
            if (dims.Width > 50f && dims.Height > 20f)
            {
                buttons.Add(current);
            }
        }

        // Also check for UIAutoScaleTextTextPanel which may be used
        if (typeName is not null && typeName.Contains("UIAutoScaleTextTextPanel", StringComparison.Ordinal))
        {
            var dims = current.GetDimensions();
            if (dims.Width > 50f && dims.Height > 20f)
            {
                buttons.Add(current);
            }
        }

        IEnumerable<UIElement>? children = current.Children;
        if (children is null)
        {
            return;
        }

        foreach (UIElement child in children)
        {
            CollectBottomButtonsRecursive(child, buttons);
        }
    }

    private static string ExtractButtonText(UIElement button)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = button.GetType();

        try
        {
            // Try Text property first (UITextPanel<T> has this)
            PropertyInfo? textProp = type.GetProperty("Text", flags);
            if (textProp is not null)
            {
                object? value = textProp.GetValue(button);

                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return TextSanitizer.Clean(text);
                }

                if (value is LocalizedText localized && !string.IsNullOrWhiteSpace(localized.Value))
                {
                    return TextSanitizer.Clean(localized.Value);
                }

                // For generic UITextPanel<T>, the Value might be convertible to string
                if (value is not null)
                {
                    string? str = value.ToString();
                    // Check if it's a meaningful string and not just a type name
                    if (!string.IsNullOrWhiteSpace(str) &&
                        !str.StartsWith("Terraria.", StringComparison.Ordinal) &&
                        !str.Contains("LocalizedText", StringComparison.Ordinal))
                    {
                        return TextSanitizer.Clean(str);
                    }

                    // For LocalizedText, try to get the Value property
                    PropertyInfo? valueProp = value.GetType().GetProperty("Value", flags);
                    if (valueProp?.GetValue(value) is string localizedValue && !string.IsNullOrWhiteSpace(localizedValue))
                    {
                        return TextSanitizer.Clean(localizedValue);
                    }
                }
            }

            // Try _text field
            FieldInfo? textField = type.GetField("_text", flags);
            if (textField?.GetValue(button) is object textFieldValue)
            {
                if (textFieldValue is string fieldText && !string.IsNullOrWhiteSpace(fieldText))
                {
                    return TextSanitizer.Clean(fieldText);
                }

                if (textFieldValue is LocalizedText localizedField && !string.IsNullOrWhiteSpace(localizedField.Value))
                {
                    return TextSanitizer.Clean(localizedField.Value);
                }

                // Try Value property on the text object
                PropertyInfo? valueProp = textFieldValue.GetType().GetProperty("Value", flags);
                if (valueProp?.GetValue(textFieldValue) is string localizedValue && !string.IsNullOrWhiteSpace(localizedValue))
                {
                    return TextSanitizer.Clean(localizedValue);
                }
            }

            // Try SetText method's backing field or similar
            FieldInfo? innerTextField = type.GetField("_innerText", flags) ?? type.GetField("innerText", flags);
            if (innerTextField?.GetValue(button) is UIElement innerTextElement)
            {
                // Try to get text from the inner element
                PropertyInfo? innerTextProp = innerTextElement.GetType().GetProperty("Text", flags);
                if (innerTextProp?.GetValue(innerTextElement) is string innerText && !string.IsNullOrWhiteSpace(innerText))
                {
                    return TextSanitizer.Clean(innerText);
                }
            }
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[WorldCreation] Failed to extract button text: {ex.Message}");
        }

        return string.Empty;
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
