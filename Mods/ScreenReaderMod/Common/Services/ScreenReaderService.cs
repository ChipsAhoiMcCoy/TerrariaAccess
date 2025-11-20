#nullable enable
using System;
using System.Collections.Generic;
using Terraria;

namespace ScreenReaderMod.Common.Services;

public static class ScreenReaderService
{
    public enum AnnouncementCategory
    {
        Default,
        Tile,
        Wall,
        Pickup,
    }

    private static readonly Queue<string> RecentMessages = new();
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(300);
    private static DateTime _lastAnnouncedAt = DateTime.MinValue;
    private static string? _lastMessage;
    private static readonly Dictionary<AnnouncementCategory, string?> LastCategoryAnnouncements = new();

    public static IReadOnlyCollection<string> Snapshot => RecentMessages.ToArray();

    public static void Initialize()
    {
        RecentMessages.Clear();
        _lastAnnouncedAt = DateTime.MinValue;
        _lastMessage = null;
        LastCategoryAnnouncements.Clear();
        ScreenReaderDiagnostics.DumpStartupSnapshot();
        NvdaSpeechProvider.Initialize();
    }

    public static void Unload()
    {
        RecentMessages.Clear();
        _lastMessage = null;
        LastCategoryAnnouncements.Clear();
        NvdaSpeechProvider.Interrupt();
        NvdaSpeechProvider.Shutdown();
    }

    public static void Interrupt()
    {
        NvdaSpeechProvider.Interrupt();
    }

    public static void Announce(
        string? message,
        bool force = false,
        bool interrupt = true,
        AnnouncementCategory category = AnnouncementCategory.Default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string trimmed = message.Trim();
        DateTime now = DateTime.UtcNow;
        if (!force)
        {
            if (ShouldSuppressByCategory(category, trimmed))
            {
                return;
            }

            if (category == AnnouncementCategory.Default &&
                string.Equals(trimmed, _lastMessage, StringComparison.OrdinalIgnoreCase) &&
                now - _lastAnnouncedAt < RepeatWindow)
            {
                return;
            }
        }

        TrackCategoryAnnouncement(category, trimmed);
        _lastMessage = trimmed;
        _lastAnnouncedAt = now;
        RecentMessages.Enqueue(trimmed);
        while (RecentMessages.Count > 25)
        {
            RecentMessages.Dequeue();
        }

        if (interrupt)
        {
            NvdaSpeechProvider.Interrupt();
        }

        NvdaSpeechProvider.Speak(trimmed);
        ScreenReaderMod.Instance?.Logger.Info($"[Narration] {trimmed}");

        if (!Main.dedServ)
        {
            Main.NewText($"[Narration] {trimmed}", 255, 255, 160);
        }
    }

    private static bool ShouldSuppressByCategory(AnnouncementCategory category, string trimmed)
    {
        if (category == AnnouncementCategory.Default)
        {
            return false;
        }

        if (!LastCategoryAnnouncements.TryGetValue(category, out string? last))
        {
            return false;
        }

        return string.Equals(trimmed, last, StringComparison.OrdinalIgnoreCase);
    }

    private static void TrackCategoryAnnouncement(AnnouncementCategory category, string trimmed)
    {
        if (category == AnnouncementCategory.Default)
        {
            return;
        }

        LastCategoryAnnouncements[category] = trimmed;
    }
}
