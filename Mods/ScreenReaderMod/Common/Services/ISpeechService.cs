#nullable enable
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Services;

// Use the AnnouncementCategory defined in ScreenReaderService for backward compatibility
using AnnouncementCategory = ScreenReaderService.AnnouncementCategory;

/// <summary>
/// Defines the contract for speech announcement services.
/// This interface abstracts the speech functionality to enable testing and alternative implementations.
/// </summary>
public interface ISpeechService
{
    /// <summary>
    /// Gets the collection of recent messages for diagnostics.
    /// </summary>
    IReadOnlyCollection<string> RecentMessages { get; }

    /// <summary>
    /// Gets whether speech output is currently enabled.
    /// </summary>
    bool SpeechEnabled { get; }

    /// <summary>
    /// Gets whether speech interruption is enabled.
    /// </summary>
    bool SpeechInterruptEnabled { get; }

    /// <summary>
    /// Gets whether there is a pending prefix waiting to be prepended to the next announcement.
    /// </summary>
    bool HasPendingPrefix { get; }

    /// <summary>
    /// Initializes the speech service.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shuts down the speech service and releases resources.
    /// </summary>
    void Unload();

    /// <summary>
    /// Announces a message through the screen reader.
    /// </summary>
    /// <param name="message">The message to announce.</param>
    /// <param name="force">If true, bypasses duplicate suppression.</param>
    /// <param name="category">The announcement category for rate limiting.</param>
    /// <param name="allowWhenMuted">If true, announces even when speech is muted.</param>
    /// <param name="channel">The speech channel to use.</param>
    /// <param name="requestInterrupt">If true, interrupts current speech before announcing.</param>
    void Announce(
        string? message,
        bool force = false,
        AnnouncementCategory category = AnnouncementCategory.Default,
        bool allowWhenMuted = false,
        SpeechChannel channel = SpeechChannel.Primary,
        bool requestInterrupt = true);

    /// <summary>
    /// Interrupts current speech on the specified channel.
    /// </summary>
    /// <param name="channel">The channel to interrupt.</param>
    void Interrupt(SpeechChannel channel = SpeechChannel.Primary);

    /// <summary>
    /// Toggles speech output on/off.
    /// </summary>
    /// <returns>True if speech is now enabled, false if disabled.</returns>
    bool ToggleSpeechEnabled();

    /// <summary>
    /// Toggles speech interruption on/off.
    /// </summary>
    /// <returns>True if interruption is now enabled, false if disabled.</returns>
    bool ToggleSpeechInterrupt();

    #region Prefix Queue

    /// <summary>
    /// Sets a prefix that will be prepended to the next announcement.
    /// Replaces any existing pending prefix.
    /// </summary>
    /// <param name="prefix">The prefix to set, or null to clear.</param>
    void SetPendingPrefix(string? prefix);

    /// <summary>
    /// Clears the pending prefix without using it.
    /// </summary>
    void ClearPendingPrefix();

    /// <summary>
    /// Enqueues a prefix to be prepended to the next announcement.
    /// Multiple prefixes can be queued and will be combined.
    /// </summary>
    /// <param name="prefix">The prefix to enqueue.</param>
    void EnqueuePrefix(string prefix);

    /// <summary>
    /// Clears all pending prefixes without using them.
    /// </summary>
    void ClearAllPrefixes();

    #endregion

    #region Suppression

    /// <summary>
    /// Marks a key for one-shot suppression. The next announcement that checks
    /// this key will be suppressed, and the key will be cleared.
    /// </summary>
    /// <param name="key">The suppression key.</param>
    void SuppressNext(string key);

    /// <summary>
    /// Checks if a suppression key is set and clears it if so.
    /// </summary>
    /// <param name="key">The suppression key to check.</param>
    /// <returns>True if the key was set (meaning the announcement should be suppressed).</returns>
    bool CheckAndClearSuppression(string key);

    #endregion

    #region Context Tracking

    /// <summary>
    /// Marks a context as having been announced. Used for one-time announcements
    /// that should only happen once per context.
    /// </summary>
    /// <param name="contextKey">The context key to mark.</param>
    void MarkContextAnnounced(string contextKey);

    /// <summary>
    /// Checks if a context has already been announced.
    /// </summary>
    /// <param name="contextKey">The context key to check.</param>
    /// <returns>True if the context was previously announced.</returns>
    bool WasContextAnnounced(string contextKey);

    /// <summary>
    /// Clears announced context keys.
    /// </summary>
    /// <param name="prefix">If provided, only clears keys starting with this prefix; otherwise clears all.</param>
    void ClearContexts(string? prefix = null);

    #endregion

    #region Cooldowns

    /// <summary>
    /// Sets a cooldown for a key that expires after the specified number of frames.
    /// </summary>
    /// <param name="key">The cooldown key.</param>
    /// <param name="frames">Number of frames until the cooldown expires.</param>
    void SetCooldown(string key, uint frames);

    /// <summary>
    /// Checks if a cooldown is still active for the given key.
    /// </summary>
    /// <param name="key">The cooldown key to check.</param>
    /// <returns>True if the cooldown is still active.</returns>
    bool IsOnCooldown(string key);

    /// <summary>
    /// Clears a specific cooldown.
    /// </summary>
    /// <param name="key">The cooldown key to clear.</param>
    void ClearCooldown(string key);

    #endregion
}
