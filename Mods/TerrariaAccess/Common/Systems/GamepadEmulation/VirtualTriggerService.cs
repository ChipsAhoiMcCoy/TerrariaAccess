#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Handles virtual trigger injection from keyboard inputs into Terraria's trigger system.
/// </summary>
internal static class VirtualTriggerService
{
    private static bool _wasMouseLeftTriggerActive;
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
    /// Checks if a ModKeybind is bound to any letter key (A-Z).
    /// Used to determine if first letter navigation should suppress this keybind.
    /// </summary>
    internal static bool IsKeybindBoundToLetterKey(ModKeybind? keybind)
    {
        if (keybind is null)
        {
            return false;
        }

        try
        {
            List<string> assignedKeys = keybind.GetAssignedKeys();
            if (assignedKeys is null || assignedKeys.Count == 0)
            {
                return false;
            }

            foreach (string keyName in assignedKeys)
            {
                if (Enum.TryParse<Keys>(keyName, ignoreCase: true, out Keys key))
                {
                    if (key >= Keys.A && key <= Keys.Z)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return false;
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
        ModKeybind? interactKeybind = GamepadEmulationKeybinds.InventoryInteract;
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
    /// When the MouseLeft trigger is active (from keyboard InventorySelect key), ensure Main.mouseLeft
    /// and Main.mouseLeftRelease are set so ItemSlot.LeftClick can process the action.
    /// This is needed because forced gamepad UI mode may interfere with normal keyboard trigger processing.
    /// </summary>
    internal static void ApplyMouseLeftFromTrigger()
    {
        // Check both the trigger and the keybind directly as a fallback
        bool triggerActive = PlayerInput.Triggers.Current.MouseLeft;

        // Also check the InventorySelect keybind directly in case trigger injection timing is off
        ModKeybind? selectKeybind = GamepadEmulationKeybinds.InventorySelect;
        if (selectKeybind is not null)
        {
            bool keybindPressed = selectKeybind.Current || IsKeybindPressedRaw(selectKeybind);
            triggerActive = triggerActive || keybindPressed;
        }

        bool justPressed = triggerActive && !_wasMouseLeftTriggerActive;
        _wasMouseLeftTriggerActive = triggerActive;

        if (justPressed)
        {
            // Set the mouse flags so ItemSlot.LeftClick can process the action
            Main.mouseLeft = true;
            Main.mouseLeftRelease = true;
        }
        else if (triggerActive)
        {
            // Continue holding mouseLeft for held actions
            Main.mouseLeft = true;
        }
    }

    /// <summary>
    /// Resets the mouse trigger tracking state.
    /// Call this when the feature is disabled or during cleanup.
    /// </summary>
    internal static void ResetState()
    {
        _wasMouseLeftTriggerActive = false;
        _wasMouseRightTriggerActive = false;
    }

    /// <summary>
    /// Samples the current keybind state to keep tracking in sync without injecting mouse flags.
    /// Call this when trigger injection is temporarily suppressed (e.g., first-letter navigation)
    /// so that resuming injection doesn't produce a stale justPressed edge.
    /// </summary>
    internal static void UpdateTrackingOnly()
    {
        ModKeybind? selectKeybind = GamepadEmulationKeybinds.InventorySelect;
        _wasMouseLeftTriggerActive = selectKeybind is not null
            && (selectKeybind.Current || IsKeybindPressedRaw(selectKeybind));

        ModKeybind? interactKeybind = GamepadEmulationKeybinds.InventoryInteract;
        _wasMouseRightTriggerActive = interactKeybind is not null
            && (interactKeybind.Current || IsKeybindPressedRaw(interactKeybind));
    }

    private static void SetTriggerState(TriggersPack pack, string triggerName, InputMode sourceMode)
    {
        pack.Current.KeyStatus[triggerName] = true;
        pack.Current.LatestInputMode[triggerName] = sourceMode;
        pack.JustReleased.KeyStatus[triggerName] = false;
    }
}
