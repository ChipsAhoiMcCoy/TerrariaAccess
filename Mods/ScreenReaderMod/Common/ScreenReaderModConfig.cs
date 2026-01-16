#nullable enable
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ScreenReaderMod.Common;

public class ScreenReaderModConfig : ModConfig
{
    public static ScreenReaderModConfig Instance { get; private set; } = null!;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    // Core feature toggle
    [DefaultValue(true)]
    public bool GamepadEmulationEnabled { get; set; } = true;

    // Narration
    [DefaultValue(false)]
    public bool AnnounceDamageNumbers { get; set; }

    // Movement audio cues
    [DefaultValue(true)]
    public bool EdgeDetectionEnabled { get; set; } = true;

    // Multiplayer footsteps
    [DefaultValue(true)]
    public bool MultiplayerFootstepsEnabled { get; set; } = true;

    // Cursor audio cues
    [DefaultValue(true)]
    public bool CursorTileSounds { get; set; } = true;

    // Experimental features
    [DefaultValue(true)]
    public bool SpatialInventoryAudio { get; set; } = true;

    public override void OnLoaded()
    {
        Instance = this;
    }
}
