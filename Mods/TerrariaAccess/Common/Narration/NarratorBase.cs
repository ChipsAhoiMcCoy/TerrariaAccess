#nullable enable
using System;
using TerrariaAccess.Common.Services;
using Terraria;
using AnnouncementCategory = TerrariaAccess.Common.Services.ScreenReaderService.AnnouncementCategory;

namespace TerrariaAccess.Common.Narration;

/// <summary>
/// Abstract base class for narrators providing common functionality.
/// Narrators can extend this class to get built-in support for:
/// - Focus tracking with change detection
/// - Announcement throttling and deduplication
/// - State reset on context changes
/// - Logging and diagnostics
/// </summary>
public abstract class NarratorBase : INarrator
{
    private readonly AnnouncementThrottler _throttler = new();
    private readonly StringFocusTracker _focusKeyTracker = new();
    private bool _isInitialized;
    private uint _lastResetFrame;

    /// <summary>
    /// Creates a narrator with the specified name.
    /// </summary>
    /// <param name="name">Unique name for this narrator.</param>
    protected NarratorBase(string name)
    {
        Name = name;
    }

    #region INarrator Implementation

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public virtual NarrationPriority Priority => NarrationPriority.Normal;

    /// <inheritdoc />
    public virtual bool IsActive => true;

    /// <inheritdoc />
    public void Update(NarratorContext context)
    {
        if (!_isInitialized)
        {
            OnInitialize();
            _isInitialized = true;
        }

        if (!IsActive)
        {
            return;
        }

        if (!ShouldUpdate(context))
        {
            return;
        }

        OnUpdate(context);
    }

    /// <inheritdoc />
    public void Reset()
    {
        uint currentFrame = Main.GameUpdateCount;

        // Prevent multiple resets in the same frame
        if (currentFrame == _lastResetFrame)
        {
            return;
        }

        _lastResetFrame = currentFrame;
        _throttler.Clear();
        _focusKeyTracker.Clear();
        OnReset();
    }

    #endregion

    #region Protected Properties

    /// <summary>
    /// The current game frame number.
    /// </summary>
    protected uint CurrentFrame => Main.GameUpdateCount;

    /// <summary>
    /// Access to the announcement throttler for custom throttling logic.
    /// </summary>
    protected AnnouncementThrottler Throttler => _throttler;

    /// <summary>
    /// Access to the focus key tracker for custom focus tracking.
    /// </summary>
    protected StringFocusTracker FocusKeyTracker => _focusKeyTracker;

    #endregion

    #region Template Methods (Override in Subclasses)

    /// <summary>
    /// Called once when the narrator is first updated.
    /// Override to perform one-time initialization.
    /// </summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>
    /// Determines whether the narrator should update this frame.
    /// Override to add custom activation conditions.
    /// </summary>
    protected virtual bool ShouldUpdate(NarratorContext context)
    {
        return true;
    }

    /// <summary>
    /// Main update logic. Called each frame when narrator is active.
    /// Override to implement narrator-specific behavior.
    /// </summary>
    protected abstract void OnUpdate(NarratorContext context);

    /// <summary>
    /// Called when Reset() is invoked.
    /// Override to clear narrator-specific state.
    /// </summary>
    protected virtual void OnReset()
    {
    }

    #endregion

    #region Announcement Helpers

    /// <summary>
    /// Announces text through the screen reader with standard options.
    /// Automatically handles throttling and deduplication.
    /// </summary>
    /// <param name="text">Text to announce.</param>
    /// <param name="force">Bypass throttling if true.</param>
    /// <param name="category">Announcement category for rate limiting.</param>
    /// <returns>True if the announcement was made.</returns>
    protected bool Announce(
        string? text,
        bool force = false,
        AnnouncementCategory category = AnnouncementCategory.Default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (_throttler.ShouldThrottle(text, CurrentFrame, force))
        {
            return false;
        }

        ScreenReaderService.Announce(text, force, category);
        _throttler.Record(text, CurrentFrame);
        return true;
    }

    /// <summary>
    /// Announces text with a specific key for deduplication tracking.
    /// Different keys allow the same text to be announced in different contexts.
    /// </summary>
    /// <param name="key">Unique key for deduplication.</param>
    /// <param name="text">Text to announce.</param>
    /// <param name="force">Bypass throttling if true.</param>
    /// <param name="cooldownFrames">Custom cooldown (0 for default).</param>
    /// <param name="category">Announcement category.</param>
    /// <returns>True if the announcement was made.</returns>
    protected bool AnnounceWithKey(
        string key,
        string? text,
        bool force = false,
        uint cooldownFrames = 0,
        AnnouncementCategory category = AnnouncementCategory.Default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (_throttler.ShouldThrottle(key, text, CurrentFrame, cooldownFrames, force))
        {
            return false;
        }

        ScreenReaderService.Announce(text, force, category);
        _throttler.Record(key, text, CurrentFrame);
        return true;
    }

