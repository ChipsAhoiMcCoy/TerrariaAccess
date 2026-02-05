#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles multiplayer menus including host/join, server IP entry, port entry, and password entry.
/// </summary>
internal sealed class MultiplayerMenuHandler : MenuHandlerBase
{
    // Multiplayer menu modes
    private const int MultiplayerMode = 12;
    private const int HostAndPlayMode = 889;
    private const int ConnectionStatusMode = 14;
    private const int WorldLoadingMode = 10;

    public override int Priority => 55;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        return menuMode is MultiplayerMode or HostAndPlayMode or ConnectionStatusMode or WorldLoadingMode
            or MenuID.ServerIP
            or MenuID.ServerPort
            or MenuID.ServerPasswordRequested;
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            return events;
        }

        int currentMode = context.MenuMode;
        DateTime now = context.Timestamp;

        if (ModeJustEntered)
        {
            HandleMenuModeChanged(context, now, events);
            ModeJustEntered = false;
            return events;
        }

        // Handle hover events
        if (TryHandleUiHover(context, events))
        {
            return events;
        }

        // Handle focus changes
        if (!TryHandleFocus(context, false, events))
        {
            // For connection status, check if there's a status message
            if (currentMode is ConnectionStatusMode or WorldLoadingMode)
            {
                TryAnnounceConnectionStatus(context, events);
            }
            else
            {
                AnnounceFallback(context, events);
            }
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
        if (TryHandleUiHover(context, events))
        {
            return;
        }

        // For connection status screens, announce the status
        if (context.MenuMode is ConnectionStatusMode or WorldLoadingMode)
        {
            TryAnnounceConnectionStatus(context, events);
            return;
        }

        // Force focus announcement
        TryHandleFocus(context, true, events);
    }

    private void TryAnnounceConnectionStatus(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        string status = TextSanitizer.Clean(Main.statusText ?? string.Empty);
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        // Check if this is the same status we just announced
        if (!string.IsNullOrWhiteSpace(State.LastFocusAnnouncement) &&
            string.Equals(status, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            context.Timestamp - State.LastFocusAnnouncedAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[MultiplayerHandler] Connection status: {status}");
        events.Add(new MenuNarrationEvent(status, true, MenuNarrationEventKind.Focus));
        State.LastFocusAnnouncement = status;
        State.LastFocusAnnouncedAt = context.Timestamp;
    }
}
