#nullable enable
using System;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Utilities;

internal static class LocalizationHelper
{
    public static string GetText(string key)
    {
        return TextSanitizer.Clean(Language.GetTextValue(key));
    }

    public static string GetTextOrFallback(string key, string fallback)
    {
        string value = Language.GetTextValue(key);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return TextSanitizer.Clean(fallback);
        }

        return TextSanitizer.Clean(value);
    }
}
