#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.UI.Minimap;
using Terraria.GameContent.UI.ResourceSets;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;
using Terraria.Social;
using Terraria.UI;
using Terraria.IO;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

internal static class MenuNarrationCatalog
{
    private static readonly Dictionary<int, string> MenuModeNames = new()
    {
        [0] = "Main menu",
        [1] = "Single player worlds",
        [2] = "Player selection",
        [11] = "Multiplayer menu",
        [12] = "Join by IP",
        [14] = "Settings",
        [17] = "Controls",
        [18] = "Credits",
        [26] = "Achievements",
        [9] = "World deletion confirmation",
        [1212] = "Language selection",
        [1213] = "Language selection",
        [888] = "tModLoader menu",
        [889] = "Host and play settings",
        [10017] = "tModLoader settings",
    };

    private static readonly Dictionary<int, Func<int, string>> ModeResolvers = new()
    {
        [0] = DescribeMainMenuItem,
        [11] = DescribeSettingsMenu,
        [26] = DescribeSettingsAudioMenu,
        [112] = DescribeSettingsGeneralMenu,
        [1112] = DescribeSettingsInterfaceMenu,
        [1111] = DescribeSettingsVideoMenu,
        [2008] = DescribeEffectsMenu,
        [111] = DescribeResolutionMenu,
        [1125] = DescribeSettingsCursorMenu,
        [1127] = DescribeSettingsGameplayMenu,
        [12] = DescribeMultiplayerMenu,
        [9] = DescribeWorldDeletionConfirmation,
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

    public static string DescribeMenuMode(int menuMode)
    {
        if (MenuModeNames.TryGetValue(menuMode, out string? value))
        {
            return value;
        }

        return $"Menu mode {menuMode}";
    }

    public static string DescribeMenuItem(int menuMode, int focusedIndex)
    {
        if (focusedIndex < 0)
        {
            return string.Empty;
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
        bool withinMenuItems = items.Length > 0 && focusedIndex < items.Length;
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

        string label = TryGetFromLangMenu(focusedIndex);
        if (!string.IsNullOrEmpty(label))
        {
            return label;
        }

        return $"Option {focusedIndex + 1}";
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

    private static string DescribeMainMenuItem(int index)
    {
        const int capacity = 32;
        string[] names = new string[capacity];
        float[] scales = new float[capacity];

        int cursor = 0;
        int offY = 220;
        int spacing = 52;
        int numButtons = 7;

        names[cursor++] = Lang.menu[12].Value;                  // Single Player
        names[cursor++] = Lang.menu[13].Value;                  // Multiplayer
        names[cursor++] = Lang.menu[131].Value;                 // Achievements
        names[cursor++] = Language.GetTextValue("UI.Workshop"); // Workshop

        int buttonIndex = cursor;
        InvokeOptionalAddMenuButtons(names, scales, ref offY, ref spacing, ref buttonIndex, ref numButtons);
        cursor = Math.Max(cursor, buttonIndex);

        names[cursor++] = Lang.menu[14].Value;                  // Settings
        names[cursor++] = Language.GetTextValue("UI.Credits");  // Credits
        names[cursor++] = Lang.menu[15].Value;                  // Exit

        if (index >= 0 && index < cursor)
        {
            return TextSanitizer.Clean(names[index]);
        }

        return string.Empty;
    }

    private static void InvokeOptionalAddMenuButtons(string[] names, float[] scales, ref int offY, ref int spacing, ref int buttonIndex, ref int numButtons)
    {
        MethodInfo? method = AddMenuButtonsMethod.Value;
        if (method is null || Main.instance is null)
        {
            return;
        }

        object[] args = { Main.instance, -1, names, scales, offY, spacing, buttonIndex, numButtons };
        try
        {
            method.Invoke(null, args);
            offY = (int)args[4];
            spacing = (int)args[5];
            buttonIndex = (int)args[6];
            numButtons = (int)args[7];
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Interface.AddMenuButtons reflection failed: {ex.Message}");
        }
    }

    private static string DescribeSettingsMenu(int index)
    {
        string[] entries =
        {
            TextSanitizer.Clean(Lang.menu[114].Value),
            TextSanitizer.Clean(Lang.menu[210].Value),
            TextSanitizer.Clean(Lang.menu[63].Value),
            TextSanitizer.Clean(Lang.menu[65].Value),
            TextSanitizer.Clean(Lang.menu[218].Value),
            TextSanitizer.Clean(Lang.menu[219].Value),
            TextSanitizer.Clean(Lang.menu[103].Value),
            TextSanitizer.Clean(Language.GetTextValue("tModLoader.tModLoaderSettings")),
            TextSanitizer.Clean(Lang.menu[5].Value),
        };

        return OptionOrEmpty(entries, index);
    }

    private static string DescribeWorldDeletionConfirmation(int index)
    {
        string worldName = GetSelectedWorldName();

        switch (index)
        {
            case 0:
            {
                string deletePrompt = TextSanitizer.Clean(Lang.menu[46].Value);
                if (string.IsNullOrWhiteSpace(deletePrompt))
                {
                    deletePrompt = LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete");
                }

                if (!string.IsNullOrWhiteSpace(worldName))
                {
                    return TextSanitizer.Clean($"{deletePrompt} {worldName}?");
                }

                return deletePrompt;
            }

            case 1:
            {
                string confirmLabel = TextSanitizer.Clean(Lang.menu[104].Value);
                if (string.IsNullOrWhiteSpace(confirmLabel))
                {
                    confirmLabel = LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete");
                }

                if (!string.IsNullOrWhiteSpace(worldName))
                {
                    return TextSanitizer.JoinWithComma(confirmLabel, worldName);
                }

                return confirmLabel;
            }

            case 2:
            {
                string cancelLabel = TextSanitizer.Clean(Lang.menu[105].Value);
                if (string.IsNullOrWhiteSpace(cancelLabel))
                {
                    cancelLabel = LocalizationHelper.GetTextOrFallback("UI.Cancel", "Cancel");
                }

                return cancelLabel;
            }

            default:
                return string.Empty;
        }
    }

    private static string GetSelectedWorldName()
    {
        try
        {
            int selectedWorld = Main.selectedWorld;
            if (selectedWorld >= 0)
            {
                List<WorldFileData> worlds = Main.WorldList;
                if (worlds is not null && selectedWorld < worlds.Count)
                {
                    return TextSanitizer.Clean(worlds[selectedWorld].Name);
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to resolve selected world: {ex.Message}");
        }

        return string.Empty;
    }

    private static string DescribeHostAndPlayServerMenu(int index)
    {
        bool lobbyEnabled = MenuServerModeHasFlag("Lobby");
        bool friendsEnabled = MenuServerModeHasFlag("FriendsCanJoin");
        bool friendsOfFriendsEnabled = MenuServerModeHasFlag("FriendsOfFriends");
        bool showConsole = Main.showServerConsole;

        return index switch
        {
            0 => TextSanitizer.Clean(Lang.menu[135].Value),
            1 => DescribeLobbyToggle(lobbyEnabled),
            2 => DescribeFriendsToggle(lobbyEnabled, friendsEnabled),
            3 => DescribeFriendsOfFriendsToggle(lobbyEnabled, friendsEnabled, friendsOfFriendsEnabled),
            4 => TextSanitizer.Clean(Lang.menu[144].Value),
            5 => TextSanitizer.Clean(Lang.menu[5].Value),
            6 => DescribeShowConsoleToggle(showConsole),
            _ => string.Empty,
        };
    }

    private static string DescribeLobbyToggle(bool lobbyEnabled)
    {
        string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.LobbyLabel", "Steam lobby");
        string status = DescribeEnabledDisabled(lobbyEnabled);
        return TextSanitizer.JoinWithComma(label, status);
    }

    private static string DescribeFriendsToggle(bool lobbyEnabled, bool friendsEnabled)
    {
        string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.FriendsLabel", "Friends can join");
        string status = DescribeEnabledDisabled(lobbyEnabled && friendsEnabled);
        if (!lobbyEnabled)
        {
            string note = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.RequiresLobby", "Enable the lobby to configure friend access");
            return TextSanitizer.JoinWithComma(label, status, note);
        }

        return TextSanitizer.JoinWithComma(label, status);
    }

    private static string DescribeFriendsOfFriendsToggle(bool lobbyEnabled, bool friendsEnabled, bool friendsOfFriendsEnabled)
    {
        string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.FriendsOfFriendsLabel", "Friends of friends can join");
        string status = DescribeEnabledDisabled(lobbyEnabled && friendsEnabled && friendsOfFriendsEnabled);
        if (!lobbyEnabled)
        {
            string note = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.RequiresLobby", "Enable the lobby to configure friend access");
            return TextSanitizer.JoinWithComma(label, status, note);
        }

        if (!friendsEnabled)
        {
            string note = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.RequiresFriends", "Allow friends to join before enabling this option");
            return TextSanitizer.JoinWithComma(label, status, note);
        }

        return TextSanitizer.JoinWithComma(label, status);
    }

    private static string DescribeShowConsoleToggle(bool showConsole)
    {
        string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.HostMenu.ConsoleLabel", "Show server console");
        string status = DescribeEnabledDisabled(showConsole);
        return TextSanitizer.JoinWithComma(label, status);
    }

    private static string DescribeEnabledDisabled(bool enabled)
    {
        string key = enabled ? "GameUI.Enabled" : "GameUI.Disabled";
        return TextSanitizer.Clean(Language.GetTextValue(key));
    }

    private static bool MenuServerModeHasFlag(string flagName)
    {
        try
        {
            object menuServerMode = Main.MenuServerMode;
            Type? enumType = menuServerMode?.GetType();
            if (enumType is null || !enumType.IsEnum)
            {
                return false;
            }

            object flag = Enum.Parse(enumType, flagName);
            long modeValue = Convert.ToInt64(menuServerMode);
            long flagValue = Convert.ToInt64(flag);
            return (modeValue & flagValue) == flagValue;
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to inspect MenuServerMode flag '{flagName}': {ex.Message}");
            return false;
        }
    }

    private static string DescribeSettingsAudioMenu(int index)
    {
        if (index == 2)
        {
            return TextSanitizer.Clean(Language.GetTextValue("UI.Back"));
        }

        string[] menuItems = GetMenuItemArray();
        if (index >= 0 && index < menuItems.Length)
        {
            string option = menuItems[index];
            if (!string.IsNullOrWhiteSpace(option))
            {
                return option;
            }
        }

        if (index is 0 or 1)
        {
            return TextSanitizer.Clean(Lang.menu[65].Value);
        }

        return string.Empty;
    }

    private static string DescribeSettingsGeneralMenu(int index)
    {
        string[] entries =
        {
            TextSanitizer.Clean(Main.autoSave ? Lang.menu[67].Value : Lang.menu[68].Value),
            TextSanitizer.Clean(Main.autoPause ? Lang.menu[69].Value : Lang.menu[70].Value),
            TextSanitizer.Clean(Main.mapEnabled ? Lang.menu[112].Value : Lang.menu[113].Value),
            TextSanitizer.Clean(Main.HidePassword ? Lang.menu[212].Value : Lang.menu[211].Value),
            TextSanitizer.Clean(Lang.menu[5].Value),
        };

        return OptionOrEmpty(entries, index);
    }

    private static string DescribeSettingsInterfaceMenu(int index)
    {
        var items = new List<string>
        {
            TextSanitizer.Clean(Main.showItemText ? Lang.menu[71].Value : Lang.menu[72].Value),
            TextSanitizer.Clean($"{Lang.menu[123].Value} {Lang.menu[124 + Utils.Clamp(Main.invasionProgressMode, 0, 2)].Value}"),
            TextSanitizer.Clean(Main.placementPreview ? Lang.menu[128].Value : Lang.menu[129].Value),
            TextSanitizer.Clean(ItemSlot.Options.HighlightNewItems ? Lang.inter[117].Value : Lang.inter[116].Value),
            TextSanitizer.Clean(Main.MouseShowBuildingGrid ? Lang.menu[229].Value : Lang.menu[230].Value),
            TextSanitizer.Clean(Main.GamepadDisableInstructionsDisplay ? Lang.menu[241].Value : Lang.menu[242].Value),
        };

        string mapBorderKey = Main.MinimapFrameManagerInstance?.ActiveSelectionKeyName ?? string.Empty;
        string mapBorder = Language.GetTextValue("UI.MinimapFrame_" + mapBorderKey);
        items.Add(TextSanitizer.Clean(Language.GetTextValue("UI.SelectMapBorder", mapBorder)));

        string resourceName = Main.ResourceSetsManager?.ActiveSet.DisplayedName ?? string.Empty;
        items.Add(TextSanitizer.Clean(Language.GetTextValue("UI.SelectHealthStyle", resourceName)));

        items.Add(TextSanitizer.Clean(Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff")));

        string bossBar = Language.GetTextValue("tModLoader.BossBarStyle", BossBarLoader.CurrentStyle?.DisplayName ?? string.Empty);
        items.Add(TextSanitizer.Clean(bossBar));

        items.Add(TextSanitizer.Clean(Lang.menu[5].Value));

        return OptionOrEmpty(items, index);
    }

    private static string DescribeLanguageMenu(int index, bool includeBackOption)
    {
        string[] menuItems = GetMenuItemArray();
        string header = TextSanitizer.Clean(Lang.menu[102].Value);
        if (menuItems.Length > 0 && string.Equals(menuItems[0], header, StringComparison.OrdinalIgnoreCase))
        {
            return OptionOrEmpty(menuItems, index);
        }

        var entries = new List<string> { header };
        string[] languageKeys =
        {
            "Language.English",
            "Language.German",
            "Language.Italian",
            "Language.French",
            "Language.Spanish",
            "Language.Russian",
            "Language.Chinese",
            "Language.Portuguese",
            "Language.Polish",
        };

        foreach (string key in languageKeys)
        {
            string value = TextSanitizer.Clean(Language.GetTextValue(key));
            if (!string.IsNullOrWhiteSpace(value))
            {
                entries.Add(value);
            }
        }

        if (includeBackOption)
        {
            entries.Add(TextSanitizer.Clean(Lang.menu[5].Value));
        }

        return OptionOrEmpty(entries, index);
    }

    private static string DescribeTmlSettingsMenu(int index)
    {
        string[] menuItems = GetMenuItemArray();
        string expectedYes = TextSanitizer.Clean(Language.GetTextValue("tModLoader.DownloadFromServersYes"));
        string expectedNo = TextSanitizer.Clean(Language.GetTextValue("tModLoader.DownloadFromServersNo"));
        if (menuItems.Length >= 1 && (string.Equals(menuItems[0], expectedYes, StringComparison.OrdinalIgnoreCase) || string.Equals(menuItems[0], expectedNo, StringComparison.OrdinalIgnoreCase)))
        {
            return OptionOrEmpty(menuItems, index);
        }

        bool downloadMods = ReadBool(ModNetDownloadModsField, fallback: true);
        bool autoReload = ReadBool(ModLoaderAutoReloadField, fallback: true);
        bool removeMinimumZoom = ReadBool(ModLoaderRemoveMinZoomField, fallback: false);
        int attackSpeedVisibility = ReadInt(ModLoaderAttackSpeedVisibilityField, fallback: 0);
        bool notifyMenuThemes = ReadBool(ModLoaderNotifyMenuThemesField, fallback: true);
        bool showUpdatedModsInfo = ReadBool(ModLoaderShowUpdatedModsInfoField, fallback: true);
        bool showConfirmation = ReadBool(ModLoaderShowConfirmationField, fallback: true);

        var entries = new List<string>
        {
            TextSanitizer.Clean(Language.GetTextValue(downloadMods ? "tModLoader.DownloadFromServersYes" : "tModLoader.DownloadFromServersNo")),
            TextSanitizer.Clean(Language.GetTextValue(autoReload ? "tModLoader.AutomaticallyReloadRequiredModsLeavingModsScreenYes" : "tModLoader.AutomaticallyReloadRequiredModsLeavingModsScreenNo")),
            TextSanitizer.Clean(Language.GetTextValue("tModLoader.RemoveForcedMinimumZoom" + (removeMinimumZoom ? "Yes" : "No"))),
            TextSanitizer.Clean(Language.GetTextValue($"tModLoader.AttackSpeedScalingTooltipVisibility{attackSpeedVisibility}")),
            TextSanitizer.Clean(Language.GetTextValue("tModLoader.ShowModMenuNotifications" + (notifyMenuThemes ? "Yes" : "No"))),
            TextSanitizer.Clean(Language.GetTextValue("tModLoader.ShowNewUpdatedModsInfo" + (showUpdatedModsInfo ? "Yes" : "No"))),
            TextSanitizer.Clean(Language.GetTextValue("tModLoader.ShowConfirmationWindowWhenEnableDisableAllMods" + (showConfirmation ? "Yes" : "No"))),
            TextSanitizer.Clean(Lang.menu[5].Value),
        };

        return OptionOrEmpty(entries, index);
    }

    private static string DescribeSettingsVideoMenu(int index)
    {
        int frameSkipIndex = (int)Main.FrameSkipMode;
        string[] menuItems = GetMenuItemArray();
        if ((uint)index < (uint)menuItems.Length)
        {
            string actual = TextSanitizer.Clean(menuItems[index]);
            if (!string.IsNullOrWhiteSpace(actual))
            {
                string backLabel = TextSanitizer.Clean(Lang.menu[5].Value);
                return string.Equals(actual, backLabel, StringComparison.OrdinalIgnoreCase) ? backLabel : actual;
            }
        }

        var items = new List<string>
        {
            TextSanitizer.Clean(Lang.menu[51].Value),
            TextSanitizer.Clean(Lang.menu[52].Value),
            TextSanitizer.Clean(Lang.menu[247 + Utils.Clamp(frameSkipIndex, 0, 3)].Value),
            TextSanitizer.Clean(Language.GetTextValue("UI.LightMode_" + Lighting.Mode)),
        };

        string quality = Main.qaStyle switch
        {
            0 => TextSanitizer.Clean(Lang.menu[59].Value),
            1 => TextSanitizer.Clean(Lang.menu[60].Value),
            2 => TextSanitizer.Clean(Lang.menu[61].Value),
            _ => TextSanitizer.Clean(Lang.menu[62].Value),
        };
        items.Add(quality);

        items.Add(TextSanitizer.Clean(Main.BackgroundEnabled ? Lang.menu[100].Value : Lang.menu[101].Value));
        items.Add(TextSanitizer.Clean(ChildSafety.Disabled ? Lang.menu[132].Value : Lang.menu[133].Value));
        items.Add(TextSanitizer.Clean(Main.SettingsEnabled_MinersWobble ? Lang.menu[250].Value : Lang.menu[251].Value));
        items.Add(TextSanitizer.Clean(Main.SettingsEnabled_TilesSwayInWind ? Language.GetTextValue("UI.TilesSwayInWindOn") : Language.GetTextValue("UI.TilesSwayInWindOff")));
        items.Add(TextSanitizer.Clean(Language.GetTextValue("UI.Effects")));
        items.Add(TextSanitizer.Clean(Lang.menu[5].Value));

        return OptionOrEmpty(items, index);
    }

    private static string DescribeEffectsMenu(int index)
    {
        var items = new List<string>
        {
            string.Empty,
            TextSanitizer.Clean(Language.GetTextValue("UI.Effects")),
            TextSanitizer.Clean(Language.GetTextValue("GameUI.StormEffects", Main.UseStormEffects ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled"))),
            TextSanitizer.Clean(Language.GetTextValue("GameUI.HeatDistortion", Main.UseHeatDistortion ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled"))),
            TextSanitizer.Clean(Language.GetTextValue("GameUI.WaveQuality", Main.WaveQuality switch
            {
                1 => Language.GetTextValue("GameUI.QualityLow"),
                2 => Language.GetTextValue("GameUI.QualityMedium"),
                3 => Language.GetTextValue("GameUI.QualityHigh"),
                _ => Language.GetTextValue("GameUI.QualityOff"),
            })),
            TextSanitizer.Clean(Lang.menu[5].Value),
        };

        return OptionOrEmpty(items, index);
    }

    private static string DescribeResolutionMenu(int index)
    {
        bool borderlessAvailable = Platform.IsWindows;
        var items = new List<string>
        {
            TextSanitizer.Clean($"{Lang.menu[73].Value}: {Main.PendingResolutionWidth}x{Main.PendingResolutionHeight}"),
        };

        if (borderlessAvailable)
        {
            items.Add(TextSanitizer.Clean(Lang.menu[Main.PendingBorderlessState ? 245 : 246].Value));
        }

        items.Add(TextSanitizer.Clean(Main.graphics?.IsFullScreen == true ? Lang.menu[49].Value : Lang.menu[50].Value));
        items.Add(TextSanitizer.Clean(Lang.menu[134].Value));
        items.Add(TextSanitizer.Clean(Lang.menu[5].Value));

        return OptionOrEmpty(items, index);
    }

    private static string DescribeSettingsCursorMenu(int index)
    {
        var items = new List<string>
        {
            TextSanitizer.Clean(Lang.menu[64].Value),
            TextSanitizer.Clean(Lang.menu[217].Value),
            TextSanitizer.Clean(Main.cSmartCursorModeIsToggleAndNotHold ? Lang.menu[121].Value : Lang.menu[122].Value),
            TextSanitizer.Clean(Player.SmartCursorSettings.SmartAxeAfterPickaxe ? Lang.menu[214].Value : Lang.menu[213].Value),
            TextSanitizer.Clean(Player.SmartCursorSettings.SmartBlocksEnabled ? Lang.menu[215].Value : Lang.menu[216].Value),
        };

        string lockOn = LockOnHelper.UseMode switch
        {
            LockOnHelper.LockOnMode.FocusTarget => TextSanitizer.Clean(Lang.menu[232].Value),
            LockOnHelper.LockOnMode.TargetClosest => TextSanitizer.Clean(Lang.menu[233].Value),
            LockOnHelper.LockOnMode.ThreeDS => TextSanitizer.Clean(Lang.menu[234].Value),
            _ => string.Empty,
        };
        items.Add(lockOn);
        items.Add(TextSanitizer.Clean(Lang.menu[5].Value));

        return OptionOrEmpty(items, index);
    }

    private static string DescribeSettingsGameplayMenu(int index)
    {
        var items = new List<string>
        {
            TextSanitizer.Clean(Main.ReversedUpDownArmorSetBonuses ? Lang.menu[220].Value : Lang.menu[221].Value),
            TextSanitizer.Clean(ItemSlot.Options.DisableQuickTrash ? Lang.menu[253].Value : (ItemSlot.Options.DisableLeftShiftTrashCan ? Lang.menu[224].Value : Lang.menu[223].Value)),
            TextSanitizer.Clean(Lang.menu[222].Value),
            TextSanitizer.Clean(Lang.menu[5].Value),
        };

        return OptionOrEmpty(items, index);
    }

    private static string DescribeMultiplayerMenu(int index)
    {
        bool hasSocial = SocialAPI.Network != null;
        string[] entries = hasSocial
            ? new[]
            {
                TextSanitizer.Clean(Lang.menu[146].Value),
                TextSanitizer.Clean(Lang.menu[145].Value),
                TextSanitizer.Clean(Lang.menu[88].Value),
                TextSanitizer.Clean(Lang.menu[5].Value),
            }
            : new[]
            {
                TextSanitizer.Clean(Lang.menu[87].Value),
                TextSanitizer.Clean(Lang.menu[88].Value),
                TextSanitizer.Clean(Lang.menu[5].Value),
            };

        return OptionOrEmpty(entries, index);
    }
}
