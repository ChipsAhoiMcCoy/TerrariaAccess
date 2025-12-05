#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

internal static partial class MenuNarrationCatalog
{
    private static readonly Dictionary<int, MenuOption[]> MenuOptionTables = BuildMenuOptionTables();

    private static readonly Dictionary<int, string> MenuModeNames = new()
    {
        [0] = "Main menu",
        [1] = "Player selection",
        [2] = "Player selection",
        [11] = "Settings",
        [12] = "Join by IP",
        [14] = "Settings",
        [17] = "Controls",
        [18] = "Credits",
        [26] = "Audio settings",
        [MenuID.CharacterDeletion] = "Player deletion",
        [MenuID.CharacterDeletionConfirmation] = "Player deletion",
        [9] = "World deletion confirmation",
        [1212] = "Language selection",
        [1213] = "Language selection",
        [888] = "tModLoader menu",
        [889] = "Host and play settings",
        [10017] = "tModLoader settings",
    };

    private static readonly Dictionary<int, Func<int, string>> ModeResolvers = new()
    {
        [888] = DescribeMainMenuItem,
        [MenuID.Title] = DescribeMainMenuItem,
        [MenuID.CharacterDeletion] = DescribePlayerDeletionConfirmation,
        [MenuID.CharacterDeletionConfirmation] = DescribePlayerDeletionConfirmation,
        [26] = DescribeSettingsAudioMenu,
        [112] = DescribeSettingsGeneralMenu,
        [1112] = DescribeSettingsInterfaceMenu,
        [1111] = DescribeSettingsVideoMenu,
        [2008] = DescribeEffectsMenu,
        [111] = DescribeResolutionMenu,
        [1125] = DescribeSettingsCursorMenu,
        [1127] = DescribeSettingsGameplayMenu,
        [12] = DescribeMultiplayerMenu,
        [MenuID.WorldDeletionConfirmation] = DescribeWorldDeletionConfirmation,
        [1212] = static index => DescribeLanguageMenu(index, includeBackOption: false),
        [1213] = static index => DescribeLanguageMenu(index, includeBackOption: true),
        [889] = DescribeHostAndPlayServerMenu,
        [10017] = DescribeTmlSettingsMenu,
    };

