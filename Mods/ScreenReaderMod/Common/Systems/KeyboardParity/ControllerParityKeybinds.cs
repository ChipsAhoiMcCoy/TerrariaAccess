#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

internal static class ControllerParityKeybinds
{
    internal static ModKeybind? InventorySmartSelect { get; private set; }
    internal static ModKeybind? InventorySectionNext { get; private set; }
    internal static ModKeybind? InventorySectionPrevious { get; private set; }
    internal static ModKeybind? InventoryQuickUse { get; private set; }
    internal static ModKeybind? InventoryHousingQuery { get; private set; }
    internal static ModKeybind? LockOn { get; private set; }
    internal static ModKeybind? RightStickUp { get; private set; }
    internal static ModKeybind? RightStickDown { get; private set; }
    internal static ModKeybind? RightStickLeft { get; private set; }
    internal static ModKeybind? RightStickRight { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        InventorySmartSelect = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySmartSelect", Keys.F);
        InventorySectionNext = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionNext", Keys.E);
        InventorySectionPrevious = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionPrevious", Keys.Q);
        InventoryQuickUse = KeybindLoader.RegisterKeybind(mod, "ControllerInventoryQuickUse", Keys.J);
        InventoryHousingQuery = KeybindLoader.RegisterKeybind(mod, "ControllerInventoryHousingQuery", Keys.G);
        LockOn = KeybindLoader.RegisterKeybind(mod, "ControllerLockOn", Keys.Tab);
        RightStickUp = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickUp", Keys.O);
        RightStickDown = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickDown", Keys.L);
        RightStickLeft = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickLeft", Keys.K);
        RightStickRight = KeybindLoader.RegisterKeybind(mod, "ControllerRightStickRight", Keys.OemSemicolon);
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        InventorySmartSelect = null;
        InventorySectionNext = null;
        InventorySectionPrevious = null;
        InventoryQuickUse = null;
        InventoryHousingQuery = null;
        LockOn = null;
        RightStickUp = null;
        RightStickDown = null;
        RightStickLeft = null;
        RightStickRight = null;
    }
}
