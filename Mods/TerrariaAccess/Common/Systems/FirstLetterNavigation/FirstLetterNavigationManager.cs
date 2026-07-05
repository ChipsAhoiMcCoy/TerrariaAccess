#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Systems.ModBrowser;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Localization;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI.Gamepad;

// Note: Spatial audio feedback is handled automatically by InventoryNarrator
// when focus changes via UILinkPointNavigator.ChangePoint().

namespace TerrariaAccess.Common.Systems.FirstLetterNavigation;

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

        if (SearchModeManager.IsRelevantMenu)
        {
            DisableForSearchMode();
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
        // Note: Navigation trigger suppression is handled by SuppressNavigationTriggers()
        // which runs in PostUpdateInput, BEFORE UILinkPointNavigator.Update()
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
            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayOpen();
            string announcement = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.FirstLetterNavigation.Enabled",
                "First Letter Navigation Enabled. Press a letter to find items.");
            ScreenReaderService.Announce(announcement, force: true);
        }
        else
        {
            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayClose();
            string announcement = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.FirstLetterNavigation.Disabled",
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
                if (ShouldReserveLetterKeyForShopSmartSelect(key))
                {
                    continue;
                }

                char letter = (char)('A' + i);
                LogDebug($"Letter key '{letter}' just pressed, CurrentPoint before={UILinkPointNavigator.CurrentPoint}");
                ProcessLetter(player, letter);
                LogDebug($"After ProcessLetter, CurrentPoint={UILinkPointNavigator.CurrentPoint}");
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
            LogDebug($"Cycling to next match for '{upperLetter}': index={_currentMatchIndex}/{_currentMatches.Count}");
            NavigateToCurrentMatch();
            return;
        }

        // Different letter - rebuild matches
        _currentLetter = upperLetter;
        _currentMatchIndex = 0;
        _currentMatches = CollectMatches(player, upperLetter);

        LogDebug($"Collected {_currentMatches.Count} matches for '{upperLetter}'");
        foreach (var match in _currentMatches)
        {
            LogDebug($"  Match: '{match.Name}' at linkPoint={match.LinkPointId} ({match.Location})");
        }

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
    /// Uses the base item name (without prefix) for matching so that e.g. "Legendary Copper Shortsword"
    /// matches on 'C' (for Copper Shortsword), not 'L' (for Legendary).
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

        // Use base item name (without reforge prefix) for letter matching
        string baseName = TextSanitizer.Clean(item.Name);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = TextSanitizer.Clean(Lang.GetItemNameValue(item.type));
        }
        string matchTarget = !string.IsNullOrEmpty(baseName) ? baseName : name;

        if (char.ToUpperInvariant(matchTarget[0]) == upperLetter)
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
        // Validate this is an actual inventory/storage slot link point
        if (!IsValidSlotLinkPoint(linkPointId))
        {
            LogDebug($"Rejected invalid slot link point: {linkPointId}");
            return;
        }

        if (!UILinkPointNavigator.Points.ContainsKey(linkPointId))
        {
            LogDebug($"Link point {linkPointId} does not exist in navigator");
            return;
        }

        int beforePoint = UILinkPointNavigator.CurrentPoint;
        UILinkPointNavigator.ChangePoint(linkPointId);
        int afterPoint = UILinkPointNavigator.CurrentPoint;

        LogDebug($"Navigation: requested={linkPointId}, before={beforePoint}, after={afterPoint}");
    }

    /// <summary>
    /// Checks if the link point ID corresponds to a valid inventory or storage slot.
    /// This prevents accidentally navigating to UI buttons like Sort Inventory.
    /// </summary>
    private static bool IsValidSlotLinkPoint(int linkPointId)
    {
        // Inventory slots: 0-57 (hotbar 0-9, main 10-49, coins 50-53, ammo 54-57)
        if (linkPointId >= 0 && linkPointId <= 57)
        {
            return true;
        }

        // Chest/storage slots: 400-439
        if (linkPointId >= 400 && linkPointId <= 439)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Announces the match. Currently a no-op since InventoryNarrator handles
    /// announcing the item name and location when focus changes.
    /// </summary>
    private static void AnnounceMatch(ItemMatch match, int index, int total)
    {
        // The InventoryNarrator handles announcing the item name and location
        // when focus changes via UILinkPointNavigator.ChangePoint().
        // No additional announcement needed here.
    }

    /// <summary>
    /// Announces that no items were found starting with the specified letter.
    /// </summary>
    private static void AnnounceNoMatches(char letter)
    {
        string template = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.FirstLetterNavigation.NoMatches",
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

    /// <summary>
    /// Suppresses navigation triggers that would interfere with first letter navigation.
    /// Must be called from PostUpdateInput, BEFORE UILinkPointNavigator.Update() runs.
    /// </summary>
    internal static void SuppressNavigationTriggers()
    {
        if (!_isEnabled || !Main.playerInventory)
        {
            return;
        }

        if (SearchModeManager.IsRelevantMenu)
        {
            DisableForSearchMode();
            return;
        }

        // Skip when in text input mode
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return;
        }

        // Force XBoxGamepadUI mode so UILinkPointNavigator processes our ChangePoint calls
        // and doesn't fall back to keyboard-based navigation
        PlayerInput.CurrentInputMode = InputMode.XBoxGamepadUI;

        var current = PlayerInput.Triggers.Current;
        var justPressed = PlayerInput.Triggers.JustPressed;

        // Block navigation triggers that GetNavigatorDirections() reads
        // These are what actually move the selection cursor in XBoxGamepadUI mode
        ClearTrigger(current, justPressed, "Up");
        ClearTrigger(current, justPressed, "Down");
        ClearTrigger(current, justPressed, "Left");
        ClearTrigger(current, justPressed, "Right");
        ClearTrigger(current, justPressed, "MenuUp");
        ClearTrigger(current, justPressed, "MenuDown");
        ClearTrigger(current, justPressed, "MenuLeft");
        ClearTrigger(current, justPressed, "MenuRight");

        // Block all triggers that could cause unintended actions while typing letters.
        // Users expect letter keys to ONLY search for items, not trigger game actions.

        // Throw (T) - would junk/drop items
        ClearTrigger(current, justPressed, "Throw");

        // SmartSelect (F) - drops held items in inventory
        ClearTrigger(current, justPressed, "SmartSelect");

        // Grapple (E) - triggers crafting in crafting UI
        ClearTrigger(current, justPressed, "Grapple");

        // Inventory (I) - would close the inventory
        ClearTrigger(current, justPressed, "Inventory");

        // Quick actions that would use consumables or trigger abilities
        ClearTrigger(current, justPressed, "QuickHeal");   // H - uses healing item
        ClearTrigger(current, justPressed, "QuickMana");   // J - uses mana item
        ClearTrigger(current, justPressed, "QuickBuff");   // B - uses buff items
        ClearTrigger(current, justPressed, "QuickMount");  // R - mounts/dismounts

        // Map (M) - would open fullscreen map
        ClearTrigger(current, justPressed, "MapFull");

        // Creative menu toggle
        ClearTrigger(current, justPressed, "ToggleCreativeMenu");

        // Block mouse action triggers that would interact with items while searching.
        // This is a safety net against any injection paths outside the gamepad emulation system.
        ClearTrigger(current, justPressed, "MouseLeft");
        ClearTrigger(current, justPressed, "MouseRight");

        // Clear Main.mouseLeft/mouseRight flags directly as these may be set
        // by code paths outside the trigger system (e.g., ApplyMouseLeftFromTrigger).
        Main.mouseLeft = false;
        Main.mouseLeftRelease = false;
        Main.mouseRight = false;
        Main.mouseRightRelease = false;

        // Also clear the thumbstick to prevent any residual analog navigation
        PlayerInput.GamepadThumbstickLeft = Vector2.Zero;
    }

    private static bool ShouldReserveLetterKeyForShopSmartSelect(Keys key)
    {
        return Main.npcShop != 0 &&
               VirtualTriggerService.IsKeybindPressedRaw(GamepadEmulationKeybinds.SmartSelect) &&
               VirtualTriggerService.IsKeybindBoundToKey(GamepadEmulationKeybinds.SmartSelect, key);
    }

    /// <summary>
    /// Clears a trigger from both Current and JustPressed trigger sets.
    /// </summary>
    private static void ClearTrigger(TriggersSet current, TriggersSet justPressed, string triggerName)
    {
        current.KeyStatus[triggerName] = false;
        justPressed.KeyStatus[triggerName] = false;
    }

    private static void DisableForSearchMode()
    {
        if (!_isEnabled)
        {
            return;
        }

        _isEnabled = false;
        ClearMatches();
    }

    /// <summary>
    /// Logs debug information to the mod logger.
    /// </summary>
    private static void LogDebug(string message)
    {
        TerrariaAccess.Instance?.Logger.Info($"[FirstLetterNav] {message}");
    }
}
