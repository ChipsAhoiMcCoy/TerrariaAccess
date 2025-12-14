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

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly FieldInfo? LastHoverField = typeof(UserInterface).GetField("_lastElementHover", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Dictionary<Type, LabelAccessors> LabelAccessorCache = new();
    private const BindingFlags CharacterBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<LabelResolver> LabelResolvers = new()
    {
        new(
            static element => IsCharacterCreationElement(element),
            static element => DescribeCharacterCreationElement(element)),
        new(
            static element => IsWorldCreationElement(element),
            static element => DescribeWorldCreationElement(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UICharacterListItem"),
            static element => DescribePlayerListItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIWorldListItem"),
            static element => DescribeWorldListItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIKeybindingSimpleListItem"),
            static element => DescribeKeybindingSimpleItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIKeybindingListItem"),
            static element => DescribeKeybindingListItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIKeybindingSliderItem"),
            static element => DescribeKeybindingSliderItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIKeybindingToggleListItem"),
            static element => DescribeKeybindingToggleItem(element)),
        new(
            static element => HasFullName(element, "Terraria.GameContent.UI.Elements.UIImageButton"),
            static element => DescribeImageButton(element)),
        new(
            static element => HasNamespacePrefix(element, "Terraria.ModLoader.UI.ModBrowser.UIBrowserFilterToggle"),
            static element => DescribeBrowserFilterToggle(element)),
        new(
            static element => NameContains(element, "UIHoverImage", StringComparison.OrdinalIgnoreCase),
            static element => DescribeModBrowserButton(element)),
        new(
            static element => HasFullName(element, "Terraria.ModLoader.UI.UICycleImage"),
            static element => DescribeTagFilterToggle(element)),
        new(
            static element => IsConfigElement(element),
            static element => DescribeConfigElement(element)),
    };

    private UIElement? _lastElement;
    private string? _lastLabel;
    private readonly Stack<UIElement> _traversalStack = new();

    public void Reset()
    {
        _lastElement = null;
        _lastLabel = null;
        _lastWorldCreationRoot = null;
        _lastWorldCreationGroup = null;
        _lastWorldCreationElement = null;
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

    private static bool HasFullName(UIElement element, string fullName)
    {
        return string.Equals(element.GetType().FullName, fullName, StringComparison.Ordinal);
    }

    private static bool HasNamespacePrefix(UIElement element, string prefix)
    {
        string? name = element.GetType().FullName;
        return name is not null && name.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool NameContains(UIElement element, string substring, StringComparison comparison)
    {
        string? name = element.GetType().FullName;
        return name?.IndexOf(substring, comparison) >= 0;
    }

    private static string DescribePlayerListItem(UIElement element)
    {
        DumpElementMetadata(element.GetType(), element);
        if (TryGetData(element, out PlayerFileData? playerData) && playerData is not null)
        {
            return DescribePlayer(playerData);
        }

        return string.Empty;
    }

    private static string DescribeWorldListItem(UIElement element)
    {
        DumpElementMetadata(element.GetType(), element);
        if (TryGetData(element, out WorldFileData? worldData) && worldData is not null)
        {
            return DescribeWorld(worldData);
        }

        return string.Empty;
    }

    private static string ExtractSpecializedLabel(Type type, UIElement element)
    {
        ResetWorldCreationContextIfNeeded(element);

        foreach (LabelResolver resolver in LabelResolvers)
        {
            if (!resolver.Matches(element))
            {
                continue;
            }

            string resolved = resolver.Format(element);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        string? fullName = type.FullName;
        if (HasNamespacePrefix(element, "Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem"))
        {
            return DescribeModDownloadItem(element);
        }

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

    #region Config Element Support

    private static readonly Type? ConfigElementType = Type.GetType("Terraria.ModLoader.Config.UI.ConfigElement, tModLoader");

    private static bool IsConfigElement(UIElement element)
    {
        if (element is null)
        {
            return false;
        }

        Type type = element.GetType();

        // Check if it's a ConfigElement or derived type
        if (ConfigElementType is not null && ConfigElementType.IsAssignableFrom(type))
        {
            return true;
        }

        // Check by type name patterns
        string? typeName = type.FullName;
        if (typeName is null)
        {
            return false;
        }

        return typeName.Contains("ConfigElement", StringComparison.Ordinal) ||
               typeName.Contains("BooleanElement", StringComparison.Ordinal) ||
               typeName.Contains("IntInputElement", StringComparison.Ordinal) ||
               typeName.Contains("FloatElement", StringComparison.Ordinal) ||
               typeName.Contains("StringInputElement", StringComparison.Ordinal) ||
               typeName.Contains("EnumElement", StringComparison.Ordinal) ||
               typeName.Contains("ColorElement", StringComparison.Ordinal) ||
               typeName.Contains("ItemDefinitionElement", StringComparison.Ordinal) ||
               typeName.Contains("HeaderElement", StringComparison.Ordinal);
    }

    private static string DescribeConfigElement(UIElement element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = element.GetType();

        // Try to get Label property
        string label = TryGetConfigLabel(element, type, flags);

        // Try to get Value
        string value = TryGetConfigValue(element, type, flags);

        // Build description
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(label))
        {
            parts.Add(TextSanitizer.Clean(label));
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(TextSanitizer.Clean(value));
        }

        if (parts.Count == 0)
        {
            // Fallback to type name
            return type.Name;
        }

        return string.Join(": ", parts);
    }

    private static string TryGetConfigLabel(UIElement element, Type type, BindingFlags flags)
    {
        // Try common label properties/fields
        string[] memberNames = { "Label", "DisplayName", "Name", "_label", "_text" };

        foreach (string name in memberNames)
        {
            PropertyInfo? prop = type.GetProperty(name, flags);
            if (prop?.GetValue(element) is object propValue)
            {
                string text = ConvertConfigValueToText(propValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            FieldInfo? field = type.GetField(name, flags);
            if (field?.GetValue(element) is object fieldValue)
            {
                string text = ConvertConfigValueToText(fieldValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        // Try TextDisplayFunction delegate
        FieldInfo? funcField = type.GetField("_TextDisplayFunction", flags) ?? type.GetField("TextDisplayFunction", flags);
        if (funcField?.GetValue(element) is Delegate textFunc)
        {
            try
            {
                object? result = textFunc.DynamicInvoke();
                string text = ConvertConfigValueToText(result);
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

        return string.Empty;
    }

    private static string TryGetConfigValue(UIElement element, Type type, BindingFlags flags)
    {
        // Try common value properties/fields
        string[] memberNames = { "Value", "CurrentValue", "_value" };

        foreach (string name in memberNames)
        {
            PropertyInfo? prop = type.GetProperty(name, flags);
            if (prop?.GetValue(element) is object propValue)
            {
                return FormatConfigValueForAnnouncement(propValue);
            }

            FieldInfo? field = type.GetField(name, flags);
            if (field?.GetValue(element) is object fieldValue)
            {
                return FormatConfigValueForAnnouncement(fieldValue);
            }
        }

        // Try GetValue method
        MethodInfo? getValueMethod = type.GetMethod("GetValue", flags, null, Type.EmptyTypes, null);
        if (getValueMethod is not null)
        {
            try
            {
                object? result = getValueMethod.Invoke(element, Array.Empty<object>());
                return FormatConfigValueForAnnouncement(result);
            }
            catch
            {
                // Ignore method invocation failures
            }
        }

        return string.Empty;
    }

    private static string ConvertConfigValueToText(object? value)
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

    private static string FormatConfigValueForAnnouncement(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            bool b => b ? "On" : "Off",
            float f => f.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
            double d => d.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
            Enum e => e.ToString(),
            _ => ConvertConfigValueToText(value),
        };
    }

    #endregion

    }

internal sealed record LabelAccessors
{
    public PropertyInfo? TextProperty { get; init; }
    public FieldInfo? TextField { get; init; }
    public FieldInfo? ValueField { get; init; }
}

internal readonly record struct MenuUiLabel(UIElement Element, string Text, bool IsNew);

internal readonly record struct LabelResolver(
    Func<UIElement, bool> Matches,
    Func<UIElement, string> Format);

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
