#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.Waypoints;

internal static class WaypointKeybinds
{
    internal static ModKeybind? Next { get; private set; }
    internal static ModKeybind? Previous { get; private set; }
    internal static ModKeybind? Create { get; private set; }
    internal static ModKeybind? Delete { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        Next = KeybindLoader.RegisterKeybind(mod, "WaypointNext", Keys.OemCloseBrackets);
        Previous = KeybindLoader.RegisterKeybind(mod, "WaypointPrevious", Keys.OemOpenBrackets);
        Create = KeybindLoader.RegisterKeybind(mod, "WaypointCreate", Keys.OemPipe);
        Delete = KeybindLoader.RegisterKeybind(mod, "WaypointDelete", Keys.Delete);

        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        Next = null;
        Previous = null;
        Create = null;
        Delete = null;
    }
}
