#nullable enable
using System;
using System.Collections.Generic;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration.Handlers;
using Terraria;

namespace TerrariaAccess.Common.Systems.MenuNarration;

/// <summary>
/// Controller for menu narration.
/// Creates and configures the handler registry with all menu-specific handlers.
/// </summary>
internal sealed class MenuNarrationController
{
    private readonly MenuNarrationHandlerRegistry _registry;

    internal MenuNarrationController()
    {
        _registry = new MenuNarrationHandlerRegistry();

        // Register handlers in any order - they're sorted by priority automatically
        // Higher priority handlers are checked first
        _registry.RegisterHandler(new ModConfigHandler());        // Priority 100 - highest, catches mod config screens
        _registry.RegisterHandler(new WorldCreationHandler());    // Priority 70
        _registry.RegisterHandler(new WorldSelectionHandler());   // Priority 70 - world selection (UIWorldSelect, UIWorldList)
        _registry.RegisterHandler(new CharacterCreationHandler()); // Priority 70
        _registry.RegisterHandler(new SettingsMenuHandler());     // Priority 60
        _registry.RegisterHandler(new MultiplayerMenuHandler());  // Priority 55
        _registry.RegisterHandler(new TitleMenuHandler());        // Priority 50
        _registry.RegisterHandler(new FallbackMenuHandler());     // Priority 0 - lowest, catches everything else
    }

    /// <summary>
    /// Gets the handler registry (for diagnostic purposes).
    /// </summary>
    internal MenuNarrationHandlerRegistry Registry => _registry;

    /// <summary>
    /// Processes the current menu state and announces any narration events.
    /// </summary>
    public void Process(Main main)
    {
        if (!Main.gameMenu)
        {
            _registry.Reset();
            return;
        }

        MenuNarrationContext context = new(main, Main.MenuUI?.CurrentState, Main.menuMode, DateTime.UtcNow);
        IReadOnlyList<MenuNarrationEvent> events = _registry.Process(context);
        bool queueNextAfterModeChange = false;

        foreach (MenuNarrationEvent narrationEvent in events)
        {
            if (string.IsNullOrWhiteSpace(narrationEvent.Text))
            {
                continue;
            }

            if (narrationEvent.Kind == MenuNarrationEventKind.ModeChanged)
            {
                queueNextAfterModeChange = true;
                ScreenReaderService.Announce(narrationEvent.Text, narrationEvent.Force);
            }
            else
            {
                bool requestInterrupt = true;
                if (queueNextAfterModeChange)
                {
                    requestInterrupt = narrationEvent.Kind != MenuNarrationEventKind.EntryFollowUp;
                    queueNextAfterModeChange = false;
                }

                ScreenReaderService.Announce(
                    narrationEvent.Text,
                    narrationEvent.Force,
                    requestInterrupt: requestInterrupt);
            }
        }
    }
}
