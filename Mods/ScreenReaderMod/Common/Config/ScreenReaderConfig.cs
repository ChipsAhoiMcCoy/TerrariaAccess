#nullable enable
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ScreenReaderMod.Common.Config;

public sealed class ScreenReaderConfig : ModConfig
{
    public static ScreenReaderConfig? Instance;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(false)]
    public bool AnnounceDamageNumbers { get; set; }
}
