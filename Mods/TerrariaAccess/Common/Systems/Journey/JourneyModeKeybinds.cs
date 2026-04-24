#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.Journey;

internal static class JourneyModeKeybinds
{
    internal static ModKeybind? ToggleMenu { get; private set; }
    internal static ModKeybind? PanelStateCheck { get; private set; }
    internal static ModKeybind? CyclePowerCategory { get; private set; }
    internal static ModKeybind? ReadSacrificeProgress { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        ToggleMenu = KeybindLoader.RegisterKeybind(mod, "JourneyToggleMenu", Keys.None);
        PanelStateCheck = KeybindLoader.RegisterKeybind(mod, "JourneyPanelStateCheck", Keys.None);
        CyclePowerCategory = KeybindLoader.RegisterKeybind(mod, "JourneyCyclePowerCategory", Keys.None);
        ReadSacrificeProgress = KeybindLoader.RegisterKeybind(mod, "JourneyReadSacrificeProgress", Keys.None);
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        ToggleMenu = null;
        PanelStateCheck = null;
        CyclePowerCategory = null;
        ReadSacrificeProgress = null;
    }
}
