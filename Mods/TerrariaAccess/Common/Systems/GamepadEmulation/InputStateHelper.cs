#nullable enable

using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Compatibility facade for older call sites. New input policy belongs in
/// <see cref="InputContextResolver"/>.
/// </summary>
internal static class InputStateHelper
{
    internal static bool IsSignEditingActive()
    {
        return SignInputModeSystem.IsTextEntryActive;
    }

    internal static bool IsTextInputActive()
    {
        return InputContextResolver.IsKeyboardTextInputActive();
    }

    internal static bool ShouldPreserveGamepadUiDuringTextInput()
    {
        return false;
    }

    internal static bool NeedsGamepadUiMode()
    {
        return InputContextResolver.NeedsGamepadUiMode();
    }

    internal static bool IsFancyUiActive()
    {
        return InputContextResolver.IsFancyUiActive();
    }

    internal static bool IsKeyboardInputMode()
    {
        InputMode mode = PlayerInput.CurrentInputMode;
        return mode == InputMode.Keyboard || mode == InputMode.KeyboardUI;
    }

    internal static bool ShouldEmulateGamepad()
    {
        GamepadEmulationInputContext context = InputContextResolver.Current;
        return context is GamepadEmulationInputContext.WorldGameplay
            or GamepadEmulationInputContext.GamepadUi;
    }

    internal static bool ShouldUseNativeGamepadWorldInput(bool needsUiMode = false)
    {
        return InputContextResolver.ShouldUseNativePhysicalGamepadWorldInput(needsUiMode);
    }

    internal static bool IsPhysicalGamepadConnected()
    {
        return InputContextResolver.IsPhysicalGamepadConnected();
    }
}
