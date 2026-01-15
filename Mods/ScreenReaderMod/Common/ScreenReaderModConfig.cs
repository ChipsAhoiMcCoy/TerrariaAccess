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

    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int FootstepVolume { get; set; } = 100;

    // Multiplayer footsteps
    [DefaultValue(true)]
    public bool MultiplayerFootstepsEnabled { get; set; } = true;

    [DefaultValue(50)]
    [Range(0, 100)]
    [Slider]
    public int MultiplayerFootstepVolume { get; set; } = 50;

    // Cursor audio cues
    [DefaultValue(true)]
    public bool CursorTileSounds { get; set; } = true;

    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int CursorVolume { get; set; } = 100;

    // Other audio cue volumes
    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int EnemySoundVolume { get; set; } = 100;

    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int GuidanceVolume { get; set; } = 100;

    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int InteractableCueVolume { get; set; } = 100;

    // Experimental features
    [DefaultValue(true)]
    public bool SpatialInventoryAudio { get; set; } = true;

    public override void OnLoaded()
    {
        Instance = this;
    }
}
