#nullable enable
using System.Collections.Generic;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles Terraria rejection/status menus that are drawn from Main.statusText.
/// These menus can leave the previous UIState active, so they need to win by menu mode.
/// </summary>
internal sealed class RejectionMenuHandler : MenuHandlerBase
{
    public override int Priority => 95;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        return MenuNarrationCatalog.IsRejectionMenuMode(menuMode);
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
        if (!MenuNarrationCatalog.TryGetRejectionStatusText(context.MenuMode, out string status))
        {
            return;
        }

        TerrariaAccess.Instance?.Logger.Info($"[RejectionMenuHandler] Status: {status}");
        events.Add(new MenuNarrationEvent(status, true, MenuNarrationEventKind.ModeChanged));
        State.LastModeAnnouncement = status;
        State.LastModeAnnouncedAt = context.Timestamp;
        State.LastFocusAnnouncement = status;
        State.LastFocusAnnouncedAt = context.Timestamp;
        State.QueueNextFocusAsEntryFollowUp = true;
    }
}
