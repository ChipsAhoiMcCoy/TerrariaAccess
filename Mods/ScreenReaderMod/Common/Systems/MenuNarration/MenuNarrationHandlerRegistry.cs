#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuNarrationHandlerRegistry
{
    private readonly List<IMenuNarrationHandler> _handlers = new();
    private IMenuNarrationHandler? _activeHandler;
    private int? _lastMenuMode;

    internal MenuNarrationHandlerRegistry(IEnumerable<IMenuNarrationHandler> handlers)
    {
        _handlers.AddRange(handlers);
    }

    internal IReadOnlyList<MenuNarrationEvent> Process(MenuNarrationContext context)
    {
        if (_handlers.Count == 0)
        {
            _activeHandler?.OnMenuLeft();
            _activeHandler = null;
            _lastMenuMode = null;
            return Array.Empty<MenuNarrationEvent>();
        }

        IMenuNarrationHandler handler = ResolveHandler(context);
        bool handlerChanged = handler != _activeHandler;
        bool modeChanged = !_lastMenuMode.HasValue || _lastMenuMode.Value != context.MenuMode;

        if (handlerChanged)
        {
            _activeHandler?.OnMenuLeft();
            handler.OnMenuEntered(context);
            _activeHandler = handler;
        }
        else if (modeChanged)
        {
            handler.OnMenuEntered(context);
        }

        _lastMenuMode = context.MenuMode;

        List<MenuNarrationEvent> events = new();
        foreach (MenuNarrationEvent narrationEvent in handler.Update(context))
        {
            events.Add(narrationEvent);
        }

        return events;
    }

    internal void Reset()
    {
        _activeHandler?.OnMenuLeft();
        _activeHandler = null;
        _lastMenuMode = null;
    }

    private IMenuNarrationHandler ResolveHandler(MenuNarrationContext context)
    {
        foreach (IMenuNarrationHandler handler in _handlers)
        {
            if (handler.CanHandle(context))
            {
                return handler;
            }
        }

        return _handlers[^1];
    }
}
