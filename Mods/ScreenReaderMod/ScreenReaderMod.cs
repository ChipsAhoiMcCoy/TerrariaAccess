#nullable enable
using ScreenReaderMod.Common.Services;
using Terraria.ModLoader;

namespace ScreenReaderMod;

public class ScreenReaderMod : Mod
{
    public static ScreenReaderMod? Instance { get; private set; }

    public override void Load()
    {
        Instance = this;
        ScreenReaderService.Initialize();
    }

    public override void Unload()
    {
        ScreenReaderService.Unload();
        Instance = null;
    }
}
