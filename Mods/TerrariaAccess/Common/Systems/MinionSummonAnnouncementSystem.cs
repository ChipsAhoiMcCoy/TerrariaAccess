#nullable enable
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Systems;

public sealed class MinionSummonAnnouncementSystem : ModSystem
{
    private const int PendingAnnouncementFrames = 6;
    private const string SingularSlotUsageKey = "Mods.TerrariaAccess.Minions.SummonedSlotUsageSingular";
    private const string PluralSlotUsageKey = "Mods.TerrariaAccess.Minions.SummonedSlotUsagePlural";

    private static int _pendingAnnouncementFrames;

    internal static void NotifyLocalMinionSummoned()
    {
        _pendingAnnouncementFrames = PendingAnnouncementFrames;
    }

    public override void PostUpdateProjectiles()
    {
        if (Main.dedServ || _pendingAnnouncementFrames <= 0)
        {
            return;
        }

        _pendingAnnouncementFrames--;

        if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers)
        {
            return;
        }

        Player player = Main.player[Main.myPlayer];
        if (!player.active || player.dead || player.slotsMinions <= 0f)
        {
            return;
        }

        _pendingAnnouncementFrames = 0;
        ScreenReaderService.Announce(BuildAnnouncement(player), requestInterrupt: false);
    }

    public override void OnWorldUnload()
    {
        _pendingAnnouncementFrames = 0;
    }

    private static string BuildAnnouncement(Player player)
    {
        string usedSlots = MinionSlotNarrationFormatter.FormatSlotValue(player.slotsMinions);
        int maxSlots = player.maxMinions;
        bool singular = MinionSlotNarrationFormatter.UsesSingularSlotUnit(maxSlots);
        string key = singular ? SingularSlotUsageKey : PluralSlotUsageKey;
        string fallback = singular
            ? "{0} of {1} minion slot used"
            : "{0} of {1} minion slots used";
        string template = LocalizationHelper.GetTextOrFallback(key, fallback);

        return string.Format(template, usedSlots, maxSlots);
    }
}

public sealed class MinionSummonAnnouncementProjectile : GlobalProjectile
{
    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (Main.dedServ || !projectile.minion || projectile.owner != Main.myPlayer)
        {
            return;
        }

        if (source is EntitySource_ItemUse)
        {
            MinionSummonAnnouncementSystem.NotifyLocalMinionSummoned();
        }
    }
}
