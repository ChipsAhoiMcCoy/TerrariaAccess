#nullable enable
using System.Text;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class DeathNarrationPlayer : ModPlayer
{
    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        string? deathLine;
        try
        {
            deathLine = damageSource.GetDeathText(Player.name).ToString();
        }
        catch
        {
            deathLine = null;
        }

        if (string.IsNullOrWhiteSpace(deathLine))
        {
            return;
        }

        string? coinDetail = BuildCoinDetail(Player);
        string message = coinDetail is { Length: > 0 }
            ? $"{deathLine}. {coinDetail}"
            : deathLine;

        ScreenReaderService.Announce(message, force: true);
    }

    private static string? BuildCoinDetail(Player player)
    {
        long lostCoins = player.lostCoins;
        if (lostCoins <= 0)
        {
            return null;
        }

        string coinString = player.lostCoinString;
        if (string.IsNullOrWhiteSpace(coinString))
        {
            coinString = ValueToCoinString(lostCoins);
        }

        if (string.IsNullOrWhiteSpace(coinString))
        {
            return null;
        }

        return Language.GetTextValue("Game.DroppedCoins", coinString);
    }

    private static string ValueToCoinString(long coins)
    {
        if (coins <= 0)
        {
            return string.Empty;
        }

        long platinum = coins / 1000000;
        coins %= 1000000;

        long gold = coins / 10000;
        coins %= 10000;

        long silver = coins / 100;
        long copper = coins % 100;

        var builder = new StringBuilder();
        AppendCoin(builder, platinum, Lang.inter[15].Value);
        AppendCoin(builder, gold, Lang.inter[16].Value);
        AppendCoin(builder, silver, Lang.inter[17].Value);
        AppendCoin(builder, copper, Lang.inter[18].Value);

        return builder.ToString().Trim();
    }

    private static void AppendCoin(StringBuilder builder, long amount, string label)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(amount).Append(' ').Append(label);
    }
}
