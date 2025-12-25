#nullable enable
using System;
using System.Linq;
using System.Text;

namespace ScreenReaderMod.Common.Utilities;

internal static class TextSanitizer
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (!trimmed.Contains('['))
        {
            return trimmed;
        }

        return StripFormatting(trimmed);
    }

    public static string JoinWithComma(params string?[] parts)
    {
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", parts
            .Select(Clean)
            .Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string StripFormatting(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '[')
            {
                int closing = text.IndexOf(']', i + 1);
                if (closing > i)
                {
                    string token = text.Substring(i + 1, closing - i - 1);
                    if (HandleFormattingToken(token, builder))
                    {
                        i = closing;
                        continue;
                    }
                }
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static bool HandleFormattingToken(string token, StringBuilder builder)
    {
        if (token.StartsWith("c/", StringComparison.OrdinalIgnoreCase))
        {
            int colon = token.IndexOf(':');
            if (colon >= 0 && colon + 1 < token.Length)
            {
                builder.Append(token.AsSpan(colon + 1));
            }

            return true;
        }

        if (token.StartsWith("n:", StringComparison.OrdinalIgnoreCase))
        {
            string name = token.Length > 2 ? token[2..] : string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                string resolved = name.Replace("\\[", "[").Replace("\\]", "]");
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    builder.Append(resolved.Trim());
                    builder.Append(',');
                }
            }

            return true;
        }

        if (token.StartsWith("i:", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("rb", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("g", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("wave", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

