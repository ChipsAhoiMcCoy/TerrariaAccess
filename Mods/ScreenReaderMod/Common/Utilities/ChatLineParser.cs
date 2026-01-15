#nullable enable
using System;

namespace ScreenReaderMod.Common.Utilities;

internal static class ChatLineParser
{
    internal static bool TryParseLeadingNameTagChat(string? rawText, out string playerName, out string message)
    {
        playerName = string.Empty;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        string trimmed = rawText.TrimStart();
        if (!trimmed.StartsWith("[n:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int closing = trimmed.IndexOf(']');
        if (closing <= 3 || closing >= trimmed.Length)
        {
            return false;
        }

        string rawName = trimmed.Substring(3, closing - 3).Trim();
        string rawMessage = trimmed[(closing + 1)..].TrimStart();

        if (string.IsNullOrWhiteSpace(rawName) || string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        rawName = rawName.Replace("\\[", "[").Replace("\\]", "]");

        playerName = TextSanitizer.Clean(rawName);
        message = TextSanitizer.Clean(rawMessage);

        return !string.IsNullOrWhiteSpace(playerName) && !string.IsNullOrWhiteSpace(message);
    }

    internal static string FormatNameMessage(string playerName, string message)
    {
        string name = TextSanitizer.Clean(playerName);
        string msg = TextSanitizer.Clean(message);

        if (string.IsNullOrWhiteSpace(name))
        {
            return msg;
        }

        if (string.IsNullOrWhiteSpace(msg))
        {
            return name;
        }

        return $"{name}: {msg}";
    }
}

