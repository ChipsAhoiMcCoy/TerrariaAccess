#nullable enable
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems;

internal static class StatusCheckKeybinds
{
    internal static ModKeybind? StatusCheck { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        StatusCheck = KeybindLoader.RegisterKeybind(mod, "StatusCheck", "Back");
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        StatusCheck = null;
    }
}
