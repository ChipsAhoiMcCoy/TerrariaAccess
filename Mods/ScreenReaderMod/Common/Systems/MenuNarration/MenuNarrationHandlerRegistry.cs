#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ScreenReaderMod.Common.Systems.MenuNarration.Handlers;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

/// <summary>
/// Registry for menu narration handlers.
/// Handlers are sorted by priority (highest first) and the first matching handler is used.
/// </summary>
internal sealed class MenuNarrationHandlerRegistry
{
    private readonly List<IMenuHandler> _handlers = new();
    private IMenuHandler? _activeHandler;
    private int? _lastMenuMode;
    private UIState? _lastUiState;

    // Global announcement tracking that survives handler transitions
    private string? _lastGlobalAnnouncement;
    private DateTime _lastGlobalAnnouncementTime = DateTime.MinValue;
    private static readonly TimeSpan GlobalDeduplicationWindow = TimeSpan.FromMilliseconds(1200);

    // Tracks consecutive frames where UIState is null during a handler transition.
    // Prevents stale data announcements when Terraria swaps UIState objects over multiple frames.
    private int _transientNullSkipCount;
    private const int MaxTransientNullSkips = 30; // ~500ms at 60fps

    /// <summary>
    /// Creates a new registry with no handlers.
    /// Use RegisterHandler to add handlers.
    /// </summary>
    internal MenuNarrationHandlerRegistry()
    {
    }

