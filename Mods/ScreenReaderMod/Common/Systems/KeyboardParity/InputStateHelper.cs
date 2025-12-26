#nullable enable
using Terraria;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Provides shared input state checks for the keyboard parity subsystem.
/// </summary>
internal static class InputStateHelper
{
    /// <summary>
    /// Returns true if text input is currently active (chat, sign editing, etc.).
    /// When true, keyboard parity should be disabled to allow normal typing.
    /// </summary>
    internal static bool IsTextInputActive()
    {
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return true;
        }

        return Main.CurrentInputTextTakerOverride is not null;
    }

    /// <summary>
    /// Returns true if the game should be in gamepad UI mode for proper navigation.
    /// </summary>
    internal static bool NeedsGamepadUiMode()
    {
        if (!KeyboardParityFeatureState.Enabled && !IsKeyboardInputMode())
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
    /// Returns true if keyboard parity should emulate gamepad input.
    /// Returns false if text input is active or feature is disabled.
    /// </summary>
    internal static bool ShouldEmulateGamepad()
    {
        if (!KeyboardParityFeatureState.Enabled)
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
