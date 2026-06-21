#nullable enable
using System;
using System.Collections.Generic;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles the main title menu (Single Player, Multiplayer, Achievements, etc.)
/// </summary>
internal sealed class TitleMenuHandler : MenuHandlerBase
{
    private static readonly TimeSpan StartupFocusSettleWindow = TimeSpan.FromMilliseconds(1500);

    public override int Priority => 50;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        return menuMode == MenuID.Title;
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

        // Handle hover events
        if (TryHandleTitleMenuHover(context, events))
        {
            return events;
        }

        // Handle focus changes
        if (!TryHandleTitleMenuFocus(context, now, events))
        {
            // No fallback for title menu - it handles itself
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        // Handle hover if available
        if (TryHandleTitleMenuHover(context, events))
        {
            return;
        }

        // Announce the default focused item immediately on mode entry,
        // bypassing stabilization delays for instant feedback.
        string defaultItem = MenuNarrationCatalog.DescribeMenuItem(context.MenuMode, 0);
        if (!string.IsNullOrEmpty(defaultItem))
        {
            TerrariaAccess.Instance?.Logger.Info($"[TitleMenuHandler] Immediate entry announcement: {defaultItem}");
            events.Add(new MenuNarrationEvent(defaultItem, true, MenuNarrationEventKind.Focus));
            State.LastFocusAnnouncement = defaultItem;
            State.LastFocusAnnouncedAt = timestamp;
            State.LastFocus = new MenuFocus(0, "ModeEntry");
        }
    }

    private bool TryHandleTitleMenuHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
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

        if (!IsAllowedTitleHover(cleaned))
        {
            TerrariaAccess.Instance?.Logger.Info($"[TitleMenuHandler] Hover suppressed: {cleaned}");
            return false;
        }

        TerrariaAccess.Instance?.Logger.Info($"[TitleMenuHandler] Announcing hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }

    private static bool IsAllowedTitleHover(string cleanedLabel)
    {
        if (string.IsNullOrWhiteSpace(cleanedLabel))
        {
            return false;
        }

        // Only allow known main menu entries; suppress stray tooltips like Steam join messages or migration prompts.
        string[] allowed =
        {
            TextSanitizer.Clean(Lang.menu[12].Value), // Single Player
            TextSanitizer.Clean(Lang.menu[13].Value), // Multiplayer
            TextSanitizer.Clean(Lang.menu[131].Value), // Achievements
            TextSanitizer.Clean(Language.GetTextValue("UI.Workshop")),
            TextSanitizer.Clean(Lang.menu[14].Value), // Settings
            TextSanitizer.Clean(Language.GetTextValue("UI.Credits")),
            TextSanitizer.Clean(Lang.menu[15].Value), // Exit
        };

        foreach (string allowedLabel in allowed)
        {
            if (!string.IsNullOrWhiteSpace(allowedLabel) &&
                string.Equals(cleanedLabel, allowedLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryHandleTitleMenuFocus(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        return TryHandleFocus(context, ModeJustEntered, events);
    }

    protected override bool ShouldSuppressResolvedFocus(
        MenuNarrationContext context,
        MenuFocus focus,
        string optionLabel,
        string announcement)
    {
        if (State.SawHoverThisMode)
        {
            ClearStartupFocusSuppression();
            return false;
        }

        if (context.Timestamp - State.ModeEnteredAt > StartupFocusSettleWindow)
        {
            ClearStartupFocusSuppression();
            return false;
        }

        bool usesMenuIndexFocus =
            focus.Source.Equals("Main.focusMenu", StringComparison.Ordinal) ||
            focus.Source.Equals("Main.selectedMenu", StringComparison.Ordinal);
        if (!usesMenuIndexFocus)
        {
            return false;
        }

        // Vanilla lands on Single Player already. During startup, tML can churn through
        // title-menu indices before the UI actually settles, which causes stray follow-up
        // announcements like "Settings" after the correct initial "Single Player".
        // Require one stable repeat for non-default indices during that startup window.
        if (focus.Index == 0)
        {
            ClearStartupFocusSuppression();
            return false;
        }

        bool sameAsPending =
            State.PendingInitialFocus.HasValue &&
            State.PendingInitialFocus.Value.Equals(focus) &&
            string.Equals(State.PendingInitialFocusAnnouncement, announcement, StringComparison.OrdinalIgnoreCase);
        if (sameAsPending)
        {
            ClearStartupFocusSuppression();
            return false;
        }

        State.PendingInitialFocus = focus;
        State.PendingInitialFocusAnnouncement = announcement;
        TerrariaAccess.Instance?.Logger.Debug($"[TitleMenuHandler] Suppressing transient startup focus: {focus.Index} -> {optionLabel}");
        return true;
    }

    protected override bool IsAllowedHover(int menuMode, string cleanedLabel)
    {
        return IsAllowedTitleHover(cleanedLabel);
    }

    private void ClearStartupFocusSuppression()
    {
        State.PendingInitialFocus = null;
        State.PendingInitialFocusAnnouncement = null;
    }
}
