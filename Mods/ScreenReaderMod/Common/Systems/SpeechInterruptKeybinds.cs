#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems;

internal static class SpeechInterruptKeybinds
{
    internal static ModKeybind? Interrupt { get; private set; }

    internal static void EnsureInitialized(Mod mod)
    {
        if (Interrupt is not null)
        {
            return;
        }

        Interrupt = KeybindLoader.RegisterKeybind(mod, "SpeechInterrupt", Keys.F2);
    }

    internal static void Unload()
    {
        Interrupt = null;
    }
}
