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

    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(250);
    private static readonly Queue<string> RecentMessages = new();
    private static readonly Dictionary<AnnouncementCategory, string?> LastCategoryAnnouncements = new();
    private static string? _lastMessage;
    private static DateTime _lastAnnouncedAt = DateTime.MinValue;
    private static bool _muted;
    private static bool _interruptEnabled;

    public static IReadOnlyCollection<string> Snapshot => RecentMessages.ToArray();
    public static bool SpeechEnabled => !_muted;
    public static bool SpeechInterruptEnabled => _interruptEnabled;

    public static void Initialize()
    {
        RecentMessages.Clear();
        LastCategoryAnnouncements.Clear();
        _lastMessage = null;
        _lastAnnouncedAt = DateTime.MinValue;
        _muted = false;
        _interruptEnabled = true;
        ScreenReaderDiagnostics.DumpStartupSnapshot();
        NvdaSpeechProvider.Initialize();
    }

    public static void Unload()
    {
        RecentMessages.Clear();
        LastCategoryAnnouncements.Clear();
        _lastMessage = null;
        _lastAnnouncedAt = DateTime.MinValue;
        _muted = false;
        _interruptEnabled = false;
        NvdaSpeechProvider.Shutdown();
    }

    public static void Interrupt()
    {
        NvdaSpeechProvider.Interrupt();
        SapiSpeechProvider.Interrupt();
    }

    public static bool ToggleSpeechInterrupt()
    {
        _interruptEnabled = !_interruptEnabled;
        return _interruptEnabled;
    }

    public static bool ToggleSpeechEnabled()
    {
        _muted = !_muted;
        return !_muted;
    }

    public static void Announce(
        string? message,
        bool force = false,
        AnnouncementCategory category = AnnouncementCategory.Default,
        bool allowWhenMuted = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_muted && !allowWhenMuted)
        {
            return;
        }

        string trimmed = message.Trim();
        DateTime now = DateTime.UtcNow;
        if (!force && ShouldSuppress(category, trimmed, now))
        {
            return;
        }

        if (_interruptEnabled)
        {
            Interrupt();
        }

        TrackAnnouncement(category, trimmed, now);
        RecentMessages.Enqueue(trimmed);
        while (RecentMessages.Count > 25)
        {
            RecentMessages.Dequeue();
        }

        NvdaSpeechProvider.Speak(trimmed);
        ScreenReaderMod.Instance?.Logger.Info($"[Narration] {trimmed}");
    }

    private static bool ShouldSuppress(AnnouncementCategory category, string trimmed, DateTime now)
    {
        if (category != AnnouncementCategory.Default &&
            LastCategoryAnnouncements.TryGetValue(category, out string? lastForCategory) &&
            string.Equals(trimmed, lastForCategory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (category == AnnouncementCategory.Default &&
            string.Equals(trimmed, _lastMessage, StringComparison.OrdinalIgnoreCase) &&
            now - _lastAnnouncedAt < RepeatWindow)
        {
            return true;
        }

        return false;
    }

    private static void TrackAnnouncement(AnnouncementCategory category, string trimmed, DateTime now)
    {
        if (category != AnnouncementCategory.Default)
        {
            LastCategoryAnnouncements[category] = trimmed;
        }

        _lastMessage = trimmed;
        _lastAnnouncedAt = now;
    }
}
