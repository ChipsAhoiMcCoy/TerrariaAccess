#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.UI.Gamepad;
using Services = ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Handles narration for the crafting UI, including recipe announcements.
/// Works with the InventoryNarrator to coordinate crafting-related narration.
/// </summary>
internal sealed class CraftingNarrator
{
    #region Constants

    private const int CraftingGridLinkPointStart = 700;
    private const int CraftingGridLinkPointEnd = 1500;
    private const int CraftingListLinkPointStart = 1500;
    private const int CraftingListLinkPointEnd = 2000;

    #endregion

    #region State

    private readonly RegionDetector _regionDetector;
    private readonly RecipeResolver _recipeResolver = new();
    private readonly HashSet<int> _missingRequirementRecipes = new();

    private RecipeAnnouncement? _lastAnnouncement;
    private bool? _lastWasGridMode;
    private RecipeFocus? _hoveredFocusOverride;
    private uint _hoveredFocusFrame;

    #endregion

    #region Constructor

    public CraftingNarrator(RegionDetector regionDetector)
    {
        _regionDetector = regionDetector ?? throw new ArgumentNullException(nameof(regionDetector));
    }

    #endregion

    #region Public API

    /// <summary>
    /// Main update method called each frame when the player inventory is open.
    /// </summary>
    public void Update(Player player, Func<int, int, bool>? contextChecker)
    {
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

        int currentPoint = PlayerInput.UsingGamepadUI ? UILinkPointNavigator.CurrentPoint : -1;
        bool isOnCraftingGrid = currentPoint >= CraftingGridLinkPointStart && currentPoint < CraftingGridLinkPointEnd;
        bool isOnCraftingList = currentPoint >= CraftingListLinkPointStart && currentPoint < CraftingListLinkPointEnd;

        if (PlayerInput.UsingGamepadUI)
        {
            if (contextChecker != null && contextChecker(currentPoint, 0))
            {
                // Context checker handled validation
            }
            else if (!IsLinkPointInCraftingRange(currentPoint))
            {
                ResetFocus();
                return;
            }
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
        }

        if (!UiAreaNarrationContext.IsActiveArea(UiNarrationArea.Crafting | UiNarrationArea.Guide))
        {
            ResetFocus();
            return;
        }

        bool actualGridMode = isOnCraftingGrid && Main.recBigList;
        bool linkPointGridMode = isOnCraftingGrid;
        bool isFirstEntry = !_lastWasGridMode.HasValue;
        bool modeChanged = _lastWasGridMode.HasValue && _lastWasGridMode.Value != linkPointGridMode;
        _lastWasGridMode = linkPointGridMode;

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

        string? regionPrefix = _regionDetector.TryGetAndUpdateCraftingRegionPrefix(linkPointGridMode);

        if ((modeChanged || isFirstEntry) && string.IsNullOrWhiteSpace(regionPrefix))
        {
            regionPrefix = RegionDetector.GetCraftingRegionDisplayName(linkPointGridMode);
        }

        bool hasRegionPrefix = !string.IsNullOrWhiteSpace(regionPrefix);
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

    /// <summary>
    /// Resets all narrator state.
    /// </summary>
    public void ForceReset()
    {
        Reset();
    }

    /// <summary>
    /// Called when inventory opens to reset crafting state.
    /// </summary>
    public void OnInventoryOpened()
    {
        ResetFocus();
    }

    /// <summary>
    /// Attempts to capture a hovered recipe from an item being displayed.
    /// </summary>
    public bool TryCaptureHoveredRecipe(Item item)
    {
        if (!Main.playerInventory)
        {
            return false;
        }

        if (!_recipeResolver.TryResolveFromReference(item, out RecipeFocus focus) &&
            !_recipeResolver.TryResolve(item, out focus))
        {
            return false;
        }

        RegisterHoveredFocus(focus);
        UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
        return true;
    }

    /// <summary>
    /// Attempts to focus a recipe at the given available index.
    /// </summary>
    public bool TryFocusRecipeAtAvailableIndex(int availableIndex)
    {
        if (!_recipeResolver.TryCreateFromAvailableIndex(availableIndex, out RecipeFocus focus))
        {
            return false;
        }

        RegisterHoveredFocus(focus);
        UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
        return true;
    }

    /// <summary>
    /// Gets requirement tooltip details for an item if it's a craftable recipe result.
    /// </summary>
    public string? TryGetRequirementTooltipDetails(Item item, bool locationIsEmpty)
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

        if (!_recipeResolver.TryResolve(item, out RecipeFocus focus))
        {
            return null;
        }

        string? message = RecipeRequirementBuilder.BuildMessage(focus.Recipe, out _);
        return string.IsNullOrWhiteSpace(message) ? null : message;
    }

