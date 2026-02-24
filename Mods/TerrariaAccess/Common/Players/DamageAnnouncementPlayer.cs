#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

public sealed class DamageAnnouncementPlayer : ModPlayer
{
    private const string DamageAnnouncementKey = "Mods.TerrariaAccess.Combat.DamageAnnouncement";

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (Main.dedServ || Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        TerrariaAccessConfig? config = TerrariaAccessConfig.Instance;
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
