#nullable enable
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Guidance;
using ScreenReaderMod.Common.Systems;
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
        SpeechInterruptKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        SpeechInterruptKeybinds.Unload();
        GuidanceKeybinds.Unload();
        WorldAnnouncementService.Unload();
        ScreenReaderService.Unload();
        Instance = null;
    }
}
