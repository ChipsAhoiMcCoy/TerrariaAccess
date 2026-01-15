#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Services;

internal static class ContextualInputRouter
{
    internal static bool TryHandle(IEnumerable<ContextualHotkey> hotkeys, TriggersSet triggersSet)
    {
        bool handled = false;

        foreach (ContextualHotkey hotkey in hotkeys.OrderByDescending(static h => h.Priority))
        {
            if (!hotkey.Condition())
            {
                continue;
            }

            if (!hotkey.TryConsume(triggersSet))
            {
                continue;
            }

            hotkey.OnTriggered();
            handled = true;

            if (hotkey.Exclusive)
            {
                break;
            }
        }

        return handled;
    }
}

internal sealed record ContextualHotkey(
    string Name,
    Func<bool> Condition,
    IReadOnlyList<InputChord> Chords,
    Action OnTriggered,
    int Priority = 0,
    bool Exclusive = true)
{
    internal bool TryConsume(TriggersSet triggersSet)
    {
        foreach (InputChord chord in Chords)
        {
            if (chord.TryConsume(triggersSet))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record InputChord(string Name, Func<TriggersSet, bool> IsPressed, Action<TriggersSet>? OnConsume = null)
{
    internal bool TryConsume(TriggersSet triggersSet)
    {
        if (!IsPressed(triggersSet))
        {
            return false;
        }

        OnConsume?.Invoke(triggersSet);
        return true;
    }

    internal static InputChord FromKeybind(ModKeybind? keybind, string? name = null)
    {
        return new InputChord(
            name ?? keybind?.ToString() ?? "keybind",
            _ => keybind?.JustPressed ?? false);
    }

    internal static InputChord FromTrigger(string name, Func<TriggersSet, bool> pressed, Action<TriggersSet>? suppress = null)
    {
        return new InputChord(name, pressed, suppress);
    }
}
