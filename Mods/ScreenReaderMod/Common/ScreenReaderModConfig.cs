#nullable enable
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ScreenReaderMod.Common;

public class ScreenReaderModConfig : ModConfig
{
    public static ScreenReaderModConfig Instance { get; private set; } = null!;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(EdgeDetectionMode.Off)]
    public EdgeDetectionMode EdgeDetection { get; set; } = EdgeDetectionMode.Off;

    [DefaultValue(true)]
    public bool SmartCursorTileSounds { get; set; } = true;

    [DefaultValue(100)]
    [Range(0, 100)]
    [Slider]
    public int FootstepVolume { get; set; } = 100;

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

    public override void OnLoaded()
    {
        Instance = this;
    }
}

public enum EdgeDetectionMode
{
    Echo,
    Static,
    Off
}
