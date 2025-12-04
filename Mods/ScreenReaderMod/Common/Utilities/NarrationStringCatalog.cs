#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace ScreenReaderMod.Common.Utilities;

/// <summary>
/// Central helpers for common narration strings so phrasing stays consistent across menus and in-game.
/// </summary>
internal static class NarrationStringCatalog
{
    public static string SliderValue(string label, float percent, bool includeLabel)
    {
        string valueText = $"{percent:0} percent";
        if (!includeLabel)
        {
            return valueText;
        }

        string sanitized = TextSanitizer.Clean(label);
        return string.IsNullOrWhiteSpace(sanitized) ? valueText : $"{sanitized} {valueText}";
    }

    public static string Coordinates(Vector2 worldPosition)
    {
        int tileX = (int)MathF.Round(worldPosition.X / 16f);
        int tileY = (int)MathF.Round(worldPosition.Y / 16f);
        return $"X {tileX}, Y {tileY}";
    }

    public static string Coordinates(int tileX, int tileY)
    {
        return $"X {tileX}, Y {tileY}";
    }

    public static string Price(string label, long value)
    {
        string coinText = CoinFormatter.ValueToCoinString(value);
        string sanitizedLabel = TextSanitizer.Clean(label);
        string valueText = string.IsNullOrWhiteSpace(coinText) ? value.ToString() : coinText;

        return string.IsNullOrWhiteSpace(sanitizedLabel)
            ? valueText
            : $"{sanitizedLabel}: {valueText}";
    }

    public static string ItemLabel(string name, int stack, bool favorited, bool includeCountWhenSingular = false)
    {
        string sanitizedName = TextSanitizer.Clean(name);
        bool includeCount = stack > 1 || includeCountWhenSingular;
        int normalizedStack = includeCount ? Math.Max(1, stack) : stack;
        string main = includeCount ? $"{normalizedStack} {sanitizedName}" : sanitizedName;
        if (favorited)
        {
            return TextSanitizer.JoinWithComma(main, "favorited");
        }

        return main;
    }
}