    private static readonly Lazy<FieldInfo?> ModNetDownloadModsField = new(() =>
        typeof(ModNet).GetField("downloadModsFromServers", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderAutoReloadField = new(() =>
        typeof(ModLoader).GetField("autoReloadRequiredModsLeavingModsScreen", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderRemoveMinZoomField = new(() =>
        typeof(ModLoader).GetField("removeForcedMinimumZoom", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderAttackSpeedVisibilityField = new(() =>
        typeof(ModLoader).GetField("attackSpeedScalingTooltipVisibility", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderNotifyMenuThemesField = new(() =>
        typeof(ModLoader).GetField("notifyNewMainMenuThemes", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderShowUpdatedModsInfoField = new(() =>
        typeof(ModLoader).GetField("showNewUpdatedModsInfo", BindingFlags.NonPublic | BindingFlags.Static));

    private static readonly Lazy<FieldInfo?> ModLoaderShowConfirmationField = new(() =>
        typeof(ModLoader).GetField("showConfirmationWindowWhenEnableDisableAllMods", BindingFlags.NonPublic | BindingFlags.Static));

    private static FieldInfo? _menuItemsField;
    private static readonly Lazy<MethodInfo?> AddMenuButtonsMethod = new(() =>
        Type.GetType("Terraria.ModLoader.UI.Interface, tModLoader")?.GetMethod(
            "AddMenuButtons",
            BindingFlags.NonPublic | BindingFlags.Static));
    private static int _lastSnapshotMode = int.MinValue;
    private static DateTime _lastSnapshotAt = DateTime.MinValue;

    public static string DescribeMenuMode(int menuMode, UIState? uiState = null)
    {
        if (TryDescribeUiState(uiState, out string uiLabel))
        {
            return uiLabel;
        }

        if (MenuModeNames.TryGetValue(menuMode, out string? value))
        {
            return value;
        }

        return $"Menu mode {menuMode}";
    }

    private static bool TryDescribeUiState(UIState? uiState, out string label)
    {
        label = string.Empty;
        if (uiState is null)
        {
            return false;
        }

        string typeName = uiState.GetType().FullName ?? string.Empty;
        if (typeName.Contains("UICharacterSelect", StringComparison.Ordinal))
        {
            label = "Player selection";
            return true;
        }

        if (typeName.Contains("UIWorldSelect", StringComparison.Ordinal) ||
            typeName.Contains("UIWorldList", StringComparison.Ordinal))
        {
            label = "World selection";
            return true;
        }

        if (typeName.Contains("UIWorkshopHub", StringComparison.Ordinal))
        {
            label = "Workshop hub";
            return true;
        }

        if (typeName.Contains("UIManageControls", StringComparison.Ordinal))
        {
            label = "Controls menu";
            return true;
        }

        return false;
    }

    public static string DescribeMenuItem(int menuMode, int focusedIndex)
    {
        if (focusedIndex < 0)
        {
            return string.Empty;
        }

        if (TryDescribeFromTable(menuMode, focusedIndex, out string tableLabel))
        {
            return tableLabel;
        }

        if (ModeResolvers.TryGetValue(menuMode, out Func<int, string>? resolver))
        {
            string modeSpecific = resolver(focusedIndex);
            if (!string.IsNullOrWhiteSpace(modeSpecific))
            {
                return modeSpecific;
            }
        }

        string[] items = GetMenuItemArray();
        bool hasMenuItems = items.Length > 0;
        bool withinMenuItems = hasMenuItems && focusedIndex < items.Length;
        if (withinMenuItems)
        {
            string option = items[focusedIndex];
            if (!string.IsNullOrWhiteSpace(option))
            {
                return option;
            }

            if (menuMode == 26)
            {
                return string.Empty;
            }
        }

        if (!hasMenuItems && ShouldDeferLangMenuFallback(menuMode))
        {
            return string.Empty;
        }

        string label = TryGetFromLangMenu(focusedIndex);
        if (!string.IsNullOrEmpty(label))
        {
            return label;
        }

        return $"Option {focusedIndex + 1}";
    }

    private static bool TryDescribeFromTable(int menuMode, int focusedIndex, out string label)
    {
        label = string.Empty;

        if (!MenuOptionTables.TryGetValue(menuMode, out MenuOption[]? options) ||
            options is null ||
            focusedIndex < 0 ||
            focusedIndex >= options.Length)
        {
            return false;
        }

        string resolved = TextSanitizer.Clean(options[focusedIndex].Resolve());
        if (string.IsNullOrWhiteSpace(resolved) && !options[focusedIndex].AllowEmpty)
        {
            return false;
        }

        label = resolved;
        return !string.IsNullOrWhiteSpace(label);
    }

    private static bool TryReadStatic<T>(Lazy<FieldInfo?> fieldHandle, out T value) where T : struct
    {
        try
        {
            if (fieldHandle.Value is FieldInfo field && field.GetValue(null) is T typed)
            {
                value = typed;
                return true;
            }
        }
        catch
        {
            // ignore reflection failures
        }

        value = default;
        return false;
    }

    private static bool ReadBool(Lazy<FieldInfo?> fieldHandle, bool fallback)
    {
        return TryReadStatic(fieldHandle, out bool value) ? value : fallback;
    }

    private static int ReadInt(Lazy<FieldInfo?> fieldHandle, int fallback)
    {
        return TryReadStatic(fieldHandle, out int value) ? value : fallback;
    }

    private static bool ShouldDeferLangMenuFallback(int menuMode)
    {
        // Some modes intentionally expose no menuItems (e.g., world loading screen = 10); skip Lang.menu fallback there.
        return menuMode is 1 or 2 or 10 or 888;
    }

    private static string TryGetFromLangMenu(int focusedIndex)
    {
        try
        {
            LocalizedText[] menu = Lang.menu;
            if (focusedIndex >= 0 && focusedIndex < menu.Length)
            {
                string value = menu[focusedIndex].Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return TextSanitizer.Clean(value);
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Lang.menu lookup failed (index {focusedIndex}): {ex.Message}");
        }

        return string.Empty;
    }

    private static string[] GetMenuItemArray()
    {
        _menuItemsField ??= typeof(Main).GetField("menuItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (_menuItemsField is null)
        {
            return Array.Empty<string>();
        }

        object? rawValue = _menuItemsField.GetValue(null);
        if (rawValue is null)
        {
            return Array.Empty<string>();
        }

        return ConvertMenuItems(rawValue);
    }

    public static void LogMenuSnapshot(int menuMode, bool allowRepeat = false)
    {
        if (!allowRepeat && _lastSnapshotMode == menuMode)
        {
            return;
        }

        if (allowRepeat && _lastSnapshotMode == menuMode && DateTime.UtcNow - _lastSnapshotAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        string[] items = GetMenuItemArray();
        if (items.Length == 0)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[MenuNarration] menuItems reflection returned empty for menu mode {menuMode}.");
            return;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] menuMode {menuMode} exposes {items.Length} entries.");
        for (int i = 0; i < items.Length; i++)
        {
            string entry = items[i];
            if (!string.IsNullOrWhiteSpace(entry))
            {
                ScreenReaderMod.Instance?.Logger.Info($"[MenuNarration] menuItems[{i}] = {entry}");
            }
        }

        _lastSnapshotMode = menuMode;
        _lastSnapshotAt = DateTime.UtcNow;
    }

    private static string[] ConvertMenuItems(object raw)
    {
        switch (raw)
        {
            case string[] stringArray:
                return Array.ConvertAll(stringArray, static value => TextSanitizer.Clean(value));
            case LocalizedText[] localizedArray:
                return Array.ConvertAll(localizedArray, static text => TextSanitizer.Clean(text.Value));
            case IList list when raw is not Array:
                return ConvertList(list);
            case Array array:
                return ConvertArray(array);
            default:
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Unsupported menuItems type: {raw.GetType().FullName}");
                return Array.Empty<string>();
        }
    }

    private static string[] ConvertList(IList list)
    {
        var result = new List<string>(list.Count);
        foreach (object? entry in list)
        {
            result.Add(ConvertEntry(entry));
        }

        return result.ToArray();
    }

    private static string[] ConvertArray(Array array)
    {
        var result = new List<string>(array.Length);
        foreach (object? entry in array)
        {
            result.Add(ConvertEntry(entry));
        }

        return result.ToArray();
    }

    private static string ConvertEntry(object? entry)
    {
        if (entry is null)
        {
            return string.Empty;
        }

        if (entry is string str)
        {
            return TextSanitizer.Clean(str);
        }

        if (entry is LocalizedText localized)
        {
            return TextSanitizer.Clean(localized.Value);
        }

        return TextSanitizer.Clean(entry.ToString());
    }

    private static string OptionOrEmpty(IReadOnlyList<string> entries, int index)
    {
        return (uint)index < (uint)entries.Count ? entries[index] : string.Empty;
    }

    private static Dictionary<int, MenuOption[]> BuildMenuOptionTables()
    {
        return new Dictionary<int, MenuOption[]>
        {
            [11] = new[]
            {
                MenuOption.FromLangMenu(114),
                MenuOption.FromLangMenu(210),
                MenuOption.FromLangMenu(63),
                MenuOption.FromLangMenu(65),
                MenuOption.FromLangMenu(218),
                MenuOption.FromLangMenu(219),
                MenuOption.FromLangMenu(103),
                MenuOption.FromLocalizationKey("tModLoader.tModLoaderSettings"),
                MenuOption.FromLangMenu(5),
            },
        };
    }

    private readonly record struct MenuOption(Func<string> Resolver, bool AllowEmpty = false)
    {
        public string Resolve()
        {
            try
            {
                return Resolver();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static MenuOption FromLangMenu(int index, bool allowEmpty = false)
        {
            return new MenuOption(() =>
            {
                if (index < 0 || index >= Lang.menu.Length)
                {
                    return string.Empty;
                }

                return Lang.menu[index].Value;
            }, allowEmpty);
        }

        public static MenuOption FromLocalizationKey(string key, bool allowEmpty = false)
        {
            return new MenuOption(() => Language.GetTextValue(key), allowEmpty);
        }
    }
}
