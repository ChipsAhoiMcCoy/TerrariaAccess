#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.IO;
using Terraria.Localization;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuUiSelectionTracker
{
    private static readonly FieldInfo? LastHoverField = typeof(UserInterface).GetField("_lastElementHover", BindingFlags.NonPublic | BindingFlags.Instance);

    private UIElement? _lastElement;
    private string? _lastLabel;

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

        string text = ExtractLabel(hovered);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool isNew = !ReferenceEquals(hovered, _lastElement) || !string.Equals(text, _lastLabel, StringComparison.OrdinalIgnoreCase);
        _lastElement = hovered;
        _lastLabel = text;
        label = new MenuUiLabel(text, isNew);
        return true;
    }

    private static readonly Dictionary<Type, bool> LoggedMissingLabels = new();

    private static string ExtractLabel(UIElement element)
    {
        string label = ExtractDirectLabel(element);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        foreach (UIElement child in element.Children ?? Enumerable.Empty<UIElement>())
        {
            label = ExtractLabel(child);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
        }

        UIElement? current = element.Parent;
        while (current is not null)
        {
            label = ExtractDirectLabel(current);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static string ExtractDirectLabel(UIElement element)
    {
        Type type = element.GetType();

        string custom = ExtractSpecializedLabel(type, element);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        PropertyInfo? textProperty = type.GetProperty("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (textProperty is not null)
        {
            object? value = textProperty.GetValue(element);
            if (value is string text)
            {
                return text.Trim();
            }

            if (value is not null)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        FieldInfo? textField = type.GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance);
        if (textField is not null)
        {
            object? value = textField.GetValue(element);
            if (value is string text)
            {
                return text.Trim();
            }
        }

        FieldInfo? valueField = type.GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance);
        if (valueField is not null)
        {
            object? value = valueField.GetValue(element);
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
            case "Terraria.GameContent.UI.Elements.UIImageButton":
                return DescribeImageButton(element);
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
            1 => LocalizeOr("UI.Mediumcore", "Mediumcore"),
            2 => LocalizeOr("UI.Hardcore", "Hardcore"),
            3 => LocalizeOr("UI.Journey", "Journey"),
            _ => LocalizeOr("UI.Classic", "Classic"),
        };

        string favorite = data.IsFavorite ? LocalizeOr("UI.FavoriteTooltip", "Favorite") : string.Empty;

        return JoinParts(name, difficulty, favorite);
    }

    private static string DescribeWorld(WorldFileData data)
    {
        string name = DescribeWorldName(data);

        string size = DescribeWorldSize(data.WorldSizeX);

        string mode = data.GameMode switch
        {
            1 => LocalizeOr("UI.Expert", "Expert"),
            2 => LocalizeOr("UI.Master", "Master"),
            3 => LocalizeOr("UI.Journey", "Journey"),
            _ => LocalizeOr("UI.Classic", "Classic"),
        };

        string favorite = data.IsFavorite ? LocalizeOr("UI.FavoriteTooltip", "Favorite") : string.Empty;

        return JoinParts(name, mode, size, favorite);
    }

    private static string DescribePlayerName(PlayerFileData? data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Name))
        {
            return LocalizeOr("UI.PlayerNameDefault", "Player");
        }

        return Sanitize(data.Name);
    }

    private static string DescribeWorldName(WorldFileData? data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Name))
        {
            return LocalizeOr("UI.WorldNameDefault", "World");
        }

        return Sanitize(data.Name);
    }

    private static string DescribeWorldSize(int worldWidth)
    {
        if (worldWidth <= 4400)
        {
            return Sanitize(Lang.menu[92].Value);
        }

        if (worldWidth <= 7000)
        {
            return Sanitize(Lang.menu[93].Value);
        }

        return Sanitize(Lang.menu[94].Value);
    }

    private static string JoinParts(params string[] parts)
    {
        return string.Join(", ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string LocalizeOr(string key, string fallback)
    {
        string value = Language.GetTextValue(key);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return fallback;
        }

        return Sanitize(value);
    }

    private static string Sanitize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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
            0 => JoinParts(LocalizeOr("UI.Play", "Play"), playerName),
            1 => JoinParts(
                LocalizeOr("UI.Favorite", "Favorite"),
                (data?.IsFavorite ?? false) ? LocalizeOr("UI.FavoriteOn", "On") : LocalizeOr("UI.FavoriteOff", "Off"),
                playerName),
            2 => JoinParts(DescribeCloudToggle(data?.IsCloudSave ?? false), playerName),
            3 => JoinParts(LocalizeOr("UI.Rename", "Rename"), playerName),
            4 => JoinParts(LocalizeOr("UI.Delete", "Delete"), playerName),
            _ => JoinParts(LocalizeOr("UI.Button", $"Button {index + 1}"), playerName),
        };
    }

    private static string DescribeWorldButtonByIndex(int index, WorldFileData? data)
    {
        string worldName = DescribeWorldName(data);
        return index switch
        {
            0 => JoinParts(LocalizeOr("UI.Play", "Play"), worldName),
            1 => JoinParts(
                LocalizeOr("UI.Favorite", "Favorite"),
                (data?.IsFavorite ?? false) ? LocalizeOr("UI.FavoriteOn", "On") : LocalizeOr("UI.FavoriteOff", "Off"),
                worldName),
            2 => JoinParts(DescribeCloudToggle(data?.IsCloudSave ?? false), worldName),
            3 => JoinParts(DescribeWorldSeed(data), worldName),
            4 => JoinParts(LocalizeOr("UI.Rename", "Rename"), worldName),
            5 => JoinParts(LocalizeOr("UI.Delete", "Delete"), worldName),
            _ => JoinParts(LocalizeOr("UI.Button", $"Button {index + 1}"), worldName),
        };
    }

    private static string DescribeWorldSeed(WorldFileData? data)
    {
        string label = LocalizeOr("UI.CopySeedToClipboard", "Copy seed");
        string seed = ExtractWorldSeed(data);
        return string.IsNullOrWhiteSpace(seed) ? label : $"{label}: {seed}";
    }

    private static string DescribeCloudToggle(bool isCloudSave)
    {
        string label = LocalizeOr("UI.MoveToCloud", "Move to cloud");
        string status = DescribeCloudStatus(isCloudSave);
        if (string.IsNullOrWhiteSpace(status) || string.Equals(label, status, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return JoinParts(label, status);
    }

    private static string DescribeCloudStatus(bool isCloudSave)
    {
        if (isCloudSave)
        {
            return LocalizeOr("UI.MoveFromCloud", "Stored in cloud");
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

        return localized.Trim();
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
                return value.Trim();
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
                    return value.Trim();
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
                return value.Trim();
            }
        }
        catch
        {
            // ignore reflection failures
        }

        return string.Empty;
    }
}

internal readonly record struct MenuUiLabel(string Text, bool IsNew);
