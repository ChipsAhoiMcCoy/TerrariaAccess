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
        State.PendingHoverFocusSuppression = cleaned;
        State.PendingInitialFocus = null;
        State.PendingInitialFocusAnnouncement = null;
        State.SawHoverThisMode = true;
        return true;
    }

    /// <summary>
    /// Tries to announce the first concrete list entry for UIState-based menus that
    /// already have their item list populated on entry.
    /// </summary>
    protected bool TryAnnounceFirstListEntry(
        MenuNarrationContext context,
        FieldInfo? listField,
        Func<UIElement, bool> isListEntry,
        List<MenuNarrationEvent> events)
    {
        if (context.UiState is null || listField is null)
        {
            return false;
        }

        UIElement? listRoot;
        try
        {
            listRoot = listField.GetValue(context.UiState) as UIElement;
        }
        catch
        {
            return false;
        }

        UIElement? entry = FindFirstMatchingElement(listRoot, isListEntry);
        if (entry is null)
        {
            return false;
        }

        string label = TextSanitizer.Clean(MenuUiSelectionTracker.ResolveLabel(entry));
        if (string.IsNullOrWhiteSpace(label) || !IsAllowedHover(context.MenuMode, label))
        {
            return false;
        }

        TerrariaAccess.Instance?.Logger.Info($"[MenuHandler] Immediate list entry -> {label}");
        events.Add(new MenuNarrationEvent(label, true, MenuNarrationEventKind.Focus));
        State.LastFocusAnnouncement = label;
        State.LastFocusAnnouncedAt = context.Timestamp;
        State.LastFocus = new MenuFocus(0, "UiListEntry");
        State.AnnouncedFallback = false;
        State.ForceNextFocus = false;
        State.PendingInitialFocus = null;
        State.PendingInitialFocusAnnouncement = null;
        return true;
    }

    protected static bool TryGetFirstListEntry(
        MenuNarrationContext context,
        FieldInfo? listField,
        Func<UIElement, bool> isListEntry,
        out UIElement? entry)
    {
        entry = null;

        if (context.UiState is null || listField is null)
        {
            return false;
        }

        UIElement? listRoot;
        try
        {
            listRoot = listField.GetValue(context.UiState) as UIElement;
        }
        catch
        {
            return false;
        }

        entry = FindFirstMatchingElement(listRoot, isListEntry);
        return entry is not null;
    }

    protected static bool TryGetAssignableField<T>(object source, out T? value)
        where T : class
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in source.GetType().GetFields(flags))
        {
            if (!typeof(T).IsAssignableFrom(field.FieldType))
            {
                continue;
            }

            try
            {
                if (field.GetValue(source) is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            catch
            {
                // ignore reflection failures
            }
        }

        value = null;
        return false;
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

        string optionLabel = MenuNarrationCatalog.DescribeMenuItem(currentMode, focus.Index);
        bool hasDeletionAnnouncement = MenuNarrationCatalog.TryBuildDeletionAnnouncement(currentMode, focus.Index, out string combinedLabel);
        string announcement = hasDeletionAnnouncement ? combinedLabel : optionLabel;

        if (ShouldDelayUnconfirmedInitialFocus(focus, announcement))
        {
            return false;
        }

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
            bool matchesRecentHover = !string.IsNullOrWhiteSpace(State.PendingHoverFocusSuppression) &&
                string.Equals(optionLabel, State.PendingHoverFocusSuppression, StringComparison.OrdinalIgnoreCase);
            bool matchesLastFocus = !string.IsNullOrWhiteSpace(State.LastFocusAnnouncement) &&
                string.Equals(announcement, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);

            if (!force && !announcementChanged && (matchesRecentHover || matchesLastFocus))
            {
                State.PendingHoverFocusSuppression = null;
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
                State.PendingHoverFocusSuppression = null;
                State.PendingInitialFocus = null;
                State.PendingInitialFocusAnnouncement = null;
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
            string.Equals(fallback, State.LastFocusAnnouncement, StringComparison.OrdinalIgnoreCase);
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
        State.PendingInitialFocus = null;
        State.PendingInitialFocusAnnouncement = null;
        State.AnnouncedFallback = true;
        State.ForceNextFocus = true;
    }

    private bool ShouldDelayUnconfirmedInitialFocus(MenuFocus focus, string announcement)
    {
        if (State.SawHoverThisMode || State.LastFocus.HasValue || IsReliableInitialFocusSource(focus.Source))
        {
            State.PendingInitialFocus = null;
            State.PendingInitialFocusAnnouncement = null;
            return false;
        }

        bool sameAsPending = State.PendingInitialFocus.HasValue &&
            State.PendingInitialFocus.Value.Equals(focus) &&
            string.Equals(State.PendingInitialFocusAnnouncement, announcement, StringComparison.OrdinalIgnoreCase);
        if (sameAsPending)
        {
            State.PendingInitialFocus = null;
            State.PendingInitialFocusAnnouncement = null;
            return false;
        }

        State.PendingInitialFocus = focus;
        State.PendingInitialFocusAnnouncement = announcement;
        return true;
    }

    private static bool IsReliableInitialFocusSource(string source)
    {
        return source.Equals("Main.focusMenu", StringComparison.Ordinal) ||
            source.Equals("Main.selectedMenu", StringComparison.Ordinal) ||
            source.Equals("PlayerMenuFallback", StringComparison.Ordinal);
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

    protected static UIElement? FindFirstMatchingElement(UIElement? root, Func<UIElement, bool> predicate)
    {
        if (root is null)
        {
            return null;
        }

        if (predicate(root))
        {
            return root;
        }

        foreach (UIElement child in root.Children)
        {
            UIElement? match = FindFirstMatchingElement(child, predicate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
