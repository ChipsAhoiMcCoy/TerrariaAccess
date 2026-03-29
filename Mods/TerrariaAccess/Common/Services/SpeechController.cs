#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariaAccess.Common.Services;

// Use the AnnouncementCategory from ScreenReaderService for backward compatibility
using AnnouncementCategory = ScreenReaderService.AnnouncementCategory;

/// <summary>
/// Speech channels for routing announcements to different providers.
/// </summary>
public enum SpeechChannel
{
    /// <summary>Primary speech channel for most announcements.</summary>
    Primary,

    /// <summary>World announcement channel for event notifications.</summary>
    World,
}

/// <summary>
/// Represents a request to speak a message.
/// </summary>
internal readonly record struct SpeechRequest(
    string Text,
    AnnouncementCategory Category,
    SpeechChannel Channel,
    bool Force,
    bool AllowWhenMuted,
    bool RequestInterrupt);

/// <summary>
/// Snapshot of the speech controller state for diagnostics.
/// </summary>
internal readonly record struct SpeechControllerSnapshot(
    bool Initialized,
    bool Muted,
    bool InterruptEnabled,
    bool LogOnly,
    IReadOnlyList<string> RecentMessages,
    IReadOnlyDictionary<AnnouncementCategory, string?> LastCategoryMessages,
    IReadOnlyList<SpeechProviderSnapshot> Providers);

/// <summary>
/// Manages speech announcement coordination including prefix queues,
/// suppression keys, context tracking, and cooldowns.
/// Thread-safe for all operations.
/// </summary>
internal sealed class SpeechCoordinator
{
    private readonly object _syncRoot = new();

    // Prefix queue system
    private string? _pendingPrefix;
    private readonly Queue<string> _pendingPrefixes = new();

    // Suppression system
    private readonly HashSet<string> _suppressionKeys = new();

    // Context tracking (one-time announcements)
    private readonly HashSet<string> _announcedOnceKeys = new();

    // Frame-based cooldowns
    private readonly Dictionary<string, uint> _cooldownEndFrames = new();

    /// <summary>
    /// Gets whether there is a pending prefix waiting to be used.
    /// </summary>
    public bool HasPendingPrefix
    {
        get
        {
            lock (_syncRoot)
            {
                return !string.IsNullOrWhiteSpace(_pendingPrefix) || _pendingPrefixes.Count > 0;
            }
        }
    }

    /// <summary>
    /// Resets all coordinator state.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            _pendingPrefix = null;
            _pendingPrefixes.Clear();
            _suppressionKeys.Clear();
            _announcedOnceKeys.Clear();
            _cooldownEndFrames.Clear();
        }
    }

    #region Prefix Queue

    /// <summary>
    /// Sets a prefix that will be prepended to the next announcement.
    /// Replaces any existing single pending prefix.
    /// </summary>
    public void SetPendingPrefix(string? prefix)
    {
        lock (_syncRoot)
        {
            _pendingPrefix = prefix;
        }
    }

    /// <summary>
    /// Clears the pending prefix without using it.
    /// </summary>
    public void ClearPendingPrefix()
    {
        lock (_syncRoot)
        {
            _pendingPrefix = null;
        }
    }

    /// <summary>
    /// Enqueues a prefix to be prepended to the next announcement.
    /// Multiple prefixes can be queued and will be combined.
    /// </summary>
    public void EnqueuePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        lock (_syncRoot)
        {
            _pendingPrefixes.Enqueue(prefix.Trim());
        }
    }

    /// <summary>
    /// Clears all pending prefixes (both single and queued) without using them.
    /// </summary>
    public void ClearAllPrefixes()
    {
        lock (_syncRoot)
        {
            _pendingPrefix = null;
            _pendingPrefixes.Clear();
        }
    }

    /// <summary>
    /// Consumes and returns all pending prefixes, combining them into a single string.
    /// Returns null if no prefixes are pending.
    /// </summary>
    public string? ConsumeAllPrefixes()
    {
        lock (_syncRoot)
        {
            var prefixParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(_pendingPrefix))
            {
                prefixParts.Add(_pendingPrefix);
                _pendingPrefix = null;
            }

            while (_pendingPrefixes.Count > 0)
            {
                string queuedPrefix = _pendingPrefixes.Dequeue();
                if (!string.IsNullOrWhiteSpace(queuedPrefix))
                {
                    prefixParts.Add(queuedPrefix);
                }
            }

            return prefixParts.Count > 0 ? string.Join(". ", prefixParts) : null;
        }
    }

    #endregion

    #region Suppression

    /// <summary>
    /// Marks a key for one-shot suppression.
    /// </summary>
    public void SuppressNext(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_syncRoot)
        {
            _suppressionKeys.Add(key);
        }
    }

    /// <summary>
    /// Checks if a suppression key is set and clears it if so.
    /// </summary>
    /// <returns>True if the key was set (meaning the announcement should be suppressed).</returns>
    public bool CheckAndClearSuppression(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _suppressionKeys.Remove(key);
        }
    }

    #endregion

    #region Context Tracking

    /// <summary>
    /// Marks a context as having been announced.
    /// </summary>
    public void MarkContextAnnounced(string contextKey)
    {
        if (string.IsNullOrWhiteSpace(contextKey))
        {
            return;
        }

        lock (_syncRoot)
        {
            _announcedOnceKeys.Add(contextKey);
        }
    }

    /// <summary>
    /// Checks if a context has already been announced.
    /// </summary>
    public bool WasContextAnnounced(string contextKey)
    {
        if (string.IsNullOrWhiteSpace(contextKey))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _announcedOnceKeys.Contains(contextKey);
        }
    }

    /// <summary>
    /// Clears announced context keys.
    /// </summary>
    /// <param name="prefix">If provided, only clears keys starting with this prefix.</param>
    public void ClearContexts(string? prefix = null)
    {
        lock (_syncRoot)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                _announcedOnceKeys.Clear();
                _suppressionKeys.Clear();
            }
            else
            {
                _announcedOnceKeys.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal));
                _suppressionKeys.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal));
            }
        }
    }

    #endregion

    #region Cooldowns

    /// <summary>
    /// Sets a cooldown for a key that expires after the specified number of frames.
    /// </summary>
    public void SetCooldown(string key, uint frames, uint currentFrame)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_syncRoot)
        {
            _cooldownEndFrames[key] = currentFrame + frames;
        }
    }

    /// <summary>
    /// Checks if a cooldown is still active for the given key.
    /// </summary>
    public bool IsOnCooldown(string key, uint currentFrame)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (!_cooldownEndFrames.TryGetValue(key, out uint endFrame))
            {
                return false;
            }

            // Handle frame counter wrap-around
            if (currentFrame >= endFrame)
            {
                _cooldownEndFrames.Remove(key);
                return false;
            }

            // Check for wrap-around case
            if (endFrame - currentFrame > uint.MaxValue / 2)
            {
                _cooldownEndFrames.Remove(key);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Clears a specific cooldown.
    /// </summary>
    public void ClearCooldown(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_syncRoot)
        {
            _cooldownEndFrames.Remove(key);
        }
    }

    #endregion
}

