#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles character creation menus including hair, clothing styles, and character options.
/// </summary>
internal sealed class CharacterCreationHandler : MenuHandlerBase
{
    public override int Priority => 70;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        // Check if the UI state is a character creation screen
        if (uiState is null)
        {
            return false;
        }

        string? typeName = uiState.GetType().FullName;
        return typeName?.Contains("UICharacterCreation", StringComparison.Ordinal) == true ||
               typeName?.Contains("UICharacterSelect", StringComparison.Ordinal) == true &&
               menuMode == 2; // Character creation mode
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            return events;
        }

        DateTime now = context.Timestamp;

        if (ModeJustEntered)
        {
            HandleMenuModeChanged(context, now, events);
            ModeJustEntered = false;
            return events;
        }

        // Handle hover events for character creation elements
        if (TryHandleCharacterCreationHover(context, events))
        {
            return events;
        }

        // Handle focus changes
        if (!TryHandleFocus(context, false, events))
        {
            AnnounceFallback(context, events);
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(context.MenuMode, context.UiState);
        State.LastModeAnnouncement = modeLabel;
        State.LastModeAnnouncedAt = timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        // Handle hover
        if (TryHandleCharacterCreationHover(context, events))
        {
            return;
        }

        // Force focus announcement
        TryHandleFocus(context, true, events);
    }

    private bool TryHandleCharacterCreationHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        if (!UiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover))
        {
            return false;
        }

        if (!hover.IsNew)
        {
            return true;
        }

        string cleaned = TextSanitizer.Clean(hover.Text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (!IsAllowedHover(context.MenuMode, cleaned))
        {
            return false;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[CharacterCreationHandler] Announcing hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }
}
