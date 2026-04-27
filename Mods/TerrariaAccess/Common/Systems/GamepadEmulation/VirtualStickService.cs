#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Systems.FirstLetterNavigation;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Handles virtual analog stick injection from keyboard inputs.
/// Converts WASD keys to left stick and right-stick keybinds to right stick input.
/// </summary>
internal static class VirtualStickService
{
    private static uint _lastAnalogStickFrame = uint.MaxValue;

    /// <summary>
    /// Returns true if analog stick virtualization was active this frame.
    /// Used by cursor clamping systems to detect analog input mode.
    /// </summary>
    internal static bool WasAnalogStickActiveThisFrame()
    {
        uint elapsedFrames = Main.GameUpdateCount - _lastAnalogStickFrame;
        return elapsedFrames <= 1;
    }

    internal static bool AreUnlockedCursorArrowKeysHeld()
    {
        return IsKeybindPressed(GamepadEmulationKeybinds.ArrowUp)
            || IsKeybindPressed(GamepadEmulationKeybinds.ArrowDown)
            || IsKeybindPressed(GamepadEmulationKeybinds.ArrowLeft)
            || IsKeybindPressed(GamepadEmulationKeybinds.ArrowRight)
            || Main.keyState.IsKeyDown(Keys.Up)
            || Main.keyState.IsKeyDown(Keys.Down)
            || Main.keyState.IsKeyDown(Keys.Left)
            || Main.keyState.IsKeyDown(Keys.Right);
    }

    internal static bool TryReadUnlockedCursorArrowStick(out Vector2 result)
    {
        KeyboardState state = Main.keyState;
        bool hasInput = TryReadStick(
            GamepadEmulationKeybinds.ArrowUp,
            GamepadEmulationKeybinds.ArrowDown,
            GamepadEmulationKeybinds.ArrowLeft,
            GamepadEmulationKeybinds.ArrowRight,
            out result);

        if (!hasInput)
        {
            hasInput = TryReadStick(state, Keys.Up, Keys.Down, Keys.Left, Keys.Right, out result);
        }

        return hasInput;
    }

    internal static void MarkAnalogStickActiveThisFrame()
    {
        _lastAnalogStickFrame = Main.GameUpdateCount;
    }

    /// <summary>
    /// Injects virtual stick input from keyboard keys into the gamepad input system.
    /// Should be called during the GamePadInput IL hook.
    /// </summary>
    internal static void InjectFromKeyboard()
    {
        if (!GamepadEmulationState.Enabled || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        KeyboardState state = Main.keyState;
        bool smartCursorActive = GetEffectiveSmartCursorState();
        bool inMenuContext = Main.gameMenu || InputStateHelper.IsFancyUiActive();

        // Suppress WASD movement input when first letter navigation is active in inventory.
        // This prevents letter keys from being interpreted as both navigation AND item search.
        bool suppressWasdMovement = Main.playerInventory && FirstLetterNavigationManager.IsEnabled;
        Vector2 movement = Vector2.Zero;
        bool allowMovementStickOverride = inMenuContext;
        bool movementOverride = allowMovementStickOverride &&
            !suppressWasdMovement &&
            TryReadStick(state, Keys.W, Keys.S, Keys.A, Keys.D, out movement);

        // When Smart Cursor is off, right stick keys (OKLS) are used for cursor nudge instead.
        // Arrow keys act as virtual analog only in unlocked cursor mode.
        // In menu contexts, OKLS should always act as right stick for scrolling.
        // Suppress right stick letter keys (O, K, L) when first letter navigation is active,
        // so those keys are reserved for item searching instead of injecting stick input.
        bool suppressRightStickLetterKeys = Main.playerInventory && FirstLetterNavigationManager.IsEnabled;
        bool aimOverride = false;
        Vector2 aim = Vector2.Zero;
        if ((smartCursorActive || inMenuContext) && !suppressRightStickLetterKeys)
        {
            // OKLS keys act as analog stick when Smart Cursor is on OR in menu context
            aimOverride = TryReadStick(
                GamepadEmulationKeybinds.RightStickUp,
                GamepadEmulationKeybinds.RightStickDown,
                GamepadEmulationKeybinds.RightStickLeft,
                GamepadEmulationKeybinds.RightStickRight,
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
            MarkAnalogStickActiveThisFrame();
            ApplyStickInversion(ref aim,
                PlayerInput.CurrentProfile?.RightThumbstickInvertX == true,
                PlayerInput.CurrentProfile?.RightThumbstickInvertY == true);
            PlayerInput.GamepadThumbstickRight = aim;
            MirrorJourneySliderInputToLeftStick(aim);
        }

        if (movementOverride || aimOverride || state.IsKeyDown(Keys.Space) || Main.mouseLeft || Main.mouseRight)
        {
            bool inUiContext = Main.playerInventory || Main.gameMenu || InputStateHelper.IsFancyUiActive();
            PlayerInput.SettingsForUI.SetCursorMode(inUiContext ? CursorMode.Gamepad : CursorMode.Mouse);
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
    /// Uses raw keyboard state reading which works in both gameplay and menu contexts.
    /// </summary>
    internal static bool TryReadStick(ModKeybind? up, ModKeybind? down, ModKeybind? left, ModKeybind? right, out Vector2 result)
    {
        float x = 0f;
        float y = 0f;

        if (IsKeybindPressed(up))
        {
            y -= 1f;
        }

        if (IsKeybindPressed(down))
        {
            y += 1f;
        }

        if (IsKeybindPressed(left))
        {
            x -= 1f;
        }

        if (IsKeybindPressed(right))
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
    /// Checks if a keybind is pressed using raw keyboard state.
    /// This works in menu contexts where ModKeybind.Current may not be processed.
    /// </summary>
    private static bool IsKeybindPressed(ModKeybind? keybind)
    {
        return VirtualTriggerService.IsKeybindPressed(keybind);
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

    private static void MirrorJourneySliderInputToLeftStick(Vector2 aim)
    {
        if (!Main.CreativeMenu.Enabled || Math.Abs(aim.Y) <= 0f)
        {
            return;
        }

        Vector2 leftStick = PlayerInput.GamepadThumbstickLeft;
        leftStick.Y = aim.Y;
        PlayerInput.GamepadThumbstickLeft = leftStick;
    }

    /// <summary>
    /// Resets the virtual stick state when the feature is disabled.
    /// </summary>
    internal static void ResetState()
    {
        _lastAnalogStickFrame = uint.MaxValue;
        PlayerInput.GamepadThumbstickLeft = Vector2.Zero;
        PlayerInput.GamepadThumbstickRight = Vector2.Zero;
    }

    private static bool GetEffectiveSmartCursorState()
    {
        return GamepadEmulationSystem.GetEffectiveSmartCursorState(ignoreTemporarySuppression: true);
    }
}
