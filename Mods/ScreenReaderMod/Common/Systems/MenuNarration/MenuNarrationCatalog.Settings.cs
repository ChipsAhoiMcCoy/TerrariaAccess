#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.UI.Minimap;
using Terraria.GameContent.UI.ResourceSets;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

internal static partial class MenuNarrationCatalog
{
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
        string backLabel = TextSanitizer.Clean(Lang.menu[5].Value);
        string effectsLabel = TextSanitizer.Clean(Language.GetTextValue("UI.Effects"));
        string[] menuItems = GetMenuItemArray();
        bool hasEffectsButton = menuItems.Any(item => string.Equals(TextSanitizer.Clean(item), effectsLabel, StringComparison.OrdinalIgnoreCase));

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
        if (hasEffectsButton)
        {
            items.Add(effectsLabel);
        }
        items.Add(backLabel);

        string expected = OptionOrEmpty(items, index);

        if ((uint)index < (uint)menuItems.Length)
        {
            string actual = TextSanitizer.Clean(menuItems[index]);
            if (!string.IsNullOrWhiteSpace(actual))
            {
                if (string.Equals(actual, backLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return backLabel;
                }

                bool actualIsEffects = string.Equals(actual, effectsLabel, StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(expected) &&
                    string.Equals(expected, backLabel, StringComparison.OrdinalIgnoreCase) &&
                    actualIsEffects)
                {
                    return backLabel;
                }

                if (actualIsEffects && index == menuItems.Length - 1)
                {
                    return backLabel;
                }

                return actual;
            }
        }

        return expected;
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

}
