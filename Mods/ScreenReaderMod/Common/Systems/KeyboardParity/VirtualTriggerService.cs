#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Handles virtual trigger injection from keyboard inputs into Terraria's trigger system.
/// </summary>
internal static class VirtualTriggerService
{
    private static bool _wasMouseRightTriggerActive;

    /// <summary>
    /// Injects a virtual trigger from a ModKeybind into the game's trigger pack.
    /// Uses both ModKeybind.Current and raw keyboard state detection for reliability.
    /// </summary>
    internal static void InjectFromKeybind(ModKeybind? keybind, string triggerName)
    {
        if (keybind is null)
        {
            return;
        }

        // Check ModKeybind first, then fall back to raw keyboard state detection
        // This ensures detection works even in gamepad UI mode
        bool isPressed = keybind.Current || IsKeybindPressedRaw(keybind);
        if (!isPressed)
        {
            return;
        }

        TriggersPack pack = PlayerInput.Triggers;
        if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
        {
            return;
        }

        // Use gamepad UI mode when in UI context so the game properly processes the trigger
        InputMode sourceMode = PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI
            ? InputMode.XBoxGamepadUI
            : InputMode.Keyboard;

        bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
        SetTriggerState(pack, triggerName, sourceMode);
        if (!wasHeldLastFrame)
        {
            pack.JustPressed.KeyStatus[triggerName] = true;
            pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
        }
    }

    /// <summary>
    /// Injects a virtual trigger from a boolean state into the game's trigger pack.
    /// </summary>
    internal static void InjectFromState(string triggerName, bool isHeld)
    {
        if (!isHeld)
        {
            return;
        }

        TriggersPack pack = PlayerInput.Triggers;
        if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
        {
            return;
        }

        // Use gamepad UI mode when in UI context so the game properly processes the trigger
        InputMode sourceMode = PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI
            ? InputMode.XBoxGamepadUI
            : InputMode.Keyboard;

        bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
        SetTriggerState(pack, triggerName, sourceMode);
        if (!wasHeldLastFrame)
        {
            pack.JustPressed.KeyStatus[triggerName] = true;
            pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
        }
    }

    /// <summary>
    /// Checks if a ModKeybind's assigned keys are pressed using raw keyboard state.
    /// This is a fallback for when ModKeybind.Current doesn't work correctly in gamepad modes.
    /// </summary>
    internal static bool IsKeybindPressedRaw(ModKeybind keybind)
    {
        try
        {
            List<string> assignedKeys = keybind.GetAssignedKeys();
            if (assignedKeys is null || assignedKeys.Count == 0)
            {
                return false;
            }

            KeyboardState kbState = Main.keyState;
            foreach (string keyName in assignedKeys)
            {
                if (Enum.TryParse<Keys>(keyName, ignoreCase: true, out Keys key))
                {
                    if (kbState.IsKeyDown(key))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors in fallback detection
        }

        return false;
    }

    /// <summary>
    /// When the MouseRight trigger is active (from keyboard Interact key), ensure Main.mouseRight
    /// and Main.mouseRightRelease are set so ItemSlot.RightClick can process the action.
    /// This is needed because forced gamepad UI mode may interfere with normal keyboard trigger processing.
    /// </summary>
    internal static void ApplyMouseRightFromTrigger()
    {
        // Check both the trigger and the keybind directly as a fallback
        bool triggerActive = PlayerInput.Triggers.Current.MouseRight;

        // Also check the InventoryInteract keybind directly in case trigger injection timing is off
        ModKeybind? interactKeybind = ControllerParityKeybinds.InventoryInteract;
        if (interactKeybind is not null)
        {
            bool keybindPressed = interactKeybind.Current || IsKeybindPressedRaw(interactKeybind);
            triggerActive = triggerActive || keybindPressed;
        }

        bool justPressed = triggerActive && !_wasMouseRightTriggerActive;
        _wasMouseRightTriggerActive = triggerActive;

        if (justPressed)
        {
            // Set the mouse flags so ItemSlot.RightClick can process the action
            Main.mouseRight = true;
            Main.mouseRightRelease = true;
        }
        else if (triggerActive)
        {
            // Continue holding mouseRight for held actions (like stack splitting)
            Main.mouseRight = true;
        }
    }

    /// <summary>
    /// Resets the mouse right trigger tracking state.
    /// Call this when the feature is disabled or during cleanup.
    /// </summary>
    internal static void ResetState()
    {
        _wasMouseRightTriggerActive = false;
    }

    private static void SetTriggerState(TriggersPack pack, string triggerName, InputMode sourceMode)
    {
        pack.Current.KeyStatus[triggerName] = true;
        pack.Current.LatestInputMode[triggerName] = sourceMode;
        pack.JustReleased.KeyStatus[triggerName] = false;
    }
}
