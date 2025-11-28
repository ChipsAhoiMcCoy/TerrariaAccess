#nullable enable
using System;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Services;

internal static class WorldAnnouncementService
{
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(2);
    private static readonly object SyncRoot = new();

    private static string? _lastMessage;
    private static DateTime _lastAnnouncedAt = DateTime.MinValue;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            _lastMessage = null;
            _lastAnnouncedAt = DateTime.MinValue;
        }

        SapiSpeechProvider.Initialize();
    }

    public static void Unload()
    {
        lock (SyncRoot)
        {
            _lastMessage = null;
            _lastAnnouncedAt = DateTime.MinValue;
        }

        SapiSpeechProvider.Shutdown();
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

        if (!force && !ScreenReaderService.SpeechEnabled)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        lock (SyncRoot)
        {
            if (!force && string.Equals(sanitized, _lastMessage, StringComparison.OrdinalIgnoreCase) && now - _lastAnnouncedAt < RepeatWindow)
            {
                return;
            }

            _lastMessage = sanitized;
            _lastAnnouncedAt = now;
        }

        if (ScreenReaderService.SpeechInterruptEnabled)
        {
            ScreenReaderService.Interrupt();
        }

        SapiSpeechProvider.Speak(sanitized);
        ScreenReaderMod.Instance?.Logger.Info($"[WorldNarration] {sanitized}");
    }
}
