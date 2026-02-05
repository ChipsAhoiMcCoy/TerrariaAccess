#nullable enable
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Handles special inventory selection buttons (Quick Stack, Sort, loadouts, etc.).
/// </summary>
internal sealed class SpecialSelectionHandler
{
    private static readonly HashSet<int> LoggedUnknownPoints = new();
    private int _lastPoint = -1;

    /// <summary>
    /// Gets the label for a special selection link point.
    /// </summary>
    public string? GetLabel(int point, bool hoverIsAir, string? location)
    {
        string? result = point switch
        {
            301 => FormatButtonLabel(Language.GetTextValue("GameUI.QuickStackToNearby")),
            302 => FormatButtonLabel(Language.GetTextValue("GameUI.SortInventory")),
            304 => FormatButtonLabel(Lang.inter[19].Value),
            305 => FormatButtonLabel(Lang.inter[79].Value),
            306 => FormatButtonLabel(Lang.inter[80].Value),
            307 => FormatButtonLabel(Main.CaptureModeDisabled ? Lang.inter[115].Value : Lang.inter[81].Value),
            308 => FormatButtonLabel(Lang.inter[62].Value),
            309 => FormatButtonLabel(Language.GetTextValue("GameUI.Emote")),
            310 => FormatButtonLabel(Language.GetTextValue("GameUI.Bestiary")),
            311 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.LoadoutControls", "Loadout controls")),
            int loadout when loadout >= 312 && loadout <= 320 => FormatButtonLabel(GetLoadoutLabel(loadout)),
            int chestButton when chestButton >= 500 && chestButton <= 505 => DescribeChestButton(chestButton),
            1550 => FormatButtonLabel(GetPvpToggleText()),
            int teamButton when teamButton >= 1551 && teamButton <= 1556 => FormatButtonLabel(GetTeamButtonText(teamButton)),
            1557 => DescribeDefenseCounter(),
            1570 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.AchievementAdvisor", "Achievement Advisor")),
            _ => null,
        };

        if (!string.IsNullOrEmpty(result))
        {
            return result;
        }

        if (!Main.ingameOptionsWindow && ShouldLogUnknown(hoverIsAir, location))
        {
            LogUnknownPoint(point, hoverIsAir, location);
        }

        return null;
    }

    /// <summary>
    /// Checks if a point is a known special inventory button.
    /// </summary>
    public static bool IsSpecialInventoryPoint(int point)
    {
        return point switch
        {
            301 or 302 or 304 or 305 or 306 or 307 or 308 or 309 or 310 or 311 => true,
            >= 312 and <= 320 => true,
            >= 500 and <= 505 => true,
            1550 => true,
            >= 1551 and <= 1556 => true,
            1557 => true,
            1570 => true,
            _ => false,
        };
    }

    /// <summary>
    /// Resolves the inventory region for a special selection point.
    /// </summary>
    public InventoryRegion ResolveRegion(int point)
    {
        return point switch
        {
            301 or 302 => InventoryRegion.InventoryExtras,
            >= 304 and <= 308 => InventoryRegion.CharacterPanel,
            309 or 310 or 311 => InventoryRegion.InventoryExtras,
            >= 312 and <= 320 => InventoryRegion.CharacterPanel,
            >= 500 and <= 505 => InventoryRegion.Storage,
            >= 1550 and <= 1557 => InventoryRegion.CharacterPanel,
            1570 => InventoryRegion.CharacterPanel,
            _ => InventoryRegion.None,
        };
    }

    /// <summary>
    /// Checks if a special selection should be announced (prevents repeats).
    /// </summary>
    public bool ShouldAnnounce(int point)
    {
        return point >= 0 && point != _lastPoint;
    }

    /// <summary>
    /// Records a special selection to prevent repeats.
    /// </summary>
    public void Record(int point)
    {
        if (point < 0)
        {
            Clear();
            return;
        }

        _lastPoint = point;
    }

