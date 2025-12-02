#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly Type? UiCharacterCreationType = Type.GetType("Terraria.GameContent.UI.States.UICharacterCreation, tModLoader");
    private static readonly Type? UiColoredImageButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIColoredImageButton, tModLoader");
    private static readonly Type? UiHairStyleButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIHairStyleButton, tModLoader");
    private static readonly Type? UiClothStyleButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIClothStyleButton, tModLoader");
    private static readonly Type? UiColoredSliderType = Type.GetType("Terraria.GameContent.UI.Elements.UIColoredSlider, tModLoader");
    private static readonly Type? UiCharacterNameButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UICharacterNameButton, tModLoader");
    private static readonly Type? UiDifficultyButtonType = Type.GetType("Terraria.GameContent.UI.Elements.UIDifficultyButton, tModLoader");

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

        if (UiCharacterNameButtonType?.IsInstanceOfType(element) == true)
        {
            return DescribeCharacterName(element);
        }

        if (UiDifficultyButtonType?.IsInstanceOfType(element) == true)
        {
            return DescribeDifficultyButton(root, element);
        }

        UIElement? difficultyButton = UiDifficultyButtonType is null ? null : FindAncestor(element, static type => UiDifficultyButtonType.IsAssignableFrom(type));
        if (difficultyButton is not null)
        {
            return DescribeDifficultyButton(root, difficultyButton);
        }

        if (UiColoredSliderType?.IsInstanceOfType(element) == true || FindAncestor(element, static type => UiColoredSliderType?.IsAssignableFrom(type) == true) is not null)
        {
            return DescribeHslSlider(root, element);
        }

        if (UiHairStyleButtonType?.IsInstanceOfType(element) == true)
        {
            return DescribeHairStyleOption(root, element);
        }

        if (UiClothStyleButtonType?.IsInstanceOfType(element) == true)
        {
            return DescribeClothingStyleOption(root, element);
        }

        if (UiColoredImageButtonType?.IsInstanceOfType(element) == true)
        {
            string label = DescribeCharacterCreationImageButton(root, element);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
        }

        // Some of the clothing/hair option panels do not subclass the expected base buttons. Handle these by reflection.
        try
        {
            FieldInfo[] fields = root.GetType().GetFields(CharacterBindingFlags);
            foreach (FieldInfo field in fields)
            {
                object? value = field.GetValue(root);
                if (value is not Array array)
                {
                    continue;
                }

                foreach (object? item in array)
                {
                    if (item is not UIElement target)
                    {
                        continue;
                    }

                    if (ReferenceEquals(target, element))
                    {
                        string name = field.Name.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
                        if (name.Contains("Hair", StringComparison.OrdinalIgnoreCase))
                        {
                            return DescribeHairStyleOption(root, element);
                        }

                        if (name.Contains("Cloth", StringComparison.OrdinalIgnoreCase))
                        {
                            return DescribeClothingStyleOption(root, element);
                        }
                    }
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
                if (pickerArray.GetValue(i) is UIElement picker && ReferenceEquals(picker, element))
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
        (int tabIndex, string title) = category switch
        {
            CharacterCreationCategoryId.CharInfo => (1, "Character info"),
            CharacterCreationCategoryId.Clothing => (2, "Clothing styles"),
            CharacterCreationCategoryId.HairStyle => (3, "Hair styles"),
            CharacterCreationCategoryId.HairColor => (4, "Hair color"),
            CharacterCreationCategoryId.Eye => (5, "Eye color"),
            CharacterCreationCategoryId.Skin => (6, "Skin color"),
            CharacterCreationCategoryId.Shirt => (7, "Shirt color"),
            CharacterCreationCategoryId.Undershirt => (8, "Undershirt color"),
            CharacterCreationCategoryId.Pants => (9, "Pants color"),
            CharacterCreationCategoryId.Shoes => (10, "Shoes color"),
            _ => (0, string.Empty),
        };

        if (tabIndex <= 0 || string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        if (isSelected)
        {
            return TextSanitizer.JoinWithComma("Selected", title, $"{tabIndex} of {CharacterCreationTabCount}");
        }

        return TextSanitizer.JoinWithComma(title, $"{tabIndex} of {CharacterCreationTabCount}");
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
        int totalStyles = ClothingStyleDescriptions.Length;
        string? ordinal = null;
        if (styleId.HasValue)
        {
            ordinal = TryGetClothingStylePosition(styleId.Value, out int position)
                ? $"{position} of {totalStyles}"
                : $"{styleId.Value + 1} of {totalStyles}";
        }

        var parts = new List<string>(4);

        Player? player = TryGetCharacterCreationPlayer(root);
        if (player is not null && styleId.HasValue && player.skinVariant == styleId.Value)
        {
            parts.Add("Selected");
        }

        if (styleId.HasValue && TryGetClothingStyleDescription(styleId.Value, out string? description))
        {
            parts.Add(description);
        }

        if (!string.IsNullOrWhiteSpace(ordinal))
        {
            parts.Add(ordinal);
        }
        else
        {
            parts.Insert(0, "Clothing style");
        }

        return TextSanitizer.Clean(TextSanitizer.JoinWithComma(parts.ToArray()));
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
        byte? difficulty = TryGetDifficultyValue(element);
        (string difficultyLabel, string? difficultyDescription) = DescribeDifficultyValue(difficulty);

        Player? player = TryGetCharacterCreationPlayer(root);
        bool isSelected = IsDifficultySelected(element, difficulty, player);

        var parts = new List<string>(3);
        if (isSelected)
        {
            parts.Add(LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "Selected"));
        }

        parts.Add(difficultyLabel);

        if (!string.IsNullOrWhiteSpace(difficultyDescription))
        {
            parts.Add(difficultyDescription);
        }

        return TextSanitizer.Clean(TextSanitizer.JoinWithComma(parts.ToArray()));
    }

    private static (string Label, string? Description) DescribeDifficultyValue(byte? difficulty)
    {
        return difficulty switch
        {
            0 => (LocalizationHelper.GetTextOrFallback("UI.Classic", "Classic"), GetMenuTextOrFallback(31, "Classic characters drop money on death.")),
            1 => (LocalizationHelper.GetTextOrFallback("UI.Mediumcore", "Mediumcore"), GetMenuTextOrFallback(30, "Mediumcore characters drop items on death.")),
            2 => (LocalizationHelper.GetTextOrFallback("UI.Hardcore", "Hardcore"), GetMenuTextOrFallback(29, "Hardcore characters die for good.")),
            3 => (LocalizationHelper.GetTextOrFallback("UI.Journey", "Journey"), LocalizationHelper.GetTextOrFallback("CreativeDescriptionPlayer", "Journey characters start with extra equipment. Can only be played on Journey worlds.")),
            _ => (LocalizationHelper.GetTextOrFallback("UI.Difficulty", "Difficulty"), null),
        };
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

        if (IsGroupOptionSelected(element))
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

    private static byte? TryGetDifficultyValue(UIElement element)
    {
        try
        {
            object? value = DifficultyButtonValueField?.GetValue(element);
            if (value is null)
            {
                PropertyInfo? optionValue = element.GetType().GetProperty("OptionValue", CharacterBindingFlags);
                value = optionValue?.GetValue(element);
            }

            return value switch
            {
                byte b => b,
                sbyte sb => (byte)sb,
                int i => unchecked((byte)i),
                uint ui => unchecked((byte)ui),
                Enum e => Convert.ToByte(e),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetMenuTextOrFallback(int index, string fallback)
    {
        try
        {
            LocalizedText[] menu = Lang.menu;
            if ((uint)index < (uint)menu.Length && !string.IsNullOrWhiteSpace(menu[index].Value))
            {
                return menu[index].Value;
            }
        }
        catch
        {
            // ignore menu lookup failures and fall back to hard-coded text
        }

        return fallback;
    }
}
