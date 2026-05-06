#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

internal static class SmartCursorStateController
{
    private const uint SmartCursorSyncSettleFrames = 12;

    private static bool _bindingWasPressed;
    private static bool _desiredEnabled;
    private static bool _desiredInitialized;
    private static bool _desiredSyncPending;
    private static uint _desiredSyncDeadline;
    private static bool _hasSessionDesiredState;
    private static bool _sessionDesiredEnabled;

    internal static bool DesiredEnabled => _desiredEnabled;
    internal static bool DesiredInitialized => _desiredInitialized;
    internal static bool DesiredSyncPending => _desiredSyncPending;
    internal static uint DesiredSyncDeadline => _desiredSyncDeadline;

    internal static void Reset()
    {
        _bindingWasPressed = false;
        _desiredInitialized = false;
        _desiredSyncPending = false;
        _desiredSyncDeadline = 0;
    }

    internal static void ClearSessionState()
    {
        Reset();
        _hasSessionDesiredState = false;
        _sessionDesiredEnabled = false;
    }

    internal static void Update(GamepadEmulationInputContext context, bool reservedForUi)
    {
        if (context == GamepadEmulationInputContext.KeyboardTextInput ||
            context == GamepadEmulationInputContext.GamepadUi ||
            context == GamepadEmulationInputContext.SuppressedByModalOrSpecialSystem ||
            reservedForUi)
        {
            _bindingWasPressed = false;
            return;
        }

        EnsureInitialized();

        if (context == GamepadEmulationInputContext.NativePhysicalGamepad)
        {
            _bindingWasPressed = false;
            return;
        }

        bool smartCursorPressed = IsSmartCursorBindingPressedRaw();
        ApplyStateFromBinding(smartCursorPressed);
        SuppressVanillaSmartCursorTriggerFromKeyboard(smartCursorPressed);

        if (DpadVirtualizationSystem.IsTemporarilySuppressingSmartCursor())
        {
            return;
        }

        ApplyWantedState(_desiredEnabled);
        if (_desiredSyncPending && WantedStateMatchesDesired())
        {
            _desiredSyncPending = false;
            _desiredSyncDeadline = 0;
        }
    }

    internal static void ApplyWantedState(bool enabled)
    {
        Main.SmartCursorWanted_Mouse = enabled;
        Main.SmartCursorWanted_GamePad = enabled;
    }

    internal static bool TryGetForcedState(out bool enabled)
    {
        if (!_desiredInitialized ||
            InputContextResolver.Current == GamepadEmulationInputContext.NativePhysicalGamepad)
        {
            enabled = false;
            return false;
        }

        enabled = _desiredEnabled;
        return true;
    }

    internal static bool GetEffectiveState()
    {
        if (TryGetForcedState(out bool enabled))
        {
            return enabled;
        }

        return Main.SmartCursorIsUsed;
    }

    private static void EnsureInitialized()
    {
        if (_desiredInitialized)
        {
            return;
        }

        _desiredEnabled = _hasSessionDesiredState
            ? _sessionDesiredEnabled
            : Main.SmartCursorWanted_Mouse || Main.SmartCursorIsUsed;
        _desiredInitialized = true;
        _desiredSyncPending = false;
        _desiredSyncDeadline = 0;
        ApplyWantedState(_desiredEnabled);
    }

    private static void ApplyStateFromBinding(bool smartCursorPressed)
    {
        bool previousDesiredState = _desiredEnabled;

        if (Main.cSmartCursorModeIsToggleAndNotHold)
        {
            if (smartCursorPressed && !_bindingWasPressed)
            {
                _desiredEnabled = !_desiredEnabled;
            }
        }
        else
        {
            _desiredEnabled = smartCursorPressed;
        }

        if (previousDesiredState != _desiredEnabled)
        {
            _hasSessionDesiredState = true;
            _sessionDesiredEnabled = _desiredEnabled;
            _desiredSyncPending = true;
            _desiredSyncDeadline = Main.GameUpdateCount + SmartCursorSyncSettleFrames;
        }

        _bindingWasPressed = smartCursorPressed;
    }

    private static bool WantedStateMatchesDesired()
    {
        return Main.SmartCursorWanted_Mouse == _desiredEnabled &&
               Main.SmartCursorWanted_GamePad == _desiredEnabled;
    }

    internal static bool IsSmartCursorBindingPressedRaw()
    {
        return IsVanillaTriggerPressedRaw("SmartCursor");
    }

    private static void SuppressVanillaSmartCursorTriggerFromKeyboard(bool smartCursorPressed)
    {
        TriggersPack triggerPack = PlayerInput.Triggers;
        bool latestInputWasKeyboard =
            triggerPack.Current.LatestInputMode.TryGetValue(TriggerNames.SmartCursor, out InputMode mode) &&
            (mode == InputMode.Keyboard || mode == InputMode.KeyboardUI);

        if (!smartCursorPressed && !latestInputWasKeyboard)
        {
            return;
        }

        triggerPack.Current.KeyStatus[TriggerNames.SmartCursor] = false;
        triggerPack.JustPressed.KeyStatus[TriggerNames.SmartCursor] = false;
        triggerPack.JustReleased.KeyStatus[TriggerNames.SmartCursor] = false;
    }

    private static bool IsVanillaTriggerPressedRaw(string triggerName)
    {
        PlayerInputProfile? profile = PlayerInput.CurrentProfile;
        if (profile is null)
        {
            return false;
        }

        return IsVanillaTriggerPressedRaw(profile, InputMode.Keyboard, triggerName) ||
               IsVanillaTriggerPressedRaw(profile, InputMode.KeyboardUI, triggerName);
    }

    private static bool IsVanillaTriggerPressedRaw(PlayerInputProfile profile, InputMode mode, string triggerName)
    {
        if (!profile.InputModes.TryGetValue(mode, out KeyConfiguration? config))
        {
            return false;
        }

        if (!config.KeyStatus.TryGetValue(triggerName, out List<string>? assignments) || assignments is null)
        {
            return false;
        }

        KeyboardState keyState = Main.keyState;
        foreach (string assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment))
            {
                continue;
            }

            if (TryIsMouseBindingPressed(assignment))
            {
                return true;
            }

            if (Enum.TryParse(assignment, ignoreCase: true, out Keys key) && keyState.IsKeyDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsMouseBindingPressed(string assignment)
    {
        return assignment switch
        {
            "Mouse1" => Main.mouseLeft,
            "Mouse2" => Main.mouseRight,
            _ => false,
        };
    }
}
