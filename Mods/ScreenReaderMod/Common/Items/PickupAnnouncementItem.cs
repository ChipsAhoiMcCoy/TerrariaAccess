#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Items;

public sealed class PickupAnnouncementItem : GlobalItem
{
    public override bool OnPickup(Item item, Player player)
    {
        if (player.whoAmI == Main.myPlayer && item is not null && !item.IsAir)
        {
            string message = $"Picked up {InGameNarrationSystem.ComposeItemLabel(item)}";
            ScreenReaderService.Announce(message, category: ScreenReaderService.AnnouncementCategory.Pickup);
        }

        return base.OnPickup(item, player);
    }
}
