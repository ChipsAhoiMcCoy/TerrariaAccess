#nullable enable
using ScreenReaderMod.Common.Config;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class DamageAnnouncementPlayer : ModPlayer
{
    private const string DamageAnnouncementKey = "Mods.ScreenReaderMod.Combat.DamageAnnouncement";

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (Main.dedServ || Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        ScreenReaderConfig? config = ScreenReaderConfig.Instance;
        if (config is null || !config.AnnounceDamageNumbers)
        {
            return;
        }

        if (damageDone <= 0)
        {
            return;
        }

        if (target.friendly || target.lifeMax <= 1 || hit.HideCombatText || hit.InstantKill || target.HideStrikeDamage)
        {
            return;
        }

        string template = LocalizationHelper.GetTextOrFallback(DamageAnnouncementKey, "{0} damage");
        string message = string.Format(template, damageDone);
        ScreenReaderService.Announce(message, requestInterrupt: false);
    }
}
