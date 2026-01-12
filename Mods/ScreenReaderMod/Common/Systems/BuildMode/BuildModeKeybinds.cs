#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.BuildMode;

internal static class BuildModeKeybinds
{
    internal static ModKeybind? Toggle { get; private set; }
    internal static ModKeybind? Place { get; private set; }

    /// <summary>
    /// When true, build mode only affects the outline (perimeter) of the selection.
    /// When false, build mode fills the entire selection.
    /// </summary>
    internal static bool OutlineModeEnabled { get; set; }

    internal static void EnsureInitialized(Mod mod)
    {
        if (Toggle is not null)
        {
            return;
        }

        Toggle = KeybindLoader.RegisterKeybind(mod, "BuildModeToggle", Keys.X);
        Place = KeybindLoader.RegisterKeybind(mod, "BuildModePlace", Keys.C);
    }

    internal static void Unload()
    {
        Toggle = null;
        Place = null;
        OutlineModeEnabled = false;
    }
}
