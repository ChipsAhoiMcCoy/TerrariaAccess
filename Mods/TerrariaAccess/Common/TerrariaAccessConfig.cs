#nullable enable
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TerrariaAccess.Common;

public enum EdgeDetectionMode
{
    Off,
    Static,
    Beeps
}

public enum FallProximityMode
{
    Tone,
    Beeps
}

public enum GuidanceAllMode
{
    Sweep,
    NearestOnly
}

public class TerrariaAccessConfig : ModConfig
{
    public static TerrariaAccessConfig Instance { get; private set; } = null!;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    // Narration
    [DefaultValue(false)]
    public bool AnnounceDamageNumbers { get; set; }

    [DefaultValue(true)]
    public bool BossWarningsEnabled { get; set; } = true;

    // Movement audio cues
    [DefaultValue(EdgeDetectionMode.Static)]
    public EdgeDetectionMode EdgeDetectionMode { get; set; } = EdgeDetectionMode.Static;

    [DefaultValue(true)]
    public bool OverheadTraversalCuesEnabled { get; set; } = true;

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

    [DefaultValue(GuidanceAllMode.Sweep)]
    public GuidanceAllMode GuidanceAllMode { get; set; } = GuidanceAllMode.Sweep;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float InteractableCueVolume { get; set; } = 1f;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float CursorVolume { get; set; } = 1f;

    // Passage detection (side tunnel chirps while falling)
    [DefaultValue(true)]
    public bool PassageDetectionEnabled { get; set; } = true;

    // Terrain sonification
    [DefaultValue(true)]
    public bool CavitySonarEnabled { get; set; } = true;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float CavitySonarVolume { get; set; } = 1f;

    // Health heartbeat
    [DefaultValue(true)]
    public bool HeartbeatEnabled { get; set; } = true;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float HeartbeatVolume { get; set; } = 1f;

    // Wall collision
    [DefaultValue(true)]
    public bool WallCollisionEnabled { get; set; } = true;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float WallCollisionVolume { get; set; } = 1f;

    // Fall detection
    [DefaultValue(true)]
    public bool FallDetectionEnabled { get; set; } = true;

    [DefaultValue(FallProximityMode.Tone)]
    public FallProximityMode FallDetectionMode { get; set; } = FallProximityMode.Tone;

    [DefaultValue(1f)]
    [Range(0f, 1f)]
    [Slider]
    public float FallDetectionVolume { get; set; } = 1f;

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