/// <summary>
/// Tracks announcement history for duplicate suppression.
/// Thread-safe for all operations.
/// </summary>
internal sealed class AnnouncementTracker
{
    private const int MaxRecentMessages = 25;

    private readonly object _syncRoot = new();
    private readonly Queue<string> _recentMessages = new();
    private readonly Dictionary<AnnouncementCategory, string?> _lastCategoryAnnouncements = new();

    private string? _lastMessage;
    private AnnouncementCategory _lastCategory = AnnouncementCategory.Default;

    public AnnouncementTracker()
    {
    }

    /// <summary>
    /// Gets the recent messages for diagnostics.
    /// </summary>
    public IReadOnlyList<string> GetRecentMessages()
    {
        lock (_syncRoot)
        {
            return _recentMessages.ToArray();
        }
    }

    /// <summary>
    /// Gets a copy of the last category announcements for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<AnnouncementCategory, string?> GetLastCategoryMessages()
    {
        lock (_syncRoot)
        {
            return new Dictionary<AnnouncementCategory, string?>(_lastCategoryAnnouncements);
        }
    }

    /// <summary>
    /// Sets the repeat suppression window for a category.
    /// </summary>
    public void SetCategoryWindow(AnnouncementCategory category, TimeSpan window)
    {
    }

    /// <summary>
    /// Resets all tracking state.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            _recentMessages.Clear();
            _lastCategoryAnnouncements.Clear();
            _lastMessage = null;
            _lastCategory = AnnouncementCategory.Default;
        }
    }

    /// <summary>
    /// Checks if an announcement should be suppressed due to repeat within the window.
    /// </summary>
    public bool ShouldSuppress(string text, AnnouncementCategory category, DateTime now)
    {
        lock (_syncRoot)
        {
            return string.Equals(text, _lastMessage, StringComparison.OrdinalIgnoreCase) &&
                category == _lastCategory;
        }
    }

    /// <summary>
    /// Records an announcement for tracking.
    /// </summary>
    public void Track(string text, AnnouncementCategory category, DateTime now)
    {
        lock (_syncRoot)
        {
            _lastCategoryAnnouncements[category] = text;
            _lastMessage = text;
            _lastCategory = category;

            _recentMessages.Enqueue(text);
            while (_recentMessages.Count > MaxRecentMessages)
            {
                _recentMessages.Dequeue();
            }
        }
    }

}

