#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

internal static class EventProgressKeybinds
{
    internal static ModKeybind? EventProgressCheck { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        EventProgressCheck = KeybindLoader.RegisterKeybind(mod, "EventProgressCheck", Keys.G);

        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        EventProgressCheck = null;
    }
}
