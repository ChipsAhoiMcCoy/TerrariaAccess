#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

internal static class ControllerParityKeybinds
{
    internal static ModKeybind? InventorySelect { get; private set; }
    internal static ModKeybind? InventoryInteract { get; private set; }
    internal static ModKeybind? InventorySectionNext { get; private set; }
    internal static ModKeybind? InventorySectionPrevious { get; private set; }
    internal static ModKeybind? InventoryQuickUse { get; private set; }
    internal static ModKeybind? LockOn { get; private set; }
    internal static ModKeybind? RightStickUp { get; private set; }
    internal static ModKeybind? RightStickDown { get; private set; }
    internal static ModKeybind? RightStickLeft { get; private set; }
    internal static ModKeybind? RightStickRight { get; private set; }
    internal static ModKeybind? SmartSelect { get; private set; }
    internal static ModKeybind? ArrowUp { get; private set; }
    internal static ModKeybind? ArrowDown { get; private set; }
    internal static ModKeybind? ArrowLeft { get; private set; }
    internal static ModKeybind? ArrowRight { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        InventorySelect = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySelect", Keys.I);
        InventoryInteract = KeybindLoader.RegisterKeybind(mod, "ControllerInventoryInteract", Keys.P);
        InventorySectionNext = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionNext", Keys.E);
        InventorySectionPrevious = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionPrevious", Keys.Q);
        InventoryQuickUse = KeybindLoader.RegisterKeybind(mod, "ControllerInventoryQuickUse", Keys.J);
        LockOn = KeybindLoader.RegisterKeybind(mod, "ControllerLockOn", Keys.Tab);
        RightStickUp = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickUp", Keys.O);
        RightStickDown = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickDown", Keys.L);
        RightStickLeft = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickLeft", Keys.K);
        RightStickRight = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickRight", Keys.OemSemicolon);
        SmartSelect = KeybindLoader.RegisterKeybind(mod, "SmartSelect", Keys.F);
        ArrowUp = KeybindLoader.RegisterKeybind(mod, "ArrowUp", Keys.Up);
        ArrowDown = KeybindLoader.RegisterKeybind(mod, "ArrowDown", Keys.Down);
        ArrowLeft = KeybindLoader.RegisterKeybind(mod, "ArrowLeft", Keys.Left);
        ArrowRight = KeybindLoader.RegisterKeybind(mod, "ArrowRight", Keys.Right);
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        InventorySelect = null;
        InventoryInteract = null;
        InventorySectionNext = null;
        InventorySectionPrevious = null;
        InventoryQuickUse = null;
        LockOn = null;
        RightStickUp = null;
        RightStickDown = null;
        RightStickLeft = null;
        RightStickRight = null;
        SmartSelect = null;
        ArrowUp = null;
        ArrowDown = null;
        ArrowLeft = null;
        ArrowRight = null;
    }
}
