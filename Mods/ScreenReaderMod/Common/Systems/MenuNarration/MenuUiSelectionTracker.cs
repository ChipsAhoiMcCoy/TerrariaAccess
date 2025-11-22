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
