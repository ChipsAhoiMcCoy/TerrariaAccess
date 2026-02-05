#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles world creation menus including size, difficulty, evil, name, and seed selection.
/// </summary>
internal sealed class WorldCreationHandler : MenuHandlerBase
{
    private MenuUiSelectionTracker.WorldCreationSnapshot _lastWorldCreationSnapshot;

    public override int Priority => 70;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        // Check if the UI state is a world creation screen
        if (uiState is null)
        {
            return false;
        }

        string? typeName = uiState.GetType().FullName;
        return typeName?.Contains("UIWorldCreation", StringComparison.Ordinal) == true;
    }

    public override void OnEntered(MenuNarrationContext context)
    {
        base.OnEntered(context);
        _lastWorldCreationSnapshot = default;
    }

    public override void OnLeft()
    {
        base.OnLeft();
        _lastWorldCreationSnapshot = default;
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

        // Handle world creation UI elements
        if (TryHandleWorldCreationSnapshot(context, events))
        {
            return events;
        }

        // Handle hover events for non-tracked elements
        if (TryHandleWorldCreationHover(context, events))
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

        // Handle world creation snapshot
        if (TryHandleWorldCreationSnapshot(context, events))
        {
            return;
        }

        // Handle hover
        if (TryHandleWorldCreationHover(context, events))
        {
            return;
        }

        // Force focus announcement
        TryHandleFocus(context, true, events);
    }

    private bool TryHandleWorldCreationSnapshot(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        UIElement? hovered = null;
        if (UiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover) &&
            MenuUiSelectionTracker.IsWorldCreationElement(hover.Element))
        {
            hovered = hover.Element;
        }

        if (!MenuUiSelectionTracker.TryBuildWorldCreationSnapshot(context.UiState, hovered, out MenuUiSelectionTracker.WorldCreationSnapshot snapshot))
        {
            _lastWorldCreationSnapshot = default;
            return false;
        }

        if (snapshot.IsEmpty)
        {
            _lastWorldCreationSnapshot = snapshot;
            return false;
        }

        var changes = new List<(string Text, bool Focused)>(5);

        AddSelectionChange(snapshot.Size, _lastWorldCreationSnapshot.Size, changes, snapshot.SizeFocused, _lastWorldCreationSnapshot.SizeFocused);
        AddSelectionChange(snapshot.Difficulty, _lastWorldCreationSnapshot.Difficulty, changes, snapshot.DifficultyFocused, _lastWorldCreationSnapshot.DifficultyFocused);
        AddSelectionChange(snapshot.Evil, _lastWorldCreationSnapshot.Evil, changes, snapshot.EvilFocused, _lastWorldCreationSnapshot.EvilFocused);
        AddInputChange(snapshot.Name, _lastWorldCreationSnapshot.Name, changes, snapshot.NameFocused, _lastWorldCreationSnapshot.NameFocused);
        AddInputChange(snapshot.Seed, _lastWorldCreationSnapshot.Seed, changes, snapshot.SeedFocused, _lastWorldCreationSnapshot.SeedFocused);

        _lastWorldCreationSnapshot = snapshot;

        if (changes.Count == 0)
        {
            return false;
        }

        (string Text, bool Focused) announcement = changes.Find(change => change.Focused);
        if (string.IsNullOrWhiteSpace(announcement.Text))
        {
            announcement = changes[0];
        }

        ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Announcing: {announcement.Text}");
        events.Add(new MenuNarrationEvent(announcement.Text, true, MenuNarrationEventKind.WorldCreation));
        return true;
    }

    private static void AddSelectionChange(
        MenuUiSelectionTracker.WorldCreationSelection current,
        MenuUiSelectionTracker.WorldCreationSelection previous,
        List<(string Text, bool Focused)> buffer,
        bool isFocused,
        bool wasFocused)
    {
        if (!isFocused && !previous.IsEmpty)
        {
            return;
        }

        if (current.IsEmpty)
        {
            return;
        }

        bool unchanged = !previous.IsEmpty &&
            string.Equals(current.Option ?? string.Empty, previous.Option ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            current.Index == previous.Index &&
            current.Total == previous.Total &&
            current.Selected == previous.Selected;
        bool focusChanged = isFocused != wasFocused;
        if (unchanged && !focusChanged)
        {
            return;
        }

        bool includeGroup = previous.IsEmpty ||
            (isFocused && !wasFocused) ||
            !string.Equals(current.Group ?? string.Empty, previous.Group ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        string option = string.IsNullOrWhiteSpace(current.Option) ? current.Group ?? string.Empty : current.Option ?? string.Empty;
        string group = current.Group ?? string.Empty;
        string description;
        if (includeGroup)
        {
            if (string.IsNullOrWhiteSpace(group))
            {
                description = current.Describe(includeGroup: true);
            }
            else
            {
                description = current.Selected
                    ? TextSanitizer.JoinWithComma(group, $"Selected {option}")
                    : TextSanitizer.JoinWithComma(group, option);
            }
        }
        else
        {
            description = TextSanitizer.Clean(option ?? string.Empty);
            if (current.Selected)
            {
                description = TextSanitizer.JoinWithComma("Selected", description);
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        buffer.Add((description, isFocused));
    }

    private static void AddInputChange(
        MenuUiSelectionTracker.WorldCreationInput current,
        MenuUiSelectionTracker.WorldCreationInput previous,
        List<(string Text, bool Focused)> buffer,
        bool isFocused,
        bool wasFocused)
    {
        if (!isFocused && !previous.IsEmpty)
        {
            return;
        }

        if (current.IsEmpty)
        {
            return;
        }

        bool unchanged = !previous.IsEmpty &&
            string.Equals(current.Value ?? string.Empty, previous.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.Prefix ?? string.Empty, previous.Prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        bool focusChanged = isFocused != wasFocused;
        if (unchanged && !focusChanged)
        {
            return;
        }

        bool includePrefix = previous.IsEmpty || (isFocused && !wasFocused);
        string description = current.Describe(includePrefix);
        if (!includePrefix && !string.IsNullOrWhiteSpace(current.Value))
        {
            description = TextSanitizer.Clean(current.Value);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        buffer.Add((description, isFocused));
    }

    private bool TryHandleWorldCreationHover(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        if (!UiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover))
        {
            return false;
        }

        if (!hover.IsNew)
        {
            return true;
        }

        // Skip hover announcements for tracked world creation elements
        if (MenuUiSelectionTracker.IsTrackedWorldCreationElement(hover.Element))
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

        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }
}
