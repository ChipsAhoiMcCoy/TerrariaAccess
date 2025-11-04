#nullable enable
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Waypoints;
using Terraria.ModLoader;

namespace ScreenReaderMod;

public class ScreenReaderMod : Mod
{
    public static ScreenReaderMod? Instance { get; private set; }

    public override void Load()
    {
        Instance = this;
        ScreenReaderService.Initialize();
        WaypointKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        WaypointKeybinds.Unload();
        ScreenReaderService.Unload();
        Instance = null;
    }
}
