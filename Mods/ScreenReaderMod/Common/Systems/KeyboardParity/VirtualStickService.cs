#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Handles virtual analog stick injection from keyboard inputs.
/// Converts WASD keys to left stick and right-stick keybinds to right stick input.
/// </summary>
internal static class VirtualStickService
{
    /// <summary>
    /// Injects virtual stick input from keyboard keys into the gamepad input system.
    /// Should be called during the GamePadInput IL hook.
    /// </summary>
    internal static void InjectFromKeyboard()
    {
        if (!KeyboardParityFeatureState.Enabled || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        KeyboardState state = Main.keyState;
        bool movementOverride = TryReadStick(state, Keys.W, Keys.S, Keys.A, Keys.D, out Vector2 movement);

        // When Smart Cursor is off, right stick keys are used for cursor nudge instead.
        bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
        bool aimOverride = false;
        Vector2 aim = Vector2.Zero;
        if (smartCursorActive)
        {
            aimOverride = TryReadStick(
                ControllerParityKeybinds.RightStickUp,
                ControllerParityKeybinds.RightStickDown,
                ControllerParityKeybinds.RightStickLeft,
                ControllerParityKeybinds.RightStickRight,
                out aim);
        }

        if (movementOverride)
        {
            ApplyStickInversion(ref movement,
                PlayerInput.CurrentProfile?.LeftThumbstickInvertX == true,
                PlayerInput.CurrentProfile?.LeftThumbstickInvertY == true);
            PlayerInput.GamepadThumbstickLeft = movement;
        }

        if (aimOverride)
        {
            ApplyStickInversion(ref aim,
                PlayerInput.CurrentProfile?.RightThumbstickInvertX == true,
                PlayerInput.CurrentProfile?.RightThumbstickInvertY == true);
            PlayerInput.GamepadThumbstickRight = aim;
        }

        if (movementOverride || aimOverride || state.IsKeyDown(Keys.Space) || Main.mouseLeft || Main.mouseRight)
        {
            PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Gamepad);
        }
    }

    /// <summary>
    /// Reads stick input from keyboard keys.
    /// </summary>
    internal static bool TryReadStick(KeyboardState state, Keys up, Keys down, Keys left, Keys right, out Vector2 result)
    {
        float x = 0f;
        float y = 0f;

        if (state.IsKeyDown(up))
        {
            y -= 1f;
        }

        if (state.IsKeyDown(down))
        {
            y += 1f;
        }

        if (state.IsKeyDown(left))
        {
            x -= 1f;
        }

        if (state.IsKeyDown(right))
        {
            x += 1f;
        }

        result = new Vector2(x, y);
        if (result == Vector2.Zero)
        {
            return false;
        }

        result.Normalize();
        return true;
    }

    /// <summary>
    /// Reads stick input from ModKeybinds.
    /// </summary>
    internal static bool TryReadStick(ModKeybind? up, ModKeybind? down, ModKeybind? left, ModKeybind? right, out Vector2 result)
    {
        float x = 0f;
        float y = 0f;

        if (up?.Current == true)
        {
            y -= 1f;
        }

        if (down?.Current == true)
        {
            y += 1f;
        }

        if (left?.Current == true)
        {
            x -= 1f;
        }

        if (right?.Current == true)
        {
            x += 1f;
        }

        result = new Vector2(x, y);
        if (result == Vector2.Zero)
        {
            return false;
        }

        result.Normalize();
        return true;
    }

    /// <summary>
    /// Applies stick axis inversion based on player profile settings.
    /// </summary>
    internal static void ApplyStickInversion(ref Vector2 stick, bool invertX, bool invertY)
    {
        if (invertX)
        {
            stick.X *= -1f;
        }

        if (invertY)
        {
            stick.Y *= -1f;
        }
    }

    /// <summary>
    /// Resets the virtual stick state when the feature is disabled.
    /// </summary>
    internal static void ResetState()
    {
        PlayerInput.GamepadThumbstickLeft = Vector2.Zero;
        PlayerInput.GamepadThumbstickRight = Vector2.Zero;
    }
}
