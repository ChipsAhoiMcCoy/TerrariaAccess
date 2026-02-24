#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Localization;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Base class for menu handlers providing common functionality.
/// </summary>
internal abstract class MenuHandlerBase : IMenuHandler
{
    protected static readonly FieldInfo? FocusMenuField = typeof(Main).GetField("focusMenu", BindingFlags.NonPublic | BindingFlags.Instance);
    protected static readonly FieldInfo? SelectedMenuField = typeof(Main).GetField("selectedMenu", BindingFlags.NonPublic | BindingFlags.Instance);

    protected readonly MenuFocusResolver FocusResolver = new();
    protected readonly MenuUiSelectionTracker UiSelectionTracker = new();
    protected readonly MenuNarrationState State = new();

    protected bool ModeJustEntered;

    public abstract bool CanHandle(int menuMode, UIState? uiState);
    public abstract int Priority { get; }

    public virtual void OnEntered(MenuNarrationContext context)
    {
        ModeJustEntered = true;
        State.ResetForMode(context.MenuMode);
        State.ModeEnteredAt = context.Timestamp;
        FocusResolver.Reset();
        UiSelectionTracker.Reset();
    }

    public virtual void OnLeft()
    {
        FocusResolver.Reset();
        UiSelectionTracker.Reset();
        State.ResetAll();
        ModeJustEntered = false;
    }

