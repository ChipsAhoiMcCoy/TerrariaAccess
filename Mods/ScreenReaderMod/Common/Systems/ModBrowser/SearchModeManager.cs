#nullable enable
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems.ModBrowser;

/// <summary>
/// Manages the search/navigation mode state for mod browser menus.
/// When in navigation mode, keyboard input is used for list navigation.
/// When in search mode, keyboard input goes to the search text box.
/// </summary>
internal static class SearchModeManager
{
    private static bool _isSearchModeActive;
    private static bool _tabWasPressed;
    private static bool _enterWasPressed;
    private static bool _wasInRelevantMenu;
    private static bool _shouldFocusFirstMod;

    /// <summary>
    /// Gets whether search mode is currently active.
    /// When false (navigation mode), keyboard input should be used for navigation.
    /// When true (search mode), keyboard input should go to the search text box.
    /// </summary>
    internal static bool IsSearchModeActive => _isSearchModeActive;

    /// <summary>
    /// Checks if focus should be set to the first mod (after exiting search via Enter).
    /// Calling this method consumes the flag (resets it to false).
    /// </summary>
    internal static bool ConsumeFocusFirstModRequest()
    {
        if (_shouldFocusFirstMod)
        {
            _shouldFocusFirstMod = false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets whether the current menu is one where search mode management applies.
    /// Returns true when in UIModBrowser or UIMods menus.
    /// </summary>
    internal static bool IsRelevantMenu
    {
        get
        {
            if (!Main.gameMenu)
            {
                return false;
            }

            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null)
            {
                return false;
            }

            string? fullName = currentState.GetType().FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            return fullName == "Terraria.ModLoader.UI.ModBrowser.UIModBrowser" ||
                   fullName == "Terraria.ModLoader.UI.UIMods";
        }
    }

    /// <summary>
    /// Called each frame to handle mode toggling via Tab key.
    /// Should be called from the accessibility systems during menu processing.
    /// </summary>
    internal static void Update()
    {
        bool inRelevantMenu = IsRelevantMenu;

        // Reset mode when entering a relevant menu
        if (inRelevantMenu && !_wasInRelevantMenu)
        {
            _isSearchModeActive = false;
            _tabWasPressed = Main.keyState.IsKeyDown(Keys.Tab);
            ScreenReaderMod.Instance?.Logger.Info("[SearchMode] Entered relevant menu, starting in navigation mode");
        }

        // Reset mode when leaving a relevant menu
        if (!inRelevantMenu && _wasInRelevantMenu)
        {
            _isSearchModeActive = false;
            ScreenReaderMod.Instance?.Logger.Info("[SearchMode] Left relevant menu, reset to navigation mode");
        }

        _wasInRelevantMenu = inRelevantMenu;

        if (!inRelevantMenu)
        {
            return;
        }

        // Check for Tab key press to toggle mode
        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool tabJustPressed = tabPressed && !_tabWasPressed;
        _tabWasPressed = tabPressed;

        if (tabJustPressed)
        {
            Toggle();
        }

        // Check for Enter key press to exit search mode (like submitting in chat)
        if (_isSearchModeActive)
        {
            bool enterPressed = Main.keyState.IsKeyDown(Keys.Enter);
            bool enterJustPressed = enterPressed && !_enterWasPressed;
            _enterWasPressed = enterPressed;

            if (enterJustPressed)
            {
                _shouldFocusFirstMod = true;
                ExitSearchMode();
            }
        }
        else
        {
            _enterWasPressed = Main.keyState.IsKeyDown(Keys.Enter);
        }
    }

    /// <summary>
    /// Toggles between search mode and navigation mode.
    /// Only announces when entering search mode.
    /// </summary>
    internal static void Toggle()
    {
        _isSearchModeActive = !_isSearchModeActive;

        if (_isSearchModeActive)
        {
            // Play menu open sound and announce when entering search mode
            SoundEngine.PlaySound(SoundID.MenuOpen);
            string announcement = GetSearchModeAnnouncement();
            ScreenReaderService.Announce(announcement, force: true);
        }
        else
        {
            // Play menu close sound when exiting search mode via Tab
            SoundEngine.PlaySound(SoundID.MenuClose);
        }

        ScreenReaderMod.Instance?.Logger.Info($"[SearchMode] Toggled to {(_isSearchModeActive ? "search" : "navigation")} mode");
    }

    /// <summary>
    /// Explicitly sets navigation mode. Used when Escape is pressed in search mode.
    /// </summary>
    internal static void ExitSearchMode()
    {
        if (!_isSearchModeActive)
        {
            return;
        }

        _isSearchModeActive = false;
        SoundEngine.PlaySound(SoundID.MenuClose);
        // No announcement when exiting search mode

        ScreenReaderMod.Instance?.Logger.Info("[SearchMode] Exited search mode");
    }

    /// <summary>
    /// Resets the state. Called when unloading the mod.
    /// </summary>
    internal static void Reset()
    {
        _isSearchModeActive = false;
        _tabWasPressed = false;
        _enterWasPressed = false;
        _wasInRelevantMenu = false;
        _shouldFocusFirstMod = false;
    }

    /// <summary>
    /// Gets the announcement for navigation mode with helpful instructions.
    /// </summary>
    private static string GetNavigationModeAnnouncement()
    {
        return LocalizationHelper.GetTextOrFallback(
            "Mods.ScreenReaderMod.SearchMode.NavigationEnabled",
            "Navigation mode. Use arrow keys to browse mods, Enter to select. Press Tab to search.");
    }

    /// <summary>
    /// Gets the announcement for search mode with helpful instructions.
    /// </summary>
    private static string GetSearchModeAnnouncement()
    {
        return LocalizationHelper.GetTextOrFallback(
            "Mods.ScreenReaderMod.SearchMode.SearchEnabled",
            "Search mode. Type to filter mods. Press Tab to return to navigation.");
    }
}
