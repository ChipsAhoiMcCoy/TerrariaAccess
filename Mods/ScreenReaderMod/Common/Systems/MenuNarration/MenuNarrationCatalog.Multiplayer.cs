#nullable enable
using System;
using Terraria;
using Terraria.Localization;
using Terraria.Social;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems;

internal static partial class MenuNarrationCatalog
{
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

    private static string DescribeServerIpMenu(int index)
    {
        string prompt = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.ServerIpPrompt", "Enter server IP or host name");
        string input = TextSanitizer.Clean(Netplay.ServerIPText ?? string.Empty);
        return index switch
        {
            0 => string.IsNullOrWhiteSpace(input) ? prompt : TextSanitizer.JoinWithComma(prompt, input),
            1 => DescribeJoinAccept(),
            2 => DescribeJoinBack(),
            _ => prompt,
        };
    }

    private static string DescribeServerPortMenu(int index)
    {
        string prompt = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.ServerPortPrompt", "Enter server port");
        string port = Netplay.ListenPort > 0 ? Netplay.ListenPort.ToString() : string.Empty;
        return index switch
        {
            0 => string.IsNullOrWhiteSpace(port) ? prompt : TextSanitizer.JoinWithComma(prompt, port),
            1 => DescribeJoinAccept(),
            2 => DescribeJoinBack(),
            _ => prompt,
        };
    }

    private static string DescribeServerPasswordMenu(int index)
    {
        string prompt = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.ServerPasswordPrompt", "Enter server password");
        string password = Netplay.ServerPassword ?? string.Empty;
        string lengthFormat = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.PasswordLength", "{0} characters entered");
        string summary = string.IsNullOrWhiteSpace(password) ? string.Empty : string.Format(lengthFormat, password.Length);

        return index switch
        {
            0 => string.IsNullOrWhiteSpace(summary) ? prompt : TextSanitizer.JoinWithComma(prompt, summary),
            1 => DescribeJoinAccept(),
            2 => DescribeJoinBack(),
            _ => prompt,
        };
    }

    private static string DescribeJoinAccept()
    {
        return LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.Accept", "Accept");
    }

    private static string DescribeJoinBack()
    {
        return LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.JoinMenu.Back", "Back");
    }

    private static string DescribeConnectionStatusMenu(int index)
    {
        string status = TextSanitizer.Clean(Main.statusText ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        // No reliable status string; stay silent to avoid misleading labels (e.g., "Starting server" while joining).
        return string.Empty;
    }

}