    public abstract IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context);

    /// <summary>
    /// Gets the current focus index from Terraria's menu system.
    /// </summary>
    protected int GetFocusMenu()
    {
        try
        {
            if (FocusMenuField?.GetValue(Main.instance) is int focusValue)
            {
                return focusValue;
            }
        }
        catch
        {
            // ignore reflection failures
        }
        return -1;
    }

    /// <summary>
    /// Gets the selected menu index from Terraria's menu system.
    /// </summary>
    protected int GetSelectedMenu()
    {
        try
        {
            if (SelectedMenuField?.GetValue(Main.instance) is int selectedValue)
            {
                return selectedValue;
            }
        }
        catch
        {
            // ignore reflection failures
        }
        return -1;
    }

    /// <summary>
    /// Tries to handle UI hover events.
    /// </summary>
    protected bool TryHandleUiHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
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
            TerrariaAccess.Instance?.Logger.Info($"[MenuNarration] UI hover suppressed -> {cleaned}");
            return false;
        }

        TerrariaAccess.Instance?.Logger.Info($"[MenuHandler] Announcing hover: '{cleaned}'");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }

    /// <summary>
    /// Determines if a hover announcement should be allowed.
    /// Override in derived classes to customize hover filtering.
    /// </summary>
    protected virtual bool IsAllowedHover(int menuMode, string cleanedLabel)
    {
        if (string.IsNullOrWhiteSpace(cleanedLabel))
        {
            return false;
        }

        // Filter out menu titles that match the mode description
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(menuMode);
        if (!string.IsNullOrWhiteSpace(modeLabel) &&
            string.Equals(cleanedLabel, TextSanitizer.Clean(modeLabel), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Tries to handle focus changes using the focus resolver.
    /// </summary>
    protected bool TryHandleFocus(MenuNarrationContext context, bool force, List<MenuNarrationEvent> events)
    {
        int currentMode = context.MenuMode;
        DateTime timestamp = context.Timestamp;

        if (!FocusResolver.TryGetFocus(context.Main, out MenuFocus focus))
        {
            if (State.FocusFailureCount++ < 5)
            {
                TerrariaAccess.Instance?.Logger.Debug($"[MenuHandler] Unable to determine focus for menu mode {currentMode} (attempt {State.FocusFailureCount}).");
            }
            return false;
        }

        State.FocusFailureCount = 0;

        // Wait for UI to stabilize after mode entry before announcing focus.
        // Focus data (Main.focusMenu, menuItemScale) may be stale from the previous
        // menu during the first few frames of a transition.
        if (!State.SawHoverThisMode && timestamp - State.ModeEnteredAt < TimeSpan.FromMilliseconds(250))
        {
            return false;
        }

        string optionLabel = MenuNarrationCatalog.DescribeMenuItem(currentMode, focus.Index);
        bool hasDeletionAnnouncement = MenuNarrationCatalog.TryBuildDeletionAnnouncement(currentMode, focus.Index, out string combinedLabel);
        string announcement = hasDeletionAnnouncement ? combinedLabel : optionLabel;

        bool focusChanged = !State.LastFocus.HasValue || State.LastFocus.Value.Index != focus.Index;
        bool announcementChanged = !focusChanged &&
            State.LastFocus.HasValue &&
            State.LastFocus.Value.Index == focus.Index &&
            !string.IsNullOrWhiteSpace(State.LastFocusAnnouncement) &&
            !string.IsNullOrWhiteSpace(announcement) &&
            !string.Equals(announcement, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
        bool shouldAnnounce = force || focusChanged || State.ForceNextFocus || announcementChanged;

        if (shouldAnnounce)
        {
            bool matchesRecentHover = !string.IsNullOrWhiteSpace(State.LastHoverAnnouncement) &&
                string.Equals(optionLabel, State.LastHoverAnnouncement, StringComparison.OrdinalIgnoreCase) &&
                timestamp - State.LastHoverAnnouncedAt < TimeSpan.FromMilliseconds(900);
            bool matchesLastFocus = !string.IsNullOrWhiteSpace(State.LastFocusAnnouncement) &&
                string.Equals(announcement, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
            bool repeatedRecently = matchesLastFocus && timestamp - State.LastFocusAnnouncedAt < TimeSpan.FromMilliseconds(900);

            if (!force && !announcementChanged && (matchesRecentHover || repeatedRecently))
            {
                State.ForceNextFocus = false;
                State.LastFocus = focus;
                return true;
            }

            if (!string.IsNullOrEmpty(optionLabel))
            {
                TerrariaAccess.Instance?.Logger.Info($"[MenuHandler] Focus {focus.Index} via {focus.Source} -> {optionLabel}");
                bool forceSpeech = force || State.ForceNextFocus || announcementChanged;
                events.Add(new MenuNarrationEvent(announcement, forceSpeech, MenuNarrationEventKind.Focus));
                State.LastFocusAnnouncement = announcement;
                State.LastFocusAnnouncedAt = timestamp;
                State.ForceNextFocus = false;
            }
            else
            {
                TerrariaAccess.Instance?.Logger.Info($"[MenuHandler] Missing label for focus {focus.Index} (source {focus.Source}) in menu mode {currentMode}.");
                MenuNarrationCatalog.LogMenuSnapshot(currentMode, allowRepeat: true);
            }
        }
        else if (State.LastFocus.HasValue && !State.LastFocus.Value.Source.Equals(focus.Source, StringComparison.Ordinal))
        {
            TerrariaAccess.Instance?.Logger.Debug($"[MenuHandler] Focus source switched to {focus.Source} for index {focus.Index}.");
        }

        State.LastFocus = focus;
        State.AnnouncedFallback = false;
        return true;
    }

    /// <summary>
    /// Announces a fallback option when no focus is detected.
    /// </summary>
    protected void AnnounceFallback(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        int currentMode = context.MenuMode;
        DateTime timestamp = context.Timestamp;

        if (context.UiState is not null && !State.SawHoverThisMode)
        {
            return;
        }

        if (State.AnnouncedFallback)
        {
            return;
        }

        string fallback = MenuNarrationCatalog.DescribeMenuItem(currentMode, 0);
        if (string.IsNullOrEmpty(fallback))
        {
            return;
        }

        bool sameAsLastFocus = !string.IsNullOrWhiteSpace(State.LastFocusAnnouncement) &&
            string.Equals(fallback, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase) &&
            timestamp - State.LastFocusAnnouncedAt < TimeSpan.FromSeconds(1);
        if (sameAsLastFocus)
        {
            State.AnnouncedFallback = true;
            State.ForceNextFocus = true;
            return;
        }

        TerrariaAccess.Instance?.Logger.Info($"[MenuHandler] Fallback focus -> {fallback}");
        events.Add(new MenuNarrationEvent(fallback, true, MenuNarrationEventKind.Focus));
        State.LastFocusAnnouncement = fallback;
        State.LastFocusAnnouncedAt = timestamp;
        State.AnnouncedFallback = true;
        State.ForceNextFocus = true;
    }

    /// <summary>
    /// Gets the localized back button label.
    /// </summary>
    protected static string GetBackLabel()
    {
        string label = TextSanitizer.Clean(Language.GetTextValue("UI.Back"));
        if (string.IsNullOrWhiteSpace(label))
        {
            label = TextSanitizer.Clean(Lang.menu[5].Value);
        }
        if (string.IsNullOrWhiteSpace(label))
        {
            label = "Back";
        }
        return label;
    }

    /// <summary>
    /// Checks if a label looks like a back button.
    /// </summary>
    protected static bool IsBackLabel(string label)
    {
        string cleaned = TextSanitizer.Clean(label);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        string backLabel = TextSanitizer.Clean(Language.GetTextValue("UI.Back"));
        if (!string.IsNullOrWhiteSpace(backLabel) && string.Equals(cleaned, backLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string langBack = TextSanitizer.Clean(Lang.menu[5].Value);
        if (!string.IsNullOrWhiteSpace(langBack) && string.Equals(cleaned, langBack, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return cleaned.Contains("back", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("close", StringComparison.OrdinalIgnoreCase);
    }
}
