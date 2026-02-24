#nullable enable
using System;
using System.Collections.Generic;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Default implementation of <see cref="ISpeechService"/> that wraps a <see cref="SpeechController"/>.
/// </summary>
internal sealed class SpeechService : ISpeechService
{
    private readonly SpeechController _controller;

    public SpeechService(SpeechController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public IReadOnlyCollection<string> RecentMessages => _controller.GetSnapshot().RecentMessages;
    public bool SpeechEnabled => _controller.SpeechEnabled;
    public bool SpeechInterruptEnabled => _controller.InterruptEnabled;
    public bool HasPendingPrefix => _controller.HasPendingPrefix;

    public void Initialize() => _controller.Initialize();
    public void Unload() => _controller.Shutdown();

    public void Announce(
        string? message,
        bool force = false,
        ScreenReaderService.AnnouncementCategory category = ScreenReaderService.AnnouncementCategory.Default,
        bool allowWhenMuted = false,
        SpeechChannel channel = SpeechChannel.Primary,
        bool requestInterrupt = true)
    {
        _controller.Enqueue(
            new SpeechRequest(
                Text: message ?? string.Empty,
                Category: category,
                Channel: channel,
                Force: force,
                AllowWhenMuted: allowWhenMuted,
                RequestInterrupt: requestInterrupt));
    }

    public void Interrupt(SpeechChannel channel = SpeechChannel.Primary) => _controller.Interrupt(channel);

    public bool ToggleSpeechEnabled() => _controller.ToggleMute();
    public bool ToggleSpeechInterrupt() => _controller.ToggleInterrupts();

    public void SetPendingPrefix(string? prefix) => _controller.SetPendingPrefix(prefix);
    public void ClearPendingPrefix() => _controller.ClearPendingPrefix();
    public void EnqueuePrefix(string prefix) => _controller.EnqueuePrefix(prefix);
    public void ClearAllPrefixes() => _controller.ClearAllPrefixes();

    public void SuppressNext(string key) => _controller.SuppressNext(key);
    public bool CheckAndClearSuppression(string key) => _controller.CheckAndClearSuppression(key);

    public void MarkContextAnnounced(string contextKey) => _controller.MarkContextAnnounced(contextKey);
    public bool WasContextAnnounced(string contextKey) => _controller.WasContextAnnounced(contextKey);
    public void ClearContexts(string? prefix = null) => _controller.ClearContexts(prefix);

    public void SetCooldown(string key, uint frames) => _controller.SetCooldown(key, frames);
    public bool IsOnCooldown(string key) => _controller.IsOnCooldown(key);
    public void ClearCooldown(string key) => _controller.ClearCooldown(key);

    /// <summary>
    /// Gets the underlying controller for advanced operations.
    /// </summary>
    internal SpeechController Controller => _controller;

    /// <summary>
    /// Sets the log-only mode on the controller.
    /// </summary>
    internal void SetLogOnly(bool enabled) => _controller.SetLogOnly(enabled);

    /// <summary>
    /// Gets a diagnostic snapshot of the controller state.
    /// </summary>
    internal SpeechControllerSnapshot GetSnapshot() => _controller.GetSnapshot();
}

/// <summary>
/// Static facade for the speech service, maintaining backward compatibility.
/// Provides access to speech functionality through static methods.
/// For new code, prefer injecting <see cref="ISpeechService"/> instead.
/// </summary>
public static class ScreenReaderService
{
    /// <summary>
    /// Announcement category for rate limiting.
    /// This type allows existing code using ScreenReaderService.AnnouncementCategory to continue working.
    /// </summary>
    public enum AnnouncementCategory
    {
        /// <summary>Default announcements with standard rate limiting.</summary>
        Default = 0,

        /// <summary>Tile-related announcements with shorter rate limiting.</summary>
        Tile = 1,

        /// <summary>Wall-related announcements with shorter rate limiting.</summary>
        Wall = 2,

        /// <summary>Item pickup announcements with shorter rate limiting.</summary>
        Pickup = 3,

        /// <summary>World event announcements with longer rate limiting.</summary>
        World = 4,
    }

    private static SpeechService? _service;
    private static ISpeechService? _customService;

    private static SpeechService Service => _service ??= BuildService();

    /// <summary>
    /// Gets or sets a custom speech service implementation.
    /// When set, all static methods delegate to this service instead of the default.
    /// Useful for testing or alternative implementations.
    /// </summary>
    public static ISpeechService? CustomService
    {
        get => _customService;
        set => _customService = value;
    }

    /// <summary>
    /// Gets the active speech service (custom if set, otherwise default).
    /// </summary>
    public static ISpeechService ActiveService => _customService ?? Service;

    /// <summary>
    /// Gets the collection of recent messages for diagnostics.
    /// </summary>
    public static IReadOnlyCollection<string> Snapshot => ActiveService.RecentMessages;

    /// <summary>
    /// Gets whether speech output is currently enabled.
    /// </summary>
    public static bool SpeechEnabled => ActiveService.SpeechEnabled;

