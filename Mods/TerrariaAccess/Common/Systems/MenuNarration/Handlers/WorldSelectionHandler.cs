#nullable enable
using System;
using System.Collections.Generic;
using Terraria.IO;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles world selection menus (UIWorldSelect, UIWorldList).
/// </summary>
internal sealed class WorldSelectionHandler : MenuHandlerBase
{
    private const string WorldSelectStateName = "UIWorldSelect";
    private const string WorldListStateName = "UIWorldList";

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

        TerrariaAccess.Instance?.Logger.Info($"[WorldSelectionHandler] Entered: {modeLabel}");

        if (TryBuildEntryAnnouncements(context, out string? entryAction, out string? entryHover))
        {
            if (!string.IsNullOrWhiteSpace(modeLabel))
            {
                events.Add(new MenuNarrationEvent(modeLabel, true, MenuNarrationEventKind.ModeChanged));
            }

            if (!string.IsNullOrWhiteSpace(entryAction))
            {
                events.Add(new MenuNarrationEvent(entryAction, true, MenuNarrationEventKind.EntryFollowUp));
                State.LastFocusAnnouncement = entryAction;
                State.LastFocusAnnouncedAt = timestamp;
                State.LastFocus = new MenuFocus(0, "ModeEntryPrimaryAction");
            }

            if (!string.IsNullOrWhiteSpace(entryHover))
            {
                State.SuppressedEntryHoverAnnouncement = entryHover;
            }

            return;
        }

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

        if (!string.IsNullOrWhiteSpace(State.SuppressedEntryHoverAnnouncement) &&
            string.Equals(cleaned, State.SuppressedEntryHoverAnnouncement, StringComparison.OrdinalIgnoreCase))
        {
            State.SuppressedEntryHoverAnnouncement = null;
            return true;
        }

        State.SuppressedEntryHoverAnnouncement = null;

        if (!IsAllowedHover(context.MenuMode, cleaned))
        {
            return false;
        }

        TerrariaAccess.Instance?.Logger.Info($"[WorldSelectionHandler] Announcing hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.PendingHoverFocusSuppression = cleaned;
        State.SawHoverThisMode = true;
        return true;
    }

    private static bool IsWorldSelectionScreen(UIState? uiState)
    {
        string? typeName = uiState?.GetType().FullName;
        return typeName?.Contains(WorldSelectStateName, StringComparison.Ordinal) == true ||
            typeName?.Contains(WorldListStateName, StringComparison.Ordinal) == true;
    }

    private static bool TryBuildEntryAnnouncements(MenuNarrationContext context, out string? entryAction, out string? entryHover)
    {
        entryAction = null;
        entryHover = null;

        if (!IsWorldSelectionScreen(context.UiState) ||
            !TryGetFirstListEntry(
                context,
                ReflectionCache.UIWorldSelect.WorldList,
                static element => string.Equals(
                    element.GetType().FullName,
                    "Terraria.GameContent.UI.Elements.UIWorldListItem",
                    StringComparison.Ordinal),
                out UIElement? entry) ||
            entry is null)
        {
            return false;
        }

        entryHover = TextSanitizer.Clean(MenuUiSelectionTracker.ResolveLabel(entry));

        if (!TryGetAssignableField<WorldFileData>(entry, out WorldFileData? data) || data is null)
        {
            return false;
        }

        string worldName = string.IsNullOrWhiteSpace(data.Name)
            ? LocalizationHelper.GetTextOrFallback("UI.WorldNameDefault", "World")
            : TextSanitizer.Clean(data.Name);
        string playLabel = LocalizationHelper.GetTextOrFallback("UI.Play", "Play");
        entryAction = TextSanitizer.JoinWithComma(playLabel, worldName);
        return !string.IsNullOrWhiteSpace(entryAction);
    }
}
