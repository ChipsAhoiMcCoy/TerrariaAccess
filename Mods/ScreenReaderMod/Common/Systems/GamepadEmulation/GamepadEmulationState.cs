#nullable enable
using System;

namespace ScreenReaderMod.Common.Systems.GamepadEmulation;

internal static class GamepadEmulationState
{
    internal static bool Enabled { get; private set; }

    /// <summary>
    /// Fired when the state changes via user toggle. Not fired during initial load.
    /// </summary>
    internal static event Action<bool>? StateChanged;

    internal static void Toggle()
    {
        SetEnabled(!Enabled);
    }

    internal static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        StateChanged?.Invoke(enabled);
    }

    /// <summary>
    /// Sets the enabled state without firing the StateChanged event.
    /// Used during initial load to restore saved state silently.
    /// </summary>
    internal static void SetEnabledSilent(bool enabled)
    {
        Enabled = enabled;
    }
}
