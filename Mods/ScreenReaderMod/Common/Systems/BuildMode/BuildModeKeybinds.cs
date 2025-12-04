#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.BuildMode;

internal static class BuildModeKeybinds
{
    internal static ModKeybind? Toggle { get; private set; }
    internal static ModKeybind? Place { get; private set; }

    internal static void EnsureInitialized(Mod mod)
    {
        if (Toggle is not null)
        {
            return;
        }

        Toggle = KeybindLoader.RegisterKeybind(mod, "BuildModeToggle", Keys.None);
        Place = KeybindLoader.RegisterKeybind(mod, "BuildModePlace", Keys.A);
    }

    internal static void Unload()
    {
        Toggle = null;
        Place = null;
    }
}
