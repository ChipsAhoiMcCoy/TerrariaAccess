#nullable enable
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Services;

internal static class WorldAnnouncementService
{
    public static void Initialize()
    {
    }

    public static void Unload()
    {
    }

    public static void Announce(string? message, bool force = false)
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

        ScreenReaderService.Announce(
            sanitized,
            force: force,
            category: ScreenReaderService.AnnouncementCategory.World,
            allowWhenMuted: true,
            channel: SpeechChannel.World);
    }
}
