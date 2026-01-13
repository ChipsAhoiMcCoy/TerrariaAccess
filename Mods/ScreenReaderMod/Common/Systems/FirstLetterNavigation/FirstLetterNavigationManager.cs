#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI.Gamepad;

// Note: Spatial audio feedback is handled automatically by InventoryNarrator
// when focus changes via UILinkPointNavigator.ChangePoint().

namespace ScreenReaderMod.Common.Systems.FirstLetterNavigation;

/// <summary>
/// Manages first-letter navigation mode for inventory and storage.
/// When enabled, pressing a letter key (A-Z) cycles through items starting with that letter.
/// </summary>
internal static class FirstLetterNavigationManager
{
    private static bool _isEnabled;
    private static bool _tabWasPressed;
    private static char _currentLetter;
    private static int _currentMatchIndex;
    private static List<ItemMatch> _currentMatches = new();
    private static bool _wasInventoryOpen;
    private static readonly bool[] _letterKeyStates = new bool[26];

    /// <summary>
    /// Gets whether first-letter navigation mode is currently active.
    /// </summary>
    internal static bool IsEnabled => _isEnabled;

    /// <summary>
    /// Data structure for a matched item.
    /// </summary>
    private readonly record struct ItemMatch(
        string Name,
        int LinkPointId,
        Item Item,
        string Location
    );

    /// <summary>
    /// Called each frame to handle mode toggling and letter key processing.
    /// Should be called from InGameNarrationSystem when inventory is relevant.
    /// </summary>
    internal static void Update()
    {
        Player player = Main.LocalPlayer;
        if (player is null)
        {
            return;
        }

        bool inventoryOpen = IsInventoryOpen(player);

        // Reset when inventory closes
        if (!inventoryOpen && _wasInventoryOpen)
        {
            Reset();
        }

        _wasInventoryOpen = inventoryOpen;

        if (!inventoryOpen)
        {
            return;
        }

        // Skip when in text input mode
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return;
        }

        // Tab toggle detection
        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool tabJustPressed = tabPressed && !_tabWasPressed;
        _tabWasPressed = tabPressed;

        if (tabJustPressed)
        {
            Toggle();
            return;
        }

