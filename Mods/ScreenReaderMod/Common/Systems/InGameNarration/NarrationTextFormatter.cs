#nullable enable
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Systems;

internal static class NarrationTextFormatter
{
    public static string ComposePrice(long value)
    {
        string coinText = CoinFormatter.ValueToCoinString(value);
        if (!string.IsNullOrWhiteSpace(coinText))
        {
            return coinText;
        }

        return value.ToString();
    }

    public static string ComposeItemName(Item item)
    {
        string name = TextSanitizer.Clean(item.AffixName());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(item.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(Lang.GetItemNameValue(item.type));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Item {item.type}";
        }

        return name;
    }

    public static string ComposeItemLabel(Item item, bool includeCountWhenSingular = false)
    {
        string name = ComposeItemName(item);
        return NarrationStringCatalog.ItemLabel(name, item.stack, item.favorited, includeCountWhenSingular);
    }

    public static string CombineItemAnnouncement(string message, string? details)
    {
        string normalizedMessage = GlyphTagFormatter.Normalize(message.Trim());
        if (string.IsNullOrWhiteSpace(details))
        {
            return normalizedMessage;
        }

        string normalizedDetails = GlyphTagFormatter.Normalize(details.Trim());
        if (!HasTerminalPunctuation(normalizedMessage))
        {
            normalizedMessage += '.';
        }

        string combined = $"{normalizedMessage} {normalizedDetails}";
        return combined;
    }

    internal static bool HasTerminalPunctuation(string text)
    {
        text = text.TrimEnd();
        if (text.Length == 0)
        {
            return false;
        }

        char last = text[^1];
        return last == '.' || last == '!' || last == '?' || last == ':' || last == ')';
    }
}
