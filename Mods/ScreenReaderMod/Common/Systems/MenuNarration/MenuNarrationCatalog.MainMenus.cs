#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.IO;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

internal static partial class MenuNarrationCatalog
{
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
        return TryDescribeFromTable(11, index, out string label) ? label : string.Empty;
    }

    private static string DescribePlayerDeletionConfirmation(int index)
    {
        string playerName = GetSelectedPlayerName();

        switch (index)
        {
            case 0:
            {
                string deletePrompt = TextSanitizer.Clean(Lang.menu[46].Value);
                if (string.IsNullOrWhiteSpace(deletePrompt))
                {
                    deletePrompt = LocalizationHelper.GetTextOrFallback("UI.Delete", "Delete");
                }

                if (!string.IsNullOrWhiteSpace(playerName))
                {
                    return TextSanitizer.Clean($"{deletePrompt} {playerName}?");
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

                if (!string.IsNullOrWhiteSpace(playerName))
                {
                    return TextSanitizer.JoinWithComma(confirmLabel, playerName);
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

    public static bool TryBuildDeletionAnnouncement(int menuMode, int focusIndex, out string combinedLabel)
    {
        combinedLabel = string.Empty;

        if (focusIndex is not (1 or 2))
        {
            return false;
        }

        if (!IsDeletionMenuMode(menuMode))
        {
            return false;
        }

        string prompt = DescribeMenuItem(menuMode, 0);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        string response = focusIndex == 1 ? GetDeletionConfirmLabel() : GetDeletionCancelLabel();
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        combinedLabel = TextSanitizer.Clean($"{prompt} {response}".Trim());
        return !string.IsNullOrWhiteSpace(combinedLabel);
    }

    private static bool IsDeletionMenuMode(int menuMode)
    {
        return menuMode == MenuID.CharacterDeletion
            || menuMode == MenuID.CharacterDeletionConfirmation
            || menuMode == MenuID.WorldDeletionConfirmation;
    }

    private static string GetDeletionConfirmLabel()
    {
        string label = TextSanitizer.Clean(Lang.menu[104].Value);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return LocalizationHelper.GetTextOrFallback("UI.Yes", "Yes");
    }

    private static string GetDeletionCancelLabel()
    {
        string label = TextSanitizer.Clean(Lang.menu[105].Value);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return LocalizationHelper.GetTextOrFallback("UI.No", "No");
    }

    private static string GetSelectedPlayerName()
    {
        try
        {
            int selectedPlayer = Main.selectedPlayer;
            if (selectedPlayer >= 0)
            {
                List<PlayerFileData> players = Main.PlayerList;
                if (players is not null && selectedPlayer < players.Count)
                {
                    string? name = players[selectedPlayer].Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return TextSanitizer.Clean(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to resolve selected player: {ex.Message}");
        }

        return LocalizationHelper.GetTextOrFallback("UI.PlayerNameDefault", "Player");
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

}
