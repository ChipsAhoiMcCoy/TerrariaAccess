#nullable enable
using System;
using System.Text;
using TerrariaAccess.Common.Abstractions;

namespace TerrariaAccess.Common.Utilities;

public static class CoinFormatter
{
    /// <summary>
    /// Default localization provider. Set by the mod at startup.
    /// </summary>
    public static ITerrariaLocalization? DefaultLocalization { get; set; }

    /// <summary>
    /// Converts a coin value to a human-readable string.
    /// </summary>
    /// <param name="coins">The coin value in copper units.</param>
    /// <param name="localization">Optional localization provider for testing. Falls back to DefaultLocalization.</param>
    public static string ValueToCoinString(long coins, ITerrariaLocalization? localization = null)
    {
        if (coins <= 0)
        {
            return string.Empty;
        }

        localization ??= DefaultLocalization
            ?? throw new InvalidOperationException("No localization provider configured. Set CoinFormatter.DefaultLocalization at startup.");

        long platinum = coins / 1_000_000;
        coins %= 1_000_000;

        long gold = coins / 10_000;
        coins %= 10_000;

        long silver = coins / 100;
        long copper = coins % 100;

        var builder = new StringBuilder();
        AppendCoin(builder, platinum, localization.GetCoinLabel(15));
        AppendCoin(builder, gold, localization.GetCoinLabel(16));
        AppendCoin(builder, silver, localization.GetCoinLabel(17));
        AppendCoin(builder, copper, localization.GetCoinLabel(18));

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