    /// <summary>
    /// Announces a NarrationRequest directly.
    /// </summary>
    /// <param name="request">The request to announce.</param>
    /// <returns>True if the announcement was made.</returns>
    protected bool Announce(NarrationRequest request)
    {
        string? key = request.DeduplicationKey;
        bool useKey = !string.IsNullOrWhiteSpace(key);

        if (useKey)
        {
            if (_throttler.ShouldThrottle(key!, request.Text, CurrentFrame, force: request.Force))
            {
                return false;
            }
        }
        else
        {
            if (_throttler.ShouldThrottle(request.Text, CurrentFrame, request.Force))
            {
                return false;
            }
        }

        ScreenReaderService.Announce(
            request.Text,
            request.Force,
            request.Category,
            request.AllowWhenMuted,
            requestInterrupt: request.RequestInterrupt);

        if (useKey)
        {
            _throttler.Record(key!, request.Text, CurrentFrame);
        }
        else
        {
            _throttler.Record(request.Text, CurrentFrame);
        }

        return true;
    }

    #endregion

    #region Focus Tracking Helpers

    /// <summary>
    /// Tracks a focus key and returns true if it changed.
    /// Automatically plays a tick sound on change.
    /// </summary>
    /// <param name="focusKey">The new focus key.</param>
    /// <param name="playTick">Whether to play a tick sound on change.</param>
    /// <returns>True if focus changed.</returns>
    protected bool UpdateFocusKey(string? focusKey, bool playTick = true)
    {
        bool changed = _focusKeyTracker.Update(focusKey, CurrentFrame);

        if (changed && playTick)
        {
            PlayFocusTick();
        }

        return changed;
    }

    /// <summary>
    /// Checks if a focus key would represent a change.
    /// Does not update the tracked state.
    /// </summary>
    protected bool HasFocusKeyChanged(string? focusKey, out string? previous)
    {
        return _focusKeyTracker.HasFocusChanged(focusKey, out previous);
    }

    /// <summary>
    /// Gets the current focus key.
    /// </summary>
    protected string? CurrentFocusKey => _focusKeyTracker.Current;

    /// <summary>
    /// Gets the previous focus key.
    /// </summary>
    protected string? PreviousFocusKey => _focusKeyTracker.Previous;

    /// <summary>
    /// Plays the standard menu tick sound for focus changes.
    /// Override to customize the sound.
    /// </summary>
    protected virtual void PlayFocusTick()
    {
        Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
    }

    #endregion

    #region Logging Helpers

    /// <summary>
    /// Logs a debug message with the narrator name prefix.
    /// </summary>
    protected void LogDebug(string message)
    {
        TerrariaAccess.Instance?.Logger.Debug($"[{Name}] {message}");
    }

    /// <summary>
    /// Logs an info message with the narrator name prefix.
    /// </summary>
    protected void LogInfo(string message)
    {
        TerrariaAccess.Instance?.Logger.Info($"[{Name}] {message}");
    }

    /// <summary>
    /// Logs a warning message with the narrator name prefix.
    /// </summary>
    protected void LogWarn(string message)
    {
        TerrariaAccess.Instance?.Logger.Warn($"[{Name}] {message}");
    }

    #endregion
}

/// <summary>
/// Base class for narrators that can be suppressed.
/// </summary>
public abstract class SuppressableNarratorBase : NarratorBase, ISuppressableNarrator
{
    private bool _isSuppressed;

    protected SuppressableNarratorBase(string name) : base(name)
    {
    }

    /// <inheritdoc />
    public bool IsSuppressed => _isSuppressed;

    /// <inheritdoc />
    public override bool IsActive => base.IsActive && !_isSuppressed;

    /// <inheritdoc />
    public void Suppress()
    {
        _isSuppressed = true;
        OnSuppressed();
    }

    /// <inheritdoc />
    public void Resume()
    {
        _isSuppressed = false;
        OnResumed();
    }

    /// <summary>
    /// Called when the narrator is suppressed.
    /// Override to perform cleanup when suppression starts.
    /// </summary>
    protected virtual void OnSuppressed()
    {
    }

    /// <summary>
    /// Called when the narrator is resumed after suppression.
    /// Override to reinitialize state when resuming.
    /// </summary>
    protected virtual void OnResumed()
    {
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
        _isSuppressed = false;
        base.OnReset();
    }
}

/// <summary>
/// Base class for narrators that respond to events from other narrators.
/// </summary>
public abstract class EventAwareNarratorBase : NarratorBase, IEventAwareNarrator
{
    protected EventAwareNarratorBase(string name) : base(name)
    {
    }

    /// <inheritdoc />
    public virtual void OnEvent(string eventName, object? data = null)
    {
        // Default implementation does nothing.
        // Override in subclasses to handle specific events.
    }
}
