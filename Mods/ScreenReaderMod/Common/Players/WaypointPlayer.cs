#nullable enable
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class WaypointPlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        WaypointSystem.HandleKeybinds(Player);
    }
}

