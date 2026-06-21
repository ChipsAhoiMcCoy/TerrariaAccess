#nullable enable

using System;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Legacy compatibility surface for call sites that still ask whether keyboard
/// gamepad emulation is available. The feature is intentionally always on.
/// </summary>
internal static class GamepadEmulationState
{
    internal static bool Enabled => true;

    internal static event Action<bool>? StateChanged
    {
        add { }
        remove { }
    }

    internal static void Toggle()
    {
    }

    internal static void SetEnabled(bool enabled)
    {
    }

    internal static void SetEnabledSilent(bool enabled)
    {
    }
}
