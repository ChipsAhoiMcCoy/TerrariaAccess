#nullable enable
using System.IO;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Systems.BuildMode;
using ScreenReaderMod.Common.Systems.Guidance;
using ScreenReaderMod.Common.Systems.GamepadEmulation;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod;

public class ScreenReaderMod : Mod
{
    public static ScreenReaderMod? Instance { get; private set; }

    public override void Load()
    {
        Instance = this;

        // Safety guard: skip client-only initialization on dedicated servers
        // (with side = Client this shouldn't happen, but guard defensively)
        if (Main.dedServ)
        {
            return;
        }

        ScreenReaderService.Initialize();
        WorldAnnouncementService.Initialize();
        UiTickSoundPlayer.Initialize();
        GuidanceKeybinds.EnsureInitialized(this);
        GamepadEmulationKeybinds.EnsureInitialized(this);
        SpeechInterruptKeybinds.EnsureInitialized(this);
        StatusCheckKeybinds.EnsureInitialized(this);
        BuildModeKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        GamepadEmulationKeybinds.Unload();
        BuildModeKeybinds.Unload();
        StatusCheckKeybinds.Unload();
        SpeechInterruptKeybinds.Unload();
        GuidanceKeybinds.Unload();
        UiTickSoundPlayer.Dispose();
        WorldAnnouncementService.Unload();
        ScreenReaderService.Unload();
        Instance = null;
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        GuidanceSystem.HandlePacket(reader, whoAmI);
    }
}
