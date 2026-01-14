#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.InGameNarration;

/// <summary>
/// Shared utilities for UI slot navigation that can be used across different narrators.
/// Provides common methods for resolving link point positions, slot contexts, and
/// navigation state detection.
/// </summary>
internal static class SlotNavigationHelper
{
    // Link point ranges for different UI areas
    internal const int HotbarStart = 0;
    internal const int HotbarEnd = 10;
    internal const int MainInventoryStart = 10;
    internal const int MainInventoryEnd = 50;
    internal const int CoinsStart = 50;
    internal const int CoinsEnd = 54;
    internal const int AmmoStart = 54;
    internal const int AmmoEnd = 58;
    internal const int ChestStart = 400;
    internal const int ChestEnd = 440;
    internal const int ChestButtonStart = 500;
    internal const int ChestButtonEnd = 506;
    internal const int CraftingGridStart = 700;
    internal const int CraftingGridEnd = 1500;
    internal const int CraftingListStart = 1500;
    internal const int CraftingListEnd = 2000;

    /// <summary>
    /// Determines if the current link point is within the inventory slot range.
    /// </summary>
    internal static bool IsInventoryLinkPoint(int point)
    {
        return (point >= HotbarStart && point < HotbarEnd) ||
               (point >= MainInventoryStart && point < MainInventoryEnd) ||
               (point >= CoinsStart && point < CoinsEnd) ||
               (point >= AmmoStart && point < AmmoEnd);
    }

    /// <summary>
    /// Determines if the current link point is within the chest/storage slot range.
    /// </summary>
    internal static bool IsChestLinkPoint(int point)
    {
        return point >= ChestStart && point < ChestEnd;
    }

    /// <summary>
    /// Determines if the current link point is within the crafting grid range.
    /// </summary>
    internal static bool IsCraftingGridLinkPoint(int point)
    {
        return point >= CraftingGridStart && point < CraftingGridEnd;
    }

    /// <summary>
    /// Determines if the current link point is within the crafting list range.
    /// </summary>
    internal static bool IsCraftingListLinkPoint(int point)
    {
        return point >= CraftingListStart && point < CraftingListEnd;
    }

    /// <summary>
    /// Gets the current UI link point when using gamepad navigation.
    /// Returns -1 if not using gamepad UI.
    /// </summary>
    internal static int GetCurrentLinkPoint()
    {
        return PlayerInput.UsingGamepadUI ? UILinkPointNavigator.CurrentPoint : -1;
    }

    /// <summary>
    /// Attempts to resolve the inventory slot index from a link point ID.
    /// </summary>
    /// <param name="point">The link point ID.</param>
    /// <param name="slot">The resolved slot index.</param>
    /// <param name="context">The resolved ItemSlot context.</param>
    /// <returns>True if the link point maps to a valid inventory slot.</returns>
    internal static bool TryResolveInventorySlot(int point, out int slot, out int context)
    {
        slot = -1;
        context = -1;

        if (point >= HotbarStart && point < HotbarEnd)
        {
            slot = point;
            context = ItemSlot.Context.InventoryItem;
            return true;
        }

        if (point >= MainInventoryStart && point < MainInventoryEnd)
        {
            slot = point;
            context = ItemSlot.Context.InventoryItem;
            return true;
        }

        if (point >= CoinsStart && point < CoinsEnd)
        {
            slot = point;
            context = ItemSlot.Context.InventoryCoin;
            return true;
        }

        if (point >= AmmoStart && point < AmmoEnd)
        {
            slot = point;
            context = ItemSlot.Context.InventoryAmmo;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to resolve the chest slot index from a link point ID.
    /// </summary>
    /// <param name="point">The link point ID.</param>
    /// <param name="slot">The resolved slot index within the chest.</param>
    /// <returns>True if the link point maps to a valid chest slot.</returns>
    internal static bool TryResolveChestSlot(int point, out int slot)
    {
        slot = -1;

        if (point >= ChestStart && point < ChestEnd)
        {
            slot = point - ChestStart;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to get the Item array for the current player's open chest.
    /// Handles both world chests and player banks (piggy bank, safe, etc.).
    /// </summary>
    /// <param name="player">The player to check.</param>
    /// <param name="items">The item array if a chest is open.</param>
    /// <returns>True if a chest is open and items are available.</returns>
    internal static bool TryGetOpenChestItems(Player player, out Item[]? items)
    {
        items = null;

        if (player is null)
        {
            return false;
        }

        int chestIndex = player.chest;
        if (chestIndex == -1)
        {
            return false;
        }

        items = chestIndex switch
        {
            >= 0 when chestIndex < Main.chest.Length => Main.chest[chestIndex]?.item,
            -2 => player.bank.item,
            -3 => player.bank2.item,
            -4 => player.bank3.item,
            -5 => player.bank4.item,
            _ => null,
        };

        return items is not null;
    }

    /// <summary>
    /// Resolves navigation direction from keyboard/gamepad input.
    /// </summary>
    internal static NavigationDirection GetNavigationDirection()
    {
        bool left = PlayerInput.Triggers.JustPressed.Left ||
                    PlayerInput.Triggers.JustPressed.DpadMouseSnap1 ||
                    PlayerInput.Triggers.JustPressed.HotbarMinus;

        bool right = PlayerInput.Triggers.JustPressed.Right ||
                     PlayerInput.Triggers.JustPressed.DpadMouseSnap2 ||
                     PlayerInput.Triggers.JustPressed.HotbarPlus;

        bool up = PlayerInput.Triggers.JustPressed.Up ||
                  PlayerInput.Triggers.JustPressed.DpadMouseSnap3;

        bool down = PlayerInput.Triggers.JustPressed.Down ||
                    PlayerInput.Triggers.JustPressed.DpadMouseSnap4;

        if (left) return NavigationDirection.Left;
        if (right) return NavigationDirection.Right;
        if (up) return NavigationDirection.Up;
        if (down) return NavigationDirection.Down;

        return NavigationDirection.None;
    }

    internal enum NavigationDirection
    {
        None,
        Left,
        Right,
        Up,
        Down,
    }
}
