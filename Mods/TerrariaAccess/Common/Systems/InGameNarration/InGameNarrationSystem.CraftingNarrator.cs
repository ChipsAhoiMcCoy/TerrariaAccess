#nullable enable
using System;
using System.Collections.Generic;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Handles narration for the crafting UI, including recipe announcements,
    /// Guide crafting menu, and Goblin Tinkerer reforge menu.
    /// Split across partial files:
    /// - CraftingNarrator.cs (this file): Core update loop and focus management
    /// - CraftingNarrator.Recipe.cs: Recipe types, focus resolution, and announcement building
    /// - CraftingNarrator.Guide.cs: Guide and Reforge menu handling
    /// </summary>
    private sealed partial class CraftingNarrator
    {
        #region State

        private RecipeAnnouncement? _lastAnnouncement;
        private bool? _lastWasGridMode;
        private static RecipeFocus? _hoveredFocusOverride;
        private static uint _hoveredFocusFrame;
        private readonly HashSet<int> _missingRequirementRecipes = new();
        private bool _subscribedToInventoryOpened;

        #endregion

        #region Link Point Constants

        // Link point ranges for crafting-related UI in gamepad mode
        private const int CraftingGridLinkPointStart = 700;
        private const int CraftingGridLinkPointEnd = 1500;
        private const int CraftingListLinkPointStart = 1500;
        private const int CraftingListLinkPointEnd = 2000;

        #endregion

        #region Lifecycle

        private void EnsureSubscribedToInventoryOpened()
        {
            if (_subscribedToInventoryOpened)
            {
                return;
            }

            InventoryNarrator.InventoryOpened += OnInventoryOpened;
            _subscribedToInventoryOpened = true;
        }

        private void OnInventoryOpened()
        {
            // Reset crafting state when inventory opens to prevent stale announcements
            // and ensure crafting doesn't speak until user navigates to it
            ResetFocus();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Main update method called each frame when the player inventory might be open.
        /// </summary>
        public void Update(Player player)
        {
            EnsureSubscribedToInventoryOpened();

            if (Main.ingameOptionsWindow)
            {
                Reset();
                return;
            }

            if (!InventoryNarrator.IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            HandleGuideAndReforge(player);

            // When using gamepad UI, detect crafting link points FIRST and set the area context
            // before the area check. This ensures we can announce when navigating to crafting.
            int currentPoint = PlayerInput.UsingGamepadUI ? UILinkPointNavigator.CurrentPoint : -1;
            bool isOnCraftingGrid = currentPoint >= CraftingGridLinkPointStart && currentPoint < CraftingGridLinkPointEnd;
            bool isOnCraftingList = currentPoint >= CraftingListLinkPointStart && currentPoint < CraftingListLinkPointEnd;

            if (PlayerInput.UsingGamepadUI)
            {
                // First check if we have cached context for this link point
                if (InventoryNarrator.TryGetContextForLinkPoint(currentPoint, out int context))
                {
                    if (!ItemSlotContextFacts.IsCraftingContext(context))
                    {
                        ResetFocus();
                        return;
                    }
                    // Valid crafting context - ensure area is set
                    UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
                }
                else
                {
                    // No cached context - check if link point is in a crafting-related range
                    // If not, don't announce (prevents speaking first recipe when inventory opens)
                    if (!IsLinkPointInCraftingRange(currentPoint))
                    {
                        ResetFocus();
                        return;
                    }
                    // Link point is in crafting range - set the area context
                    UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
                }
            }

            if (!UiAreaNarrationContext.IsActiveArea(UiNarrationArea.Crafting | UiNarrationArea.Guide))
            {
                ResetFocus();
                return;
            }

            // Determine actual grid mode based on link point position, not Main.recBigList,
            // because Terraria doesn't reset recBigList when leaving the grid page.
            bool actualGridMode = isOnCraftingGrid && Main.recBigList;

            // Track mode based on link point position for region announcements, because
            // Main.recBigList may not be set on the same frame the link point changes.
            // This prevents the region prefix from being skipped on first navigation.
            bool linkPointGridMode = isOnCraftingGrid;

            // Detect first entry to crafting area (from inventory/hotbar) - when _lastWasGridMode
            // is null, we haven't been in crafting yet this session and should announce the region.
            bool isFirstEntry = !_lastWasGridMode.HasValue;

            // Detect mode change (grid <-> list) BEFORE capturing focus - this ensures
            // we clear stale hover data before building the announcement.
            // Use linkPointGridMode to avoid flickering when recBigList lags behind.
            bool modeChanged = _lastWasGridMode.HasValue && _lastWasGridMode.Value != linkPointGridMode;
            _lastWasGridMode = linkPointGridMode;

            // When mode changes, clear the stale hovered focus from the previous mode
            // to prevent announcing the old grid item when switching to the list
            if (modeChanged)
            {
                _hoveredFocusOverride = null;
                _hoveredFocusFrame = 0;
            }

            if (!TryCaptureFocus(out RecipeFocus focus))
            {
                ResetFocus();
                return;
            }

            if (!TryBuildAnnouncement(focus, out RecipeAnnouncement announcement))
            {
                return;
            }

            // Get region prefix for display (will return prefix if region changed in its tracking)
            // Use linkPointGridMode rather than actualGridMode for region prefix, because
            // Main.recBigList may not be set on the same frame the link point changes,
            // causing the region prefix to be skipped when first navigating to the grid.
            string? regionPrefix = InventoryNarrator.TryGetAndUpdateCraftingRegionPrefix(linkPointGridMode);

            // On first entry to crafting (from inventory/hotbar) or when mode changes (grid <-> list),
            // always announce the region prefix even if the region tracking thinks we're already
            // in that region. This handles cases where _lastAnnouncedRegion was updated by
            // InventoryNarrator before CraftingNarrator had a chance to announce.
            if ((modeChanged || isFirstEntry) && string.IsNullOrWhiteSpace(regionPrefix))
            {
                regionPrefix = InventoryNarrator.GetCraftingRegionDisplayName(linkPointGridMode);
            }

            bool hasRegionPrefix = !string.IsNullOrWhiteSpace(regionPrefix);

            // Skip deduplication if mode changed, first entry, OR if region prefix indicates a change
            bool skipDedup = modeChanged || isFirstEntry || hasRegionPrefix;

            if (!skipDedup && _lastAnnouncement.HasValue && _lastAnnouncement.Value.Equals(announcement))
            {
                return;
            }

            _lastAnnouncement = announcement;

            string message = hasRegionPrefix
                ? $"{regionPrefix}. {announcement.Message}"
                : announcement.Message;

            ScreenReaderService.Announce(message, force: true);
        }

        public void ForceReset()
        {
            Reset();
        }

        #endregion

        #region External API for InventoryNarrator

        /// <summary>
        /// Attempts to capture a hovered recipe from an item being displayed in the crafting UI.
        /// Called from InventoryNarrator when a recipe result is hovered.
        /// </summary>
        internal static bool TryCaptureHoveredRecipe(Item item)
        {
            if (!Main.playerInventory)
            {
                return false;
            }

            if (!TryResolveRecipeFocusFromReference(item, out RecipeFocus focus) &&
                !TryResolveRecipeFocus(item, out focus))
            {
                return false;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
            return true;
        }

        /// <summary>
        /// Attempts to focus a recipe at the given available index.
        /// Called from InventoryNarrator when navigating the crafting grid via gamepad.
        /// </summary>
        internal static bool TryFocusRecipeAtAvailableIndex(int availableIndex)
        {
            if (!TryCreateFocusFromAvailableIndex(availableIndex, out RecipeFocus focus))
            {
                return false;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
            return true;
        }

        /// <summary>
        /// Gets requirement tooltip details for an item if it's a craftable recipe result.
        /// Called from InventoryNarrator when building item details.
        /// </summary>
        internal static string? TryGetRequirementTooltipDetails(Item item, bool locationIsEmpty)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            if (!locationIsEmpty)
            {
                return null;
            }

            if (!Main.playerInventory)
            {
                return null;
            }

            if (!Main.mouseItem.IsAir)
            {
                return null;
            }

            if (!TryResolveRecipeFocus(item, out RecipeFocus focus))
            {
                return null;
            }

            string? message = BuildRequirementMessage(focus.Recipe, out _);
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }

        /// <summary>
        /// Captures a recipe hover when an item slot with crafting context is focused.
        /// Called from InventoryNarrator's RecordFocus method.
        /// </summary>
        internal static void TryCaptureRecipeHover(Item item, int context)
        {
            if (item is null)
            {
                return;
            }

            UiNarrationArea area = ItemSlotContextFacts.ResolveArea(context);
            if ((area & (UiNarrationArea.Crafting | UiNarrationArea.Guide)) == 0)
            {
                return;
            }

            if (!TryResolveRecipeFocusFromReference(item, out RecipeFocus focus) &&
                !TryResolveRecipeFocus(item, out focus))
            {
                return;
            }

            RegisterHoveredFocus(focus);
            UiAreaNarrationContext.RecordArea(area);
        }

        #endregion

        #region Hovered Focus Management

        private static bool TryGetActiveHoveredFocus(out RecipeFocus focus)
        {
            focus = default;
            if (!_hoveredFocusOverride.HasValue)
            {
                return false;
            }

            uint current = Main.GameUpdateCount;
            uint frame = _hoveredFocusFrame;
            uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
            if (age > 10)
            {
                _hoveredFocusOverride = null;
                _hoveredFocusFrame = 0;
                return false;
            }

            focus = _hoveredFocusOverride.Value;
            if (Main.focusRecipe == focus.FocusIndex)
            {
                _hoveredFocusOverride = null;
                _hoveredFocusFrame = 0;
            }

            return true;
        }

        private static void RegisterHoveredFocus(in RecipeFocus focus)
        {
            if (_hoveredFocusOverride.HasValue &&
                _hoveredFocusOverride.Value.RecipeIndex == focus.RecipeIndex &&
                _hoveredFocusOverride.Value.FocusIndex == focus.FocusIndex)
            {
                _hoveredFocusFrame = Main.GameUpdateCount;
                return;
            }

            _hoveredFocusOverride = focus;
            _hoveredFocusFrame = Main.GameUpdateCount;
        }

        #endregion

        #region Link Point Utilities

        private static bool IsLinkPointInCraftingRange(int point)
        {
            // Crafting grid (when recBigList is active): 700-1499
            if (point >= CraftingGridLinkPointStart && point < CraftingGridLinkPointEnd)
            {
                return Main.recBigList;
            }

            // Crafting list (normal view): 1500-1999, but exclude special inventory points
            // (e.g., PvP toggle, team buttons, defense counter) that overlap this range
            if (point >= CraftingListLinkPointStart && point < CraftingListLinkPointEnd)
            {
                return !InventoryNarrator.IsSpecialInventoryPoint(point);
            }

            return false;
        }

        #endregion

        #region Reset

        private void Reset()
        {
            ResetFocus();
            ResetGuideState();
            ResetReforgeState();
            _missingRequirementRecipes.Clear();
        }

        private void ResetFocus()
        {
            _lastAnnouncement = null;
            _lastWasGridMode = null;
            _hoveredFocusOverride = null;
            _hoveredFocusFrame = 0;
        }

        #endregion
    }
}
