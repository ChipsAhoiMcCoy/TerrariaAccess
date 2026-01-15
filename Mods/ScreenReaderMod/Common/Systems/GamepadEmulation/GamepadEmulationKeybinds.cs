#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.GamepadEmulation;

internal static class GamepadEmulationKeybinds
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

        InventorySelect = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationInventorySelect", Keys.I);
        InventoryInteract = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationInventoryInteract", Keys.P);
        InventorySectionNext = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationSectionNext", Keys.E);
        InventorySectionPrevious = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationSectionPrevious", Keys.Q);
        InventoryQuickUse = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationQuickUse", Keys.J);
        LockOn = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationLockOn", Keys.Tab);
        RightStickUp = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationRightStickUp", Keys.O);
        RightStickDown = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationRightStickDown", Keys.L);
        RightStickLeft = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationRightStickLeft", Keys.K);
        RightStickRight = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationRightStickRight", Keys.OemSemicolon);
        SmartSelect = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationSmartSelect", Keys.F);
        ArrowUp = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationArrowUp", Keys.Up);
        ArrowDown = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationArrowDown", Keys.Down);
        ArrowLeft = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationArrowLeft", Keys.Left);
        ArrowRight = KeybindLoader.RegisterKeybind(mod, "GamepadEmulationArrowRight", Keys.Right);
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
