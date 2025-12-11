#nullable enable
using System;
using ScreenReaderMod.Common.Systems.MenuNarration;

namespace ScreenReaderMod.Common.Utilities;

internal static class SliderNarrationHelper
{
    public static string BuildSliderAnnouncement(string rawLabel, MenuSliderKind kind, float percent, bool includeLabel)
    {
        string baseLabel = ExtractBaseLabel(rawLabel, kind);
        return NarrationStringCatalog.SliderValue(baseLabel, percent, includeLabel);
    }

    public static string ExtractBaseLabel(string rawLabel, MenuSliderKind kind)
    {
        string sanitized = TextSanitizer.Clean(rawLabel);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            int percentIndex = sanitized.IndexOf('%');
            if (percentIndex >= 0)
            {
                sanitized = sanitized[..percentIndex];
            }

            int percentWord = sanitized.IndexOf("percent", StringComparison.OrdinalIgnoreCase);
            if (percentWord >= 0)
            {
                sanitized = sanitized[..percentWord];
            }

            sanitized = sanitized.Trim().TrimEnd(':').Trim();
            sanitized = TrimTrailingNumber(sanitized);
        }

        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        return GetDefaultSliderLabel(kind);
    }

    public static string GetDefaultSliderLabel(MenuSliderKind kind)
    {
        return kind switch
        {
            MenuSliderKind.Music => "Music volume",
            MenuSliderKind.Sound => "Sound volume",
            MenuSliderKind.Ambient => "Ambient volume",
            MenuSliderKind.Zoom => "Zoom",
            MenuSliderKind.InterfaceScale => "Interface scale",
            MenuSliderKind.Parallax => "Background parallax",
            _ => "Slider",
        };
    }

    public static string TrimTrailingNumber(string value)
    {
        int end = value.Length;
        while (end > 0 && (char.IsWhiteSpace(value[end - 1]) || char.IsDigit(value[end - 1]) || value[end - 1] == ':' || value[end - 1] == '.'))
        {
            end--;
        }

        if (end < value.Length)
        {
            return value[..end].TrimEnd();
        }

        return value;
    }
}