    /// <summary>
    /// Captures a recipe hover when an item slot with crafting context is focused.
    /// </summary>
    public void TryCaptureRecipeHover(Item item, int context)
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

        if (!_recipeResolver.TryResolveFromReference(item, out RecipeFocus focus) &&
            !_recipeResolver.TryResolve(item, out focus))
        {
            return;
        }

        RegisterHoveredFocus(focus);
        UiAreaNarrationContext.RecordArea(area);
    }

    #endregion

    #region Private Methods

    private void Reset()
    {
        ResetFocus();
        _missingRequirementRecipes.Clear();
    }

    private void ResetFocus()
    {
        _lastAnnouncement = null;
        _lastWasGridMode = null;
        _hoveredFocusOverride = null;
        _hoveredFocusFrame = 0;
    }

    private bool TryCaptureFocus(out RecipeFocus focus)
    {
        if (TryGetActiveHoveredFocus(out focus))
        {
            return true;
        }

        return _recipeResolver.TryGetFocused(out focus);
    }

    private bool TryGetActiveHoveredFocus(out RecipeFocus focus)
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

    private void RegisterHoveredFocus(in RecipeFocus focus)
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

    private static bool IsLinkPointInCraftingRange(int point)
    {
        if (point >= CraftingGridLinkPointStart && point < CraftingGridLinkPointEnd)
        {
            return Main.recBigList;
        }

        if (point >= CraftingListLinkPointStart && point < CraftingListLinkPointEnd)
        {
            return !SpecialSelectionHandler.IsSpecialInventoryPoint(point);
        }

        return false;
    }

    private bool TryBuildAnnouncement(in RecipeFocus focus, out RecipeAnnouncement announcement)
    {
        announcement = default;

        Item result = focus.Result;
        if (result is null || result.IsAir)
        {
            return false;
        }

        string label = ItemDescriber.ComposeLabel(result);

        string? details = TooltipBuilder.BuildDetails(
            result,
            result.Name ?? string.Empty,
            allowMouseText: false,
            suppressControllerPrompts: true);

        string? requirementMessage = RecipeRequirementBuilder.BuildMessage(focus.Recipe, out bool hadRequirementData);
        if (!string.IsNullOrWhiteSpace(requirementMessage))
        {
            details = string.IsNullOrWhiteSpace(details)
                ? requirementMessage
                : $"{details}. {requirementMessage}";
            _missingRequirementRecipes.Remove(focus.RecipeIndex);
        }
        else if (hadRequirementData)
        {
            LogMissingRequirementNarration(focus.RecipeIndex, label);
        }

        string combined = CombineAnnouncement(label, details);
        if (string.IsNullOrWhiteSpace(combined))
        {
            combined = label;
        }

        string message = $"{combined}. Recipe {focus.FocusIndex + 1} of {focus.AvailableCount}";
        message = GlyphTagFormatter.Normalize(message);

        announcement = new RecipeAnnouncement(
            RecipeIdentity.From(result),
            focus.RecipeIndex,
            focus.FocusIndex,
            focus.AvailableCount,
            message);

        return true;
    }

    private static string CombineAnnouncement(string label, string? details)
    {
        string normalizedLabel = GlyphTagFormatter.Normalize(label.Trim());
        if (string.IsNullOrWhiteSpace(details))
        {
            return normalizedLabel;
        }

        string normalizedDetails = GlyphTagFormatter.Normalize(details.Trim());
        if (!HasTerminalPunctuation(normalizedLabel))
        {
            normalizedLabel += '.';
        }

        return $"{normalizedLabel} {normalizedDetails}";
    }

    private static bool HasTerminalPunctuation(string text)
    {
        text = text.TrimEnd();
        if (text.Length == 0)
        {
            return false;
        }

        char last = text[^1];
        return last == '.' || last == '!' || last == '?' || last == ':' || last == ')';
    }

    private void LogMissingRequirementNarration(int recipeIndex, string label)
    {
        if (_missingRequirementRecipes.Add(recipeIndex))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Requirement narration missing for recipe {recipeIndex} ({label})");
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// A complete recipe announcement for deduplication.
    /// </summary>
    private readonly record struct RecipeAnnouncement(
        RecipeIdentity Identity,
        int RecipeIndex,
        int FocusIndex,
        int AvailableCount,
        string Message);

    #endregion
}
