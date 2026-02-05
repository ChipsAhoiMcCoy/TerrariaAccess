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
            events.Add(narrationEvent);
        }

        return events;
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
