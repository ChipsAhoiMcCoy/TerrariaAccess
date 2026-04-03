#nullable enable
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

internal static class StatusCheckKeybinds
{
    internal static ModKeybind? StatusCheck { get; private set; }
    internal static ModKeybind? InfoAccessoryCheck { get; private set; }

    private static bool _initialized;

    internal static void EnsureInitialized(Mod mod)
    {
        if (_initialized || Main.dedServ)
        {
            return;
        }

        StatusCheck = KeybindLoader.RegisterKeybind(mod, "StatusCheck", "Back");
        InfoAccessoryCheck = KeybindLoader.RegisterKeybind(mod, "InfoAccessoryCheck", "OemQuotes");
        _initialized = true;
    }

    internal static void Unload()
    {
        _initialized = false;
        InfoAccessoryCheck = null;
        StatusCheck = null;
    }
}
