#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles world selection menus (UIWorldSelect, UIWorldList).
/// </summary>
internal sealed class WorldSelectionHandler : MenuHandlerBase
{
    public override int Priority => 70; // Same as WorldCreationHandler and CharacterCreationHandler

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        if (uiState is null)
        {
            return false;
        }

        string? typeName = uiState.GetType().FullName;

        // Handle UIWorldSelect and UIWorldList (world selection screens)
        if (typeName?.Contains("UIWorldSelect", StringComparison.Ordinal) == true ||
            typeName?.Contains("UIWorldList", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return false;
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

        // Handle hover events for world list items.
        // This is a UIState-based screen — all navigation is via hover on UIElements,
        // not via Main.focusMenu / Main.menuItems. Do NOT call TryHandleFocus or
        // AnnounceFallback here, as those read stale data left over from the previous
        // menu (e.g. "Singleplayer" from the title screen).
        TryHandleWorldSelectionHover(context, events);

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(context.MenuMode, context.UiState);
        State.LastModeAnnouncement = modeLabel;
        State.LastModeAnnouncedAt = timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        ScreenReaderMod.Instance?.Logger.Info($"[WorldSelectionHandler] Entered: {modeLabel}");

        // Handle hover first
        if (TryHandleWorldSelectionHover(context, events))
        {
            return;
        }

        // Announce the mode description
        if (!string.IsNullOrWhiteSpace(modeLabel))
        {
            events.Add(new MenuNarrationEvent(modeLabel, true, MenuNarrationEventKind.ModeChanged));
        }
    }

    private bool TryHandleWorldSelectionHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
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

        ScreenReaderMod.Instance?.Logger.Info($"[WorldSelectionHandler] Announcing hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }
}