    /// <summary>
    /// Clears the repeat guard state.
    /// </summary>
    public void Clear()
    {
        _lastPoint = -1;
    }

    private static string? FormatButtonLabel(string? text)
    {
        string cleaned = TextSanitizer.Clean(text ?? string.Empty);
        return string.IsNullOrWhiteSpace(cleaned) ? null : $"{cleaned} button";
    }

    private static string? GetLoadoutLabel(int point)
    {
        int index = point - 311;
        if (index < 1 || index > 9)
        {
            return null;
        }

        return Language.GetTextValue($"UI.Loadout{index}");
    }

    private static string? DescribeChestButton(int point)
    {
        string? label = point switch
        {
            500 => GetLegacyInterfaceText(29), // Loot All
            501 => GetLegacyInterfaceText(30), // Deposit All
            502 => GetLegacyInterfaceText(31), // Quick Stack
            503 => GetLegacyInterfaceText(82), // Restock
            504 => GetLegacyInterfaceText(61), // Rename
            505 => GetLegacyInterfaceText(122), // Sort Items
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return FormatButtonLabel(label);
    }

    private static string? GetLegacyInterfaceText(int index)
    {
        if (index < 0 || index >= Lang.inter.Length)
        {
            return null;
        }

        string value = Lang.inter[index]?.Value ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? null : TextSanitizer.Clean(value);
    }

    private static string? GetPvpToggleText()
    {
        Player? player = Main.LocalPlayer;
        bool hostile = player?.hostile ?? false;
        string key = hostile
            ? "Mods.ScreenReaderMod.InventorySpecial.DisablePvp"
            : "Mods.ScreenReaderMod.InventorySpecial.EnablePvp";
        string fallback = hostile ? "Disable PvP" : "Enable PvP";
        return LocalizationHelper.GetTextOrFallback(key, fallback);
    }

    private static string? GetTeamButtonText(int point)
    {
        int teamIndex = point - 1551;
        string[] fallbacks =
        {
            "No team",
            "Red team",
            "Green team",
            "Blue team",
            "Yellow team",
            "Pink team",
        };

        if (teamIndex < 0 || teamIndex >= fallbacks.Length)
        {
            return null;
        }

        string key = teamIndex switch
        {
            0 => "Mods.ScreenReaderMod.InventorySpecial.TeamNeutral",
            1 => "Mods.ScreenReaderMod.InventorySpecial.TeamRed",
            2 => "Mods.ScreenReaderMod.InventorySpecial.TeamGreen",
            3 => "Mods.ScreenReaderMod.InventorySpecial.TeamBlue",
            4 => "Mods.ScreenReaderMod.InventorySpecial.TeamYellow",
            5 => "Mods.ScreenReaderMod.InventorySpecial.TeamPink",
            _ => string.Empty,
        };

        return LocalizationHelper.GetTextOrFallback(key, fallbacks[teamIndex]);
    }

    private static string? DescribeDefenseCounter()
    {
        Player? player = Main.LocalPlayer;
        int defense = player?.statDefense ?? 0;
        string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.Defense", "Defense");
        string cleaned = TextSanitizer.Clean(label);
        return $"{cleaned} {defense}";
    }

    private static bool ShouldLogUnknown(bool hoverIsAir, string? location)
    {
        if (!hoverIsAir)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        return true;
    }

    private static void LogUnknownPoint(int point, bool hoverIsAir, string? location)
    {
        if (!LoggedUnknownPoints.Add(point))
        {
            return;
        }

        string state = Main.InGameUI?.CurrentState?.GetType().FullName ?? "<null>";
        bool usingGamepad = PlayerInput.UsingGamepadUI;
        bool inventoryOpen = Main.playerInventory;
        ScreenReaderMod.Instance?.Logger.Info(
            $"[InventoryNarration] Unknown UI link point {point} (hoverIsAir={hoverIsAir}, location='{location ?? string.Empty}', usingGamepad={usingGamepad}, inventory={inventoryOpen}, state={state})");
    }
}
