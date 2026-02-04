#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Services;

public static class ScreenReaderService
{
    public enum AnnouncementCategory
    {
        Default,
        Tile,
        Wall,
        Pickup,
        World,
    }

    private static SpeechController? _controller;

    private static SpeechController Controller => _controller ??= BuildController();

    public static IReadOnlyCollection<string> Snapshot => Controller.GetSnapshot().RecentMessages;
    public static bool SpeechEnabled => Controller.SpeechEnabled;
    public static bool SpeechInterruptEnabled => Controller.InterruptEnabled;

    public static void Initialize()
    {
        SpeechController controller = Controller;
        controller.SetLogOnly(ScreenReaderDiagnostics.IsSpeechLogOnlyEnabled());
        controller.Initialize();
        ScreenReaderDiagnostics.DumpStartupSnapshot(controller.GetSnapshot());
    }

    public static void Unload()
    {
        _controller?.Shutdown();
        _controller = null;
    }

    public static void Interrupt(SpeechChannel channel = SpeechChannel.Primary)
    {
        Controller.Interrupt(channel);
    }

    /// <summary>
    /// Sets a prefix that will be prepended to the next announcement.
    /// This enables coordinated announcements between narrators without
    /// frame timing hacks or cross-narrator static queues.
    /// For example, when switching cursor modes, the mode change can be
    /// queued as a prefix and the next tile announcement will include it.
    /// </summary>
    public static void SetPendingPrefix(string? prefix)
    {
        Controller.SetPendingPrefix(prefix);
    }

    /// <summary>
    /// Clears any pending prefix without using it.
    /// </summary>
    public static void ClearPendingPrefix()
    {
        Controller.ClearPendingPrefix();
    }

    /// <summary>
    /// Returns whether there is a pending prefix waiting to be used.
    /// </summary>
    public static bool HasPendingPrefix => Controller.HasPendingPrefix;

    public static bool ToggleSpeechInterrupt()
    {
        return Controller.ToggleInterrupts();
    }

    public static bool ToggleSpeechEnabled()
    {
        return Controller.ToggleMute();
    }

    public static void Announce(
        string? message,
        bool force = false,
        AnnouncementCategory category = AnnouncementCategory.Default,
        bool allowWhenMuted = false,
        SpeechChannel channel = SpeechChannel.Primary,
        bool requestInterrupt = true)
    {
        RecordInstrumentationKey(message);
        Controller.Enqueue(
            new SpeechRequest(
                Text: message ?? string.Empty,
                Category: category,
                Channel: channel,
                Force: force,
                AllowWhenMuted: allowWhenMuted,
                RequestInterrupt: requestInterrupt));
    }

    private static void RecordInstrumentationKey(string? message)
    {
        string? key = NarrationInstrumentationContext.ConsumePendingKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = message;
        }

        NarrationInstrumentationContext.RecordKey(key);
    }

    private static SpeechController BuildController()
    {
        var controller = new SpeechController(new TolkSpeechProvider());
        controller.SetCategoryWindow(AnnouncementCategory.World, TimeSpan.FromSeconds(2));
        controller.SetCategoryWindow(AnnouncementCategory.Tile, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Wall, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Pickup, TimeSpan.FromMilliseconds(150));
        return controller;
    }
}
