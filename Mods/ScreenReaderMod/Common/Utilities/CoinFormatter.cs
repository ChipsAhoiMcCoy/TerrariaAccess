#nullable enable
using System.Text;
using Terraria;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Utilities;

public static class CoinFormatter
{
    public static string ValueToCoinString(long coins)
    {
        if (coins <= 0)
        {
            return string.Empty;
        }

        long platinum = coins / 1_000_000;
        coins %= 1_000_000;

        long gold = coins / 10_000;
        coins %= 10_000;

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