        // Process letter keys when enabled
        if (_isEnabled)
        {
            ProcessLetterKeys(player);
        }
    }

    /// <summary>
    /// Toggles first-letter navigation mode on or off.
    /// </summary>
    private static void Toggle()
    {
        _isEnabled = !_isEnabled;

        if (_isEnabled)
        {
            SoundEngine.PlaySound(SoundID.MenuOpen);
            string announcement = LocalizationHelper.GetTextOrFallback(
                "Mods.ScreenReaderMod.FirstLetterNavigation.Enabled",
                "First Letter Navigation Enabled. Press a letter to find items.");
            ScreenReaderService.Announce(announcement, force: true);
        }
        else
        {
            SoundEngine.PlaySound(SoundID.MenuClose);
            string announcement = LocalizationHelper.GetTextOrFallback(
                "Mods.ScreenReaderMod.FirstLetterNavigation.Disabled",
                "First Letter Navigation Disabled");
            ScreenReaderService.Announce(announcement, force: true);
            ClearMatches();
        }
    }

    /// <summary>
    /// Processes letter key presses (A-Z) to find and navigate to matching items.
    /// </summary>
    private static void ProcessLetterKeys(Player player)
    {
        for (int i = 0; i < 26; i++)
        {
            Keys key = Keys.A + i;
            bool pressed = Main.keyState.IsKeyDown(key);
            bool wasPressed = _letterKeyStates[i];
            _letterKeyStates[i] = pressed;

            if (pressed && !wasPressed)
            {
                char letter = (char)('A' + i);
                ProcessLetter(player, letter);
                break; // Only process one letter per frame
            }
        }
    }

    /// <summary>
    /// Processes a specific letter key press.
    /// </summary>
    private static void ProcessLetter(Player player, char letter)
    {
        char upperLetter = char.ToUpperInvariant(letter);

        // If same letter, cycle to next match
        if (upperLetter == _currentLetter && _currentMatches.Count > 0)
        {
            _currentMatchIndex = (_currentMatchIndex + 1) % _currentMatches.Count;
            NavigateToCurrentMatch();
            return;
        }

        // Different letter - rebuild matches
        _currentLetter = upperLetter;
        _currentMatchIndex = 0;
        _currentMatches = CollectMatches(player, upperLetter);

        if (_currentMatches.Count == 0)
        {
            AnnounceNoMatches(upperLetter);
            return;
        }

        NavigateToCurrentMatch();
    }

    /// <summary>
    /// Collects all items matching the specified letter from inventory and storage.
    /// </summary>
    private static List<ItemMatch> CollectMatches(Player player, char upperLetter)
    {
        var matches = new List<ItemMatch>();

        // Hotbar (slots 0-9)
        for (int i = 0; i < 10; i++)
        {
            TryAddMatch(matches, player.inventory[i], i, $"Hotbar slot {i + 1}", upperLetter);
        }

        // Main inventory (slots 10-49)
        for (int i = 10; i < 50; i++)
        {
            TryAddMatch(matches, player.inventory[i], i, $"Inventory slot {i - 9}", upperLetter);
        }

        // Coins (slots 50-53)
        for (int i = 50; i < 54; i++)
        {
            TryAddMatch(matches, player.inventory[i], i, $"Coin slot {i - 49}", upperLetter);
        }

        // Ammo (slots 54-57)
        for (int i = 54; i < 58; i++)
        {
            TryAddMatch(matches, player.inventory[i], i, $"Ammo slot {i - 53}", upperLetter);
        }

        // Storage container (if open)
        if (player.chest != -1)
        {
            Item[]? containerItems = GetContainerItems(player, player.chest);
            string containerName = SlotContextFormatter.DescribeContainer(player.chest);

            if (containerItems is not null)
            {
                for (int i = 0; i < containerItems.Length; i++)
                {
                    int linkPoint = 400 + i;
                    string location = $"{containerName} slot {i + 1}";
                    TryAddMatch(matches, containerItems[i], linkPoint, location, upperLetter);
                }
            }
        }

        // Sort alphabetically by name
        matches.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return matches;
    }

    /// <summary>
    /// Tries to add an item to the matches list if it starts with the specified letter.
    /// </summary>
    private static void TryAddMatch(List<ItemMatch> matches, Item item, int linkPointId, string location, char upperLetter)
    {
        if (item is null || item.IsAir)
        {
            return;
        }

        string name = NarrationTextFormatter.ComposeItemName(item);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (char.ToUpperInvariant(name[0]) == upperLetter)
        {
            matches.Add(new ItemMatch(name, linkPointId, item, location));
        }
    }

    /// <summary>
    /// Navigates to the current match and announces it.
    /// </summary>
    private static void NavigateToCurrentMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _currentMatches.Count)
        {
            return;
        }

        ItemMatch match = _currentMatches[_currentMatchIndex];

        // Move focus to the slot
        MoveFocusToSlot(match.LinkPointId);

        // Announce the match
        AnnounceMatch(match, _currentMatchIndex, _currentMatches.Count);
    }

    /// <summary>
    /// Moves the UI focus to the specified link point.
    /// The existing InventoryNarrator will handle spatial audio feedback.
    /// </summary>
    private static void MoveFocusToSlot(int linkPointId)
    {
        if (!UILinkPointNavigator.Points.ContainsKey(linkPointId))
        {
            return;
        }

        UILinkPointNavigator.ChangePoint(linkPointId);
    }

    /// <summary>
    /// Announces the match count when there are multiple matches.
    /// The item name and location are announced by InventoryNarrator when focus changes.
    /// </summary>
    private static void AnnounceMatch(ItemMatch match, int index, int total)
    {
        // Only announce the count when there are multiple matches.
        // The InventoryNarrator handles announcing the item name and location
        // when focus changes via UILinkPointNavigator.ChangePoint().
        if (total > 1)
        {
            string message = $"{index + 1} of {total}";
            ScreenReaderService.Announce(message, force: true);
        }
    }

    /// <summary>
    /// Announces that no items were found starting with the specified letter.
    /// </summary>
    private static void AnnounceNoMatches(char letter)
    {
        string template = LocalizationHelper.GetTextOrFallback(
            "Mods.ScreenReaderMod.FirstLetterNavigation.NoMatches",
            "No items starting with {0}");
        string message = string.Format(template, letter);
        ScreenReaderService.Announce(message, force: true);
    }

    /// <summary>
    /// Gets the items from a storage container.
    /// </summary>
    private static Item[]? GetContainerItems(Player player, int chestIndex)
    {
        if (chestIndex >= 0 && chestIndex < Main.chest.Length)
        {
            return Main.chest[chestIndex]?.item;
        }

        return chestIndex switch
        {
            -2 => player.bank.item,
            -3 => player.bank2.item,
            -4 => player.bank3.item,
            -5 => player.bank4.item,
            _ => null,
        };
    }

    /// <summary>
    /// Checks if the inventory UI is currently open.
    /// </summary>
    private static bool IsInventoryOpen(Player player)
    {
        return Main.playerInventory ||
               player.chest != -1 ||
               Main.npcShop != 0 ||
               Main.InGuideCraftMenu ||
               Main.InReforgeMenu;
    }

    /// <summary>
    /// Clears the current matches and resets letter state.
    /// </summary>
    private static void ClearMatches()
    {
        _currentMatches.Clear();
        _currentLetter = '\0';
        _currentMatchIndex = 0;
    }

    /// <summary>
    /// Resets all state. Called when inventory closes or mod unloads.
    /// </summary>
    internal static void Reset()
    {
        _isEnabled = false;
        _tabWasPressed = false;
        _wasInventoryOpen = false;
        ClearMatches();
        Array.Clear(_letterKeyStates, 0, _letterKeyStates.Length);
    }
}
