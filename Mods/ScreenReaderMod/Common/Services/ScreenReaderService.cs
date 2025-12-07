#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        ISpeechProvider primary;
        ISpeechProvider? worldAnnouncement = null;

#if WINDOWS
        primary = new NvdaSpeechProvider();
        worldAnnouncement = new SapiSpeechProvider();
#elif OSX || MACOS
        primary = new AvFoundationSpeechProvider(1);
        worldAnnouncement = new AvFoundationSpeechProvider(2); 
#else
        // Runtime detection for when no compile-time flag is set
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            primary = new AvFoundationSpeechProvider(1);
            worldAnnouncement = new AvFoundationSpeechProvider(2);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            primary = new NvdaSpeechProvider();
            worldAnnouncement = new SapiSpeechProvider();
        }
        else
        {
            primary = new DummySpeechProvider();
        }
#endif

        var controller = new SpeechController(primary, worldAnnouncement);
        controller.SetCategoryWindow(AnnouncementCategory.World, TimeSpan.FromSeconds(2));
        controller.SetCategoryWindow(AnnouncementCategory.Tile, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Wall, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Pickup, TimeSpan.FromMilliseconds(150));
        return controller;
    }
}