    /// <summary>
    /// Creates a new registry with the specified handlers.
    /// Handlers will be sorted by priority (highest first).
    /// </summary>
    internal MenuNarrationHandlerRegistry(IEnumerable<IMenuHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            RegisterHandler(handler);
        }
    }

    /// <summary>
    /// Registers a handler with the registry.
    /// Handlers are sorted by priority (highest first) after registration.
    /// </summary>
    internal void RegisterHandler(IMenuHandler handler)
    {
        _handlers.Add(handler);
        // Sort by priority descending so highest priority handlers are checked first
        _handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>
    /// Gets all registered handlers (for diagnostic purposes).
    /// </summary>
    internal IReadOnlyList<IMenuHandler> Handlers => _handlers;

    /// <summary>
    /// Gets the currently active handler.
    /// </summary>
    internal IMenuHandler? ActiveHandler => _activeHandler;

    /// <summary>
    /// Processes the current menu context and returns narration events.
    /// </summary>
    internal IReadOnlyList<MenuNarrationEvent> Process(MenuNarrationContext context)
    {
        if (_handlers.Count == 0)
        {
            _activeHandler?.OnLeft();
            _activeHandler = null;
            _lastMenuMode = null;
            _lastUiState = null;
            return Array.Empty<MenuNarrationEvent>();
        }

        IMenuHandler? handler = ResolveHandler(context.MenuMode, context.UiState);
        if (handler is null)
        {
            _activeHandler?.OnLeft();
            _activeHandler = null;
            _lastMenuMode = context.MenuMode;
            _lastUiState = context.UiState;
            return Array.Empty<MenuNarrationEvent>();
        }

        bool handlerChanged = handler != _activeHandler;
        bool modeChanged = !_lastMenuMode.HasValue || _lastMenuMode.Value != context.MenuMode;
        bool uiStateChanged = !ReferenceEquals(_lastUiState, context.UiState);

        // When UIState transitions from non-null to null and the handler would change
        // to the FallbackHandler, skip frames until UIState recovers or a timeout expires.
        // This is a transient state while Terraria swaps UIState objects (e.g.,
        // UICharacterSelect -> null -> UIWorldSelect). Without this guard, the
        // FallbackHandler picks up null-UIState frames and announces stale menu items.
        // We only guard FallbackHandler transitions — specific handlers like
        // TitleMenuHandler know their own menu mode and can be trusted immediately.
        if (_lastUiState is not null && context.UiState is null && handlerChanged && handler is FallbackMenuHandler)
        {
            _transientNullSkipCount++;
            if (_transientNullSkipCount <= MaxTransientNullSkips)
            {
                ScreenReaderMod.Instance?.Logger.Debug(
                    $"[MenuRegistry] Skipping transient null-UIState frame {_transientNullSkipCount} (previous handler={_activeHandler?.GetType().Name})");
                _lastMenuMode = context.MenuMode;
                // Don't update _lastUiState — keep previous non-null value so this
                // guard keeps firing on subsequent null-UIState frames.
                return Array.Empty<MenuNarrationEvent>();
            }

            ScreenReaderMod.Instance?.Logger.Debug(
                $"[MenuRegistry] Transient null-UIState guard timed out after {_transientNullSkipCount} frames");
            _transientNullSkipCount = 0;
        }
        else
        {
            _transientNullSkipCount = 0;
        }

        if (handlerChanged)
        {
            ScreenReaderMod.Instance?.Logger.Debug(
                $"[MenuRegistry] Handler changed: {_activeHandler?.GetType().Name ?? "null"} -> {handler.GetType().Name} (mode={context.MenuMode})");
            _activeHandler?.OnLeft();
            handler.OnEntered(context);
            _activeHandler = handler;
        }
        else if (modeChanged || uiStateChanged)
        {
            ScreenReaderMod.Instance?.Logger.Debug(
                $"[MenuRegistry] Mode/state changed: handler={handler.GetType().Name}, mode={_lastMenuMode}->{context.MenuMode}");
            handler.OnEntered(context);
        }

        _lastMenuMode = context.MenuMode;
        _lastUiState = context.UiState;

        var events = new List<MenuNarrationEvent>();
        foreach (MenuNarrationEvent narrationEvent in handler.Update(context))
        {
            // Deduplicate against recent global announcements to prevent
            // redundant speech during handler transitions (e.g., returning to main menu)
            if (ShouldSuppress(narrationEvent, context.Timestamp))
            {
                ScreenReaderMod.Instance?.Logger.Debug(
                    $"[MenuRegistry] Suppressed duplicate: '{narrationEvent.Text}'");
                continue;
            }

            events.Add(narrationEvent);

            // Track this announcement globally
            if (!string.IsNullOrWhiteSpace(narrationEvent.Text))
            {
                _lastGlobalAnnouncement = narrationEvent.Text;
                _lastGlobalAnnouncementTime = context.Timestamp;
            }
        }

        return events;
    }

    /// <summary>
    /// Checks if an announcement should be suppressed as a duplicate of a recent global announcement.
    /// </summary>
    private bool ShouldSuppress(MenuNarrationEvent narrationEvent, DateTime timestamp)
    {
        // Don't suppress forced announcements (e.g., returning to the title menu)
        if (narrationEvent.Force)
        {
            return false;
        }

        // Don't suppress sliders or special features
        if (narrationEvent.Kind == MenuNarrationEventKind.Slider ||
            narrationEvent.Kind == MenuNarrationEventKind.ModConfig)
        {
            return false;
        }

        // Check if this matches a recent global announcement
        if (string.IsNullOrWhiteSpace(_lastGlobalAnnouncement) ||
            string.IsNullOrWhiteSpace(narrationEvent.Text))
        {
            return false;
        }

        bool matchesRecent = string.Equals(
            narrationEvent.Text,
            _lastGlobalAnnouncement,
            StringComparison.OrdinalIgnoreCase);

        if (!matchesRecent)
        {
            return false;
        }

        bool withinWindow = timestamp - _lastGlobalAnnouncementTime < GlobalDeduplicationWindow;
        return withinWindow;
    }

    /// <summary>
    /// Resets the registry state.
    /// </summary>
    internal void Reset()
    {
        _activeHandler?.OnLeft();
        _activeHandler = null;
        _lastMenuMode = null;
        _lastUiState = null;
        _lastGlobalAnnouncement = null;
        _lastGlobalAnnouncementTime = DateTime.MinValue;
        _transientNullSkipCount = 0;
    }

    /// <summary>
    /// Resolves the best handler for the given menu mode and UI state.
    /// Returns the first handler (by priority) that can handle the context.
    /// </summary>
    private IMenuHandler? ResolveHandler(int menuMode, UIState? uiState)
    {
        foreach (IMenuHandler handler in _handlers)
        {
            if (handler.CanHandle(menuMode, uiState))
            {
                return handler;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the handler that would handle the given context, without changing state.
    /// Useful for diagnostics.
    /// </summary>
    internal IMenuHandler? GetHandler(int menuMode, UIState? uiState)
    {
        return ResolveHandler(menuMode, uiState);
    }
}
