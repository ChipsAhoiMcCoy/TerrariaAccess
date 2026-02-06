#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Handles world creation menus including size, difficulty, evil, name, and seed selection.
/// Uses UILinkPointNavigator.CurrentPoint (IDs 3000-3015) as the primary navigation mechanism
/// for keyboard/gamepad users, with hover-based detection as fallback for mouse users.
/// </summary>
internal sealed class WorldCreationHandler : MenuHandlerBase
{
    private MenuUiSelectionTracker.WorldCreationSnapshot _lastWorldCreationSnapshot;

    // Link point navigation tracking
    private int _lastLinkPointId = -1;
    private string? _lastLinkPointGroup;
    private bool _lastLinkPointSelected;

    public override int Priority => 70;

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
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
        _lastLinkPointId = -1;
        _lastLinkPointGroup = null;
        _lastLinkPointSelected = false;
    }

    public override void OnLeft()
    {
        base.OnLeft();
        _lastWorldCreationSnapshot = default;
        _lastLinkPointId = -1;
        _lastLinkPointGroup = null;
        _lastLinkPointSelected = false;
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

        // Primary: track UILinkPointNavigator for keyboard/gamepad navigation
        if (TryHandleLinkPointNavigation(context, events))
        {
            return events;
        }

        // When link point is in the world creation range, it's the authoritative
        // navigation source. Skip snapshot/hover to prevent double announcements
        // caused by Terraria's simulated cursor lagging behind the link point.
        if (MenuUiSelectionTracker.IsWorldCreationLinkPoint(UILinkPointNavigator.CurrentPoint))
        {
            // Keep snapshot in sync silently so it's ready if the user switches to mouse
            InitializeSnapshot(context);
            return events;
        }

        // Snapshot and hover only run for mouse users (link point not in world creation range)
        if (TryHandleWorldCreationSnapshot(context, events))
        {
            return events;
        }

        if (TryHandleWorldCreationHover(context, events))
        {
            return events;
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(context.MenuMode, context.UiState);
        State.LastModeAnnouncement = modeLabel;
        State.LastModeAnnouncedAt = context.Timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        // Announce the screen title (e.g., "World creation")
        if (!string.IsNullOrWhiteSpace(modeLabel))
        {
            ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Mode entry: {modeLabel}");
            events.Add(new MenuNarrationEvent(modeLabel, true, MenuNarrationEventKind.ModeChanged));
        }

        // Announce current focus via link point
        if (TryHandleLinkPointNavigation(context, events))
        {
            return;
        }

        // Initialize the snapshot so subsequent frames can detect value changes
        InitializeSnapshot(context);

        // Fallback to hover
        if (TryHandleWorldCreationHover(context, events))
        {
            return;
        }
    }

    /// <summary>
    /// Tracks UILinkPointNavigator.CurrentPoint changes to announce keyboard/gamepad navigation.
    /// </summary>
    private bool TryHandleLinkPointNavigation(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        int currentPoint = UILinkPointNavigator.CurrentPoint;

        // Only handle link points in the world creation range (3000-3015)
        if (!MenuUiSelectionTracker.IsWorldCreationLinkPoint(currentPoint))
        {
            return false;
        }

        bool pointChanged = currentPoint != _lastLinkPointId;

        // Check for selection state changes even if the point hasn't changed
        // (user pressed Enter/click to select an option)
        bool selectionChanged = false;
        if (!pointChanged && currentPoint >= MenuUiSelectionTracker.WcLinkSizeBase)
        {
            bool isSelected = MenuUiSelectionTracker.IsWorldCreationLinkPointSelected(context.UiState, currentPoint);
            if (isSelected != _lastLinkPointSelected)
            {
                selectionChanged = true;
                _lastLinkPointSelected = isSelected;
            }
        }

        if (!pointChanged && !selectionChanged)
        {
            return false;
        }

        // Determine if group changed (to decide whether to include group name)
        string newGroup = MenuUiSelectionTracker.GetWorldCreationLinkPointGroup(currentPoint);
        bool groupChanged = !string.Equals(_lastLinkPointGroup, newGroup, StringComparison.Ordinal);
        bool includeGroupName = groupChanged || _lastLinkPointId < 0;

        // Update tracking state
        if (pointChanged)
        {
            _lastLinkPointId = currentPoint;
            _lastLinkPointGroup = newGroup;
        }

        // Update selection tracking for group buttons
        if (currentPoint >= MenuUiSelectionTracker.WcLinkSizeBase)
        {
            _lastLinkPointSelected = MenuUiSelectionTracker.IsWorldCreationLinkPointSelected(context.UiState, currentPoint);
        }

        string description = MenuUiSelectionTracker.DescribeWorldCreationLinkPoint(
            context.UiState, currentPoint, includeGroupName);

        if (string.IsNullOrWhiteSpace(description))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[WorldCreationHandler] Empty description for link point {currentPoint}");
            return false;
        }

        ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Link point {currentPoint} -> {description}");
        events.Add(new MenuNarrationEvent(description, false, MenuNarrationEventKind.WorldCreation));

        // Keep snapshot in sync so it doesn't re-announce what the link point already announced
        InitializeSnapshot(context);

        return true;
    }

    /// <summary>
    /// Takes a snapshot of the current world creation state without announcing anything.
    /// Used to synchronize the snapshot tracker after link point announcements.
    /// </summary>
    private void InitializeSnapshot(MenuNarrationContext context)
    {
        UIElement? hovered = null;
        if (UiSelectionTracker.TryGetHoverLabel(Main.MenuUI, out MenuUiLabel hover) &&
            MenuUiSelectionTracker.IsWorldCreationElement(hover.Element))
        {
            hovered = hover.Element;
        }

        if (MenuUiSelectionTracker.TryBuildWorldCreationSnapshot(context.UiState, hovered, out var snapshot))
        {
            _lastWorldCreationSnapshot = snapshot;
        }
    }

    /// <summary>
    /// Detects value changes (selection/input) via the snapshot system.
    /// This handles mouse-driven changes and any changes not caught by link point tracking.
    /// </summary>
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

        ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Snapshot change: {announcement.Text}");
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
        if (current.IsEmpty)
        {
            return;
        }

        // Allow detection of selection value changes even without hover focus.
        // This handles keyboard users who change selections without mouse hover.
        if (!isFocused && !previous.IsEmpty)
        {
            // Still check for actual value changes (different option selected)
            bool valueChanged = current.Selected != previous.Selected ||
                current.Index != previous.Index ||
                !string.Equals(current.Option ?? string.Empty, previous.Option ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!valueChanged)
            {
                return;
            }
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

        string description = current.Describe(includeGroup: includeGroup);

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
        if (current.IsEmpty)
        {
            return;
        }

        // Allow detection of input value changes even without hover focus
        if (!isFocused && !previous.IsEmpty)
        {
            bool valueChanged = !string.Equals(current.Value ?? string.Empty, previous.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!valueChanged)
            {
                return;
            }
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

        // Need a valid element to check for tracked elements or buttons
        if (hover.Element is null)
        {
            return false;
        }

        // Skip hover announcements for tracked world creation elements (handled by link point or snapshot)
        if (MenuUiSelectionTracker.IsTrackedWorldCreationElement(hover.Element))
        {
            return true;
        }

        // Check if this is a bottom button (Create/Back) and handle specially
        if (MenuUiSelectionTracker.TryDescribeWorldCreationBottomButton(hover.Element, out string buttonDescription))
        {
            if (!string.IsNullOrWhiteSpace(buttonDescription))
            {
                ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Bottom button hover: {buttonDescription}");
                events.Add(new MenuNarrationEvent(buttonDescription, false, MenuNarrationEventKind.Hover));
                State.LastHoverAnnouncement = buttonDescription;
                State.LastHoverAnnouncedAt = context.Timestamp;
                State.SawHoverThisMode = true;
                return true;
            }
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

        ScreenReaderMod.Instance?.Logger.Info($"[WorldCreationHandler] Generic hover: {cleaned}");
        events.Add(new MenuNarrationEvent(cleaned, false, MenuNarrationEventKind.Hover));
        State.LastHoverAnnouncement = cleaned;
        State.LastHoverAnnouncedAt = context.Timestamp;
        State.SawHoverThisMode = true;
        return true;
    }
}
