#nullable enable
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Guidance;
using Terraria.ModLoader;

namespace ScreenReaderMod;

public class ScreenReaderMod : Mod
{
    public static ScreenReaderMod? Instance { get; private set; }

    public override void Load()
    {
        Instance = this;
        ScreenReaderService.Initialize();
        WorldAnnouncementService.Initialize();
        GuidanceKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        GuidanceKeybinds.Unload();
        WorldAnnouncementService.Unload();
        ScreenReaderService.Unload();
        Instance = null;
    }
}
