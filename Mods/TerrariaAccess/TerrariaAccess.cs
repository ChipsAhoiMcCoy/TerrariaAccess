#nullable enable
using System.IO;
using TerrariaAccess.Common.Adapters;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems;
using TerrariaAccess.Common.Systems.BuildMode;
using TerrariaAccess.Common.Systems.Guidance;
using TerrariaAccess.Common.Systems.Audio;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess;

public class TerrariaAccess : Mod
{
    public static TerrariaAccess? Instance { get; private set; }

    public override void Load()
    {
        Instance = this;

        // Safety guard: skip client-only initialization on dedicated servers
        // (with side = Client this shouldn't happen, but guard defensively)
        if (Main.dedServ)
        {
            return;
        }

        // Initialize utility adapters for testability
        CoinFormatter.DefaultLocalization = new TerrariaLocalizationAdapter();

        ScreenReaderService.Initialize();
        WorldAnnouncementService.Initialize();
        UiTickSoundPlayer.Initialize();
        GuidanceKeybinds.EnsureInitialized(this);
        GamepadEmulationKeybinds.EnsureInitialized(this);
        SpeechInterruptKeybinds.EnsureInitialized(this);
        StatusCheckKeybinds.EnsureInitialized(this);
        BuildModeKeybinds.EnsureInitialized(this);
        CavitySonarKeybinds.EnsureInitialized(this);
        EventProgressKeybinds.EnsureInitialized(this);
    }

    public override void Unload()
    {
        EventProgressKeybinds.Unload();
        GamepadEmulationKeybinds.Unload();
        BuildModeKeybinds.Unload();
        StatusCheckKeybinds.Unload();
        SpeechInterruptKeybinds.Unload();
        CavitySonarKeybinds.Unload();
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
