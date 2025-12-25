#nullable enable
using System.IO;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Systems.BuildMode;
using ScreenReaderMod.Common.Systems.Guidance;
using ScreenReaderMod.Common.Systems.KeyboardParity;
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
        ControllerParityKeybinds.EnsureInitialized(this);
        SpeechInterruptKeybinds.EnsureInitialized(this);
        StatusCheckKeybinds.EnsureInitialized(this);
        BuildModeKeybinds.EnsureInitialized(this);
        KeyboardCursorNudgeKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        KeyboardCursorNudgeKeybinds.Unload();
        ControllerParityKeybinds.Unload();
        BuildModeKeybinds.Unload();
        StatusCheckKeybinds.Unload();
        SpeechInterruptKeybinds.Unload();
        GuidanceKeybinds.Unload();
        WorldAnnouncementService.Unload();
        ScreenReaderService.Unload();
        Instance = null;
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        GuidanceSystem.HandlePacket(reader, whoAmI);
    }
}
