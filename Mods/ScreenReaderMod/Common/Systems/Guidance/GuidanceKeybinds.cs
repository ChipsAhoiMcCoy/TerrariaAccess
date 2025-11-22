#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.Guidance;

internal static class GuidanceKeybinds
{
    internal static ModKeybind? CategoryNext { get; private set; }
    internal static ModKeybind? CategoryPrevious { get; private set; }
    internal static ModKeybind? EntryNext { get; private set; }
    internal static ModKeybind? EntryPrevious { get; private set; }
    internal static ModKeybind? Create { get; private set; }
    internal static ModKeybind? Delete { get; private set; }
    internal static ModKeybind? Teleport { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        CategoryNext = KeybindLoader.RegisterKeybind(mod, "GuidanceCategoryNext", Keys.OemCloseBrackets);
        CategoryPrevious = KeybindLoader.RegisterKeybind(mod, "GuidanceCategoryPrevious", Keys.OemOpenBrackets);
        EntryNext = KeybindLoader.RegisterKeybind(mod, "GuidanceEntryNext", Keys.PageDown);
        EntryPrevious = KeybindLoader.RegisterKeybind(mod, "GuidanceEntryPrevious", Keys.PageUp);
        Create = KeybindLoader.RegisterKeybind(mod, "WaypointCreate", Keys.OemPipe);
        Delete = KeybindLoader.RegisterKeybind(mod, "WaypointDelete", Keys.Delete);
        Teleport = KeybindLoader.RegisterKeybind(mod, "GuidanceTeleport", Keys.P);

        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        CategoryNext = null;
        CategoryPrevious = null;
        EntryNext = null;
        EntryPrevious = null;
        Create = null;
        Delete = null;
        Teleport = null;
    }
}
