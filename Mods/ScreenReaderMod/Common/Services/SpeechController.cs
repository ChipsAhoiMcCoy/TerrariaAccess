#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenReaderMod.Common.Services;

using AnnouncementCategory = global::ScreenReaderMod.Common.Services.ScreenReaderService.AnnouncementCategory;

public enum SpeechChannel
{
    Primary,
    World,
}

internal readonly record struct SpeechRequest(
    string Text,
    AnnouncementCategory Category,
    SpeechChannel Channel,
    bool Force,
    bool AllowWhenMuted,
    bool RequestInterrupt);

internal readonly record struct SpeechControllerSnapshot(
    bool Initialized,
    bool Muted,
    bool InterruptEnabled,
    bool LogOnly,
    IReadOnlyList<string> RecentMessages,
    IReadOnlyDictionary<AnnouncementCategory, string?> LastCategoryMessages,
    IReadOnlyList<SpeechProviderSnapshot> Providers);

internal sealed class SpeechController
{
    private const int MaxRecentMessages = 25;

    private readonly object _syncRoot = new();
    private readonly Queue<SpeechRequest> _pending = new();
    private readonly Queue<string> _recentMessages = new();
    private readonly Dictionary<AnnouncementCategory, string?> _lastCategoryAnnouncements = new();
    private readonly Dictionary<AnnouncementCategory, DateTime> _lastCategoryAnnouncedAt = new();
    private readonly Dictionary<AnnouncementCategory, TimeSpan> _categoryWindows = new();
    private readonly Dictionary<SpeechChannel, ISpeechProvider> _providersByChannel = new();
    private readonly List<ISpeechProvider> _providers = new();

    private string? _lastMessage;
    private DateTime _lastAnnouncedAt = DateTime.MinValue;
    private bool _initialized;
    private bool _muted;
    private bool _interruptEnabled = true;
    private bool _logOnly;

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

        _categoryWindows[AnnouncementCategory.Default] = TimeSpan.FromMilliseconds(250);
        _categoryWindows[AnnouncementCategory.Tile] = TimeSpan.FromMilliseconds(150);
        _categoryWindows[AnnouncementCategory.Wall] = TimeSpan.FromMilliseconds(150);
        _categoryWindows[AnnouncementCategory.Pickup] = TimeSpan.FromMilliseconds(150);
        _categoryWindows[AnnouncementCategory.World] = TimeSpan.FromSeconds(2);
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
            _recentMessages.Clear();
            _lastCategoryAnnouncements.Clear();
            _lastCategoryAnnouncedAt.Clear();
            _lastMessage = null;
            _lastAnnouncedAt = DateTime.MinValue;
        }

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
            _recentMessages.Clear();
            _lastCategoryAnnouncements.Clear();
            _lastCategoryAnnouncedAt.Clear();
            _lastMessage = null;
            _lastAnnouncedAt = DateTime.MinValue;
            _initialized = false;
            _muted = false;
            _interruptEnabled = false;
        }
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
        lock (_syncRoot)
        {
            _categoryWindows[category] = window;
        }
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

    internal SpeechControllerSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new SpeechControllerSnapshot(
                Initialized: _initialized,
                Muted: _muted,
                InterruptEnabled: _interruptEnabled,
                LogOnly: _logOnly,
                RecentMessages: _recentMessages.ToArray(),
                LastCategoryMessages: new Dictionary<AnnouncementCategory, string?>(_lastCategoryAnnouncements),
                Providers: _providers.Select(p => p.GetSnapshot()).ToArray());
        }
    }

    internal void Interrupt(SpeechChannel channel = SpeechChannel.Primary)
    {
        if (!_interruptEnabled)
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

        lock (_syncRoot)
        {
            if (_muted && !request.AllowWhenMuted)
            {
                ScreenReaderDiagnostics.LogSpeechSuppressed(request with { Text = trimmed }, "muted");
                return;
            }

            SpeechRequest normalized = request with { Text = trimmed };
            if (!normalized.Force && ShouldSuppress(normalized, now))
            {
                ScreenReaderDiagnostics.LogSpeechSuppressed(normalized, "repeat");
                return;
            }

            TrackAnnouncement(normalized, now);
            _pending.Enqueue(normalized);
        }

        FlushQueue();
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

        if (_logOnly)
        {
            LogNarration(request, provider, logOnly: true);
            ScreenReaderDiagnostics.LogSpeechEvent(request, provider.Name, logOnly: true);
            return;
        }

        if (_interruptEnabled && request.RequestInterrupt)
        {
            Interrupt(SpeechChannel.Primary);
        }

        try
        {
            provider.Speak(request.Text);
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[Speech] Provider {provider.Name} failed: {ex.Message}");
        }

        LogNarration(request, provider, logOnly: false);
        ScreenReaderDiagnostics.LogSpeechEvent(request, provider.Name, logOnly: false);
    }

    private void LogNarration(SpeechRequest request, ISpeechProvider provider, bool logOnly)
    {
        if (!ScreenReaderDiagnostics.IsTraceEnabled())
        {
            return;
        }

        string prefix = request.Channel == SpeechChannel.World ? "[WorldNarration]" : "[Narration]";
        ScreenReaderMod.Instance?.Logger.Info($"{prefix} {request.Text}");

        if (logOnly)
        {
            ScreenReaderMod.Instance?.Logger.Info($"[Narration] Log-only mode active. Provider={provider.Name} Channel={request.Channel}");
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

    private bool ShouldSuppress(SpeechRequest request, DateTime now)
    {
        if (_lastCategoryAnnouncements.TryGetValue(request.Category, out string? lastForCategory) &&
            string.Equals(lastForCategory, request.Text, StringComparison.OrdinalIgnoreCase))
        {
            DateTime lastAt = _lastCategoryAnnouncedAt.TryGetValue(request.Category, out DateTime last) ? last : DateTime.MinValue;
            if (now - lastAt < GetRepeatWindow(request.Category))
            {
                return true;
            }
        }

        if (request.Category == AnnouncementCategory.Default &&
            string.Equals(request.Text, _lastMessage, StringComparison.OrdinalIgnoreCase) &&
            now - _lastAnnouncedAt < GetRepeatWindow(AnnouncementCategory.Default))
        {
            return true;
        }

        return false;
    }

    private void TrackAnnouncement(SpeechRequest request, DateTime now)
    {
        _lastCategoryAnnouncements[request.Category] = request.Text;
        _lastCategoryAnnouncedAt[request.Category] = now;

        _lastMessage = request.Text;
        _lastAnnouncedAt = now;

        _recentMessages.Enqueue(request.Text);
        while (_recentMessages.Count > MaxRecentMessages)
        {
            _recentMessages.Dequeue();
        }
    }

    private TimeSpan GetRepeatWindow(AnnouncementCategory category)
    {
        if (_categoryWindows.TryGetValue(category, out TimeSpan window))
        {
            return window;
        }

        return _categoryWindows[AnnouncementCategory.Default];
    }
}
