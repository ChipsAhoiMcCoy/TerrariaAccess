#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuNarrationController
{
    private readonly MenuNarrationHandlerRegistry _registry;

    internal MenuNarrationController()
    {
        _registry = new MenuNarrationHandlerRegistry(new IMenuNarrationHandler[]
        {
            new DefaultMenuNarrationHandler(),
        });
    }

    public void Process(Main main)
    {
        if (!Main.gameMenu)
        {
            _registry.Reset();
            return;
        }

        MenuNarrationContext context = new(main, Main.MenuUI?.CurrentState, Main.menuMode, DateTime.UtcNow);
        IReadOnlyList<MenuNarrationEvent> events = _registry.Process(context);
        foreach (MenuNarrationEvent narrationEvent in events)
        {
            if (string.IsNullOrWhiteSpace(narrationEvent.Text))
            {
                continue;
            }

            ScreenReaderService.Announce(narrationEvent.Text, narrationEvent.Force);
        }
    }
}
