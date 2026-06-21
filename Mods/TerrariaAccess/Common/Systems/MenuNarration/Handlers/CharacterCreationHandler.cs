#nullable enable
using System;
using System.Collections.Generic;
using Terraria.IO;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles character creation menus including hair, clothing styles, and character options.
/// </summary>
internal sealed class CharacterCreationHandler : MenuHandlerBase
{
    private const string CharacterSelectStateName = "UICharacterSelect";

    public override int Priority => 70;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        // Check if the UI state is a character creation screen
        if (uiState is null)
        {
            return false;
        }

        string? typeName = uiState.GetType().FullName;

        // Handle UICharacterCreation (the actual creation screen)
        if (typeName?.Contains("UICharacterCreation", StringComparison.Ordinal) == true)
        {
            return true;
        }

        // Handle UICharacterSelect (player selection screen) - appears in both menuMode 1 and 2
        // menuMode 1 = Single player character selection
        // menuMode 2 = Multiplayer character selection
        if (typeName?.Contains("UICharacterSelect", StringComparison.Ordinal) == true)
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

        if (MenuNarrationCatalog.IsDeletionMenuMode(context.MenuMode))
        {
            TryHandleFocus(context, false, events);
            return events;
        }

        // Handle hover events for character creation elements.
        // This is a UIState-based screen — all navigation is via hover on UIElements,
        // not via Main.focusMenu / Main.menuItems. Do NOT call TryHandleFocus or
        // AnnounceFallback here, as those read stale data from the previous menu.
        TryHandleCharacterCreationHover(context, events);

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(context.MenuMode, context.UiState);
        State.LastModeAnnouncement = modeLabel;
        State.LastModeAnnouncedAt = timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        TerrariaAccess.Instance?.Logger.Info($"[CharacterCreationHandler] Entered: {modeLabel}");

        if (MenuNarrationCatalog.IsDeletionMenuMode(context.MenuMode))
        {
            TryAnnounceDeletionDialogEntry(context, events);
            return;
        }

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
        if (TryHandleCharacterCreationHover(context, events))
        {
            return;
        }

        // Announce the mode description and let hover events handle item-level navigation.
        // Do NOT force-announce focus here — Main.focusMenu / menuItems may contain stale
        // data from the previous screen (e.g., title menu items like "Singleplayer").
        if (!string.IsNullOrWhiteSpace(modeLabel))
        {
            events.Add(new MenuNarrationEvent(modeLabel, true, MenuNarrationEventKind.ModeChanged));
        }
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

        TerrariaAccess.Instance?.Logger.Info($"[CharacterCreationHandler] Announcing hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.PendingHoverFocusSuppression = cleaned;
        State.SawHoverThisMode = true;
        return true;
    }

    private static bool IsCharacterSelectionScreen(UIState? uiState)
    {
        return uiState?.GetType().FullName?.Contains(CharacterSelectStateName, StringComparison.Ordinal) == true;
    }

    private static bool TryBuildEntryAnnouncements(MenuNarrationContext context, out string? entryAction, out string? entryHover)
    {
        entryAction = null;
        entryHover = null;

        if (!IsCharacterSelectionScreen(context.UiState) ||
            !TryGetFirstListEntry(
                context,
                ReflectionCache.UICharacterSelect.PlayerList,
                static element => string.Equals(
                    element.GetType().FullName,
                    "Terraria.GameContent.UI.Elements.UICharacterListItem",
                    StringComparison.Ordinal),
                out UIElement? entry) ||
            entry is null)
        {
            return false;
        }

        entryHover = TextSanitizer.Clean(MenuUiSelectionTracker.ResolveLabel(entry));

        if (!TryGetAssignableField<PlayerFileData>(entry, out PlayerFileData? data) || data is null)
        {
            return false;
        }

        string playerName = string.IsNullOrWhiteSpace(data.Name)
            ? LocalizationHelper.GetTextOrFallback("UI.PlayerNameDefault", "Player")
            : TextSanitizer.Clean(data.Name);
        string playLabel = LocalizationHelper.GetTextOrFallback("UI.Play", "Play");
        entryAction = TextSanitizer.JoinWithComma(playLabel, playerName);
        return !string.IsNullOrWhiteSpace(entryAction);
    }
}
