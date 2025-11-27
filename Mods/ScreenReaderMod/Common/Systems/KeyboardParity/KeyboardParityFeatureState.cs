#nullable enable
using System;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

internal static class KeyboardParityFeatureState
{
    internal static bool Enabled { get; private set; }

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
}
