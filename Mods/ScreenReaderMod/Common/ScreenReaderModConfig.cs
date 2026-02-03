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

    // Volume controls
    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float FootstepVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float MultiplayerFootstepVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float EnemySoundVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float GuidanceVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float InteractableCueVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float CursorVolume { get; set; } = 1f;

    // Chunk Mining
    [DefaultValue(true)]
    public bool ChunkMinerEnabled { get; set; } = true;

    [DefaultValue(100)]
    [Range(10, 500)]
    [Slider]
    public int ChunkMinerMaxTiles { get; set; } = 100;

    public override void OnLoaded()
    {
        Instance = this;
    }
}
