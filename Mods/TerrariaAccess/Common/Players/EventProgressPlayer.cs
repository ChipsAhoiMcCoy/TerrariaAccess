#nullable enable
using TerrariaAccess.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

public sealed class EventProgressPlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        _ = triggersSet;

        if (Main.dedServ || Main.gameMenu || Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (EventProgressKeybinds.EventProgressCheck?.JustPressed ?? false)
        {
            EventProgressNarrationSystem.AnnounceCurrent();
        }
    }
}
