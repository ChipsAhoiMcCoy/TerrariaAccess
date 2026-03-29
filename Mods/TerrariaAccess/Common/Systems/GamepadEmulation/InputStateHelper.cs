#nullable enable
using TerrariaAccess.Common.Systems.ModBrowser;
using Terraria;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Provides shared input state checks for the gamepad emulation subsystem.
/// </summary>
internal static class InputStateHelper
{
    internal static bool IsSignEditingActive()
    {
        Player? player = Main.myPlayer >= 0 ? Main.player[Main.myPlayer] : null;
        return Main.editSign && player is not null && player.sign != -1;
    }

    /// <summary>
    /// Returns true if text input is currently active (chat, sign editing, etc.).
    /// When true, gamepad emulation should be disabled to allow normal typing.
    /// </summary>
    internal static bool IsTextInputActive()
    {
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return true;
        }

        if (Main.CurrentInputTextTakerOverride is not null)
        {
            return true;
        }

        // When in search mode in mod browser menus, treat as text input active
        if (SearchModeManager.IsRelevantMenu && SearchModeManager.IsSearchModeActive)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when text entry is active but the UI should remain in gamepad mode.
    /// This lets sign editing keep vanilla gamepad button navigation while the keyboard
    /// continues to feed text into the sign editor.
    /// </summary>
    internal static bool ShouldPreserveGamepadUiDuringTextInput()
    {
        return IsSignEditingActive();
    }

    /// <summary>
    /// Returns true if the game should be in gamepad UI mode for proper navigation.
    /// </summary>
    internal static bool NeedsGamepadUiMode()
    {
        if (!GamepadEmulationState.Enabled && !IsKeyboardInputMode())
        {
            return false;
        }

        if (Main.gameMenu)
        {
            return true;
        }

        Player? player = Main.myPlayer >= 0 ? Main.player[Main.myPlayer] : null;
        if (player is null)
        {
            return false;
        }

        if (Main.playerInventory
            || Main.ingameOptionsWindow
            || IsFancyUiActive()
            || Main.InGuideCraftMenu
            || Main.InReforgeMenu
            || Main.CreativeMenu.Enabled
            || Main.hairWindow
            || Main.clothesWindow)
        {
            return true;
        }

        if (player.talkNPC != -1 || player.sign != -1)
        {
            return true;
        }

        if (player.chest != -1 || Main.npcShop != 0)
        {
            return true;
        }

        if (player.tileEntityAnchor.InUse)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if a fancy UI state (MenuUI or InGameUI) is currently visible.
    /// </summary>
    internal static bool IsFancyUiActive()
    {
        if (Main.MenuUI?.IsVisible ?? false)
        {
            return true;
        }

        return Main.InGameUI?.IsVisible ?? false;
    }

    /// <summary>
    /// Returns true if the current input mode is keyboard or keyboard UI.
    /// </summary>
    internal static bool IsKeyboardInputMode()
    {
        InputMode mode = PlayerInput.CurrentInputMode;
        return mode == InputMode.Keyboard || mode == InputMode.KeyboardUI;
    }

    /// <summary>
    /// Returns true if gamepad emulation should emulate gamepad input.
    /// Returns false if text input is active or feature is disabled.
    /// </summary>
    internal static bool ShouldEmulateGamepad()
    {
        if (!GamepadEmulationState.Enabled)
        {
            return false;
        }

        if (IsTextInputActive())
        {
            return false;
        }

        return true;
    }
}
