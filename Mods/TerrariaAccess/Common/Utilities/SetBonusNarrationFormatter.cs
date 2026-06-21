#nullable enable
using System;
using System.Text;

namespace TerrariaAccess.Common.Utilities;

internal static class SetBonusNarrationFormatter
{
    internal const string Label = "Set bonus";

    internal static string NormalizeDescription(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return string.Empty;
        }

        string normalized = GlyphTagFormatter.Normalize(rawDescription.Trim());
        normalized = TextSanitizer.Clean(normalized);
        return CollapseWhitespace(normalized);
    }

    internal static string? BuildStatusLine(string? rawDescription)
    {
        string description = NormalizeDescription(rawDescription);
        return string.IsNullOrWhiteSpace(description) ? null : $"{Label}: {description}";
    }

    internal static string? BuildActivatedAnnouncement(string? rawDescription)
    {
        string description = NormalizeDescription(rawDescription);
        return string.IsNullOrWhiteSpace(description) ? null : $"{Label} active: {description}";
    }

    internal static bool ContainsDescription(string? existingText, string? rawDescription)
    {
        string description = NormalizeDescription(rawDescription);
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(existingText))
        {
            return false;
        }

        string existing = GlyphTagFormatter.Normalize(existingText.Trim());
        existing = TextSanitizer.Clean(existing);
        existing = CollapseWhitespace(existing);
        return existing.Contains(description, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }
}
