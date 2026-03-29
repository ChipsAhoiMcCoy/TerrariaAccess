#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles character/world deletion confirmation prompts using menu mode only.
/// These dialogs can leave the prior UIState active, so they need a dedicated handler.
/// </summary>
internal sealed class DeletionConfirmationHandler : MenuHandlerBase
{
    public override int Priority => 90;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        return MenuNarrationCatalog.IsDeletionMenuMode(menuMode);
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            return events;
        }

        if (ModeJustEntered)
        {
            HandleMenuModeChanged(context, events);
            ModeJustEntered = false;
            return events;
        }

        TryHandleFocus(context, false, events);
        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        TerrariaAccess.Instance?.Logger.Info($"[DeletionConfirmationHandler] Entered deletion dialog mode {context.MenuMode}");

        if (TryAnnounceDeletionDialogEntry(context, events))
        {
            return;
        }

        if (MenuNarrationCatalog.TryGetDeletionPrompt(context.MenuMode, out string prompt) &&
            !string.IsNullOrWhiteSpace(prompt))
        {
            events.Add(new MenuNarrationEvent(prompt, true, MenuNarrationEventKind.ModeChanged));
            State.LastModeAnnouncement = prompt;
            State.LastModeAnnouncedAt = context.Timestamp;
        }
    }
}