    /// <summary>
    /// Gets whether speech interruption is enabled.
    /// </summary>
    public static bool SpeechInterruptEnabled => ActiveService.SpeechInterruptEnabled;

    /// <summary>
    /// Gets whether there is a pending prefix waiting to be used.
    /// </summary>
    public static bool HasPendingPrefix => ActiveService.HasPendingPrefix;

    /// <summary>
    /// Initializes the speech service.
    /// </summary>
    public static void Initialize()
    {
        SpeechService service = Service;
        service.SetLogOnly(ScreenReaderDiagnostics.IsSpeechLogOnlyEnabled());
        service.Initialize();
        ScreenReaderDiagnostics.DumpStartupSnapshot(service.GetSnapshot());
    }

    /// <summary>
    /// Shuts down the speech service and releases resources.
    /// </summary>
    public static void Unload()
    {
        _service?.Unload();
        _service = null;
        _customService = null;
    }

    /// <summary>
    /// Interrupts current speech on the specified channel.
    /// </summary>
    public static void Interrupt(SpeechChannel channel = SpeechChannel.Primary)
    {
        ActiveService.Interrupt(channel);
    }

    /// <summary>
    /// Sets a prefix that will be prepended to the next announcement.
    /// </summary>
    public static void SetPendingPrefix(string? prefix)
    {
        ActiveService.SetPendingPrefix(prefix);
    }

    /// <summary>
    /// Clears any pending prefix without using it.
    /// </summary>
    public static void ClearPendingPrefix()
    {
        ActiveService.ClearPendingPrefix();
    }

    #region Extended Speech Queue System

    /// <summary>
    /// Enqueues a prefix to be prepended to the next announcement.
    /// </summary>
    public static void EnqueuePrefix(string prefix)
    {
        ActiveService.EnqueuePrefix(prefix);
    }

    /// <summary>
    /// Clears all pending prefixes without using them.
    /// </summary>
    public static void ClearAllPrefixes()
    {
        ActiveService.ClearAllPrefixes();
    }

    /// <summary>
    /// Marks a key for one-shot suppression.
    /// </summary>
    public static void SuppressNext(string key)
    {
        ActiveService.SuppressNext(key);
    }

    /// <summary>
    /// Checks if a suppression key is set and clears it if so.
    /// </summary>
    public static bool CheckAndClearSuppression(string key)
    {
        return ActiveService.CheckAndClearSuppression(key);
    }

    /// <summary>
    /// Marks a context as having been announced.
    /// </summary>
    public static void MarkContextAnnounced(string contextKey)
    {
        ActiveService.MarkContextAnnounced(contextKey);
    }

    /// <summary>
    /// Checks if a context has already been announced.
    /// </summary>
    public static bool WasContextAnnounced(string contextKey)
    {
        return ActiveService.WasContextAnnounced(contextKey);
    }

    /// <summary>
    /// Clears announced context keys.
    /// </summary>
    public static void ClearContexts(string? prefix = null)
    {
        ActiveService.ClearContexts(prefix);
    }

    /// <summary>
    /// Sets a cooldown for a key that expires after the specified number of frames.
    /// </summary>
    public static void SetCooldown(string key, uint frames)
    {
        ActiveService.SetCooldown(key, frames);
    }

    /// <summary>
    /// Checks if a cooldown is still active for the given key.
    /// </summary>
    public static bool IsOnCooldown(string key)
    {
        return ActiveService.IsOnCooldown(key);
    }

    /// <summary>
    /// Clears a specific cooldown.
    /// </summary>
    public static void ClearCooldown(string key)
    {
        ActiveService.ClearCooldown(key);
    }

    #endregion

    /// <summary>
    /// Toggles speech interruption on/off.
    /// </summary>
    public static bool ToggleSpeechInterrupt()
    {
        return ActiveService.ToggleSpeechInterrupt();
    }

    /// <summary>
    /// Toggles speech output on/off.
    /// </summary>
    public static bool ToggleSpeechEnabled()
    {
        return ActiveService.ToggleSpeechEnabled();
    }

    /// <summary>
    /// Announces a message through the screen reader.
    /// </summary>
    public static void Announce(
        string? message,
        bool force = false,
        AnnouncementCategory category = AnnouncementCategory.Default,
        bool allowWhenMuted = false,
        SpeechChannel channel = SpeechChannel.Primary,
        bool requestInterrupt = true)
    {
        RecordInstrumentationKey(message);
        ActiveService.Announce(
            message,
            force,
            category,
            allowWhenMuted,
            channel,
            requestInterrupt);
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

    private static SpeechService BuildService()
    {
        var provider = new TolkSpeechProvider();
        var controller = new SpeechController(provider);
        controller.SetCategoryWindow(AnnouncementCategory.World, TimeSpan.FromSeconds(2));
        controller.SetCategoryWindow(AnnouncementCategory.Tile, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Wall, TimeSpan.FromMilliseconds(150));
        controller.SetCategoryWindow(AnnouncementCategory.Pickup, TimeSpan.FromMilliseconds(150));
        return new SpeechService(controller);
    }
}
