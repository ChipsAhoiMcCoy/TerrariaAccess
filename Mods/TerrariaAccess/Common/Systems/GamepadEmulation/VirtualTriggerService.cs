#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
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
    private static readonly bool InputDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));
    private static bool _wasMouseLeftTriggerActive;
    private static bool _wasMouseRightTriggerActive;
    private static readonly PropertyInfo? ModKeybindFullNameProperty =
        typeof(ModKeybind).GetProperty("FullName", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly InputMode[] RawKeybindModes =
    {
        InputMode.Keyboard,
        InputMode.KeyboardUI
    };

    /// <summary>
    /// Injects a virtual trigger from a ModKeybind into the game's trigger pack.
    /// Uses guarded trigger-state lookup and raw keyboard state detection for reliability.
    /// </summary>
    internal static void InjectFromKeybind(ModKeybind? keybind, string triggerName)
    {
        if (keybind is null)
        {
            return;
        }

        // Check ModKeybind first, then fall back to raw keyboard state detection
        // This ensures detection works even in gamepad UI mode
        bool isPressed = IsKeybindPressed(keybind);
        if (!isPressed)
        {
            return;
        }

        TriggersPack pack = PlayerInput.Triggers;
        InputMode sourceMode = ResolveSyntheticInputMode();
        if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
        {
            UpgradeAlreadyActiveTrigger(pack, triggerName, sourceMode);
            LogSyntheticTrigger(triggerName, ResolveKeybindSourceLabel(keybind), sourceMode, wasHeldLastFrame: true, alreadyActive: true);
            return;
        }

        bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
        SetTriggerState(pack, triggerName, sourceMode);
        if (!wasHeldLastFrame)
        {
            pack.JustPressed.KeyStatus[triggerName] = true;
            pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
        }

        LogSyntheticTrigger(triggerName, ResolveKeybindSourceLabel(keybind), sourceMode, wasHeldLastFrame);
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
        InputMode sourceMode = ResolveSyntheticInputMode();
        if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
        {
            UpgradeAlreadyActiveTrigger(pack, triggerName, sourceMode);
            LogSyntheticTrigger(triggerName, "direct-state", sourceMode, wasHeldLastFrame: true, alreadyActive: true);
            return;
        }

        bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
        SetTriggerState(pack, triggerName, sourceMode);
        if (!wasHeldLastFrame)
        {
            pack.JustPressed.KeyStatus[triggerName] = true;
            pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
        }

        LogSyntheticTrigger(triggerName, "direct-state", sourceMode, wasHeldLastFrame);
    }

    internal static void InjectFromKeyboardTrigger(string sourceTriggerName, string targetTriggerName)
    {
        if (!IsKeyboardTriggerPressedRaw(sourceTriggerName))
        {
            return;
        }

        TriggersPack pack = PlayerInput.Triggers;
        InputMode sourceMode = ResolveSyntheticInputMode();
        if (pack.Current.KeyStatus.TryGetValue(targetTriggerName, out bool alreadyActive) && alreadyActive)
        {
            UpgradeAlreadyActiveTrigger(pack, targetTriggerName, sourceMode);
            LogSyntheticTrigger(
                targetTriggerName,
                ResolveKeyboardTriggerSourceLabel(sourceTriggerName),
                sourceMode,
                wasHeldLastFrame: true,
                alreadyActive: true);
            return;
        }

        bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(targetTriggerName, out bool wasHeld) && wasHeld;
        SetTriggerState(pack, targetTriggerName, sourceMode);
        if (!wasHeldLastFrame)
        {
            pack.JustPressed.KeyStatus[targetTriggerName] = true;
            pack.JustPressed.LatestInputMode[targetTriggerName] = sourceMode;
        }

        LogSyntheticTrigger(targetTriggerName, ResolveKeyboardTriggerSourceLabel(sourceTriggerName), sourceMode, wasHeldLastFrame);
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
            foreach (string keyName in GetAssignedKeysSafe(keybind))
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

    internal static bool IsKeybindBoundToKey(ModKeybind? keybind, Keys key)
    {
        if (keybind is null)
        {
            return false;
        }

        try
        {
            foreach (string keyName in GetAssignedKeysSafe(keybind))
            {
                if (Enum.TryParse(keyName, ignoreCase: true, out Keys assignedKey) &&
                    assignedKey == key)
                {
                    return true;
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
    /// Reads ModKeybind.Current defensively because tModLoader can throw when a
    /// registered keybind is missing from the current input dictionary.
    /// </summary>
    internal static bool IsKeybindCurrentlyPressed(ModKeybind? keybind)
    {
        return TryGetTriggerState(PlayerInput.Triggers.Current, keybind, out bool isPressed) && isPressed;
    }

    /// <summary>
    /// Checks if a ModKeybind's assigned keys are pressed using raw keyboard state.
    /// This is a fallback for when ModKeybind.Current doesn't work correctly in gamepad modes.
    /// </summary>
    internal static bool IsKeybindPressedRaw(ModKeybind? keybind)
    {
        try
        {
            KeyboardState kbState = Main.keyState;
            foreach (string keyName in GetAssignedKeysSafe(keybind))
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

    internal static bool IsKeybindPressed(ModKeybind? keybind)
    {
        return IsKeybindCurrentlyPressed(keybind) || IsKeybindPressedRaw(keybind);
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
            bool keybindPressed = IsKeybindPressed(interactKeybind);
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
            bool keybindPressed = IsKeybindPressed(selectKeybind);
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
            && IsKeybindPressed(selectKeybind);

        ModKeybind? interactKeybind = GamepadEmulationKeybinds.InventoryInteract;
        _wasMouseRightTriggerActive = interactKeybind is not null
            && IsKeybindPressed(interactKeybind);
    }

    private static void SetTriggerState(TriggersPack pack, string triggerName, InputMode sourceMode)
    {
        pack.Current.KeyStatus[triggerName] = true;
        pack.Current.LatestInputMode[triggerName] = sourceMode;
        pack.JustReleased.KeyStatus[triggerName] = false;
    }

    private static void UpgradeAlreadyActiveTrigger(TriggersPack pack, string triggerName, InputMode sourceMode)
    {
        pack.Current.LatestInputMode[triggerName] = sourceMode;
        if (pack.JustPressed.KeyStatus.TryGetValue(triggerName, out bool justPressed) && justPressed)
        {
            pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
        }
    }

    private static InputMode ResolveSyntheticInputMode()
    {
        return PlayerInput.CurrentInputMode switch
        {
            InputMode.XBoxGamepad => InputMode.XBoxGamepad,
            InputMode.XBoxGamepadUI => InputMode.XBoxGamepadUI,
            _ => InputMode.Keyboard
        };
    }

    private static string ResolveKeybindSourceLabel(ModKeybind keybind)
    {
        string fullName = TryGetKeybindFullName(keybind, out string value)
            ? value
            : "unknown-keybind";
        string assignedKeys = string.Join("+", GetAssignedKeysSafe(keybind));
        return string.IsNullOrWhiteSpace(assignedKeys)
            ? fullName
            : $"{fullName}[{assignedKeys}]";
    }

    private static string ResolveKeyboardTriggerSourceLabel(string triggerName)
    {
        string assignedKeys = string.Join("+", GetAssignedKeysForTriggerSafe(triggerName));
        return string.IsNullOrWhiteSpace(assignedKeys)
            ? triggerName
            : $"{triggerName}[{assignedKeys}]";
    }

    private static void LogSyntheticTrigger(
        string triggerName,
        string source,
        InputMode sourceMode,
        bool wasHeldLastFrame,
        bool alreadyActive = false)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        string phase = alreadyActive ? "alreadyActive" : wasHeldLastFrame ? "held" : "justPressed";
        string message = $"[InputDebug] SyntheticTrigger: trigger={triggerName} source={source} " +
            $"sourceMode={sourceMode} currentMode={PlayerInput.CurrentInputMode} " +
            $"context={InputContextResolver.Current} phase={phase}";
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    private static List<string> GetAssignedKeysSafe(ModKeybind? keybind)
    {
        var assignedKeys = new List<string>();
        if (keybind is null ||
            PlayerInput.CurrentProfile is null ||
            !TryGetKeybindFullName(keybind, out string fullName))
        {
            return assignedKeys;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (InputMode mode in RawKeybindModes)
        {
            if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(mode, out KeyConfiguration? configuration) ||
                !configuration.KeyStatus.TryGetValue(fullName, out List<string>? keys) ||
                keys is null)
            {
                continue;
            }

            foreach (string keyName in keys)
            {
                if (!string.IsNullOrWhiteSpace(keyName) && seen.Add(keyName))
                {
                    assignedKeys.Add(keyName);
                }
            }
        }

        return assignedKeys;
    }

    private static bool IsKeyboardTriggerPressedRaw(string triggerName)
    {
        KeyboardState kbState = Main.keyState;
        foreach (string keyName in GetAssignedKeysForTriggerSafe(triggerName))
        {
            if (Enum.TryParse(keyName, ignoreCase: true, out Keys key) &&
                kbState.IsKeyDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetAssignedKeysForTriggerSafe(string triggerName)
    {
        var assignedKeys = new List<string>();
        if (PlayerInput.CurrentProfile is null || string.IsNullOrWhiteSpace(triggerName))
        {
            return assignedKeys;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (InputMode mode in RawKeybindModes)
        {
            if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(mode, out KeyConfiguration? configuration) ||
                !configuration.KeyStatus.TryGetValue(triggerName, out List<string>? keys) ||
                keys is null)
            {
                continue;
            }

            foreach (string keyName in keys)
            {
                if (!string.IsNullOrWhiteSpace(keyName) && seen.Add(keyName))
                {
                    assignedKeys.Add(keyName);
                }
            }
        }

        return assignedKeys;
    }

    private static bool TryGetTriggerState(TriggersSet triggers, ModKeybind? keybind, out bool isPressed)
    {
        isPressed = false;
        if (keybind is null || !TryGetKeybindFullName(keybind, out string fullName))
        {
            return false;
        }

        try
        {
            return triggers.KeyStatus.TryGetValue(fullName, out isPressed);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetKeybindFullName(ModKeybind keybind, out string fullName)
    {
        fullName = string.Empty;
        try
        {
            if (ModKeybindFullNameProperty?.GetValue(keybind) is string value &&
                !string.IsNullOrWhiteSpace(value))
            {
                fullName = value;
                return true;
            }
        }
        catch
        {
            // Ignore reflection failures
        }

        return false;
    }
}
