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

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        InventorySmartSelect = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySmartSelect", Keys.None);
        InventorySectionNext = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionNext", Keys.None);
        InventorySectionPrevious = KeybindLoader.RegisterKeybind(mod, "ControllerInventorySectionPrevious", Keys.None);
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        InventorySmartSelect = null;
        InventorySectionNext = null;
        InventorySectionPrevious = null;
    }
}
