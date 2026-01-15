#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Coordinates mod config menu narration. Single entry point that routes to
/// appropriate handlers for list or edit screens. Replaces ModConfigMenuNarrator.
/// </summary>
internal sealed class ModConfigNarrationCoordinator
{
    private readonly AnnouncementGate _gate = new();
    private readonly ModConfigListHandler _listHandler;
    private readonly ModConfigEditHandler _editHandler;

    private UIState? _lastUiState;
    private bool _isListScreen;
    private bool _isEditScreen;

    /// <summary>
    /// Returns true if this coordinator is currently handling input.
    /// Used by DefaultMenuNarrationHandler to suppress hover announcements.
    /// </summary>
    public static bool IsHandlingGamepadInput { get; private set; }

    // Menu mode constants
    private const int ModConfigListMenuMode = 10027;
    private const int ModConfigEditMenuMode = 10024;

    public ModConfigNarrationCoordinator()
    {
        _listHandler = new ModConfigListHandler(_gate);
        _editHandler = new ModConfigEditHandler(_gate);
    }

    /// <summary>
    /// Process mod config UI for menu context.
    /// Called by DefaultMenuNarrationHandler.
    /// </summary>
    public bool TryBuildMenuEvents(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        // Check menu mode
        bool isValidMenuMode = context.MenuMode == MenuID.FancyUI ||
                               context.MenuMode == ModConfigListMenuMode ||
                               context.MenuMode == ModConfigEditMenuMode;

        // Also check UI state type for reliability
        bool isModConfigUiState = false;
        if (context.UiState is not null)
        {
            Type stateType = context.UiState.GetType();
            string? stateTypeName = stateType.FullName;
            isModConfigUiState =
                stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfigList" ||
                stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfig";
        }

        if (!isValidMenuMode && !isModConfigUiState)
        {
            if (_isListScreen || _isEditScreen)
            {
                Reset();
            }
            return false;
        }

        ScreenReaderMod.Instance?.Logger.Debug(
            $"[ModConfigCoordinator] TryBuildMenuEvents: menuMode={context.MenuMode}, isModConfigUiState={isModConfigUiState}, uiState={context.UiState?.GetType().Name}");

        return TryProcess(
            context.UiState,
            isMenuContext: true,
            (text, force, kind) =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    events.Add(new MenuNarrationEvent(text, force, kind));
                }
            });
    }

    /// <summary>
    /// Process mod config UI for in-game context.
    /// Called by InGameNarrationSystem.
    /// </summary>
    public bool TryHandleIngameUi(UserInterface? inGameUi)
    {
        UIState? currentState = inGameUi?.CurrentState;
        if (currentState is null)
        {
            Reset();
            return false;
        }

        string? stateTypeName = currentState.GetType().FullName;
        bool isModConfigScreen =
            stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfigList" ||
            stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfig";

        if (!isModConfigScreen)
        {
            Reset();
            return false;
        }

        // Skip if menu path will handle it
        if (Main.menuMode == MenuID.FancyUI ||
            Main.menuMode == ModConfigListMenuMode ||
            Main.menuMode == ModConfigEditMenuMode)
        {
            return false;
        }

        return TryProcess(currentState, isMenuContext: false, menuEventSink: null);
    }

    private bool TryProcess(
        UIState? state,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        // Tick the announcement gate each frame
        _gate.Tick();

        if (state is null)
        {
            Reset();
            return false;
        }

        Type stateType = state.GetType();
        string? stateTypeName = stateType.FullName;

        // Detect screen type
        bool isListScreen = stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfigList";
        bool isEditScreen = stateTypeName == "Terraria.ModLoader.Config.UI.UIModConfig";

        if (!isListScreen && !isEditScreen)
        {
            Reset();
            return false;
        }

        // Check for screen transition
        bool justEntered = !ReferenceEquals(state, _lastUiState) ||
                           (isListScreen && !_isListScreen) ||
                           (isEditScreen && !_isEditScreen);

        if (justEntered)
        {
            ScreenReaderMod.Instance?.Logger.Debug(
                $"[ModConfigCoordinator] Entered {(isListScreen ? "ListScreen" : "EditScreen")}");

            // Reset the appropriate handler and clear gate to allow first announcement
            _gate.Reset();
            if (isListScreen)
            {
                _listHandler.Reset();
            }
            else
            {
                _editHandler.Reset();
            }
        }

        _lastUiState = state;
        _isListScreen = isListScreen;
        _isEditScreen = isEditScreen;

        // Set the handling flag
        IsHandlingGamepadInput = PlayerInput.UsingGamepadUI;

        // Route to appropriate handler
        if (isListScreen)
        {
            _listHandler.Process(state, justEntered, isMenuContext, menuEventSink);
        }
        else
        {
            _editHandler.Process(state, justEntered, isMenuContext, menuEventSink);
        }

        return true;
    }

    /// <summary>
    /// Reset all state.
    /// </summary>
    public void Reset()
    {
        _lastUiState = null;
        _isListScreen = false;
        _isEditScreen = false;
        _gate.Reset();
        _listHandler.Reset();
        _editHandler.Reset();
        IsHandlingGamepadInput = false;
    }
}
