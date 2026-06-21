#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Systems.ModBrowser;
using Terraria;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

internal static class InputContextResolver
{
    private const float GamepadStickDeadzone = 0.35f;
    private const float GamepadTriggerThreshold = 0.25f;
    private const uint NativeWorldGamepadLingerFrames = 20;

    private static uint _lastMeaningfulPhysicalGamepadInputFrame = uint.MaxValue;

    internal static GamepadEmulationInputContext Current => Resolve();

    internal static GamepadEmulationInputContext Resolve()
    {
        if (IsKeyboardTextInputActive())
        {
            return GamepadEmulationInputContext.KeyboardTextInput;
        }

        if (SignInputModeSystem.IsButtonNavigationActive)
        {
            return GamepadEmulationInputContext.SuppressedByModalOrSpecialSystem;
        }

        if (NeedsGamepadUiMode())
        {
            return GamepadEmulationInputContext.GamepadUi;
        }

        if (ShouldUseNativePhysicalGamepadWorldInput())
        {
            return GamepadEmulationInputContext.NativePhysicalGamepad;
        }

        if (IsWorldGameplayAvailable())
        {
            return GamepadEmulationInputContext.WorldGameplay;
        }

        return GamepadEmulationInputContext.SuppressedByModalOrSpecialSystem;
    }

    internal static bool IsKeyboardTextInputActive()
    {
        if (CharacterCreationNameInputSystem.IsNameEntryActive)
        {
            return true;
        }

        if (WorldCreationNameInputSystem.IsNameEntryActive)
        {
            return true;
        }

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

        return SearchModeManager.IsRelevantMenu && SearchModeManager.IsSearchModeActive;
    }

    internal static bool NeedsGamepadUiMode()
    {
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

        return player.tileEntityAnchor.InUse;
    }

    internal static bool IsFancyUiActive()
    {
        if (Main.MenuUI?.IsVisible ?? false)
        {
            return true;
        }

        return Main.InGameUI?.IsVisible ?? false;
    }

    internal static bool IsPhysicalGamepadConnected()
    {
        return TryGetPhysicalGamepadState(out _);
    }

    internal static bool ShouldUseNativePhysicalGamepadWorldInput(bool needsUiMode = false)
    {
        if (needsUiMode || Main.gameMenu || IsKeyboardTextInputActive())
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

    private static bool IsWorldGameplayAvailable()
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return false;
        }

        Player player = Main.LocalPlayer;
        return player is { active: true, dead: false, ghost: false };
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
