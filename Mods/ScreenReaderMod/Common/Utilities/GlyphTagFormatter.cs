#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Utilities;

internal static class GlyphTagFormatter
{
    private static readonly Dictionary<string, string> GlyphTokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "A button",
        ["2"] = "B button",
        ["3"] = "X button",
        ["4"] = "Y button",
        ["5"] = "Right bumper",
        ["6"] = "Left bumper",
        ["7"] = "Left trigger",
        ["8"] = "Right trigger",
        ["9"] = "View button",
        ["10"] = "Menu button",
        ["11"] = "Left stick",
        ["12"] = "Right stick",
        ["13"] = "D-pad up",
        ["14"] = "D-pad down",
        ["15"] = "D-pad left",
        ["16"] = "D-pad right",
        ["17"] = "Left stick click",
        ["18"] = "Right stick click",
        ["lb"] = "Left bumper",
        ["rb"] = "Right bumper",
        ["lt"] = "Left trigger",
        ["rt"] = "Right trigger",
        ["ls"] = "Left stick",
        ["rs"] = "Right stick",
        ["back"] = "View button",
        ["select"] = "View button",
        ["menu"] = "Menu button",
        ["start"] = "Menu button",
        ["up"] = "D-pad up",
        ["down"] = "D-pad down",
        ["left"] = "D-pad left",
        ["right"] = "D-pad right",
        ["mouseleft"] = "Left mouse button",
        ["mouseright"] = "Right mouse button",
        ["mousemiddle"] = "Middle mouse button",
        ["mousewheelup"] = "Mouse wheel up",
        ["mousewheeldown"] = "Mouse wheel down",
        ["mousexbutton1"] = "Mouse button four",
        ["mousexbutton2"] = "Mouse button five",
    };

    private static readonly string[] GlyphSnippetMemberCandidates =
    {
        "Glyph",
        "_glyph",
        "glyph",
        "GlyphId",
        "_glyphId",
        "glyphId",
        "GlyphIndex",
        "_glyphIndex",
        "glyphIndex",
        "Id",
        "_id",
        "id",
    };

    private static readonly HashSet<string> ReportedGlyphSnippetTypes = new(StringComparer.Ordinal);
    private static readonly TextInfo InvariantTextInfo = CultureInfo.InvariantCulture.TextInfo;

    private static Type? _glyphSnippetType;

    internal static string SanitizeTooltip(string raw)
    {
        try
        {
            List<TextSnippet>? snippets = ChatManager.ParseMessage(raw, Color.White);
            if (snippets is null || snippets.Count == 0)
            {
                return Normalize(raw);
            }

            StringBuilder builder = new();
            foreach (TextSnippet snippet in snippets)
            {
                if (snippet is null)
                {
                    continue;
                }

                if (TryAppendGlyphSnippet(builder, snippet))
                {
                    continue;
                }

                builder.Append(snippet.Text);
            }

            string sanitized = builder.ToString();
            if (raw.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sanitized.IndexOf(" to open", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LogGlyphDebug(raw, sanitized, snippets);
            }

            return Normalize(sanitized);
        }
        catch
        {
            return Normalize(raw);
        }
    }

    internal static string NormalizeNameCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string sanitized = SanitizeTooltip(value);
        return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        StringBuilder builder = new(text.Length);
        bool replaced = false;
        int index = 0;

        while (index < text.Length)
        {
            if (text[index] == '[' && index + 2 < text.Length && (text[index + 1] == 'g' || text[index + 1] == 'G'))
            {
                int end = index + 2;
                while (end < text.Length && text[end] != ']')
                {
                    end++;
                }

                if (end < text.Length)
                {
                    string token = text.Substring(index + 1, end - index - 1);
                    if (TryTranslateGlyphToken(token, out string replacement))
                    {
                        builder.Append(replacement);
                        index = end + 1;
                        replaced = true;
                        continue;
                    }
                }
            }

            if ((text[index] == 'g' || text[index] == 'G') &&
                (index == 0 || !char.IsLetterOrDigit(text[index - 1])))
            {
                int end = index + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }

                if (end > index + 1)
                {
                    string token = text.Substring(index, end - index);
                    if (TryTranslateGlyphToken(token, out string replacement))
                    {
                        builder.Append(replacement);
                        index = end;
                        replaced = true;
                        continue;
                    }
                }
            }

            builder.Append(text[index]);
            index++;
        }

        string normalized = replaced ? builder.ToString() : text;
        return ReplaceFallbackGlyphNumbers(normalized);
    }

    private static bool TryAppendGlyphSnippet(StringBuilder builder, TextSnippet snippet)
    {
        if (snippet is null)
        {
            return false;
        }

        Type snippetType = snippet.GetType();
        if (!IsGlyphSnippetType(snippetType))
        {
            ReportGlyphSnippetType(snippetType, "UnrecognizedSnippetType");
            return false;
        }

        string? glyphToken = ExtractGlyphToken(snippetType, snippet);
        if (string.IsNullOrWhiteSpace(glyphToken))
        {
            ReportGlyphSnippetType(snippetType, "MissingGlyphToken");
            return false;
        }

        glyphToken = glyphToken.Trim();

        if (!TryTranslateGlyphToken(glyphToken, out string replacement) &&
            !TryTranslateGlyphToken($"g{glyphToken}", out replacement))
        {
            string humanized = HumanizeToken(glyphToken);
            if (string.IsNullOrWhiteSpace(humanized))
            {
                ReportGlyphSnippetType(snippetType, $"UnmappedToken:{glyphToken}");
                return false;
            }

            replacement = humanized;
        }

        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
        {
            builder.Append(' ');
        }

        builder.Append(replacement);
        return true;
    }

    private static bool IsGlyphSnippetType(Type snippetType)
    {
        if (snippetType is null)
        {
            return false;
        }

        if (_glyphSnippetType is not null)
        {
            return snippetType == _glyphSnippetType || snippetType.IsSubclassOf(_glyphSnippetType);
        }

        string? fullName = snippetType.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        if (fullName.Contains("GlyphTagHandler", StringComparison.Ordinal) ||
            fullName.Contains("GlyphSnippet", StringComparison.Ordinal))
        {
            _glyphSnippetType = snippetType;
            return true;
        }

        ReportGlyphSnippetType(snippetType, "UnrecognizedSnippetType");
        return false;
    }

    private static void ReportGlyphSnippetType(Type snippetType, string note)
    {
        if (snippetType is null)
        {
            return;
        }

        string name = snippetType.FullName ?? snippetType.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string key = $"{name}:{note}";
        lock (ReportedGlyphSnippetTypes)
        {
            if (!ReportedGlyphSnippetTypes.Add(key))
            {
                return;
            }
        }

        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[GlyphSnippet] {note} -> {name}");
    }

    private static void LogGlyphDebug(string raw, string sanitized, IEnumerable<TextSnippet> snippets)
    {
        try
        {
            var logger = global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger;
            if (logger is null)
            {
                return;
            }

            logger.Info($"[GlyphDebug] raw: {raw}");
            logger.Info($"[GlyphDebug] sanitized: {sanitized}");

            foreach (TextSnippet snippet in snippets)
            {
                if (snippet is null)
                {
                    continue;
                }

                Type type = snippet.GetType();
                string typeName = type.FullName ?? type.Name;
                logger.Info($"[GlyphDebug] snippet: {typeName} -> \"{snippet.Text}\"");

                foreach (string memberName in GlyphSnippetMemberCandidates)
                {
                    object? value = TryGetMemberValue(type, snippet, memberName);
                    if (value is null)
                    {
                        continue;
                    }

                    logger.Info($"[GlyphDebug]   {memberName}: {value}");
                }
            }
        }
        catch
        {
            // Ignore logging failures to avoid noisy output during release builds.
        }
    }

    private static bool TryTranslateGlyphToken(string token, out string replacement)
    {
        replacement = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string trimmed = token.Trim().Trim('[', ']');
        if (trimmed.Length == 0)
        {
            return false;
        }

        string raw = trimmed;
        if (trimmed.Length > 0 && (trimmed[0] == 'g' || trimmed[0] == 'G'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        string normalized = trimmed.TrimStart('_');

        if (GlyphTokenMap.TryGetValue(normalized, out string? mapped) ||
            GlyphTokenMap.TryGetValue(raw, out mapped))
        {
            replacement = mapped!;
            return true;
        }

        string humanized = HumanizeToken(normalized);
        if (!string.IsNullOrWhiteSpace(humanized))
        {
            replacement = humanized;
            return true;
        }

        return false;
    }

    private static string ReplaceFallbackGlyphNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        StringBuilder builder = new(text.Length + 8);
        int index = 0;

        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsDigit(current))
            {
                int start = index;
                int end = index;
                while (end < text.Length && char.IsDigit(text[end]))
                {
                    end++;
                }

                string token = text.Substring(start, end - start);
                if (TryTranslateGlyphToken(token, out string replacement) &&
                    ShouldTreatAsGlyphNumber(text, start, end))
                {
                    builder.Append(replacement);
                }
                else
                {
                    builder.Append(token);
                }

                index = end;
                continue;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }

    private static bool ShouldTreatAsGlyphNumber(string text, int start, int end)
    {
        static bool MatchesAny(ReadOnlySpan<char> span, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (span.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        ReadOnlySpan<char> before = text.AsSpan(0, start);
        ReadOnlySpan<char> after = text.AsSpan(end);

        if (MatchesAny(after, " to ", " to_", " to-", " to.", " to,", " to!"))
        {
            return true;
        }

        if (MatchesAny(after, " button", " trigger", " shoulder", " stick"))
        {
            return true;
        }

        const int PressLength = 6; // "Press "
        if (before.Length >= PressLength)
        {
            ReadOnlySpan<char> prefix = before.Slice(before.Length - PressLength);
            if (prefix.Equals("Press ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (before.Length >= 2)
        {
            ReadOnlySpan<char> suffix = before.Slice(before.Length - 2);
            if (suffix.Equals(": ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string HumanizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        StringBuilder builder = new(token.Length + 4);
        for (int i = 0; i < token.Length; i++)
        {
            char current = token[i];
            if (i > 0)
            {
                char previous = token[i - 1];
                bool split = char.IsDigit(current) != char.IsDigit(previous) ||
                             (char.IsUpper(current) && !char.IsUpper(previous)) ||
                             current == '_';
                if (split)
                {
                    builder.Append(' ');
                }
            }

            if (current != '_')
            {
                builder.Append(char.ToLowerInvariant(current));
            }
        }

        string spaced = builder.ToString().Trim();
        if (spaced.Length == 0)
        {
            return string.Empty;
        }

        return InvariantTextInfo.ToTitleCase(spaced);
    }

    private static string? ExtractGlyphToken(Type snippetType, TextSnippet snippet)
    {
        foreach (string memberName in GlyphSnippetMemberCandidates)
        {
            object? value = TryGetMemberValue(snippetType, snippet, memberName);
            if (value is null)
            {
                continue;
            }

            string? token = ConvertGlyphMemberToString(value);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        return null;
    }

    private static object? TryGetMemberValue(Type type, object instance, string memberName)
    {
        if (type is null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(instance);
            }
            catch
            {
                // Ignore accessor failures.
            }
        }

        FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is not null)
        {
            try
            {
                return field.GetValue(instance);
            }
            catch
            {
                // Ignore accessor failures.
            }
        }

        return null;
    }

    private static string? ConvertGlyphMemberToString(object value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return text;
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case Enum enumValue:
                return Convert.ToInt32(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            default:
                string? result = value.ToString();
                return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
