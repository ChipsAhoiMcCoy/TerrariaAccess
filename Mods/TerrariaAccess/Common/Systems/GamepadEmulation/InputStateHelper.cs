#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Systems;
using TerrariaAccess.Common.Systems.ModBrowser;
using Terraria;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Provides shared input state checks for the gamepad emulation subsystem.
/// </summary>
internal static class InputStateHelper
{
    private const float GamepadStickDeadzone = 0.35f;
    private const float GamepadTriggerThreshold = 0.25f;
    private const uint NativeWorldGamepadLingerFrames = 20;
    private static uint _lastMeaningfulPhysicalGamepadInputFrame = uint.MaxValue;

    internal static bool IsSignEditingActive()
    {
        return SignInputModeSystem.IsTextEntryActive;
    }

    /// <summary>
    /// Returns true if text input is currently active (chat, sign editing, etc.).
    /// When true, gamepad emulation should be disabled to allow normal typing.
    /// </summary>
    internal static bool IsTextInputActive()
    {
        if (Main.drawingPlayerChat || Main.editChest)
        {
            return true;
        }

        if (Main.editSign)
        {
            return SignInputModeSystem.IsTextEntryActive;
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
    /// Sign text editing now deliberately drops back to keyboard mode; button navigation
    /// is handled by SignInputModeSystem and is not considered text input.
    /// </summary>
    internal static bool ShouldPreserveGamepadUiDuringTextInput()
    {
        return false;
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

        if (SignInputModeSystem.IsButtonNavigationActive)
        {
            return false;
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

    /// <summary>
    /// Returns true when a real controller is actively driving in-world gameplay.
    /// In this state, keyboard emulation should not force world input mode back to keyboard
    /// or re-virtualize the physical D-pad on top of vanilla controller handling.
    /// </summary>
    internal static bool ShouldUseNativeGamepadWorldInput(bool needsUiMode = false)
    {
        if (needsUiMode || Main.gameMenu || IsTextInputActive())
        {
            return false;
        }

        if (!TryGetPhysicalGamepadState(out GamePadState state))
        {
            return false;
        }

        if (HasMeaningfulPhysicalGamepadInput(state))
        {
            _lastMeaningfulPhysicalGamepadInputFrame = Main.GameUpdateCount;
            return true;
        }

        InputMode inputMode = PlayerInput.CurrentInputMode;
        bool gamepadInputModeActive = inputMode == InputMode.XBoxGamepad || inputMode == InputMode.XBoxGamepadUI;
        if (!gamepadInputModeActive || _lastMeaningfulPhysicalGamepadInputFrame == uint.MaxValue)
        {
            return false;
        }

        return Main.GameUpdateCount - _lastMeaningfulPhysicalGamepadInputFrame <= NativeWorldGamepadLingerFrames;
    }

    internal static bool IsPhysicalGamepadConnected()
    {
        return TryGetPhysicalGamepadState(out _);
    }

    private static bool TryGetPhysicalGamepadState(out GamePadState state)
    {
        try
        {
            state = GamePad.GetState(PlayerIndex.One);
            return state.IsConnected;
        }
        catch
        {
            state = default;
            return false;
        }
    }

    private static bool HasMeaningfulPhysicalGamepadInput(GamePadState state)
    {
        if (state.DPad.Up == ButtonState.Pressed
            || state.DPad.Right == ButtonState.Pressed
            || state.DPad.Down == ButtonState.Pressed
            || state.DPad.Left == ButtonState.Pressed)
        {
            return true;
        }

        if (state.Buttons.A == ButtonState.Pressed
            || state.Buttons.B == ButtonState.Pressed
            || state.Buttons.X == ButtonState.Pressed
            || state.Buttons.Y == ButtonState.Pressed
            || state.Buttons.LeftShoulder == ButtonState.Pressed
            || state.Buttons.RightShoulder == ButtonState.Pressed
            || state.Buttons.Start == ButtonState.Pressed
            || state.Buttons.Back == ButtonState.Pressed
            || state.Buttons.LeftStick == ButtonState.Pressed
            || state.Buttons.RightStick == ButtonState.Pressed)
        {
            return true;
        }

        if (state.Triggers.Left > GamepadTriggerThreshold || state.Triggers.Right > GamepadTriggerThreshold)
        {
            return true;
        }

        return Math.Abs(state.ThumbSticks.Left.X) > GamepadStickDeadzone
            || Math.Abs(state.ThumbSticks.Left.Y) > GamepadStickDeadzone
            || Math.Abs(state.ThumbSticks.Right.X) > GamepadStickDeadzone
            || Math.Abs(state.ThumbSticks.Right.Y) > GamepadStickDeadzone;
    }
}