/// <summary>
/// Controls speech output, managing providers, queueing, and coordination.
/// </summary>
internal sealed class SpeechController
{
    private readonly object _syncRoot = new();
    private readonly Queue<SpeechRequest> _pending = new();
    private readonly Queue<SpeechRequest> _deferred = new();
    private readonly Dictionary<SpeechChannel, ISpeechProvider> _providersByChannel = new();
    private readonly List<ISpeechProvider> _providers = new();
    private readonly SpeechCoordinator _coordinator = new();
    private readonly AnnouncementTracker _tracker = new();

    private bool _initialized;
    private bool _muted;
    private bool _interruptEnabled = true;
    private bool _logOnly;

    /// <summary>
    /// Gets the speech coordinator for prefix/suppression/context/cooldown management.
    /// </summary>
    internal SpeechCoordinator Coordinator => _coordinator;

    /// <summary>
    /// Gets the announcement tracker for history management.
    /// </summary>
    internal AnnouncementTracker Tracker => _tracker;

    internal SpeechController(ISpeechProvider primary, ISpeechProvider? worldAnnouncement = null)
    {
        _providers.Add(primary);
        _providersByChannel[SpeechChannel.Primary] = primary;

        if (worldAnnouncement is not null)
        {
            _providers.Add(worldAnnouncement);
            _providersByChannel[SpeechChannel.World] = worldAnnouncement;
        }
        else
        {
            _providersByChannel[SpeechChannel.World] = primary;
        }
    }

    internal void Initialize()
    {
        lock (_syncRoot)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _muted = false;
            _interruptEnabled = true;
            _pending.Clear();
            _deferred.Clear();
        }

        _coordinator.Reset();
        _tracker.Reset();

