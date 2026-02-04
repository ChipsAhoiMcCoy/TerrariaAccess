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

    #region Extended Speech Queue System

    /// <summary>
    /// Enqueues a prefix to be prepended to the next announcement.
    /// Multiple prefixes can be queued and will be combined with the next announcement.
    /// </summary>
    public static void EnqueuePrefix(string prefix)
    {
        Controller.EnqueuePrefix(prefix);
    }

    /// <summary>
    /// Clears all pending prefixes without using them.
    /// </summary>
    public static void ClearAllPrefixes()
    {
        Controller.ClearAllPrefixes();
    }

    /// <summary>
    /// Marks a key for one-shot suppression. The next announcement that checks
    /// this key will be suppressed, and the key will be cleared.
    /// </summary>
    public static void SuppressNext(string key)
    {
        Controller.SuppressNext(key);
    }

    /// <summary>
    /// Checks if a suppression key is set and clears it if so.
    /// Returns true if the key was set (meaning the announcement should be suppressed).
    /// </summary>
    public static bool CheckAndClearSuppression(string key)
    {
        return Controller.CheckAndClearSuppression(key);
    }

    /// <summary>
    /// Marks a context as having been announced. Used for one-time announcements
    /// that should only happen once per context (e.g., description on first focus).
    /// </summary>
    public static void MarkContextAnnounced(string contextKey)
    {
        Controller.MarkContextAnnounced(contextKey);
    }

    /// <summary>
    /// Checks if a context has already been announced.
    /// </summary>
    public static bool WasContextAnnounced(string contextKey)
    {
        return Controller.WasContextAnnounced(contextKey);
    }

    /// <summary>
    /// Clears announced context keys. If prefix is provided, only clears keys
    /// that start with that prefix; otherwise clears all keys.
    /// </summary>
    public static void ClearContexts(string? prefix = null)
    {
        Controller.ClearContexts(prefix);
    }

    /// <summary>
    /// Sets a cooldown for a key that expires after the specified number of frames.
    /// Use IsOnCooldown() to check if the cooldown is still active.
    /// </summary>
    public static void SetCooldown(string key, uint frames)
    {
        Controller.SetCooldown(key, frames);
    }

    /// <summary>
    /// Checks if a cooldown is still active for the given key.
    /// </summary>
    public static bool IsOnCooldown(string key)
    {
        return Controller.IsOnCooldown(key);
    }

    /// <summary>
    /// Clears a specific cooldown.
    /// </summary>
    public static void ClearCooldown(string key)
    {
        Controller.ClearCooldown(key);
    }

    #endregion

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
