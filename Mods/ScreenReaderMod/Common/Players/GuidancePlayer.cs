#nullable enable
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class GuidancePlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        GuidanceSystem.HandleKeybinds(Player);
    }

    public override void PreUpdateMovement()
    {
        GuidanceSystem.ApplyAutoPath(Player);
    }
}
