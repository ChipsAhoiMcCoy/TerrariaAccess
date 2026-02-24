#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.Audio;

internal static class CavitySonarKeybinds
{
    internal static ModKeybind? SonarScan { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        SonarScan = KeybindLoader.RegisterKeybind(mod, "CavitySonarScan", Keys.OemTilde);

        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        SonarScan = null;
    }
}
