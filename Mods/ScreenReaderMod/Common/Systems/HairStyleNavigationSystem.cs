#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides simplified linear navigation for the hair styles menu during character creation.
/// Instead of grid-based navigation (up/down/left/right), this system enables linear
/// left/right navigation through all hairstyles in sequence, with automatic scrolling.
/// </summary>
public sealed class HairStyleNavigationSystem : ModSystem
{
    // Reflection fields for accessing UICharacterCreation internals
    private static readonly Type? UiCharacterCreationType = typeof(UICharacterCreation);
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? SelectedPickerField = UiCharacterCreationType?.GetField("_selectedPicker", PrivateInstance);
    private static readonly FieldInfo? HairstylesContainerField = UiCharacterCreationType?.GetField("_hairstylesContainer", PrivateInstance);
    private static readonly FieldInfo? PlayerField = UiCharacterCreationType?.GetField("_player", PrivateInstance);

    // CategoryId enum value for HairStyle (index 2)
    private const int HairStyleCategoryId = 2;

    // Cached data
    private static UICharacterCreation? _cachedCharacterCreation;
    private static List<int>? _cachedHairStyleIds;
    private static UIScrollbar? _cachedScrollbar;

    // Navigation state
    private static bool _isHairStyleModeActive;
    private static bool _hasFocusedOnHairButton; // True once user actually focuses on a hair button
    private static int _currentHairIndex;
    private static int _lastAnnouncedIndex = -1;
    private static int _framesSinceLastNavigation;

