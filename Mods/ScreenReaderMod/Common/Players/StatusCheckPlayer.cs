#nullable enable
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class StatusCheckPlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        _ = triggersSet;

        if (Main.dedServ || Main.gameMenu || Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (StatusCheckKeybinds.StatusCheck?.JustPressed ?? false)
        {
            StatusCheckSystem.AnnounceStatus(Player);
        }
    }
}
