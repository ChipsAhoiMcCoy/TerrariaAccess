#nullable enable
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.ModBrowser;

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
    /// Returns true when in UIModBrowser, UIMods, or UIAchievementsMenu.
    /// </summary>
    internal static bool IsRelevantMenu
    {
        get
        {
            // Check main menu UI
            if (Main.gameMenu)
            {
                object? currentState = Main.MenuUI?.CurrentState;
                if (currentState is not null)
                {
                    string? fullName = currentState.GetType().FullName;
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        if (fullName == "Terraria.ModLoader.UI.ModBrowser.UIModBrowser" ||
                            fullName == "Terraria.ModLoader.UI.UIMods" ||
                            fullName == "Terraria.GameContent.UI.States.UIAchievementsMenu" ||
                            fullName == "Terraria.GameContent.UI.States.UIBestiaryTest")
                        {
                            return true;
                        }
                    }
                }
            }

            // Check in-game fancy UI (for achievements/bestiary accessed via pause menu)
            if (!Main.gameMenu && Main.InGameUI?.CurrentState is not null)
            {
                string? fullName = Main.InGameUI.CurrentState.GetType().FullName;
                if (fullName == "Terraria.GameContent.UI.States.UIAchievementsMenu" ||
                    fullName == "Terraria.GameContent.UI.States.UIBestiaryTest")
                {
                    return true;
                }
            }

            return false;
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
            TerrariaAccess.Instance?.Logger.Info("[SearchMode] Entered relevant menu, starting in navigation mode");
        }

        // Reset mode when leaving a relevant menu
        if (!inRelevantMenu && _wasInRelevantMenu)
        {
            _isSearchModeActive = false;
            TerrariaAccess.Instance?.Logger.Info("[SearchMode] Left relevant menu, reset to navigation mode");
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
            // Play menu open sound and enqueue search mode text as a prefix
            // so it plays before the next announcement rather than getting interrupted
            SoundEngine.PlaySound(SoundID.MenuOpen);
            string announcement = GetSearchModeAnnouncement();
            ScreenReaderService.EnqueuePrefix(announcement);
        }
        else
        {
            // Play menu close sound when exiting search mode via Tab
            SoundEngine.PlaySound(SoundID.MenuClose);
        }

        TerrariaAccess.Instance?.Logger.Info($"[SearchMode] Toggled to {(_isSearchModeActive ? "search" : "navigation")} mode");
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

        TerrariaAccess.Instance?.Logger.Info("[SearchMode] Exited search mode");
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
            "Mods.TerrariaAccess.SearchMode.NavigationEnabled",
            "Navigation mode. Use arrow keys to browse, Enter to select. Press Tab to search");
    }

    /// <summary>
    /// Gets the announcement for search mode with helpful instructions.
    /// </summary>
    private static string GetSearchModeAnnouncement()
    {
        return LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.SearchMode.SearchEnabled",
            "Search mode. Type to filter. Press Tab to return to navigation");
    }
}
