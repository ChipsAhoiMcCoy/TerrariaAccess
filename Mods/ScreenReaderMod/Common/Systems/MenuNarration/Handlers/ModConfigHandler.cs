#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Systems.MenuNarration.ModConfig;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles mod configuration screens including mod config list and mod config edit screens.
/// Delegates to ModConfigNarrationCoordinator for specialized handling.
/// </summary>
internal sealed class ModConfigHandler : MenuHandlerBase
{
    private readonly ModConfigNarrationCoordinator _coordinator = new();

    // Menu mode constants
    private const int ModConfigListMenuMode = 10027;
    private const int ModConfigEditMenuMode = 10024;

    public override int Priority => 100; // High priority to take precedence

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        // Check menu mode
        bool isValidMenuMode = menuMode == MenuID.FancyUI ||
                               menuMode == ModConfigListMenuMode ||
                               menuMode == ModConfigEditMenuMode;

        // Also check UI state type for reliability
        if (uiState is not null)
        {
            string? stateTypeName = uiState.GetType().FullName;
            if (stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfigList" ||
                stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfig")
            {
                return true;
            }
        }

        return isValidMenuMode && IsModConfigUiState(uiState);
    }

    private static bool IsModConfigUiState(UIState? uiState)
    {
        if (uiState is null)
        {
            return false;
        }

        string? stateTypeName = uiState.GetType().FullName;
        return stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfigList" ||
               stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfig";
    }

    public override void OnEntered(MenuNarrationContext context)
    {
        base.OnEntered(context);
        _coordinator.Reset();
    }

    public override void OnLeft()
    {
        base.OnLeft();
        _coordinator.Reset();
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            _coordinator.Reset();
            return events;
        }

        // Delegate to the coordinator for mod config handling
        _coordinator.TryBuildMenuEvents(context, events);

        return events;
    }
}
