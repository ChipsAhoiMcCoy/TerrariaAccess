#nullable enable
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
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
            coinString = CoinFormatter.ValueToCoinString(lostCoins);
        }

        if (string.IsNullOrWhiteSpace(coinString))
        {
            return null;
        }

        return Language.GetTextValue("Game.DroppedCoins", coinString);
    }
}