        foreach (ISpeechProvider provider in _providers)
        {
            provider.Initialize();
        }
    }

    internal void Shutdown()
    {
        foreach (ISpeechProvider provider in _providers)
        {
            provider.Shutdown();
        }

        lock (_syncRoot)
        {
            _pending.Clear();
            _deferred.Clear();
            _initialized = false;
            _muted = false;
            _interruptEnabled = false;
        }

        _coordinator.Reset();
        _tracker.Reset();
    }

    internal bool ToggleMute()
    {
        lock (_syncRoot)
        {
            _muted = !_muted;
            return !_muted;
        }
    }

    internal bool ToggleInterrupts()
    {
        lock (_syncRoot)
        {
            _interruptEnabled = !_interruptEnabled;
            return _interruptEnabled;
        }
    }

    internal void SetLogOnly(bool enabled)
    {
        lock (_syncRoot)
        {
            _logOnly = enabled;
        }
    }

    internal void SetCategoryWindow(AnnouncementCategory category, TimeSpan window)
    {
        _tracker.SetCategoryWindow(category, window);
    }

    internal bool SpeechEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return !_muted;
            }
        }
    }

    internal bool InterruptEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _interruptEnabled;
            }
        }
    }

    #region Coordinator Delegation (for backward compatibility)

    internal bool HasPendingPrefix => _coordinator.HasPendingPrefix;
    internal void SetPendingPrefix(string? prefix) => _coordinator.SetPendingPrefix(prefix);
    internal void ClearPendingPrefix() => _coordinator.ClearPendingPrefix();
    internal void EnqueuePrefix(string prefix) => _coordinator.EnqueuePrefix(prefix);
    internal void ClearAllPrefixes() => _coordinator.ClearAllPrefixes();
    internal void SuppressNext(string key) => _coordinator.SuppressNext(key);
    internal bool CheckAndClearSuppression(string key) => _coordinator.CheckAndClearSuppression(key);
    internal void MarkContextAnnounced(string contextKey) => _coordinator.MarkContextAnnounced(contextKey);
    internal bool WasContextAnnounced(string contextKey) => _coordinator.WasContextAnnounced(contextKey);
    internal void ClearContexts(string? prefix = null) => _coordinator.ClearContexts(prefix);

    internal void SetCooldown(string key, uint frames)
    {
        _coordinator.SetCooldown(key, frames, GetCurrentFrame());
    }

    internal bool IsOnCooldown(string key)
    {
        return _coordinator.IsOnCooldown(key, GetCurrentFrame());
    }

    internal void ClearCooldown(string key)
    {
        _coordinator.ClearCooldown(key);
    }

    private static uint GetCurrentFrame()
    {
        return Terraria.Main.GameUpdateCount;
    }

    #endregion

    internal SpeechControllerSnapshot GetSnapshot()
    {
        bool initialized, muted, interruptEnabled, logOnly;

        lock (_syncRoot)
        {
            initialized = _initialized;
            muted = _muted;
            interruptEnabled = _interruptEnabled;
            logOnly = _logOnly;
        }

        return new SpeechControllerSnapshot(
            Initialized: initialized,
            Muted: muted,
            InterruptEnabled: interruptEnabled,
            LogOnly: logOnly,
            RecentMessages: _tracker.GetRecentMessages(),
            LastCategoryMessages: _tracker.GetLastCategoryMessages(),
            Providers: _providers.Select(p => p.GetSnapshot()).ToArray());
    }

    internal void Interrupt(SpeechChannel channel = SpeechChannel.Primary)
    {
        bool interruptEnabled;
        lock (_syncRoot)
        {
            interruptEnabled = _interruptEnabled;
        }

        if (!interruptEnabled)
        {
            return;
        }

        ISpeechProvider provider = ResolveProvider(channel);
        provider.Interrupt();
    }

    internal void Enqueue(SpeechRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return;
        }

        string trimmed = request.Text.Trim();
        DateTime now = DateTime.UtcNow;

        bool shouldProcess;
        lock (_syncRoot)
        {
            if (_muted && !request.AllowWhenMuted)
            {
                ScreenReaderDiagnostics.LogSpeechSuppressed(request with { Text = trimmed }, "muted");
                return;
            }

            shouldProcess = true;
        }

        if (!shouldProcess)
        {
            return;
        }

        // Consume and prepend any pending prefixes
        string? combinedPrefix = _coordinator.ConsumeAllPrefixes();
        if (combinedPrefix is not null)
        {
            trimmed = $"{combinedPrefix}. {trimmed}";
        }

        SpeechRequest normalized = request with { Text = trimmed };

        // Check for repeat suppression
        if (!normalized.Force && _tracker.ShouldSuppress(normalized.Text, normalized.Category, now))
        {
            ScreenReaderDiagnostics.LogSpeechSuppressed(normalized, "repeat");
            return;
        }

        _tracker.Track(normalized.Text, normalized.Category, now);

        lock (_syncRoot)
        {
            // Non-interrupting announcements should be handed to the provider immediately
            // so Tolk can queue them natively. Relying on IsSpeaking here can strand short
            // follow-up messages like menu defaults ("Yes", "Play, <name>") behind an
            // unreliable provider speaking state.
            _pending.Enqueue(normalized);
        }

        FlushQueue();
    }

    internal void Pump()
    {
        FlushDeferredQueue();
    }

    private void FlushQueue()
    {
        while (true)
        {
            SpeechRequest request;
            lock (_syncRoot)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                request = _pending.Dequeue();
            }

            Deliver(request);
        }
    }

    private void Deliver(SpeechRequest request)
    {
        ISpeechProvider provider = ResolveProvider(request.Channel);

        bool logOnly, interruptEnabled;
        lock (_syncRoot)
        {
            logOnly = _logOnly;
            interruptEnabled = _interruptEnabled;
        }

        if (logOnly)
        {
            LogNarration(request, provider, logOnly: true);
            ScreenReaderDiagnostics.LogSpeechEvent(request, provider.Name, logOnly: true);
            return;
        }

        if (interruptEnabled && request.RequestInterrupt)
        {
            Interrupt(SpeechChannel.Primary);
        }

        try
        {
            provider.Speak(request.Text);
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[Speech] Provider {provider.Name} failed: {ex.Message}");
        }

        LogNarration(request, provider, logOnly: false);
        ScreenReaderDiagnostics.LogSpeechEvent(request, provider.Name, logOnly: false);
    }

    private void FlushDeferredQueue()
    {
        while (true)
        {
            SpeechRequest request;
            lock (_syncRoot)
            {
                if (_deferred.Count == 0)
                {
                    return;
                }

                request = _deferred.Peek();
            }

            ISpeechProvider provider = ResolveProvider(request.Channel);
            if (provider.IsSpeaking)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_deferred.Count == 0)
                {
                    return;
                }

                _pending.Enqueue(_deferred.Dequeue());
            }

            FlushQueue();
        }
    }

    private void LogNarration(SpeechRequest request, ISpeechProvider provider, bool logOnly)
    {
        if (!ScreenReaderDiagnostics.IsTraceEnabled())
        {
            return;
        }

        string prefix = request.Channel == SpeechChannel.World ? "[WorldNarration]" : "[Narration]";
        TerrariaAccess.Instance?.Logger.Info($"{prefix} {request.Text}");

        if (logOnly)
        {
            TerrariaAccess.Instance?.Logger.Info($"[Narration] Log-only mode active. Provider={provider.Name} Channel={request.Channel}");
        }
    }

    private ISpeechProvider ResolveProvider(SpeechChannel channel)
    {
        if (_providersByChannel.TryGetValue(channel, out ISpeechProvider? provider) && provider.IsAvailable)
        {
            return provider;
        }

        if (_providersByChannel.TryGetValue(SpeechChannel.Primary, out ISpeechProvider? primary) && primary.IsAvailable)
        {
            return primary;
        }

        return provider ?? _providers.First();
    }
}
