#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

internal static class KeyboardCursorNudgeKeybinds
{
    internal static ModKeybind? Up { get; private set; }
    internal static ModKeybind? Down { get; private set; }
    internal static ModKeybind? Left { get; private set; }
    internal static ModKeybind? Right { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        Up = KeybindLoader.RegisterKeybind(mod, "KeyboardCursorNudgeUp", Keys.Up);
        Down = KeybindLoader.RegisterKeybind(mod, "KeyboardCursorNudgeDown", Keys.Down);
        Left = KeybindLoader.RegisterKeybind(mod, "KeyboardCursorNudgeLeft", Keys.Left);
        Right = KeybindLoader.RegisterKeybind(mod, "KeyboardCursorNudgeRight", Keys.Right);
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        Up = null;
        Down = null;
        Left = null;
        Right = null;
    }
}
