#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.ModMenuAccessibility;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.UI.ModBrowser;
using Terraria.UI;
using Terraria.UI.Gamepad;
using TerrariaAccess.Common.Systems.ModBrowser;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Download Mods (UIModBrowser) menu.
/// Uses reflection since UIModBrowser and UIModDownloadItem are internal types.
/// </summary>
public sealed class DownloadModsAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Base Class Implementation

    protected override int BaseLinkId => LinkIdRegistry.DownloadMods;
    protected override string MenuTypeName => "Terraria.ModLoader.UI.ModBrowser.UIModBrowser";
    protected override string SystemLogName => "DownloadMods";

    protected override int GetInitialFocusFrameCount() => 60; // Wait longer for async mod loading

    #endregion

    #region Menu-Specific State

    // Navigation state tracking
    private static FocusRegion _currentRegion = FocusRegion.ModList;

    // Cached binding lists for navigation
    private static readonly List<PointBinding> FilterBindings = new();
    private static readonly List<PointBinding> ModBindingsList = new();
    private static readonly List<PointBinding> TopActionBindingsList = new();
    private static readonly List<PointBinding> BottomActionBindingsList = new();

    private enum FocusRegion
    {
        FilterButtons,
        ModList,
        TopActionButtons,
        BottomActionButtons
    }

    // Track mod item bindings for announcement lookup
    private static readonly List<ModItemBinding> ModItemBindings = new();

    // Track mod item buttons for each mod
    private static readonly List<ModItemButtonGroup> ModItemButtonGroups = new();

    // Current button index within a mod item (0 = main item, 1+ = buttons to the right)
    private static int _currentModButtonIndex;

    // Track async mod loading
    private static bool _announcedFirstMod;
    private static int _previousModCount;

    // Right analog scroll tracking
    private static int _lastScrollAnnouncedModIndex = -1;
    private static float _lastScrollPosition = -1f;

    #endregion

    #region Public API

    /// <summary>
    /// Returns true if the Download Mods menu is currently active and handling gamepad input.
    /// Used by MenuNarration to suppress hover announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            // Verify we're still in the UIModBrowser UI state
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null || ReflectionCache.UIModBrowser.Type is null || currentState.GetType() != ReflectionCache.UIModBrowser.Type)
            {
                return false;
            }

            return true;
        }
    }

    #endregion

    #region Lifecycle Overrides

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Log type availability (types are now cached in ReflectionCache)
        Mod.Logger.Info($"[DownloadMods] Load: UIModBrowser type found: {ReflectionCache.UIModBrowser.Type is not null}");
        Mod.Logger.Info($"[DownloadMods] Load: UIModDownloadItem type found: {ReflectionCache.UIModDownloadItem.Type is not null}");

        if (ReflectionCache.UIModBrowser.Type is null)
        {
            Mod.Logger.Warn("[DownloadMods] Could not find UIModBrowser type");
            return;
        }

        base.Load();
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        if (ReflectionCache.UIModBrowser.Type is not null)
        {
            base.Unload();
        }

        ModItemBindings.Clear();
        ModItemButtonGroups.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
        _announcedFirstMod = false;
        _previousModCount = 0;
        _lastScrollAnnouncedModIndex = -1;
        _lastScrollPosition = -1f;
    }

    protected override void OnMenuEntered(object menuState)
    {
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
        _announcedFirstMod = false;
        _previousModCount = 0;
        _lastScrollAnnouncedModIndex = -1;
        _lastScrollPosition = -1f;
    }

    protected override void OnMenuExited()
    {
        FilterBindings.Clear();
        ModBindingsList.Clear();
        ModItemButtonGroups.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();
        _currentModButtonIndex = 0;
    }

    protected override bool ShouldProcessInput(object menuState)
    {
        // Update search mode manager (handles Tab key toggle)
        SearchModeManager.Update();

        // If user pressed Enter to exit search mode, focus the first mod
        if (SearchModeManager.ConsumeFocusFirstModRequest())
        {
            CurrentFocusIndex = 0;
            _currentModButtonIndex = 0;
            _currentRegion = FocusRegion.ModList;
            LastAnnouncedPointId = -1; // Reset to trigger announcement
        }

        return true;
    }

    protected override bool ShouldProcessKeyboardInput()
    {
        return !SearchModeManager.IsSearchModeActive;
    }

    #endregion

    #region Configuration

    protected override void ConfigureGamepadPoints(object menuState)
    {
        BindingById.Clear();
        ModItemBindings.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>();

        // Get mod list
        object? modListObj = ReflectionCache.UIModBrowser.ModList?.GetValue(menuState);
        UIElement? modList = modListObj as UIElement;

        // Get filter/category buttons
        IList? categoryButtons = ReflectionCache.UIModBrowser.CategoryButtons?.GetValue(menuState) as IList;

        // Get tag filter toggle (not in CategoryButtons)
        UIElement? tagFilterToggle = ReflectionCache.UIModBrowser.TagFilterToggle?.GetValue(menuState) as UIElement;

        // Get action buttons
        UIElement? reloadButton = ReflectionCache.UIModBrowser.ReloadButton?.GetValue(menuState) as UIElement;
        UIElement? backButton = ReflectionCache.UIModBrowser.BackButton?.GetValue(menuState) as UIElement;
        UIElement? clearButton = ReflectionCache.UIModBrowser.ClearButton?.GetValue(menuState) as UIElement;
        UIElement? downloadAllButton = ReflectionCache.UIModBrowser.DownloadAllButton?.GetValue(menuState) as UIElement;
        UIElement? updateAllButton = ReflectionCache.UIModBrowser.UpdateAllButton?.GetValue(menuState) as UIElement;

        // Get the parent UIElement to check HasChild for conditional buttons
        UIElement? browserElement = menuState as UIElement;

        // Create bindings for category/filter buttons (top row)
        var filterBindings = new List<PointBinding>();
        if (categoryButtons is not null)
        {
            string[] filterLabels = GetFilterLabels(menuState);

            for (int i = 0; i < categoryButtons.Count && i < filterLabels.Length; i++)
            {
                if (categoryButtons[i] is UIElement button)
                {
                    CalculatedStyle dims = button.GetDimensions();
                    Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);

                    var binding = new PointBinding(nextId++, center, filterLabels[i], string.Empty, button, PointType.FilterButton);
                    filterBindings.Add(binding);
                    bindings.Add(binding);
                    BindingById[binding.Id] = binding;
                }
            }
        }

        // Add tag filter toggle after category buttons
        if (tagFilterToggle is not null)
        {
            CalculatedStyle dims = tagFilterToggle.GetDimensions();
            Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
            string tagLabel = GetTagFilterLabel(menuState);

            var binding = new PointBinding(nextId++, center, tagLabel, string.Empty, tagFilterToggle, PointType.FilterButton);
            filterBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Create bindings for mod items
        var modBindings = new List<PointBinding>();
        ModItemButtonGroups.Clear();

        IList? receivedItems = GetReceivedItems(modListObj);
        if (receivedItems is not null && receivedItems.Count > 0)
        {
            // Get list viewport for visibility check
            CalculatedStyle? listView = modList?.GetInnerDimensions();
            float listTop = listView?.Y ?? 0;
            float listBottom = listTop + (listView?.Height ?? 1000);

            int modIndex = 0;
            foreach (object? item in receivedItems)
            {
                if (item is not UIElement modItemElement)
                {
                    continue;
                }

                // Check if item is visible in viewport
                CalculatedStyle itemDims = modItemElement.GetDimensions();
                float itemCenterY = itemDims.Y + itemDims.Height / 2f;
                if (itemCenterY < listTop - 50f || itemCenterY > listBottom + 50f)
                {
                    modIndex++;
                    continue;
                }

                Vector2 center = new(itemDims.X + itemDims.Width / 2f, itemDims.Y + itemDims.Height / 2f);

                string modDisplayName = GetModDisplayName(item);
                string modStatus = GetModStatus(item);
                string fullLabel = string.IsNullOrEmpty(modStatus) ? modDisplayName : $"{modDisplayName}, {modStatus}";

                var binding = new PointBinding(nextId++, center, fullLabel, string.Empty, modItemElement, PointType.ModItem);
                modBindings.Add(binding);
                bindings.Add(binding);
                BindingById[binding.Id] = binding;

                ModItemBindings.Add(new ModItemBinding(binding.Id, item, modIndex));

                // Track button IDs for this mod item
                int mainId = binding.Id;
                int? moreInfoId = null;
                int? updateId = null;
                int? updateWithDepsId = null;
                int? tMLUpdateId = null;

                // Get More Info button (always present)
                UIElement? moreInfoButton = ReflectionCache.UIModDownloadItem.MoreInfoButton?.GetValue(item) as UIElement;
                if (moreInfoButton?.Parent is not null)
                {
                    CalculatedStyle moreInfoDims = moreInfoButton.GetDimensions();
                    Vector2 moreInfoCenter = new(moreInfoDims.X + moreInfoDims.Width / 2f, moreInfoDims.Y + moreInfoDims.Height / 2f);
                    var moreInfoBinding = new PointBinding(nextId++, moreInfoCenter, "More Info", string.Empty, moreInfoButton, PointType.ModItemButton);
                    bindings.Add(moreInfoBinding);
                    BindingById[moreInfoBinding.Id] = moreInfoBinding;
                    moreInfoId = moreInfoBinding.Id;
                }

                // Get tML Update Required button (only when mod needs newer tModLoader)
                UIElement? tMLUpdateButton = ReflectionCache.UIModDownloadItem.TMLUpdateRequired?.GetValue(item) as UIElement;
                if (tMLUpdateButton?.Parent is not null)
                {
                    CalculatedStyle tMLDims = tMLUpdateButton.GetDimensions();
                    Vector2 tMLCenter = new(tMLDims.X + tMLDims.Width / 2f, tMLDims.Y + tMLDims.Height / 2f);
                    var tMLBinding = new PointBinding(nextId++, tMLCenter, "Requires tModLoader update", string.Empty, tMLUpdateButton, PointType.ModItemButton);
                    bindings.Add(tMLBinding);
                    BindingById[tMLBinding.Id] = tMLBinding;
                    tMLUpdateId = tMLBinding.Id;
                }

                // Get Update button (restart required warning)
                UIElement? updateButton = ReflectionCache.UIModDownloadItem.UpdateButton?.GetValue(item) as UIElement;
                if (updateButton?.Parent is not null)
                {
                    CalculatedStyle updateDims = updateButton.GetDimensions();
                    Vector2 updateCenter = new(updateDims.X + updateDims.Width / 2f, updateDims.Y + updateDims.Height / 2f);
                    var updateBinding = new PointBinding(nextId++, updateCenter, "Restart required", string.Empty, updateButton, PointType.ModItemButton);
                    bindings.Add(updateBinding);
                    BindingById[updateBinding.Id] = updateBinding;
                    updateId = updateBinding.Id;
                }

                // Get Update With Deps button (download/update)
                UIElement? updateWithDepsButton = ReflectionCache.UIModDownloadItem.UpdateWithDepsButton?.GetValue(item) as UIElement;
                if (updateWithDepsButton?.Parent is not null)
                {
                    CalculatedStyle downloadDims = updateWithDepsButton.GetDimensions();
                    Vector2 downloadCenter = new(downloadDims.X + downloadDims.Width / 2f, downloadDims.Y + downloadDims.Height / 2f);
                    string downloadLabel = GetDownloadButtonLabel(item);
                    var downloadBinding = new PointBinding(nextId++, downloadCenter, downloadLabel, string.Empty, updateWithDepsButton, PointType.ModItemButton);
                    bindings.Add(downloadBinding);
                    BindingById[downloadBinding.Id] = downloadBinding;
                    updateWithDepsId = downloadBinding.Id;
                }

                // Store button group for navigation
                ModItemButtonGroups.Add(new ModItemButtonGroup(modIndex, mainId, moreInfoId, tMLUpdateId, updateId, updateWithDepsId));
                modIndex++;
            }
        }

        // Create bindings for top row action buttons (Reload/Cancel)
        var topActionBindings = new List<PointBinding>();
        if (reloadButton is not null)
        {
            var binding = CreateButtonBinding(ref nextId, reloadButton, GetReloadButtonLabel(menuState), PointType.ActionButton);
            topActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Create bindings for bottom row action buttons
        var bottomActionBindings = new List<PointBinding>();
        if (backButton is not null)
        {
            var binding = CreateButtonBinding(ref nextId, backButton, Language.GetTextValue("UI.Back"), PointType.BackButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Clear button (only in special mod pack filter mode)
        if (clearButton is not null && browserElement?.HasChild(clearButton) == true)
        {
            var binding = CreateButtonBinding(ref nextId, clearButton, "Clear Filter", PointType.ActionButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Download All button (only in special mod pack filter mode)
        if (downloadAllButton is not null && browserElement?.HasChild(downloadAllButton) == true)
        {
            var binding = CreateButtonBinding(ref nextId, downloadAllButton, Language.GetTextValue("tModLoader.MBDownloadAll"), PointType.ActionButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Update All button (only when updates available)
        if (updateAllButton is not null && browserElement?.HasChild(updateAllButton) == true)
        {
            var binding = CreateButtonBinding(ref nextId, updateAllButton, Language.GetTextValue("tModLoader.MBUpdateAll"), PointType.ActionButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        if (bindings.Count == 0)
        {
            return;
        }

        // Copy to static lists for navigation
        FilterBindings.AddRange(filterBindings);
        ModBindingsList.AddRange(modBindings);
        TopActionBindingsList.AddRange(topActionBindings);
        BottomActionBindingsList.AddRange(bottomActionBindings);

        // Check if mods just became available (async loading completed)
        int currentModCount = modBindings.Count;
        if (currentModCount > 0 && _previousModCount == 0 && !_announcedFirstMod)
        {
            _announcedFirstMod = true;
            _currentRegion = FocusRegion.ModList;
            CurrentFocusIndex = 0;
            _currentModButtonIndex = 0;

            // Announce the mod count and first mod
            string loadedMessage = $"Loaded {currentModCount} mods. ";
            string firstModLabel = modBindings[0].Label;
            string announcement = $"{loadedMessage}{firstModLabel}, 1 of {currentModCount}";

            Mod.Logger.Info($"[DownloadMods] Mods loaded, announcing: {announcement}");
            ScreenReaderService.Announce(announcement, force: true);

            // Update tracking to prevent re-announcing
            LastAnnouncedPointId = modBindings[0].Id;
            LastSeenPointId = modBindings[0].Id;
        }
        _previousModCount = currentModCount;

        // Create all link points but leave them UNLINKED
        foreach (PointBinding binding in bindings)
        {
            SetupLinkPoint(binding);
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Force initial focus - only after mods are loaded OR if we need to fallback to filter/action buttons
        if (PlayerInput.UsingGamepadUI && InitialFocusFramesRemaining > 0)
        {
            // If mods are available, set focus to first mod
            if (modBindings.Count > 0)
            {
                int defaultPointId = modBindings[0].Id;
                UILinkPointNavigator.ChangePoint(defaultPointId);
                _currentRegion = FocusRegion.ModList;
                CurrentFocusIndex = 0;
                InitialFocusFramesRemaining = 0; // Stop trying once we have mods
            }
            // Otherwise, fall back after enough frames have passed (in case mods never load)
            else if (InitialFocusFramesRemaining <= 5)
            {
                int defaultPointId = filterBindings.Count > 0 ? filterBindings[0].Id :
                                     topActionBindings.Count > 0 ? topActionBindings[0].Id :
                                     bottomActionBindings.Count > 0 ? bottomActionBindings[0].Id :
                                     bindings[0].Id;

                UILinkPointNavigator.ChangePoint(defaultPointId);
                InitialFocusFramesRemaining = 0;
            }
            else
            {
                InitialFocusFramesRemaining--;
            }
        }

        // Handle scroll announcements
        HandleScrollAnnouncements(menuState, modListObj);
    }

    private static IList? GetReceivedItems(object? modListObj)
    {
        if (modListObj is null)
        {
            return null;
        }

        try
        {
            // ReceivedItems is a property on UIAsyncList that returns IEnumerable<TUIElement>
            PropertyInfo? receivedItemsProperty = modListObj.GetType().GetProperty("ReceivedItems", BindingFlags.Public | BindingFlags.Instance);
            if (receivedItemsProperty is not null)
            {
                object? enumerable = receivedItemsProperty.GetValue(modListObj);
                if (enumerable is IEnumerable<UIElement> uiElements)
                {
                    return uiElements.ToList();
                }
                // Fallback: try to enumerate as non-generic IEnumerable
                if (enumerable is IEnumerable nonGenericEnumerable)
                {
                    var result = new List<object?>();
                    foreach (var item in nonGenericEnumerable)
                    {
                        result.Add(item);
                    }
                    return result;
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return null;
    }

    #endregion

    #region Navigation

    protected override void HandleNavigation(object menuState)
    {
        if (!CurrentInput.HasNavigation)
        {
            return;
        }

        // Log the input
        string direction = CurrentInput.Left ? "LEFT" : CurrentInput.Right ? "RIGHT" : CurrentInput.Up ? "UP" : "DOWN";
        Mod.Logger.Debug($"[DownloadMods] Input: {direction}, current region: {_currentRegion}, index: {CurrentFocusIndex}");

        // Process navigation
        bool navigated = false;

        if (CurrentInput.Left)
        {
            navigated = NavigateLeft();
        }
        else if (CurrentInput.Right)
        {
            navigated = NavigateRight();
        }
        else if (CurrentInput.Up)
        {
            navigated = NavigateUp();
        }
        else if (CurrentInput.Down)
        {
            navigated = NavigateDown();
        }

        if (navigated)
        {
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
                Mod.Logger.Info($"[DownloadMods] Navigated to region {_currentRegion}, index {CurrentFocusIndex}, point {newPointId.Value}");
            }
        }
    }

    private bool NavigateLeft()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                if (CurrentFocusIndex > 0)
                {
                    CurrentFocusIndex--;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                if (_currentModButtonIndex > 0)
                {
                    _currentModButtonIndex--;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (CurrentFocusIndex > 0)
                {
                    CurrentFocusIndex--;
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                if (CurrentFocusIndex > 0)
                {
                    CurrentFocusIndex--;
                    return true;
                }
                break;
        }

        return false;
    }

    private bool NavigateRight()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                if (CurrentFocusIndex < FilterBindings.Count - 1)
                {
                    CurrentFocusIndex++;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[CurrentFocusIndex];
                    int maxButtonIndex = buttonGroup.ButtonCount - 1;

                    if (_currentModButtonIndex < maxButtonIndex)
                    {
                        _currentModButtonIndex++;
                        return true;
                    }
                }
                break;

            case FocusRegion.TopActionButtons:
                if (CurrentFocusIndex < TopActionBindingsList.Count - 1)
                {
                    CurrentFocusIndex++;
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                if (CurrentFocusIndex < BottomActionBindingsList.Count - 1)
                {
                    CurrentFocusIndex++;
                    return true;
                }
                break;
        }

        return false;
    }

    private bool NavigateUp()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                break;

            case FocusRegion.ModList:
                if (CurrentFocusIndex > 0)
                {
                    CurrentFocusIndex--;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    CurrentFocusIndex = FilterBindings.Count / 2;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    CurrentFocusIndex = ModBindingsList.Count - 1;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    CurrentFocusIndex = Math.Min(CurrentFocusIndex, FilterBindings.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    CurrentFocusIndex = ModBindingsList.Count - 1;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    CurrentFocusIndex = FilterBindings.Count / 2;
                    return true;
                }
                break;
        }

        return false;
    }

    private bool NavigateDown()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    CurrentFocusIndex = 0;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    CurrentFocusIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                if (CurrentFocusIndex < ModBindingsList.Count - 1)
                {
                    CurrentFocusIndex++;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (BottomActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.BottomActionButtons;
                    CurrentFocusIndex = 0;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (BottomActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.BottomActionButtons;
                    CurrentFocusIndex = Math.Min(CurrentFocusIndex, BottomActionBindingsList.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                break;
        }

        return false;
    }

    protected override int? GetCurrentPointId()
    {
        if (_currentRegion == FocusRegion.ModList)
        {
            if (CurrentFocusIndex < 0 || CurrentFocusIndex >= ModItemButtonGroups.Count)
            {
                // Fall back to ModBindingsList if button groups aren't set up yet
                if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModBindingsList.Count)
                {
                    return ModBindingsList[CurrentFocusIndex].Id;
                }
                return null;
            }

            var buttonGroup = ModItemButtonGroups[CurrentFocusIndex];
            return buttonGroup.GetButtonIdAtIndex(_currentModButtonIndex);
        }

        var list = GetCurrentRegionList();
        if (list is null || list.Count == 0 || CurrentFocusIndex < 0 || CurrentFocusIndex >= list.Count)
        {
            return null;
        }

        return list[CurrentFocusIndex].Id;
    }

    private List<PointBinding>? GetCurrentRegionList()
    {
        return _currentRegion switch
        {
            FocusRegion.FilterButtons => FilterBindings,
            FocusRegion.ModList => ModBindingsList,
            FocusRegion.TopActionButtons => TopActionBindingsList,
            FocusRegion.BottomActionButtons => BottomActionBindingsList,
            _ => null
        };
    }

    #endregion

    #region Action Handling

    protected override void HandleAction(object menuState)
    {
        if (!CurrentInput.ActionPressed)
        {
            return;
        }

        int? currentPointId = GetCurrentPointId();
        if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
        {
            if (binding.Element is UIElement element)
            {
                Mod.Logger.Info($"[DownloadMods] Clicking: {binding.Label}");
                SoundEngine.PlaySound(SoundID.MenuTick);

                try
                {
                    var clickEvent = new UIMouseEvent(element, Main.MouseScreen);
                    element.LeftClick(clickEvent);
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warn($"[DownloadMods] Click failed: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Announcement

    protected override string BuildAnnouncement(PointBinding binding, object menuState)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // In search mode, only announce mod items
        if (SearchModeManager.IsSearchModeActive &&
            binding.Type != PointType.ModItem &&
            binding.Type != PointType.ModItemButton)
        {
            return string.Empty;
        }

        switch (binding.Type)
        {
            case PointType.FilterButton:
                int filterIndex = CurrentFocusIndex + 1;
                int filterTotal = FilterBindings.Count;
                return $"Filter: {label}, {filterIndex} of {filterTotal}";

            case PointType.ModItem:
                int modIndex = CurrentFocusIndex + 1;
                int modTotal = ModBindingsList.Count;
                return $"{label}, {modIndex} of {modTotal}";

            case PointType.ModItemButton:
                if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[CurrentFocusIndex];
                    int buttonCount = buttonGroup.ButtonCount;
                    int buttonNumber = _currentModButtonIndex;
                    int totalButtons = buttonCount - 1;
                    return $"{label}, button {buttonNumber} of {totalButtons}";
                }
                return label;

            case PointType.BackButton:
            case PointType.ActionButton:
            default:
                return label;
        }
    }

    #endregion

    #region Scrolling

    private void HandleScrollAnnouncements(object browser, object? modListObj)
    {
        // Only handle scroll announcements when in mod list region
        if (_currentRegion != FocusRegion.ModList)
        {
            _lastScrollAnnouncedModIndex = -1;
            _lastScrollPosition = -1f;
            return;
        }

        UIElement? modList = modListObj as UIElement;
        if (modList is not UIList uiList)
        {
            return;
        }

        FieldInfo? scrollbarField = typeof(UIList).GetField("_scrollbar", BindingFlags.NonPublic | BindingFlags.Instance);
        UIScrollbar? scrollbar = scrollbarField?.GetValue(uiList) as UIScrollbar;
        if (scrollbar is null)
        {
            return;
        }

        // Check right analog stick vertical movement
        float rightStickY = PlayerInput.GamepadThumbstickRight.Y;
        const float scrollThreshold = 0.1f;

        bool isActivelyScrolling = Math.Abs(rightStickY) >= scrollThreshold;

        if (!isActivelyScrolling)
        {
            _lastScrollPosition = scrollbar.ViewPosition;
            return;
        }

        // Apply scroll
        float scrollAmount = -rightStickY * 16f;
        scrollbar.ViewPosition += scrollAmount;

        float currentScroll = scrollbar.ViewPosition;

        if (Math.Abs(currentScroll - _lastScrollPosition) < 5f && _lastScrollPosition >= 0)
        {
            return;
        }

        _lastScrollPosition = currentScroll;

        // Find the mod item that's currently in the center of the viewport
        IList? receivedItems = GetReceivedItems(modListObj);
        if (receivedItems is null || receivedItems.Count == 0)
        {
            return;
        }

        CalculatedStyle listDims = modList.GetInnerDimensions();
        float viewportCenter = listDims.Y + listDims.Height / 2f;

        int closestModIndex = -1;
        float closestDistance = float.MaxValue;

        int index = 0;
        foreach (object? item in receivedItems)
        {
            if (item is UIElement modItemElement)
            {
                CalculatedStyle itemDims = modItemElement.GetDimensions();
                float itemCenter = itemDims.Y + itemDims.Height / 2f;
                float distance = Math.Abs(itemCenter - viewportCenter);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestModIndex = index;
                }
            }
            index++;
        }

        if (closestModIndex >= 0 && closestModIndex != _lastScrollAnnouncedModIndex)
        {
            _lastScrollAnnouncedModIndex = closestModIndex;

            int currentIndex = 0;
            foreach (object? item in receivedItems)
            {
                if (currentIndex == closestModIndex && item is not null)
                {
                    string modName = GetModDisplayName(item);
                    string modStatus = GetModStatus(item);
                    string announcement = string.IsNullOrEmpty(modStatus)
                        ? $"{modName}, {closestModIndex + 1} of {receivedItems.Count}"
                        : $"{modName}, {modStatus}, {closestModIndex + 1} of {receivedItems.Count}";

                    SoundEngine.PlaySound(SoundID.MenuTick);
                    ScreenReaderService.Announce(announcement, force: true);

                    CurrentFocusIndex = closestModIndex;
                    _currentModButtonIndex = 0;
                    break;
                }
                currentIndex++;
            }
        }
    }

    #endregion

    #region Helper Methods

    private static string GetModDisplayName(object modItem)
    {
        try
        {
            ModDownloadItem? modData = ReflectionCache.UIModDownloadItem.ModDownload?.GetValue(modItem) as ModDownloadItem;
            if (modData is not null)
            {
                string name = !string.IsNullOrWhiteSpace(modData.DisplayNameClean)
                    ? modData.DisplayNameClean
                    : modData.DisplayName ?? modData.ModName ?? "Mod";
                return TextSanitizer.Clean(name);
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return "Unknown Mod";
    }

    private static string GetModStatus(object modItem)
    {
        try
        {
            ModDownloadItem? modData = ReflectionCache.UIModDownloadItem.ModDownload?.GetValue(modItem) as ModDownloadItem;
            if (modData is null)
            {
                return string.Empty;
            }

            if (modData.AppNeedRestartToReinstall)
            {
                return "Restart required";
            }

            if (modData.NeedUpdate)
            {
                return "Update available";
            }

            if (modData.IsInstalled)
            {
                return "Installed";
            }

            return "Not installed";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetDownloadButtonLabel(object modItem)
    {
        try
        {
            ModDownloadItem? modData = ReflectionCache.UIModDownloadItem.ModDownload?.GetValue(modItem) as ModDownloadItem;
            if (modData is not null)
            {
                if (modData.NeedUpdate)
                {
                    return "Update with dependencies";
                }
                return "Download with dependencies";
            }
        }
        catch
        {
            // Ignore
        }

        return "Download";
    }

    private static string[] GetFilterLabels(object browser)
    {
        return new[]
        {
            GetSortModeLabel(browser),
            GetTimePeriodLabel(browser),
            GetUpdateFilterLabel(browser),
            GetModSideFilterLabel(browser),
            GetSearchFilterLabel(browser)
        };
    }

    private static string GetSortModeLabel(object browser)
    {
        try
        {
            PropertyInfo? sortModeProperty = browser.GetType().GetProperty("SortMode", BindingFlags.Public | BindingFlags.Instance);
            object? sortMode = sortModeProperty?.GetValue(browser);

            return sortMode?.ToString() switch
            {
                "DownloadsDescending" => "Sort by downloads",
                "RecentlyPublished" => "Sort by recently published",
                "RecentlyUpdated" => "Sort by recently updated",
                "Hot" => "Sort by hot mods",
                _ => "Sort"
            };
        }
        catch
        {
            return "Sort";
        }
    }

    private static string GetTimePeriodLabel(object browser)
    {
        try
        {
            PropertyInfo? timePeriodProperty = browser.GetType().GetProperty("TimePeriodMode", BindingFlags.Public | BindingFlags.Instance);
            object? timePeriod = timePeriodProperty?.GetValue(browser);

            return timePeriod?.ToString() switch
            {
                "Today" => "Time period: today",
                "OneWeek" => "Time period: past week",
                "ThreeMonths" => "Time period: past three months",
                "SixMonths" => "Time period: past six months",
                "OneYear" => "Time period: past year",
                "AllTime" => "Time period: all time",
                _ => "Time period"
            };
        }
        catch
        {
            return "Time period";
        }
    }

    private static string GetUpdateFilterLabel(object browser)
    {
        try
        {
            PropertyInfo? updateFilterProperty = browser.GetType().GetProperty("UpdateFilterMode", BindingFlags.Public | BindingFlags.Instance);
            object? updateFilter = updateFilterProperty?.GetValue(browser);

            return updateFilter?.ToString() switch
            {
                "All" => "Updates: all mods",
                "Available" => "Updates: available",
                "UpdateOnly" => "Updates: update only",
                "InstalledOnly" => "Updates: installed only",
                _ => "Update filter"
            };
        }
        catch
        {
            return "Update filter";
        }
    }

    private static string GetModSideFilterLabel(object browser)
    {
        try
        {
            PropertyInfo? modSideProperty = browser.GetType().GetProperty("ModSideFilterMode", BindingFlags.Public | BindingFlags.Instance);
            object? modSide = modSideProperty?.GetValue(browser);

            return modSide?.ToString() switch
            {
                "All" => "Side filter: all",
                "Both" => "Side filter: client and server",
                "Client" => "Side filter: client only",
                "Server" => "Side filter: server only",
                "NoSync" => "Side filter: no sync",
                _ => "Side filter"
            };
        }
        catch
        {
            return "Side filter";
        }
    }

    private static string GetSearchFilterLabel(object browser)
    {
        try
        {
            PropertyInfo? searchFilterProperty = browser.GetType().GetProperty("SearchFilterMode", BindingFlags.Public | BindingFlags.Instance);
            object? searchFilter = searchFilterProperty?.GetValue(browser);

            return searchFilter?.ToString() switch
            {
                "Name" => "Search by name",
                "Author" => "Search by author",
                _ => "Search filter"
            };
        }
        catch
        {
            return "Search filter";
        }
    }

    private static string GetTagFilterLabel(object browser)
    {
        try
        {
            FieldInfo? categoryTagsField = browser.GetType().GetField("CategoryTagsFilter", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo? languageTagField = browser.GetType().GetField("LanguageTagFilter", BindingFlags.Public | BindingFlags.Instance);

            HashSet<int>? categoryTags = categoryTagsField?.GetValue(browser) as HashSet<int>;
            int languageTag = (int)(languageTagField?.GetValue(browser) ?? -1);

            if ((categoryTags is not null && categoryTags.Count > 0) || languageTag >= 0)
            {
                return "Tag filters: active";
            }

            return "Tag filters: none";
        }
        catch
        {
            return "Tag filters";
        }
    }

    private static string GetReloadButtonLabel(object browser)
    {
        try
        {
            PropertyInfo? loadingProperty = browser.GetType().GetProperty("Loading", BindingFlags.Public | BindingFlags.Instance);
            bool isLoading = (bool)(loadingProperty?.GetValue(browser) ?? false);

            return isLoading ? "Cancel loading" : "Reload browser";
        }
        catch
        {
            return "Reload";
        }
    }

    #endregion

    #region Nested Types

    private readonly record struct ModItemBinding(int Id, object ModItem, int ListIndex);

    private readonly record struct ModItemButtonGroup(
        int ModIndex,
        int MainId,
        int? MoreInfoId,
        int? TMLUpdateId,
        int? UpdateId,
        int? UpdateWithDepsId
    )
    {
        public int? GetButtonIdAtIndex(int index)
        {
            return index switch
            {
                0 => MainId,
                1 => MoreInfoId ?? TMLUpdateId ?? UpdateId ?? UpdateWithDepsId,
                2 => MoreInfoId.HasValue ? (TMLUpdateId ?? UpdateId ?? UpdateWithDepsId) : (UpdateId ?? UpdateWithDepsId),
                3 => MoreInfoId.HasValue && TMLUpdateId.HasValue ? (UpdateId ?? UpdateWithDepsId) : UpdateWithDepsId,
                4 => UpdateWithDepsId,
                _ => null
            };
        }

        public int ButtonCount
        {
            get
            {
                int count = 1;
                if (MoreInfoId.HasValue) count++;
                if (TMLUpdateId.HasValue) count++;
                if (UpdateId.HasValue) count++;
                if (UpdateWithDepsId.HasValue) count++;
                return count;
            }
        }
    }

    #endregion
}
