#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        [888] = "tModLoader menu",
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
    };

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
                return option.Trim();
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
                    return Sanitize(value);
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
                return SanitizeArray(stringArray);
            case LocalizedText[] localizedArray:
                return localizedArray.Select(t => Sanitize(t.Value)).ToArray();
            case IList list when raw is not Array:
                return ConvertList(list);
            case Array array:
                return ConvertArray(array);
            default:
                ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Unsupported menuItems type: {raw.GetType().FullName}");
                return Array.Empty<string>();
        }
    }

    private static string[] SanitizeArray(IEnumerable<string> source)
    {
        return source.Select(Sanitize).ToArray();
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
            return Sanitize(str);
        }

        if (entry is LocalizedText localized)
        {
            return Sanitize(localized.Value);
        }

        return Sanitize(entry.ToString() ?? string.Empty);
    }

    private static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        if (!trimmed.Contains('['))
        {
            return trimmed;
        }

        var builder = new StringBuilder(trimmed.Length);
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '[')
            {
                int closing = trimmed.IndexOf(']', i + 1);
                if (closing > i)
                {
                    string token = trimmed.Substring(i + 1, closing - i - 1);
                    if (token.StartsWith("c/", StringComparison.OrdinalIgnoreCase))
                    {
                        int colon = token.IndexOf(':');
                        if (colon >= 0 && colon + 1 < token.Length)
                        {
                            builder.Append(token.Substring(colon + 1));
                        }
                        i = closing;
                        continue;
                    }

                    if (token.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                    {
                        i = closing;
                        continue;
                    }
                }
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
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
            return Sanitize(names[index]);
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
            Lang.menu[114].Value,
            Lang.menu[210].Value,
            Lang.menu[63].Value,
            Lang.menu[65].Value,
            Lang.menu[218].Value,
            Lang.menu[219].Value,
            Lang.menu[103].Value,
            Language.GetTextValue("tModLoader.tModLoaderSettings"),
            Lang.menu[5].Value,
        };

        if (index >= 0 && index < entries.Length)
        {
            return Sanitize(entries[index]);
        }

        return string.Empty;
    }

    private static string DescribeSettingsAudioMenu(int index)
    {
        if (index == 2)
        {
            return Sanitize(Language.GetTextValue("UI.Back"));
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
            return Sanitize(Lang.menu[65].Value);
        }

        return string.Empty;
    }

    private static string DescribeSettingsGeneralMenu(int index)
    {
        string[] entries =
        {
            Sanitize(Main.autoSave ? Lang.menu[67].Value : Lang.menu[68].Value),
            Sanitize(Main.autoPause ? Lang.menu[69].Value : Lang.menu[70].Value),
            Sanitize(Main.mapEnabled ? Lang.menu[112].Value : Lang.menu[113].Value),
            Sanitize(Main.HidePassword ? Lang.menu[212].Value : Lang.menu[211].Value),
            Sanitize(Lang.menu[5].Value),
        };

        if (index >= 0 && index < entries.Length)
        {
            return entries[index];
        }

        return string.Empty;
    }

    private static string DescribeSettingsInterfaceMenu(int index)
    {
        var items = new List<string>
        {
            Sanitize(Main.showItemText ? Lang.menu[71].Value : Lang.menu[72].Value),
            Sanitize($"{Lang.menu[123].Value} {Lang.menu[124 + Utils.Clamp(Main.invasionProgressMode, 0, 2)].Value}"),
            Sanitize(Main.placementPreview ? Lang.menu[128].Value : Lang.menu[129].Value),
            Sanitize(ItemSlot.Options.HighlightNewItems ? Lang.inter[117].Value : Lang.inter[116].Value),
            Sanitize(Main.MouseShowBuildingGrid ? Lang.menu[229].Value : Lang.menu[230].Value),
            Sanitize(Main.GamepadDisableInstructionsDisplay ? Lang.menu[241].Value : Lang.menu[242].Value),
        };

        string mapBorderKey = Main.MinimapFrameManagerInstance?.ActiveSelectionKeyName ?? string.Empty;
        string mapBorder = Language.GetTextValue("UI.MinimapFrame_" + mapBorderKey);
        items.Add(Sanitize(Language.GetTextValue("UI.SelectMapBorder", mapBorder)));

        string resourceName = Main.ResourceSetsManager?.ActiveSet.DisplayedName ?? string.Empty;
        items.Add(Sanitize(Language.GetTextValue("UI.SelectHealthStyle", resourceName)));

        items.Add(Sanitize(Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff")));

        string bossBar = Language.GetTextValue("tModLoader.BossBarStyle", BossBarLoader.CurrentStyle?.DisplayName ?? string.Empty);
        items.Add(Sanitize(bossBar));

        items.Add(Sanitize(Lang.menu[5].Value));

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeSettingsVideoMenu(int index)
    {
        int frameSkipIndex = (int)Main.FrameSkipMode;
        var items = new List<string>
        {
            Sanitize(Lang.menu[51].Value),
            Sanitize(Lang.menu[52].Value),
            Sanitize(Lang.menu[247 + Utils.Clamp(frameSkipIndex, 0, 3)].Value),
            Sanitize(Language.GetTextValue("UI.LightMode_" + Lighting.Mode)),
        };

        string quality = Main.qaStyle switch
        {
            0 => Lang.menu[59].Value,
            1 => Lang.menu[60].Value,
            2 => Lang.menu[61].Value,
            _ => Lang.menu[62].Value,
        };
        items.Add(Sanitize(quality));

        items.Add(Sanitize(Main.BackgroundEnabled ? Lang.menu[100].Value : Lang.menu[101].Value));
        items.Add(Sanitize(ChildSafety.Disabled ? Lang.menu[132].Value : Lang.menu[133].Value));
        items.Add(Sanitize(Main.SettingsEnabled_MinersWobble ? Lang.menu[250].Value : Lang.menu[251].Value));
        items.Add(Sanitize(Main.SettingsEnabled_TilesSwayInWind ? Language.GetTextValue("UI.TilesSwayInWindOn") : Language.GetTextValue("UI.TilesSwayInWindOff")));
        items.Add(Sanitize(Language.GetTextValue("UI.Effects")));
        items.Add(Sanitize(Lang.menu[5].Value));

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeEffectsMenu(int index)
    {
        var items = new List<string>
        {
            string.Empty,
            Sanitize(Language.GetTextValue("UI.Effects")),
            Sanitize(Language.GetTextValue("GameUI.StormEffects", Main.UseStormEffects ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled"))),
            Sanitize(Language.GetTextValue("GameUI.HeatDistortion", Main.UseHeatDistortion ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled"))),
            Sanitize(Language.GetTextValue("GameUI.WaveQuality", Main.WaveQuality switch
            {
                1 => Language.GetTextValue("GameUI.QualityLow"),
                2 => Language.GetTextValue("GameUI.QualityMedium"),
                3 => Language.GetTextValue("GameUI.QualityHigh"),
                _ => Language.GetTextValue("GameUI.QualityOff"),
            })),
            Sanitize(Lang.menu[5].Value),
        };

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeResolutionMenu(int index)
    {
        bool borderlessAvailable = Platform.IsWindows;
        var items = new List<string>
        {
            Sanitize($"{Lang.menu[73].Value}: {Main.PendingResolutionWidth}x{Main.PendingResolutionHeight}"),
        };

        if (borderlessAvailable)
        {
            items.Add(Sanitize(Lang.menu[Main.PendingBorderlessState ? 245 : 246].Value));
        }

        items.Add(Sanitize(Main.graphics?.IsFullScreen == true ? Lang.menu[49].Value : Lang.menu[50].Value));
        items.Add(Sanitize(Lang.menu[134].Value));
        items.Add(Sanitize(Lang.menu[5].Value));

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeSettingsCursorMenu(int index)
    {
        var items = new List<string>
        {
            Sanitize(Lang.menu[64].Value),
            Sanitize(Lang.menu[217].Value),
            Sanitize(Main.cSmartCursorModeIsToggleAndNotHold ? Lang.menu[121].Value : Lang.menu[122].Value),
            Sanitize(Player.SmartCursorSettings.SmartAxeAfterPickaxe ? Lang.menu[214].Value : Lang.menu[213].Value),
            Sanitize(Player.SmartCursorSettings.SmartBlocksEnabled ? Lang.menu[215].Value : Lang.menu[216].Value),
        };

        string lockOn = LockOnHelper.UseMode switch
        {
            LockOnHelper.LockOnMode.FocusTarget => Lang.menu[232].Value,
            LockOnHelper.LockOnMode.TargetClosest => Lang.menu[233].Value,
            LockOnHelper.LockOnMode.ThreeDS => Lang.menu[234].Value,
            _ => string.Empty,
        };
        items.Add(Sanitize(lockOn));
        items.Add(Sanitize(Lang.menu[5].Value));

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeSettingsGameplayMenu(int index)
    {
        var items = new List<string>
        {
            Sanitize(Main.ReversedUpDownArmorSetBonuses ? Lang.menu[220].Value : Lang.menu[221].Value),
            Sanitize(ItemSlot.Options.DisableQuickTrash ? Lang.menu[253].Value : (ItemSlot.Options.DisableLeftShiftTrashCan ? Lang.menu[224].Value : Lang.menu[223].Value)),
            Sanitize(Lang.menu[222].Value),
            Sanitize(Lang.menu[5].Value),
        };

        if (index >= 0 && index < items.Count)
        {
            return items[index];
        }

        return string.Empty;
    }

    private static string DescribeMultiplayerMenu(int index)
    {
        bool hasSocial = SocialAPI.Network != null;
        string[] entries = hasSocial
            ? new[]
            {
                Lang.menu[146].Value,
                Lang.menu[145].Value,
                Lang.menu[88].Value,
                Lang.menu[5].Value,
            }
            : new[]
            {
                Lang.menu[87].Value,
                Lang.menu[88].Value,
                Lang.menu[5].Value,
            };

        if (index >= 0 && index < entries.Length)
        {
            return Sanitize(entries[index]);
        }

        return string.Empty;
    }
}