    /// <summary>
    /// Indicates whether this system is currently handling hair style navigation.
    /// Used by MenuUiSelectionTracker to avoid duplicate announcements.
    /// Only returns true when actually focused on a hair button, not just when the category is selected.
    /// </summary>
    internal static bool IsHandlingNavigation
    {
        get
        {
            if (!_isHairStyleModeActive)
                return false;

            // Only handle navigation when actually on a hair button (link points 3020-3319)
            int currentPoint = UILinkPointNavigator.CurrentPoint;
            return currentPoint >= 3020 && currentPoint < 3020 + 300;
        }
    }

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UICharacterCreation.Draw += HandleCharacterCreationDraw;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_UICharacterCreation.Draw -= HandleCharacterCreationDraw;
        ClearCache();
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ || !Main.gameMenu)
        {
            return;
        }

        // Track frames for debouncing announcements
        if (_framesSinceLastNavigation < int.MaxValue)
        {
            _framesSinceLastNavigation++;
        }

        // Only active in hair style mode
        if (!_isHairStyleModeActive)
        {
            return;
        }

        // Handle navigation - always check keyboard input, plus gamepad triggers if in gamepad UI mode
        // Keyboard navigation (A/D keys) should work regardless of gamepad emulation state
        ProcessKeyboardNavigation();

        // Also check gamepad triggers when using gamepad UI
        if (PlayerInput.UsingGamepadUI)
        {
            ProcessLinearNavigation();
        }

        // Also detect and announce external hair changes (e.g., from vanilla navigation)
        DetectExternalHairChanges();
    }

    private static void ClearCache()
    {
        _cachedCharacterCreation = null;
        _cachedHairStyleIds = null;
        _cachedScrollbar = null;
        _isHairStyleModeActive = false;
        _hasFocusedOnHairButton = false;
        _currentHairIndex = 0;
        _lastAnnouncedIndex = -1;
        _framesSinceLastNavigation = 0;
    }

    /// <summary>
    /// Hook into UICharacterCreation.Draw to detect when hair style mode is active
    /// and cache necessary references.
    /// </summary>
    private static void HandleCharacterCreationDraw(On_UICharacterCreation.orig_Draw orig, UICharacterCreation self, SpriteBatch spriteBatch)
    {
        // Update our state before vanilla draw
        UpdateHairStyleModeState(self);

        orig(self, spriteBatch);

        // After vanilla draw, reconfigure navigation if in hair style mode
        if (_isHairStyleModeActive && PlayerInput.UsingGamepadUI)
        {
            ReconfigureNavigation();
        }
    }

    /// <summary>
    /// Update whether we're currently in hair style selection mode.
    /// </summary>
    private static void UpdateHairStyleModeState(UICharacterCreation self)
    {
        bool wasActive = _isHairStyleModeActive;
        _isHairStyleModeActive = IsHairStyleCategoryActive(self);

        // Cache data when entering hair style mode (category tab selected)
        if (_isHairStyleModeActive && (!wasActive || _cachedCharacterCreation != self))
        {
            CacheHairStyleData(self);

            // Initialize tracking state when entering hair mode
            // Don't announce yet - wait until user actually focuses on a hair button
            if (!wasActive && _cachedHairStyleIds is not null)
            {
                Player? player = PlayerField?.GetValue(self) as Player;
                if (player is not null)
                {
                    int idx = _cachedHairStyleIds.IndexOf(player.hair);
                    if (idx >= 0)
                    {
                        _currentHairIndex = idx;
                        _hasFocusedOnHairButton = false; // Will be set true when user enters grid
                    }
                }
            }
        }
        else if (!_isHairStyleModeActive && wasActive)
        {
            // Clear cache when leaving hair style mode
            ClearCache();
        }

        // Always update current hair index when active
        if (_isHairStyleModeActive && _cachedHairStyleIds is not null)
        {
            Player? player = PlayerField?.GetValue(self) as Player;
            if (player is not null)
            {
                int idx = _cachedHairStyleIds.IndexOf(player.hair);
                if (idx >= 0)
                {
                    _currentHairIndex = idx;
                }
            }
        }
    }

    /// <summary>
    /// Check if the hair style category is currently selected.
    /// </summary>
    private static bool IsHairStyleCategoryActive(UICharacterCreation self)
    {
        if (SelectedPickerField is null)
        {
            return false;
        }

        try
        {
            object? value = SelectedPickerField.GetValue(self);
            if (value is int categoryId)
            {
                return categoryId == HairStyleCategoryId;
            }

            // Handle enum type
            if (value is Enum enumValue)
            {
                return Convert.ToInt32(enumValue) == HairStyleCategoryId;
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return false;
    }

    /// <summary>
    /// Cache references to hair style UI elements.
    /// </summary>
    private static void CacheHairStyleData(UICharacterCreation self)
    {
        _cachedCharacterCreation = self;
        _cachedHairStyleIds = null;
        _cachedScrollbar = null;

        if (HairstylesContainerField is null)
        {
            return;
        }

        try
        {
            UIElement? container = HairstylesContainerField.GetValue(self) as UIElement;
            if (container is null)
            {
                return;
            }

            // Find the scrollbar within the container for auto-scrolling
            _cachedScrollbar = FindChildOfType<UIScrollbar>(container);

            // Get all hair style IDs from the available hairstyles
            // This ensures we follow the game's hairstyle availability order
            _cachedHairStyleIds = new List<int>(Main.Hairstyles.AvailableHairstyles);

            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] Cached {_cachedHairStyleIds.Count} hair styles");
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] Failed to cache data: {ex.Message}");
        }
    }

    /// <summary>
    /// Find a child element of a specific type within a UI hierarchy.
    /// </summary>
    private static T? FindChildOfType<T>(UIElement parent) where T : UIElement
    {
        foreach (UIElement child in parent.Children)
        {
            if (child is T typed)
            {
                return typed;
            }

            T? found = FindChildOfType<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Process linear navigation when in hair style mode.
    /// This intercepts navigation input and applies linear left/right movement.
    /// </summary>
    private static void ProcessLinearNavigation()
    {
        if (_cachedHairStyleIds is null || _cachedHairStyleIds.Count == 0 || _cachedCharacterCreation is null)
        {
            return;
        }

        // Only process if we're currently on a hair link point
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (currentPoint < 3020 || currentPoint >= 3020 + 300)
        {
            return;
        }

        TriggersPack triggers = PlayerInput.Triggers;
        int totalCount = _cachedHairStyleIds.Count;
        int newIndex = _currentHairIndex;
        bool navigated = false;

        // Handle left navigation (previous hair style)
        if (triggers.JustPressed.MenuLeft)
        {
            newIndex = _currentHairIndex - 1;
            if (newIndex < 0)
            {
                newIndex = totalCount - 1; // Wrap to end
            }
            navigated = true;
        }
        // Handle right navigation (next hair style)
        else if (triggers.JustPressed.MenuRight)
        {
            newIndex = _currentHairIndex + 1;
            if (newIndex >= totalCount)
            {
                newIndex = 0; // Wrap to beginning
            }
            navigated = true;
        }

        // Apply navigation if it occurred
        if (navigated)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] Navigating from index {_currentHairIndex} to {newIndex}");
            SelectHairStyle(newIndex, announceChange: true);
            _currentHairIndex = newIndex;
            _lastAnnouncedIndex = newIndex;
            _framesSinceLastNavigation = 0;
        }
    }

    /// <summary>
    /// Process direct keyboard input (A/D and arrow keys) for hair style navigation.
    /// Called regardless of gamepad UI mode to ensure keyboard navigation always works.
    /// </summary>
    private static void ProcessKeyboardNavigation()
    {
        if (_cachedHairStyleIds is null || _cachedHairStyleIds.Count == 0 || _cachedCharacterCreation is null)
        {
            return;
        }

        // Don't intercept A/D keys if user is typing in a text field (e.g., character name)
        if (Main.editSign || Main.editChest || Main.blockInput || PlayerInput.WritingText)
        {
            return;
        }

        bool navigated = false;
        int newIndex = _currentHairIndex;
        int totalCount = _cachedHairStyleIds.Count;

        // Check for A/Left arrow (previous) and D/Right arrow (next)
        bool leftPressed = (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A) &&
                           !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A)) ||
                          (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left) &&
                           !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left));

        bool rightPressed = (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D) &&
                            !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D)) ||
                           (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right) &&
                            !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right));

        if (leftPressed)
        {
            newIndex = _currentHairIndex - 1;
            if (newIndex < 0)
            {
                newIndex = totalCount - 1;
            }
            navigated = true;
        }
        else if (rightPressed)
        {
            newIndex = _currentHairIndex + 1;
            if (newIndex >= totalCount)
            {
                newIndex = 0;
            }
            navigated = true;
        }

        if (navigated)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] Keyboard nav from {_currentHairIndex} to {newIndex}");
            SelectHairStyle(newIndex, announceChange: true);
            _currentHairIndex = newIndex;
            _lastAnnouncedIndex = newIndex;
            _framesSinceLastNavigation = 0;
        }
    }

    /// <summary>
    /// Detect when the hair style changes externally (e.g., vanilla link point navigation)
    /// and announce only if we haven't recently navigated ourselves.
    /// Also handles announcing when user first focuses on a hair button in the grid.
    /// </summary>
    private static void DetectExternalHairChanges()
    {
        if (_cachedHairStyleIds is null || _cachedCharacterCreation is null)
        {
            return;
        }

        // Check if user is currently focused on a hair button (link points 3020-3319)
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        bool isOnHairButton = currentPoint >= 3020 && currentPoint < 3020 + 300;

        // If not on a hair button, reset state and return
        // This ensures we re-announce when user enters the grid again
        if (!isOnHairButton)
        {
            if (_hasFocusedOnHairButton)
            {
                _hasFocusedOnHairButton = false;
            }
            return;
        }

        Player? player = PlayerField?.GetValue(_cachedCharacterCreation) as Player;
        if (player is null)
        {
            return;
        }

        int actualIndex = _cachedHairStyleIds.IndexOf(player.hair);
        if (actualIndex < 0)
        {
            return;
        }

        // Check if we need to announce:
        // 1. First time focusing on a hair button (user just entered the grid)
        // 2. Hair changed externally while on a hair button
        bool justEnteredGrid = !_hasFocusedOnHairButton;
        bool hairChanged = actualIndex != _currentHairIndex;

        if (justEnteredGrid)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] User entered hair grid, current index={actualIndex}");
            _hasFocusedOnHairButton = true;
            _currentHairIndex = actualIndex;
        }
        else if (hairChanged)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] External change detected: tracked={_currentHairIndex}, actual={actualIndex}");
            _currentHairIndex = actualIndex;
        }

        // Announce if just entered grid or hair changed, with debouncing
        if ((justEnteredGrid || hairChanged) && _framesSinceLastNavigation > 3 && actualIndex != _lastAnnouncedIndex)
        {
            ScrollToIndex(actualIndex);
            string announcement = BuildHairStyleAnnouncement(actualIndex, _cachedHairStyleIds.Count);
            if (!string.IsNullOrWhiteSpace(announcement))
            {
                ScreenReaderService.Announce(announcement, force: true);
            }
            _lastAnnouncedIndex = actualIndex;
        }
    }

    /// <summary>
    /// Select a hair style by index and update scrolling.
    /// </summary>
    /// <param name="index">Index in the available hairstyles list</param>
    /// <param name="announceChange">Whether to announce the change (false when called externally)</param>
    private static void SelectHairStyle(int index, bool announceChange = true)
    {
        if (_cachedHairStyleIds is null || index < 0 || index >= _cachedHairStyleIds.Count)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] SelectHairStyle: invalid index {index}");
            return;
        }

        if (_cachedCharacterCreation is null)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] SelectHairStyle: no cached character creation");
            return;
        }

        int hairId = _cachedHairStyleIds[index];

        // Get the player and set the hair style
        Player? player = PlayerField?.GetValue(_cachedCharacterCreation) as Player;
        if (player is not null)
        {
            int oldHair = player.hair;
            player.hair = hairId;
            SoundEngine.PlaySound(SoundID.MenuTick);

            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] SelectHairStyle: index={index}, hairId={hairId}, oldHair={oldHair}, newHair={player.hair}");

            // Scroll to make the new selection visible (use index, not hairId)
            ScrollToIndex(index);

            // Announce the new hairstyle only if requested
            if (announceChange)
            {
                string announcement = BuildHairStyleAnnouncement(index, _cachedHairStyleIds.Count);
                if (!string.IsNullOrWhiteSpace(announcement))
                {
                    ScreenReaderService.Announce(announcement, force: true);
                }
            }
        }
        else
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] SelectHairStyle: player is null");
        }
    }

    /// <summary>
    /// Scroll the list to ensure the selected hair style is visible.
    /// Uses the index in AvailableHairstyles (not the hairId) to calculate position.
    /// </summary>
    /// <param name="index">Index in AvailableHairstyles list</param>
    private static void ScrollToIndex(int index)
    {
        if (_cachedScrollbar is null || _cachedHairStyleIds is null)
        {
            return;
        }

        try
        {
            // Hair styles are arranged in a grid of 10 per row, 48 pixels per row
            // The index in AvailableHairstyles determines the grid position, not the hairId
            int row = index / 10;
            float buttonTop = row * 48f;
            float buttonBottom = buttonTop + 48f;

            // Get current scroll state
            float viewPosition = _cachedScrollbar.ViewPosition;
            float viewSize = _cachedScrollbar.ViewSize;
            float viewBottom = viewPosition + viewSize;

            // Padding to keep selection from being at the very edge
            const float padding = 6f;

            if (buttonTop < viewPosition + padding)
            {
                // Button is above the visible area, scroll up
                _cachedScrollbar.ViewPosition = Math.Max(0, buttonTop - padding);
            }
            else if (buttonBottom > viewBottom - padding)
            {
                // Button is below the visible area, scroll down
                float maxScroll = _cachedScrollbar.MaxViewSize - viewSize;
                _cachedScrollbar.ViewPosition = Math.Min(maxScroll, buttonBottom - viewSize + padding);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[HairStyleNav] Failed to scroll: {ex.Message}");
        }
    }

    /// <summary>
    /// Build an announcement string for the given hair style.
    /// </summary>
    /// <param name="index">The index in AvailableHairstyles (0-based)</param>
    /// <param name="totalCount">Total number of available hairstyles</param>
    private static string BuildHairStyleAnnouncement(int index, int totalCount)
    {
        // Get the actual hair ID to look up the description
        int hairId = (_cachedHairStyleIds is not null && index >= 0 && index < _cachedHairStyleIds.Count)
            ? _cachedHairStyleIds[index]
            : -1;

        // Look up description using hairId (not index)
        string? description = null;
        var descriptions = MenuNarration.MenuUiSelectionTracker.HairStyleDescriptions;
        if (hairId >= 0 && hairId < descriptions.Length)
        {
            description = descriptions[hairId];
        }

        // Format: "Selected, [description], [position] of [total]"
        // Use 1-based position for user-friendly announcement
        if (!string.IsNullOrWhiteSpace(description))
        {
            return $"Selected, {description} {index + 1} of {totalCount}";
        }

        return $"Selected, {index + 1} of {totalCount}";
    }

    /// <summary>
    /// Reconfigure navigation link points to be linear after vanilla setup.
    ///
    /// Key insight about vanilla's link point assignment:
    /// - Snap points are created with SetSnapPoint("Middle", i) where i = index in AvailableHairstyles
    /// - During SetupGamepadPoints, vanilla culls to only visible snap points
    /// - Link IDs are assigned sequentially starting at 3020 for the first visible snap point
    /// - So if indices 15-45 are visible: index 15 → link 3020, index 16 → link 3021, etc.
    /// </summary>
    private static void ReconfigureNavigation()
    {
        if (_cachedHairStyleIds is null || _cachedHairStyleIds.Count == 0 || _cachedCharacterCreation is null)
        {
            return;
        }

        // Get the player's current hair and find its index
        Player? player = PlayerField?.GetValue(_cachedCharacterCreation) as Player;
        if (player is null)
        {
            return;
        }

        int currentHairId = player.hair;
        int currentIndex = _cachedHairStyleIds.IndexOf(currentHairId);
        if (currentIndex < 0)
        {
            return;
        }

        // Constants from vanilla UICharacterCreation.SetupGamepadPoints
        const int baseLinkId = 3000;
        const int middleLinkOffset = 20; // Hair buttons start at 3020
        const int topLinkOffset = 2;     // Category bar starts at 3002

        // Hairstyle category button is at index 2 (CategoryId.HairStyle)
        int hairCategoryLinkId = baseLinkId + topLinkOffset + 2; // 3004

        // Find all hair link points
        var hairLinkPoints = new List<(int LinkId, UILinkPoint Point)>();

        foreach (var kvp in UILinkPointNavigator.Points)
        {
            if (kvp.Key >= baseLinkId + middleLinkOffset && kvp.Key < baseLinkId + middleLinkOffset + 300)
            {
                hairLinkPoints.Add((kvp.Key, kvp.Value));
            }
        }

        if (hairLinkPoints.Count == 0)
        {
            return;
        }

        // Sort by link ID to ensure proper ordering
        hairLinkPoints.Sort((a, b) => a.LinkId.CompareTo(b.LinkId));

        // Configure each visible link point
        // IMPORTANT: Set Left/Right to point to self to prevent vanilla's navigation
        // from interfering. Our ProcessLinearNavigation handles the actual navigation.
        for (int i = 0; i < hairLinkPoints.Count; i++)
        {
            var (lpLinkId, linkPoint) = hairLinkPoints[i];

            // Disable vanilla's left/right navigation by pointing to self
            // Our ProcessLinearNavigation will handle the actual hairstyle changes
            linkPoint.Left = lpLinkId;
            linkPoint.Right = lpLinkId;

            // Configure Up to exit to the hairstyle category button
            // This allows the user to press Up to leave the hairstyle grid
            linkPoint.Up = hairCategoryLinkId;

            // Configure Down to go to Back (3000) or Create (3001) buttons
            // Use Back for left half, Create for right half
            int column = i % 10;
            linkPoint.Down = (column < 5) ? baseLinkId : (baseLinkId + 1);
        }

        // NOTE: We intentionally do NOT force navigation to a specific link point here.
        // Previously, this method called UILinkPointNavigator.ChangePoint() to move focus
        // to the hair button matching the current selection. This caused a bug where
        // pressing Up/Down would briefly navigate away but immediately snap back to the
        // hair grid on the next frame. Now we only reconfigure the link point directions
        // and let vanilla navigation handle focus changes naturally.
    }
}
