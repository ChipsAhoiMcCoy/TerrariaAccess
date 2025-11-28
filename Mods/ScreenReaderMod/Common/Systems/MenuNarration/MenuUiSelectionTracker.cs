#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Terraria;
using Terraria.GameInput;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader.Core;
using Terraria.ModLoader.UI.ModBrowser;
using Terraria.Social.Steam;
using Terraria.ModLoader;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuUiSelectionTracker
{
    private static readonly FieldInfo? LastHoverField = typeof(UserInterface).GetField("_lastElementHover", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Dictionary<Type, LabelAccessors> LabelAccessorCache = new();
    private static readonly Type? UiCharacterCreationType = Type.GetType("Terraria.GameContent.UI.States.UICharacterCreation, tModLoader");
    private static readonly Type? UiColoredImageButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIColoredImageButton, tModLoader");
    private static readonly Type? UiHairStyleButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIHairStyleButton, tModLoader");
    private static readonly Type? UiClothStyleButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIClothStyleButton, tModLoader");
    private static readonly Type? UiColoredSliderType = Type.GetType("Terraria.GameContent.UI.Elements.UIColoredSlider, tModLoader");
    private static readonly Type? UiCharacterNameButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UICharacterNameButton, tModLoader");
    private static readonly Type? UiDifficultyButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIDifficultyButton, tModLoader");
    private static readonly Type? UiWorldCreationType = Type.GetType("Terraria.GameContent.UI.States.UIWorldCreation, tModLoader");
    private const BindingFlags CharacterBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? CharacterCreationColorPickersField = UiCharacterCreationType?.GetField("_colorPickers", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationClothingButtonField = UiCharacterCreationType?.GetField("_clothingStylesCategoryButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationHairStylesButtonField = UiCharacterCreationType?.GetField("_hairStylesCategoryButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationCharInfoButtonField = UiCharacterCreationType?.GetField("_charInfoCategoryButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationCopyHexButtonField = UiCharacterCreationType?.GetField("_copyHexButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationPasteHexButtonField = UiCharacterCreationType?.GetField("_pasteHexButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationRandomColorButtonField = UiCharacterCreationType?.GetField("_randomColorButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationCopyTemplateButtonField = UiCharacterCreationType?.GetField("_copyTemplateButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationPasteTemplateButtonField = UiCharacterCreationType?.GetField("_pasteTemplateButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationRandomizePlayerButtonField = UiCharacterCreationType?.GetField("_randomizePlayerButton", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationGenderMaleField = UiCharacterCreationType?.GetField("_genderMale", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationGenderFemaleField = UiCharacterCreationType?.GetField("_genderFemale", CharacterBindingFlags);
    private static readonly FieldInfo? CharacterCreationPlayerField = UiCharacterCreationType?.GetField("_player", CharacterBindingFlags);
    private static readonly FieldInfo? UiColoredImageButtonSelectedField = UiColoredImageButtonType?.GetField("_selected", CharacterBindingFlags);
    private static readonly FieldInfo? UiColoredSliderValueFuncField = UiColoredSliderType?.GetField("_getStatusTextAct", CharacterBindingFlags);
    private static readonly FieldInfo? UiColoredSliderGamepadActionField = UiColoredSliderType?.GetField("_slideGamepadAction", CharacterBindingFlags);
    private static readonly FieldInfo? HairStyleIdField = UiHairStyleButtonType?.GetField("HairStyleId", CharacterBindingFlags);
    private static readonly FieldInfo? ClothStyleIdField = UiClothStyleButtonType?.GetField("ClothStyleId", CharacterBindingFlags);
    private static readonly FieldInfo? NameButtonContentsField = UiCharacterNameButtonType?.GetField("actualContents", CharacterBindingFlags);
    private static readonly FieldInfo? NameButtonEmptyTextField = UiCharacterNameButtonType?.GetField("_textToShowWhenEmpty", CharacterBindingFlags);
    private static readonly FieldInfo? DifficultyButtonValueField = UiDifficultyButtonType?.GetField("_difficulty", CharacterBindingFlags);
    private static readonly PropertyInfo? DifficultyButtonSelectedProperty = UiDifficultyButtonType?.GetProperty("IsSelected", CharacterBindingFlags);
    private static readonly FieldInfo? WorldCreationSizeButtonsField = UiWorldCreationType?.GetField("_sizeButtons", CharacterBindingFlags);
    private static readonly FieldInfo? WorldCreationDifficultyButtonsField = UiWorldCreationType?.GetField("_difficultyButtons", CharacterBindingFlags);
    private static readonly FieldInfo? WorldCreationEvilButtonsField = UiWorldCreationType?.GetField("_evilButtons", CharacterBindingFlags);
    private static readonly string?[] HairStyleDescriptions =
    {
        "Large messy",
        "Small messy",
        "Combed forward, two strands sticking up, long pointed beard.",
        "Anvil like, flat, short, flat top",
        "Short, covers face",
        "Bangs, small ponytail, covers face",
        "Long Curly Braid.",
        "Short, bangs",
        "Front swoop",
        "Pronounced bangs, L shaped ponytail",
        "Massive Mohawk",
        "Circular Space Buns",
        "Curly dreads, covers face",
        "Medium messy",
        "Combed forward, two strands sticking up",
        "Bald",
        "Impressively large Afro",
        "Combed, small beard",
        "Bangs, curly ponytail",
        "Short, swoop, parted in middle",
        "Wizard hat like, parted in middle, two points in back",
        "Bangs, small singular curl as ponytail",
        "Bowl ish cut, wavy long ponytail",
        "Spiky Sonic the Hedgehog-like",
        "Parted in middle, two left swoops",
        "Princess Leia buns",
        "Bangs, combed, small ponytail",
        "Old-Century styled Mens Ponytail.",
        "Combed, Elvis ish, pushed up top",
        "Bowlish cut, high ponytail, long, straight",
        "Windswept, combed back",
        "Wavy, Spiked, Tapered Sides.",
        "Small pigtails, bangs",
        "Small braids, shoulder length",
        "Tight Braids",
        "Small spiked up, like Sam from Stardew Valley",
        "Bow shape, umbrella, mop style",
        "Johnny Bravo Pompadour",
        "Large ponytail, straight, wavy ponytail",
        "A-line Bob",
        "Single curl, pronounced part in middle, covers face, Bob",
        "Partly covers left eye, single curl, parted in middle, Bob",
        "Partly covers left eye, swoop right, parted in middle, Bob",
        "Flame ponytail, partly covers left eye, Super Saiyan",
        "Parted diagonally, bob, curl",
        "Straight Karen cut, parted diagonally",
        "Curly ponytail, medium, bangs",
        "Long High Ponytail, bangs",
        "Braided Bun",
        "Very Short Flat Bob",
        "Bangs, Medium Curly Ponytail",
        "Bangs, straight, forehead peak, Y in back of hairstyle",
        "Shaved side, unibrow, forward front Pompadour",
        "Bush like, puffy, messy",
        "Leaf shaped bangs, short-medium, front part",
        "Long, leafish shaped points, front part",
        "Straight, long, beard Jesus style",
        "Straight, Medium, beard Jesus style",
        "Short, parted in middle, Jesus beard",
        "Very short, parted diagonally, Jesus beard",
        "Parted in middle, bangs, C shape, short beard",
        "Short, front part, left piece, Chin-strap Beard",
        "Fancy, combed, parted in middle, beard, mustache",
        "Wavy, long, Hair and beard Jesus style",
        "Bangs, Y shaped, long, straight, leafish shaped bangs",
        "Tucked front braid, long, straight",
        "Long, parted in middle, twist at back.",
        "Front part, medium, straight",
        "Parted diagonally, Curtain bangs, long, straight",
        "Covers left eye, wavy, front part, straight",
        "Widows peak, medium, ear space",
        "Leaf shaped bangs, parted in middle, medium, straight",
        "Covers left eye, pronounced parted in middle, long, straight",
        "Curtain bangs, parted in middle, long chunky braid",
        "Short, neat, thin evil twirly mustache with Pointy Beard",
        "Short, pronounced parted in middle, Long Curly Mustache,",
        "Bald, eyebrow, Goatee",
        "Pronounced parted in middle, short, evil mustache, straight",
        "Short ponytail, front part, v in side, covers left eye",
        "Short ponytail, Straight Blunt Bangs, Fluffy",
        "Very Long Ponytail with Scrunchie",
        "2 long Pigtails, curly front, parted in middle",
        "Short, straight, Deeply Parted, bangs",
        "Pronounced bangs, \"fox ears\", Thick Spiraled ponytail",
        "Pronounced bangs, puffy, shoulder length",
        "Middle-part Lara Croft Braid.",
        "Covers left eye, Y in part of hair, very large ponytail, Wavy",
        "Covers left eye, very long, wavy",
        "Sideswept, curly",
        "Side-swept, Braided Hair, Blunt Straight Bangs",
        "Medium Curly Pigtails, Straight Blunt Bangs",
        "Low Medium Ponytail, , straight Blunt bangs",
        "Four Large Spikes on either side of head going toward back of head.",
        "Short, straight, 2 sections, round",
        "Chinese (?) / Asian ponytail, bald other side, sm mustache",
        "Straight, medium, uncombed, pronounced parted in middle",
        "Picard, Monk Hair",
        "Bowlish cut, winter hat ish, bun raised",
        "Medium Straight Afro",
        "Combed-back Afro",
        "Medium Straight Afro, Short Beard",
        "Long, curly, fluffy",
        "Medium, curly, bangs",
        "Leaf shaped bangs, long, curly",
        "Short, front part.",
        "Short, front part, bowlish cut",
        "Long, fluffy, bangs, wavy, ahoge",
        "Combed, round.",
        "Bald, ahoge",
        "Tight pulled hair, short ponytail, eyebrow",
        "Bob, ahoge, curly",
        "Sideswept, short, Dreads",
        "Sideswept, long Dreads",
        "Fancy, combed, curl",
        "Short, flat, pronounced bangs, 2 sections",
        "Shaved side, very short bangs",
        "Shaved side, very short bangs with beard stubble",
        "Combed, short, shaved side, small beard, mustache",
        "Roundish, long thin mustache, short, with Beard",
        "Short, small swoops, small beard, small mustache",
        "Very short, thin, thin long beard",
        "Combed, round, Short Full Beard",
        "Round, combed, side shaved, mutton chops",
        "Wizard hat ish, medium, cone, cake layer ish",
        "Ram horn ish, round, short, curl",
        "Alien probe",
        "UFO",
        "Medusa Hair",
        "Many bulbs or circles, approximately 15-20",
        "Parted diagonally, 3 sections, geometric",
        "Bowl cut, short",
        "Wild, large spikes, Super Saiyan",
        "Medium mohawk, shaved side, Predator Hair",
        "Pronounced bangs, long, ahoge, wavy, round",
        "Rockstar, pronounced bangs, long, wavy",
        "Short, ear hole, bangs, parted diagonally, straight",
        "Shaved side, Short Pulled-back Ponytail, medium triangle shaped beard",
        "Short Pronounced Middle-part",
        "Flat, ear hole, , short \"The Johnny Bravo\".",
        "Picard, small beard, \"The Monk Hairstyle\"",
        "Mullet, curls out on ends.",
        "Short, parted diagonally, bangs",
        "Short, puffy, pronounced bangs, round",
        "Winter hat ish, puffy, round, loose bun",
        "Very short, buzz cut",
        "Curly Pig-tail Space-buns with Curled bangs.",
        "Long, wavy, very small ahoge, Layered",
        "Short, combed, side shaved, medium beard",
        "Curl on top, fluffy, short, small beard, parted diagonally",
        "Spiky, short, parted diagonally, very small beard. Roxas from Kingdom Hearts",
        "Roundish, short, small beard, messy",
        "Round, combed, ear hole, short, medium beard",
        "Balding, mutton chops, large beard",
        "Flat, short, mutton chops, medium beard",
        "Mohawk, small ahoge, long beard",
        "Combed, round, short, top point, medium beard. Rhett from Good Mythical Morning",
        "Combed, fancy, swirlish, earhole, small beard",
        "Messy, pronounced parted in middle, round, long beard",
        "Short, parted in middle, round, long beard",
        "Short, combed, pointy, medium beard, mutton chops",
        "Thin Pony-tail Balding, mutton chops",
        "Short, earhole, parted in middle, bangs, small beard, mutton chops",
        "Medium, wavy, medium ponytail, bangs",
        "Giant Crab-Claw-like Hair",
        "Spiked Bangs with Curly Long Pony-tail with Short Ahoge",
    };

    private UIElement? _lastElement;
    private string? _lastLabel;
    private readonly Stack<UIElement> _traversalStack = new();

    public void Reset()
    {
        _lastElement = null;
        _lastLabel = null;
    }

    public bool TryGetHoverLabel(UserInterface? menuUi, out MenuUiLabel label)
    {
        label = default;

        if (menuUi is null)
        {
            return false;
        }

        UIElement? hovered = LastHoverField?.GetValue(menuUi) as UIElement;
        if (hovered is null)
        {
            return false;
        }

        string text = ExtractLabel(hovered) ?? string.Empty;

        bool isNew = !ReferenceEquals(hovered, _lastElement) || !string.Equals(text, _lastLabel, StringComparison.OrdinalIgnoreCase);
        _lastElement = hovered;
        _lastLabel = text;
        label = new MenuUiLabel(hovered, text, isNew);
        return true;
    }

    private static readonly Dictionary<Type, bool> LoggedMissingLabels = new();

    private string ExtractLabel(UIElement element)
    {
        _traversalStack.Clear();
        _traversalStack.Push(element);

        while (_traversalStack.Count > 0)
        {
            UIElement current = _traversalStack.Pop();
            string label = ExtractDirectLabel(current);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (current.Children is { } children)
            {
                foreach (UIElement child in children)
                {
                    _traversalStack.Push(child);
                }
            }
        }

        UIElement? ancestor = element.Parent;
        while (ancestor is not null)
        {
            string label = ExtractDirectLabel(ancestor);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            ancestor = ancestor.Parent;
        }

        return string.Empty;
    }

    private static string ExtractDirectLabel(UIElement element)
    {
        Type type = element.GetType();
        LabelAccessors accessors = GetAccessors(type);

        string custom = ExtractSpecializedLabel(type, element);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        if (accessors.TextProperty is not null)
        {
            object? value = accessors.TextProperty.GetValue(element);
            if (value is string text)
            {
                return text.Trim();
            }

            if (value is not null)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        if (accessors.TextField is not null)
        {
            object? value = accessors.TextField.GetValue(element);
            if (value is string text)
            {
                return text.Trim();
            }
        }

        if (accessors.ValueField is not null)
        {
            object? value = accessors.ValueField.GetValue(element);
            if (value is string text)
            {
                return text.Trim();
            }
        }

        if (!LoggedMissingLabels.ContainsKey(type))
        {
            LoggedMissingLabels[type] = true;
            DumpElementMetadata(type, element);
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Missing hover label for UI element type {type.FullName ?? "<unknown>"}.");
        }

        return string.Empty;
    }

    private static LabelAccessors GetAccessors(Type type)
    {
        if (LabelAccessorCache.TryGetValue(type, out LabelAccessors? cached) && cached is not null)
        {
            return cached;
        }

        LabelAccessors resolved = LabelAccessorFactory.Create(type);
        LabelAccessorCache[type] = resolved;
        return resolved;
    }

    private static string ExtractSpecializedLabel(Type type, UIElement element)
    {
        string characterCreationLabel = DescribeCharacterCreationElement(element);
        if (!string.IsNullOrWhiteSpace(characterCreationLabel))
        {
            return characterCreationLabel;
        }

        string worldCreationLabel = DescribeWorldCreationElement(element);
        if (!string.IsNullOrWhiteSpace(worldCreationLabel))
        {
            return worldCreationLabel;
        }

        string? fullName = type.FullName;
        switch (fullName)
        {
            case "Terraria.GameContent.UI.Elements.UICharacterListItem":
                DumpElementMetadata(type, element);
                if (TryGetData(element, out PlayerFileData? playerData) && playerData is not null)
                {
                    return DescribePlayer(playerData);
                }
                break;
            case "Terraria.GameContent.UI.Elements.UIWorldListItem":
                DumpElementMetadata(type, element);
                if (TryGetData(element, out WorldFileData? worldData) && worldData is not null)
                {
                    return DescribeWorld(worldData);
                }
                break;
            case "Terraria.GameContent.UI.Elements.UIKeybindingSimpleListItem":
                return DescribeKeybindingSimpleItem(element);
            case "Terraria.GameContent.UI.Elements.UIKeybindingListItem":
                return DescribeKeybindingListItem(element);
            case "Terraria.GameContent.UI.Elements.UIKeybindingSliderItem":
                return DescribeKeybindingSliderItem(element);
            case "Terraria.GameContent.UI.Elements.UIKeybindingToggleListItem":
                return DescribeKeybindingToggleItem(element);
            case "Terraria.GameContent.UI.Elements.UIImageButton":
                return DescribeImageButton(element);
            case "Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem":
                return DescribeModDownloadItem(element);
            case string name when name.StartsWith("Terraria.ModLoader.UI.ModBrowser.UIBrowserFilterToggle", StringComparison.Ordinal):
                return DescribeBrowserFilterToggle(element);
            case string name when name.Contains("UIHoverImage", StringComparison.OrdinalIgnoreCase):
                {
                    string label = DescribeModBrowserButton(element);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        return label;
                    }
                    break;
                }
            case "Terraria.ModLoader.UI.UICycleImage":
            {
                string tagLabel = DescribeTagFilterToggle(element);
                if (!string.IsNullOrWhiteSpace(tagLabel))
                {
                    return tagLabel;
                }
                break;
            }
            default:
                if (fullName?.EndsWith("UICharacterListItem", StringComparison.Ordinal) == true)
                {
                    DumpElementMetadata(type, element);
                    if (TryGetData(element, out PlayerFileData? fallbackPlayer) && fallbackPlayer is not null)
                    {
                        return DescribePlayer(fallbackPlayer);
                    }
                }
                else if (fullName?.EndsWith("UIWorldListItem", StringComparison.Ordinal) == true)
                {
                    DumpElementMetadata(type, element);
                    if (TryGetData(element, out WorldFileData? fallbackWorld) && fallbackWorld is not null)
                    {
                        return DescribeWorld(fallbackWorld);
                    }
                }
                break;
        }

        if (typeof(UIState).IsAssignableFrom(type.BaseType ?? typeof(object)))
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string DescribeKeybindingSimpleItem(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        Type type = element.GetType();
        FieldInfo? textFuncField = type.GetField("_GetTextFunction", flags);
        if (textFuncField?.GetValue(element) is Delegate textFunc)
        {
            try
            {
                object? value = textFunc.DynamicInvoke();
                if (value is not null)
                {
                    return TextSanitizer.Clean(value.ToString() ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Keybinding text extraction failed: {ex.Message}");
            }
        }

        return string.Empty;
    }

    private static string DescribeKeybindingListItem(UIElement element)
    {
        Type type = element.GetType();

        string friendly = InvokeFriendlyName(type, element);
        InputMode mode = ReadField<InputMode>(type, element, "_inputmode");
        string? keybind = ReadField<string>(type, element, "_keybind");

        string assignment = DescribeBindingAssignment(mode, keybind);
        if (string.IsNullOrWhiteSpace(friendly))
        {
            return assignment;
        }

        if (string.IsNullOrWhiteSpace(assignment))
        {
            return friendly;
        }

            return TextSanitizer.Clean($"{friendly}: {assignment}");
    }

    private static string DescribeKeybindingSliderItem(UIElement element)
    {
        string label = InvokeFunc<string>(element, "_TextDisplayFunction");
        float value = InvokeFunc<float>(element, "_GetStatusFunction");

        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            value = 0f;
        }

        string valueText = value is >= 0f and <= 1f
            ? $"{Math.Round(value * 100f):0}%"
            : value.ToString("0.##");

        if (string.IsNullOrWhiteSpace(label))
        {
            return valueText;
        }

        return TextSanitizer.Clean($"{label}: {valueText}");
    }

    private static string DescribeKeybindingToggleItem(UIElement element)
    {
        string label = InvokeFunc<string>(element, "_TextDisplayFunction");
        bool isOn = InvokeFunc<bool>(element, "_IsOnFunction");
        string state = isOn
            ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "On")
            : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOff", "Off");

        if (string.IsNullOrWhiteSpace(label))
        {
            return state;
        }

        return TextSanitizer.Clean($"{label}: {state}");
    }

    private static string InvokeFriendlyName(Type type, UIElement element)
    {
        try
        {
            MethodInfo? method = type.GetMethod("GetFriendlyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is not null && method.Invoke(element, Array.Empty<object>()) is string result)
            {
                return TextSanitizer.Clean(result);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Friendly name lookup failed for {type.Name}: {ex.Message}");
        }

        return string.Empty;
    }

    private static T InvokeFunc<T>(UIElement element, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        try
        {
            FieldInfo? field = element.GetType().GetField(fieldName, flags);
            if (field?.GetValue(element) is Delegate del)
            {
                object? value = del.DynamicInvoke();
                if (value is T typed)
                {
                    return typed;
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Delegate '{fieldName}' invocation failed: {ex.Message}");
        }

        return default!;
    }

    private static T ReadField<T>(Type type, object instance, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        try
        {
            FieldInfo? field = type.GetField(fieldName, flags);
            if (field is not null && field.GetValue(instance) is T value)
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Field '{fieldName}' read failed: {ex.Message}");
        }

        return default!;
    }

    private static string DescribeBindingAssignment(InputMode mode, string? keybind)
    {
        if (string.IsNullOrWhiteSpace(keybind))
        {
            return string.Empty;
        }

        PlayerInputProfile? profile = PlayerInput.CurrentProfile;
        if (profile is null || profile.InputModes is null || !profile.InputModes.TryGetValue(mode, out KeyConfiguration? configuration))
        {
            return string.Empty;
        }

        if (!configuration.KeyStatus.TryGetValue(keybind, out List<string>? entries))
        {
            return string.Empty;
        }

        var filtered = entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToList();
        if (filtered.Count == 0)
        {
            return LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.Unbound", "Unbound");
        }

        string joined = string.Join(", ", filtered.Select(ConvertBindingToken));
        return TextSanitizer.Clean(joined);
    }

    private static string ConvertBindingToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        // PlayerInput stores identifiers like MouseRight, DpadUp, Button1, etc.
        // Insert spaces before capital letters/numbers to make them readable.
        var builder = new System.Text.StringBuilder(token.Length + 4);
        char previous = '\0';
        foreach (char c in token)
        {
            if (builder.Length > 0 && char.IsUpper(c) && !char.IsUpper(previous))
            {
                builder.Append(' ');
            }
            else if (builder.Length > 0 && char.IsDigit(c) && !char.IsDigit(previous))
            {
                builder.Append(' ');
            }

            builder.Append(c);
            previous = c;
        }

        return builder.ToString().Replace("Mouse", "Mouse ").Replace("Axis", "Axis ").Trim();
    }

    private static bool TryGetData<T>(UIElement element, out T? data) where T : class
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = element.GetType();

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!typeof(T).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            try
            {
                if (property.GetValue(element) is T propertyValue && propertyValue is not null)
                {
                    data = propertyValue;
                    return true;
                }
            }
            catch
            {
                // ignore property access failures
            }
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (!typeof(T).IsAssignableFrom(field.FieldType))
            {
                continue;
            }

            try
            {
                if (field.GetValue(element) is T fieldValue && fieldValue is not null)
                {
                    data = fieldValue;
                    return true;
                }
            }
            catch
            {
                // ignore field access failures
            }
        }

        data = null;
        return false;
    }

    private static string DescribePlayer(PlayerFileData data)
    {
        string name = DescribePlayerName(data);

        string difficulty = data.Player?.difficulty switch
        {
            1 => LocalizationHelper.GetTextOrFallback("UI.Mediumcore", "Mediumcore"),
            2 => LocalizationHelper.GetTextOrFallback("UI.Hardcore", "Hardcore"),
            3 => LocalizationHelper.GetTextOrFallback("UI.Journey", "Journey"),
            _ => LocalizationHelper.GetTextOrFallback("UI.Classic", "Classic"),
        };

        string favorite = data.IsFavorite ? LocalizationHelper.GetTextOrFallback("UI.FavoriteTooltip", "Favorite") : string.Empty;

        return TextSanitizer.JoinWithComma(name, difficulty, favorite);
    }

    private static string DescribeWorld(WorldFileData data)
    {
        string name = DescribeWorldName(data);

        string size = DescribeWorldSize(data.WorldSizeX);

        string mode = data.GameMode switch
        {
            1 => LocalizationHelper.GetTextOrFallback("UI.Expert", "Expert"),
            2 => LocalizationHelper.GetTextOrFallback("UI.Master", "Master"),
            3 => LocalizationHelper.GetTextOrFallback("UI.Journey", "Journey"),
            _ => LocalizationHelper.GetTextOrFallback("UI.Classic", "Classic"),
        };

        string favorite = data.IsFavorite ? LocalizationHelper.GetTextOrFallback("UI.FavoriteTooltip", "Favorite") : string.Empty;

        return TextSanitizer.JoinWithComma(name, mode, size, favorite);
    }

    private static string DescribePlayerName(PlayerFileData? data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Name))
        {
            return LocalizationHelper.GetTextOrFallback("UI.PlayerNameDefault", "Player");
        }

        return TextSanitizer.Clean(data.Name);
    }

    private static string DescribeWorldName(WorldFileData? data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Name))
        {
            return LocalizationHelper.GetTextOrFallback("UI.WorldNameDefault", "World");
        }

        return TextSanitizer.Clean(data.Name);
    }

    private static string DescribeWorldSize(int worldWidth)
    {
        if (worldWidth <= 4400)
        {
            return TextSanitizer.Clean(Lang.menu[92].Value);
        }

        if (worldWidth <= 7000)
        {
            return TextSanitizer.Clean(Lang.menu[93].Value);
        }

        return TextSanitizer.Clean(Lang.menu[94].Value);
    }

    private static readonly HashSet<Type> LoggedElementMetadata = new();

    private static void DumpElementMetadata(Type type, UIElement element)
    {
        if (!LoggedElementMetadata.Add(type))
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Introspection for {type.FullName ?? "<unknown>"}");

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            string typeName = property.PropertyType.FullName ?? property.PropertyType.Name;
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration]  Property: {property.Name} ({typeName})");
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            string typeName = field.FieldType.FullName ?? field.FieldType.Name;
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration]  Field: {field.Name} ({typeName})");
        }
    }

    private static string DescribeImageButton(UIElement element)
    {
        UIElement? listItem = FindAncestor(element, static type =>
        {
            string? name = type.FullName;
            return name is not null && (name.Contains("UICharacterListItem", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("UIWorldListItem", StringComparison.OrdinalIgnoreCase));
        });

        if (listItem is null)
        {
            return string.Empty;
        }

        bool isWorld = listItem.GetType().FullName?.Contains("UIWorldListItem", StringComparison.OrdinalIgnoreCase) == true;

        List<UIElement> buttons = CollectImageButtons(listItem);
        if (buttons.Count == 0)
        {
            return string.Empty;
        }

        int index = buttons.IndexOf(element);
        if (index < 0)
        {
            UIElement? match = buttons.FirstOrDefault(button => IsAncestor(button, element));
            if (match is not null)
            {
                index = buttons.IndexOf(match);
            }
        }

        if (index < 0)
        {
            return string.Empty;
        }

        if (isWorld)
        {
            _ = TryGetData(listItem, out WorldFileData? worldData);
            return DescribeWorldButtonByIndex(index, worldData);
        }

        _ = TryGetData(listItem, out PlayerFileData? playerData);
        return DescribeCharacterButtonByIndex(index, playerData);
    }

    private static UIElement? FindAncestor(UIElement element, Func<Type, bool> predicate)
    {
        UIElement? current = element;
        while (current is not null)
        {
            Type type = current.GetType();
            if (predicate(type))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static List<UIElement> CollectImageButtons(UIElement root)
    {
        var buttons = new List<UIElement>();
        CollectImageButtonsRecursive(root, buttons);

        buttons.Sort(static (a, b) =>
        {
            float ax = a.GetDimensions().X;
            float bx = b.GetDimensions().X;
            int compareX = ax.CompareTo(bx);
            if (compareX != 0)
            {
                return compareX;
            }

            float ay = a.GetDimensions().Y;
            float by = b.GetDimensions().Y;
            return ay.CompareTo(by);
        });

        return buttons;
    }

    private static void CollectImageButtonsRecursive(UIElement current, List<UIElement> buttons)
    {
        if (current.GetType().FullName == "Terraria.GameContent.UI.Elements.UIImageButton")
        {
            buttons.Add(current);
            return;
        }

        IEnumerable<UIElement>? children = current.Children;
        if (children is null)
        {
            return;
        }

        foreach (UIElement child in children)
        {
            CollectImageButtonsRecursive(child, buttons);
        }
    }

    private static bool IsAncestor(UIElement ancestor, UIElement element)
    {
        UIElement? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static string DescribeCharacterButtonByIndex(int index, PlayerFileData? data)
    {
        string playerName = DescribePlayerName(data);
        return index switch
        {
            0 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Play", "Play"), playerName),
            1 => TextSanitizer.JoinWithComma(
                LocalizationHelper.GetTextOrFallback("UI.Favorite", "Favorite"),
                (data?.IsFavorite ?? false)
                    ? LocalizationHelper.GetTextOrFallback("UI.FavoriteOn", "On")
                    : LocalizationHelper.GetTextOrFallback("UI.FavoriteOff", "Off"),
                playerName),
            2 => TextSanitizer.JoinWithComma(DescribeCloudToggle(data?.IsCloudSave ?? false), playerName),
            3 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Rename", "Rename"), playerName),
            4 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete"), playerName),
            _ => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Button", $"Button {index + 1}"), playerName),
        };
    }

    private static string DescribeWorldButtonByIndex(int index, WorldFileData? data)
    {
        string worldName = DescribeWorldName(data);
        return index switch
        {
            0 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Play", "Play"), worldName),
            1 => TextSanitizer.JoinWithComma(
                LocalizationHelper.GetTextOrFallback("UI.Favorite", "Favorite"),
                (data?.IsFavorite ?? false)
                    ? LocalizationHelper.GetTextOrFallback("UI.FavoriteOn", "On")
                    : LocalizationHelper.GetTextOrFallback("UI.FavoriteOff", "Off"),
                worldName),
            2 => TextSanitizer.JoinWithComma(DescribeCloudToggle(data?.IsCloudSave ?? false), worldName),
            3 => TextSanitizer.JoinWithComma(DescribeWorldSeed(data), worldName),
            4 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Rename", "Rename"), worldName),
            5 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete"), worldName),
            6 => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete"), worldName),
            _ => TextSanitizer.JoinWithComma(LocalizationHelper.GetTextOrFallback("UI.Button", $"Button {index + 1}"), worldName),
        };
    }

    private static string DescribeWorldSeed(WorldFileData? data)
    {
        string label = LocalizationHelper.GetTextOrFallback("UI.CopySeedToClipboard", "Copy seed");
        string seed = ExtractWorldSeed(data);
        return string.IsNullOrWhiteSpace(seed) ? label : $"{label}: {seed}";
    }

    private static string DescribeCloudToggle(bool isCloudSave)
    {
        string label = LocalizationHelper.GetTextOrFallback("UI.MoveToCloud", "Move to cloud");
        string status = DescribeCloudStatus(isCloudSave);
        if (string.IsNullOrWhiteSpace(status) || string.Equals(label, status, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return TextSanitizer.JoinWithComma(label, status);
    }

    private static string DescribeCloudStatus(bool isCloudSave)
    {
        if (isCloudSave)
        {
            return LocalizationHelper.GetTextOrFallback("UI.MoveFromCloud", "Stored in cloud");
        }

        string localized = Language.GetTextValue("UI.MoveToCloud");
        if (string.IsNullOrWhiteSpace(localized) || string.Equals(localized, "UI.MoveToCloud", StringComparison.Ordinal))
        {
            return "Stored locally";
        }

        if (localized.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Stored locally";
        }

        return TextSanitizer.Clean(localized);
    }

    private static string ExtractWorldSeed(WorldFileData? data)
    {
        if (data is null)
        {
            return string.Empty;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            MethodInfo? method = data.GetType().GetMethod("GetFullSeedText", flags, Array.Empty<Type>());
            if (method is not null && method.Invoke(data, Array.Empty<object?>()) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore reflection failures
        }

        static string TryReadProperty(WorldFileData source, string name, BindingFlags propertyFlags)
        {
            try
            {
                PropertyInfo? property = source.GetType().GetProperty(name, propertyFlags);
                if (property is not null && property.GetValue(source) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return TextSanitizer.Clean(value);
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        string candidate = TryReadProperty(data, "SeedText", flags);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        candidate = TryReadProperty(data, "Seed", flags);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        candidate = TryReadProperty(data, "SeedTextDisplay", flags);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        try
        {
            MethodInfo? method = data.GetType().GetMethod("GetSeedText", flags, Array.Empty<Type>());
            if (method is not null && method.Invoke(data, Array.Empty<object?>()) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore reflection failures
        }

        return string.Empty;
    }

    private static string DescribeModDownloadItem(UIElement element)
    {
        if (!TryGetData(element, out ModDownloadItem? modData) || modData is null)
        {
            return string.Empty;
        }

        string name = !string.IsNullOrWhiteSpace(modData.DisplayNameClean)
            ? modData.DisplayNameClean
            : modData.DisplayName ?? modData.ModName ?? "Mod";

        string author = TextSanitizer.Clean(modData.Author);
        string side = DescribeModSide(modData.ModSide);
        string status = DescribeInstallStatus(modData);

        var parts = new List<string>(6)
        {
            TextSanitizer.Clean(name),
        };

        if (!string.IsNullOrWhiteSpace(author))
        {
            parts.Add($"by {author}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            parts.Add(side);
        }

        if (modData.Banned)
        {
            parts.Add("Banned on Workshop");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add(status);
        }
        else if (modData.Version is not null)
        {
            parts.Add($"Version {modData.Version}");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static string DescribeInstallStatus(ModDownloadItem modData)
    {
        if (modData.AppNeedRestartToReinstall)
        {
            return "Restart required to reinstall";
        }

        if (modData.NeedUpdate)
        {
            string to = modData.Version?.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(to) ? "Update available" : $"Update available to {to}";
        }

        if (modData.IsInstalled)
        {
            return string.IsNullOrWhiteSpace(modData.Version?.ToString()) ? "Installed" : $"Installed, available {modData.Version}";
        }

        return string.IsNullOrWhiteSpace(modData.Version?.ToString()) ? "Not installed" : $"Not installed, version {modData.Version}";
    }

    private static string DescribeModSide(ModSide side)
    {
        return side switch
        {
            ModSide.Both => "Client and server",
            ModSide.Client => "Client only",
            ModSide.Server => "Server only",
            ModSide.NoSync => "No sync",
            _ => string.Empty,
        };
    }

    private static string DescribeBrowserFilterToggle(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? stateProperty = element.GetType().GetProperty("State", flags);
        object? stateValue = stateProperty?.GetValue(element);
        Type? stateType = stateValue?.GetType();
        if (stateType is null || !stateType.IsEnum)
        {
            return string.Empty;
        }

        string? label = stateValue switch
        {
            SearchFilter search => DescribeSearchFilter(search),
            ModBrowserSortMode sort => DescribeSortMode(sort),
            ModBrowserTimePeriod period => DescribeTimePeriod(period),
            UpdateFilter update => DescribeUpdateFilter(update),
            ModSideFilter side => DescribeModSideFilter(side),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return TextSanitizer.Clean(label);
    }

    private static string DescribeModBrowserButton(UIElement element)
    {
        UIElement? modItem = FindAncestor(element, static type => type.FullName == "Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem");
        if (modItem is null)
        {
            return string.Empty;
        }

        Type modItemType = modItem.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        bool Matches(string fieldName)
        {
            try
            {
                FieldInfo? field = modItemType.GetField(fieldName, flags);
                return field?.GetValue(modItem) is UIElement target && ReferenceEquals(target, element);
            }
            catch
            {
                return false;
            }
        }

        if (Matches("_moreInfoButton"))
        {
            return DescribeModInfo(modItemType, modItem);
        }

        if (Matches("_updateWithDepsButton"))
        {
            return DescribeUpdateWithDependencies(modItemType, modItem);
        }

        if (Matches("_updateButton"))
        {
            return "Restart required to reinstall";
        }

        if (Matches("tMLUpdateRequired"))
        {
            return "Requires tModLoader update";
        }

        string? typeName = element.GetType().FullName;
        if (typeName is not null && typeName.Contains("UIHoverImage", StringComparison.OrdinalIgnoreCase))
        {
            return "View dependencies";
        }

        return string.Empty;
    }

    private static string DescribeModInfo(Type modItemType, UIElement modItem)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            PropertyInfo? property = modItemType.GetProperty("ViewModInfoText", flags);
            if (property?.GetValue(modItem) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore
        }

        return "More info";
    }

    private static string DescribeUpdateWithDependencies(Type modItemType, UIElement modItem)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            PropertyInfo? property = modItemType.GetProperty("UpdateWithDepsText", flags);
            if (property?.GetValue(modItem) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore
        }

        return "Download with dependencies";
    }

    private static string DescribeSearchFilter(SearchFilter state)
    {
        return state switch
        {
            SearchFilter.Name => "Search by name",
            SearchFilter.Author => "Search by author",
            _ => $"Search filter {state}",
        };
    }

    private static string DescribeSortMode(ModBrowserSortMode state)
    {
        return state switch
        {
            ModBrowserSortMode.DownloadsDescending => "Sort by downloads",
            ModBrowserSortMode.RecentlyPublished => "Sort by recently published",
            ModBrowserSortMode.RecentlyUpdated => "Sort by recently updated",
            ModBrowserSortMode.Hot => "Sort by hot mods",
            _ => $"Sort mode {state}",
        };
    }

    private static string DescribeTimePeriod(ModBrowserTimePeriod state)
    {
        return state switch
        {
            ModBrowserTimePeriod.Today => "Time period: today",
            ModBrowserTimePeriod.OneWeek => "Time period: past week",
            ModBrowserTimePeriod.ThreeMonths => "Time period: past three months",
            ModBrowserTimePeriod.SixMonths => "Time period: past six months",
            ModBrowserTimePeriod.OneYear => "Time period: past year",
            ModBrowserTimePeriod.AllTime => "Time period: all time",
            _ => $"Time period {state}",
        };
    }

    private static string DescribeUpdateFilter(UpdateFilter state)
    {
        return state switch
        {
            UpdateFilter.All => "Updates: all mods",
            UpdateFilter.Available => "Updates: available",
            UpdateFilter.UpdateOnly => "Updates: update only",
            UpdateFilter.InstalledOnly => "Updates: installed only",
            _ => $"Updates filter {state}",
        };
    }

    private static string DescribeModSideFilter(ModSideFilter state)
    {
        return state switch
        {
            ModSideFilter.All => "Side filter: all",
            ModSideFilter.Both => "Side filter: client and server",
            ModSideFilter.Client => "Side filter: client only",
            ModSideFilter.Server => "Side filter: server only",
            ModSideFilter.NoSync => "Side filter: no sync",
            _ => $"Side filter {state}",
        };
    }

    private enum CharacterCreationCategoryId
    {
        CharInfo = 0,
        Clothing = 1,
        HairStyle = 2,
        HairColor = 3,
        Eye = 4,
        Skin = 5,
        Shirt = 6,
        Undershirt = 7,
        Pants = 8,
        Shoes = 9,
    }

    private static string DescribeCharacterCreationElement(UIElement element)
    {
        if (UiCharacterCreationType is null)
        {
            return string.Empty;
        }

        UIElement? root = FindAncestor(element, static type => UiCharacterCreationType.IsAssignableFrom(type));
        if (root is null)
        {
            return string.Empty;
        }

        try
        {
            if (UiCharacterNameButtonType?.IsInstanceOfType(element) == true)
            {
                return DescribeCharacterName(element);
            }

            if (UiHairStyleButtonType?.IsInstanceOfType(element) == true)
            {
                return DescribeHairStyleOption(root, element);
            }

            if (UiClothStyleButtonType?.IsInstanceOfType(element) == true)
            {
                return DescribeClothingStyleOption(root, element);
            }

            if (UiDifficultyButtonType?.IsInstanceOfType(element) == true)
            {
                return DescribeDifficultyButton(root, element);
            }

            UIElement? slider = UiColoredSliderType?.IsInstanceOfType(element) == true
                ? element
                : FindAncestor(element, type => UiColoredSliderType?.IsAssignableFrom(type) == true);

            if (slider is not null)
            {
                return DescribeHslSlider(root, slider);
            }

            if (UiColoredImageButtonType?.IsInstanceOfType(element) == true)
            {
                string label = DescribeCharacterCreationImageButton(root, element);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Character creation label lookup failed: {ex.Message}");
        }

        return string.Empty;
    }

    private static string DescribeCharacterCreationImageButton(UIElement root, UIElement element)
    {
        if (CharacterCreationColorPickersField?.GetValue(root) is Array pickerArray)
        {
            for (int i = 0; i < pickerArray.Length; i++)
            {
                if (ReferenceEquals(pickerArray.GetValue(i), element))
                {
                    return DescribeCharacterCreationCategory((CharacterCreationCategoryId)i, IsImageButtonSelected(element));
                }
            }
        }

        if (ReferenceEquals(CharacterCreationClothingButtonField?.GetValue(root), element))
        {
            return DescribeCharacterCreationCategory(CharacterCreationCategoryId.Clothing, IsImageButtonSelected(element));
        }

        if (ReferenceEquals(CharacterCreationHairStylesButtonField?.GetValue(root), element))
        {
            return DescribeCharacterCreationCategory(CharacterCreationCategoryId.HairStyle, IsImageButtonSelected(element));
        }

        if (ReferenceEquals(CharacterCreationCharInfoButtonField?.GetValue(root), element))
        {
            return DescribeCharacterCreationCategory(CharacterCreationCategoryId.CharInfo, IsImageButtonSelected(element));
        }

        if (ReferenceEquals(CharacterCreationGenderMaleField?.GetValue(root), element))
        {
            Player? player = TryGetCharacterCreationPlayer(root);
            bool selected = IsImageButtonSelected(element) || (player?.Male ?? false);
            string label = LocalizationHelper.GetTextOrFallback("UI.Male", "Male");
            return selected ? TextSanitizer.JoinWithComma(label, "Selected") : label;
        }

        if (ReferenceEquals(CharacterCreationGenderFemaleField?.GetValue(root), element))
        {
            Player? player = TryGetCharacterCreationPlayer(root);
            bool selected = IsImageButtonSelected(element) || !(player?.Male ?? true);
            string label = LocalizationHelper.GetTextOrFallback("UI.Female", "Female");
            return selected ? TextSanitizer.JoinWithComma(label, "Selected") : label;
        }

        if (ReferenceEquals(CharacterCreationCopyHexButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.CopyColorToClipboard", "Copy color to clipboard");
        }

        if (ReferenceEquals(CharacterCreationPasteHexButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.PasteColorFromClipboard", "Paste color from clipboard");
        }

        if (ReferenceEquals(CharacterCreationRandomColorButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.RandomizeColor", "Randomize color");
        }

        if (ReferenceEquals(CharacterCreationCopyTemplateButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.CopyPlayerToClipboard", "Copy player to clipboard");
        }

        if (ReferenceEquals(CharacterCreationPasteTemplateButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.PastePlayerFromClipboard", "Paste player from clipboard");
        }

        if (ReferenceEquals(CharacterCreationRandomizePlayerButtonField?.GetValue(root), element))
        {
            return LocalizationHelper.GetTextOrFallback("UI.RandomizePlayer", "Randomize player");
        }

        return string.Empty;
    }

    private static string DescribeCharacterCreationCategory(CharacterCreationCategoryId category, bool isSelected)
    {
        string label = category switch
        {
            CharacterCreationCategoryId.CharInfo => "Tab 1: Character info",
            CharacterCreationCategoryId.Clothing => "Tab 2: Clothing styles",
            CharacterCreationCategoryId.HairStyle => "Tab 3: Hair styles",
            CharacterCreationCategoryId.HairColor => "Tab 4: Hair color",
            CharacterCreationCategoryId.Eye => "Tab 5: Eye color",
            CharacterCreationCategoryId.Skin => "Tab 6: Skin color",
            CharacterCreationCategoryId.Shirt => "Tab 7: Shirt color",
            CharacterCreationCategoryId.Undershirt => "Tab 8: Undershirt color",
            CharacterCreationCategoryId.Pants => "Tab 9: Pants color",
            CharacterCreationCategoryId.Shoes => "Tab 10: Shoes color",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return isSelected ? TextSanitizer.JoinWithComma(label, "Selected") : label;
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

        return string.Empty;
    }

    private static bool TryDescribeWorldCreationButton(UIElement root, UIElement element, FieldInfo? buttonArrayField, string groupLabel, out string label)
    {
        if (buttonArrayField?.GetValue(root) is not Array buttons || buttons.Length == 0)
        {
            label = string.Empty;
            return false;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons.GetValue(i) is not UIElement button)
            {
                continue;
            }

            if (!ReferenceEquals(button, element) && !IsAncestor(button, element))
            {
                continue;
            }

            label = DescribeWorldGroupOption(button, groupLabel, i, buttons.Length);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static string DescribeWorldGroupOption(UIElement element, string groupLabel, int index, int total)
    {
        string optionLabel = TryGetGroupOptionTitle(element);
        if (string.IsNullOrWhiteSpace(optionLabel))
        {
            optionLabel = groupLabel;
        }

        string label = TextSanitizer.JoinWithComma(groupLabel, optionLabel);
        if (total > 0)
        {
            label = TextSanitizer.JoinWithComma(label, $"{index + 1} of {total}");
        }

        if (IsGroupOptionSelected(element))
        {
            label = TextSanitizer.JoinWithComma(label, "Selected");
        }

        return TextSanitizer.Clean(label);
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

    private static string DescribeCharacterName(UIElement element)
    {
        string? value = NameButtonContentsField?.GetValue(element) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (NameButtonEmptyTextField?.GetValue(element) is LocalizedText empty && !string.IsNullOrWhiteSpace(empty.Value))
            {
                value = empty.Value;
            }
            else
            {
                value = LocalizationHelper.GetTextOrFallback("UI.PlayerEmptyName", "Enter name");
            }
        }

        string label = LocalizationHelper.GetTextOrFallback("UI.WorldCreationName", "Name");
        return TextSanitizer.JoinWithComma(label, value);
    }

    private static string DescribeHairStyleOption(UIElement root, UIElement element)
    {
        int? styleId = HairStyleIdField?.GetValue(element) is int value ? value : null;
        string label = styleId.HasValue ? $"Hair style {styleId.Value + 1}" : "Hair style";

        Player? player = TryGetCharacterCreationPlayer(root);
        if (player is not null && styleId.HasValue && player.hair == styleId.Value)
        {
            label = TextSanitizer.JoinWithComma(label, "Selected");
        }

        if (styleId.HasValue && TryGetHairStyleDescription(styleId.Value, out string? description))
        {
            label = TextSanitizer.JoinWithComma(label, description);
        }

        return TextSanitizer.Clean(label);
    }

    private static string DescribeClothingStyleOption(UIElement root, UIElement element)
    {
        int? styleId = ClothStyleIdField?.GetValue(element) is int value ? value : null;
        string label = styleId.HasValue ? $"Clothing style {styleId.Value + 1}" : "Clothing style";

        Player? player = TryGetCharacterCreationPlayer(root);
        if (player is not null && styleId.HasValue && player.skinVariant == styleId.Value)
        {
            label = TextSanitizer.JoinWithComma(label, "Selected");
        }

        return TextSanitizer.Clean(label);
    }

    private static bool TryGetHairStyleDescription(int styleId, out string? description)
    {
        if ((uint)styleId < (uint)HairStyleDescriptions.Length)
        {
            description = HairStyleDescriptions[styleId];
            return !string.IsNullOrWhiteSpace(description);
        }

        description = null;
        return false;
    }

    private static string DescribeHslSlider(UIElement root, UIElement element)
    {
        string sliderName = TryGetSliderLabel(element);
        if (string.IsNullOrWhiteSpace(sliderName))
        {
            sliderName = TryGetSliderLabelByOrder(element);
        }

        float? value = TryGetSliderValue(element);

        string label = string.IsNullOrWhiteSpace(sliderName) ? "Color slider" : $"{sliderName} slider";
        if (value is float v)
        {
            float clamped = Math.Clamp(v, 0f, 1f);
            label = TextSanitizer.JoinWithComma(label, $"{Math.Round(clamped * 100f)} percent");
        }

        return TextSanitizer.Clean(label);
    }

    private static string TryGetSliderLabel(UIElement element)
    {
        if (UiColoredSliderGamepadActionField?.GetValue(element) is Delegate action && action.Method is not null)
        {
            return action.Method.Name switch
            {
                "UpdateHSL_H" => "Hue",
                "UpdateHSL_S" => "Saturation",
                "UpdateHSL_L" => "Luminance",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    private static float? TryGetSliderValue(UIElement element)
    {
        if (UiColoredSliderValueFuncField?.GetValue(element) is Delegate getter)
        {
            try
            {
                object? result = getter.DynamicInvoke();
                if (result is float value)
                {
                    return value;
                }
            }
            catch
            {
                // ignore slider lookup failures
            }
        }

        return null;
    }

    private static string TryGetSliderLabelByOrder(UIElement element)
    {
        if (element.Parent is null)
        {
            return string.Empty;
        }

        var siblings = element.Parent.Children?.Where(child => UiColoredSliderType?.IsInstanceOfType(child) == true).ToList();
        if (siblings is null || siblings.Count == 0)
        {
            return string.Empty;
        }

        int index = siblings.IndexOf(element);
        if (index < 0)
        {
            return string.Empty;
        }

        return index switch
        {
            0 => "Hue",
            1 => "Saturation",
            2 => "Luminance",
            _ => string.Empty,
        };
    }

    private static bool IsImageButtonSelected(UIElement element)
    {
        if (UiColoredImageButtonSelectedField?.GetValue(element) is bool selected)
        {
            return selected;
        }

        return false;
    }

    private static Player? TryGetCharacterCreationPlayer(UIElement root)
    {
        return CharacterCreationPlayerField?.GetValue(root) as Player;
    }

    private static string DescribeDifficultyButton(UIElement root, UIElement element)
    {
        byte? difficulty = DifficultyButtonValueField?.GetValue(element) is byte value ? value : null;
        string label = difficulty switch
        {
            0 => LocalizationHelper.GetTextOrFallback("UI.Classic", "Classic"),
            1 => LocalizationHelper.GetTextOrFallback("UI.Mediumcore", "Mediumcore"),
            2 => LocalizationHelper.GetTextOrFallback("UI.Hardcore", "Hardcore"),
            3 => LocalizationHelper.GetTextOrFallback("UI.Journey", "Journey"),
            _ => LocalizationHelper.GetTextOrFallback("UI.Difficulty", "Difficulty"),
        };

        Player? player = TryGetCharacterCreationPlayer(root);
        if (IsDifficultySelected(element, difficulty, player))
        {
            label = TextSanitizer.JoinWithComma(label, LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "Selected"));
        }

        return TextSanitizer.Clean(label);
    }

    private static bool IsDifficultySelected(UIElement element, byte? difficulty, Player? player)
    {
        if (player is not null && difficulty.HasValue && player.difficulty == difficulty.Value)
        {
            return true;
        }

        if (DifficultyButtonSelectedProperty?.GetValue(element) is bool selected && selected)
        {
            return true;
        }

        FieldInfo? selectedField = element.GetType().GetField("_selected", CharacterBindingFlags);
        if (selectedField?.GetValue(element) is bool selectedFlag && selectedFlag)
        {
            return true;
        }

        return false;
    }

    private static string DescribeTagFilterToggle(UIElement element)
    {
        UIElement? browser = FindAncestor(element, static type => type.FullName == "Terraria.ModLoader.UI.ModBrowser.UIModBrowser");
        if (browser is null)
        {
            return string.Empty;
        }

        HashSet<int>? categories = null;
        int languageTag = -1;

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? categoryField = browser.GetType().GetField("CategoryTagsFilter", flags);
            if (categoryField?.GetValue(browser) is HashSet<int> set)
            {
                categories = set;
            }

            FieldInfo? languageField = browser.GetType().GetField("LanguageTagFilter", flags);
            if (languageField?.GetValue(browser) is int lang)
            {
                languageTag = lang;
            }
        }
        catch
        {
            // ignore lookup issues
        }

        string selection = DescribeTagSelection(categories, languageTag);
        if (string.IsNullOrWhiteSpace(selection))
        {
            selection = "none selected";
        }

        return TextSanitizer.Clean($"Tag filters: {selection}");
    }

    private static string DescribeTagSelection(HashSet<int>? categories, int languageTag)
    {
        var parts = new List<string>(2);
        if (categories is not null && categories.Count > 0)
        {
            string categoryNames = ResolveTagNames(categories);
            parts.Add(string.IsNullOrWhiteSpace(categoryNames) ? $"{categories.Count} categories" : $"Categories {categoryNames}");
        }

        if (languageTag >= 0)
        {
            string languageName = ResolveTagName(languageTag);
            parts.Add(string.IsNullOrWhiteSpace(languageName) ? "Language selected" : $"Language {languageName}");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static string ResolveTagNames(IEnumerable<int> indices)
    {
        var names = new List<string>();
        int count = 0;
        foreach (int index in indices)
        {
            count++;
            string name = ResolveTagName(index);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }

            if (names.Count >= 3)
            {
                break;
            }
        }

        if (names.Count == 0)
        {
            return string.Empty;
        }

        int remaining = Math.Max(0, count - names.Count);
        string label = string.Join(", ", names);
        if (remaining > 0)
        {
            label += $" (+{remaining} more)";
        }

        return label;
    }

    private static string ResolveTagName(int index)
    {
        try
        {
            if (index >= 0 && index < SteamedWraps.ModTags.Count)
            {
                string name = Language.GetTextValue(SteamedWraps.ModTags[index].NameKey);
                return TextSanitizer.Clean(name);
            }
        }
        catch
        {
            // ignore lookup failures
        }

        return string.Empty;
    }
}

internal sealed record LabelAccessors
{
    public PropertyInfo? TextProperty { get; init; }
    public FieldInfo? TextField { get; init; }
    public FieldInfo? ValueField { get; init; }
}

internal readonly record struct MenuUiLabel(UIElement Element, string Text, bool IsNew);

internal static class LabelAccessorFactory
{
    private const BindingFlags LabelFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static LabelAccessors Create(Type type)
    {
        return new LabelAccessors
        {
            TextProperty = type.GetProperty("Text", LabelFlags),
            TextField = type.GetField("_text", LabelFlags),
            ValueField = type.GetField("_value", LabelFlags),
        };
    }
}
