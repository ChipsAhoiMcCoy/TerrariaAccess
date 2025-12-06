#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

internal static class StatusCheckSystem
{
    internal static void AnnounceStatus(Player player)
    {
        string message = BuildStatusMessage(player);
        ScreenReaderService.Announce(message, force: true);
    }

    private static string BuildStatusMessage(Player player)
    {
        int healthCurrent = Math.Max(0, player.statLife);
        int healthMax = Math.Max(1, player.statLifeMax2);
        string health = $"Health {healthCurrent} of {healthMax}";

        int manaMax = Math.Max(0, player.statManaMax2);
        string mana = manaMax > 0
            ? $"Mana {Math.Max(0, Math.Min(player.statMana, manaMax))} of {manaMax}"
            : "Mana none";

        string time = DescribeTime();
        return $"{health}. {mana}. Time: {time}.";
    }

    private static string DescribeTime()
    {
        double time = Main.time;
        double dayLength = Main.dayLength;
        double nightLength = Main.nightLength;
        double totalDay = dayLength + nightLength;

        if (!Main.dayTime)
        {
            time += dayLength;
        }

        double hours24 = (time / totalDay * 24.0) + 4.5;
        hours24 %= 24.0;

        int hours = (int)hours24;
        int minutes = (int)((hours24 - hours) * 60.0);

        string period = hours >= 12 ? "PM" : "AM";
        int displayHour = hours % 12;
        if (displayHour == 0)
        {
            displayHour = 12;
        }

        string phase = Main.dayTime ? "Daytime" : "Night";
        return $"{phase}, {displayHour}:{minutes:00} {period}";
    }
}
