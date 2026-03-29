#nullable enable
using System;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Services;

internal static class WorldAnnouncementService
{
    private static string? _lastAnnouncement;

    public static void Initialize()
    {
    }

    public static void Unload()
    {
    }

    public static void Announce(string? message, bool force = true)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(message);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        _lastAnnouncement = sanitized;

        ScreenReaderService.Announce(
            sanitized,
            force: force,
            category: ScreenReaderService.AnnouncementCategory.World,
            allowWhenMuted: true,
            channel: SpeechChannel.World,
            requestInterrupt: false);
    }

    public static bool WasRecentlyAnnounced(string? message, TimeSpan? window = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string sanitized = TextSanitizer.Clean(message);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_lastAnnouncement))
        {
            return false;
        }

        return string.Equals(_lastAnnouncement, sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
