#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
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

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Download Mods (UIModBrowser) menu.
/// Uses reflection since UIModBrowser and UIModDownloadItem are internal types.
/// </summary>
public sealed class DownloadModsAccessibilitySystem : ModSystem
{
    private const int BaseLinkId = 3300;

    private static int _lastAnnouncedPointId = -1;
    private static int _lastSeenPointId = -1;
    private static object? _lastBrowserMenu;
    private static int _initialFocusFramesRemaining;
    private static bool _announcedFirstMod;
    private static int _previousModCount;

    // Navigation state tracking
    private static int _currentFocusIndex;
    private static FocusRegion _currentRegion = FocusRegion.ModList;
    private static bool _leftWasPressed;
    private static bool _rightWasPressed;
    private static bool _upWasPressed;
    private static bool _downWasPressed;

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

    // Type references
    private static Type? _uiModBrowserType;
    private static Type? _uiModDownloadItemType;

    // UIModBrowser field references
    private static FieldInfo? _modListField;
    private static FieldInfo? _receivedItemsField;
    private static FieldInfo? _categoryButtonsField;
    private static FieldInfo? _reloadButtonField;
    private static FieldInfo? _backButtonField;
    private static FieldInfo? _clearButtonField;
    private static FieldInfo? _downloadAllButtonField;
    private static FieldInfo? _updateAllButtonField;
    private static FieldInfo? _filterTextBoxField;
    private static FieldInfo? _tagFilterToggleField;

    // UIModDownloadItem field references
    private static FieldInfo? _modDownloadField;
    private static FieldInfo? _moreInfoButtonField;
    private static FieldInfo? _updateButtonField;
    private static FieldInfo? _updateWithDepsButtonField;
    private static FieldInfo? _tMLUpdateRequiredField;

    // Track binding for announcement lookup
    private static readonly Dictionary<int, PointBinding> BindingById = new();
    private static readonly List<ModItemBinding> ModItemBindings = new();

    // Track mod item buttons for each mod
    private static readonly List<ModItemButtonGroup> ModItemButtonGroups = new();

    // Current button index within a mod item (0 = main item, 1+ = buttons to the right)
    private static int _currentModButtonIndex;

    /// <summary>
    /// Returns true if the Download Mods menu is currently active and handling gamepad input.
    /// Used by MenuNarration to suppress hover announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (_lastBrowserMenu is null || !PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            // Verify we're still in the UIModBrowser UI state
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null || _uiModBrowserType is null || currentState.GetType() != _uiModBrowserType)
            {
                _lastBrowserMenu = null;
                return false;
            }

            return true;
        }
    }

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Get type references via reflection
        _uiModBrowserType = Type.GetType("Terraria.ModLoader.UI.ModBrowser.UIModBrowser, tModLoader");
        _uiModDownloadItemType = Type.GetType("Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem, tModLoader");

        Mod.Logger.Info($"[DownloadMods] Load: UIModBrowser type found: {_uiModBrowserType is not null}");
        Mod.Logger.Info($"[DownloadMods] Load: UIModDownloadItem type found: {_uiModDownloadItemType is not null}");

        if (_uiModBrowserType is null)
        {
            Mod.Logger.Warn("[DownloadMods] Could not find UIModBrowser type");
            return;
        }

        // Hook into DrawMenu to process during menu rendering
        On_Main.DrawMenu += HandleDrawMenu;

        // Get UIModBrowser fields
        _modListField = _uiModBrowserType.GetField("ModList", BindingFlags.Public | BindingFlags.Instance);
        _categoryButtonsField = _uiModBrowserType.GetField("CategoryButtons", BindingFlags.NonPublic | BindingFlags.Instance);
        _reloadButtonField = _uiModBrowserType.GetField("_reloadButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _backButtonField = _uiModBrowserType.GetField("_backButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _clearButtonField = _uiModBrowserType.GetField("_clearButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _downloadAllButtonField = _uiModBrowserType.GetField("_downloadAllButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _updateAllButtonField = _uiModBrowserType.GetField("_updateAllButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _filterTextBoxField = _uiModBrowserType.GetField("FilterTextBox", BindingFlags.Public | BindingFlags.Instance);
        _tagFilterToggleField = _uiModBrowserType.GetField("TagFilterToggle", BindingFlags.Public | BindingFlags.Instance);

        // Get UIModDownloadItem fields
        if (_uiModDownloadItemType is not null)
        {
            _modDownloadField = _uiModDownloadItemType.GetField("ModDownload", BindingFlags.Public | BindingFlags.Instance);
            _moreInfoButtonField = _uiModDownloadItemType.GetField("_moreInfoButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _updateButtonField = _uiModDownloadItemType.GetField("_updateButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _updateWithDepsButtonField = _uiModDownloadItemType.GetField("_updateWithDepsButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _tMLUpdateRequiredField = _uiModDownloadItemType.GetField("tMLUpdateRequired", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // Get ReceivedItems from UIAsyncList
        Type? asyncListType = _modListField?.FieldType;
        if (asyncListType is not null)
        {
            _receivedItemsField = asyncListType.GetProperty("ReceivedItems", BindingFlags.Public | BindingFlags.Instance)?.GetMethod is not null
                ? null // It's a property, we'll handle it differently
                : asyncListType.GetField("ReceivedItems", BindingFlags.Public | BindingFlags.Instance);
        }
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Remove hook
        if (_uiModBrowserType is not null)
        {
            On_Main.DrawMenu -= HandleDrawMenu;
        }

        BindingById.Clear();
        ModItemBindings.Clear();
        ModItemButtonGroups.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _lastBrowserMenu = null;
        _initialFocusFramesRemaining = 0;
        _currentFocusIndex = 0;
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
        _announcedFirstMod = false;
        _previousModCount = 0;
    }

    private void HandleDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        // Call original first
        orig(self, gameTime);

        // Then process menu accessibility
        TryProcessModBrowser();
    }

    private void TryProcessModBrowser()
    {
        if (_uiModBrowserType is null)
        {
            return;
        }

        object? currentState = Main.MenuUI?.CurrentState;

        // Compare by type name since Type objects might differ across assemblies
        bool isModBrowser = currentState is not null &&
                            currentState.GetType().FullName == "Terraria.ModLoader.UI.ModBrowser.UIModBrowser";

        if (!isModBrowser)
        {
            // Clear menu reference if we've left UIModBrowser
            if (_lastBrowserMenu is not null)
            {
                Mod.Logger.Info("[DownloadMods] Left Download Mods menu");
                _lastBrowserMenu = null;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                FilterBindings.Clear();
                ModBindingsList.Clear();
                ModItemButtonGroups.Clear();
                TopActionBindingsList.Clear();
                BottomActionBindingsList.Clear();
                _currentModButtonIndex = 0;
            }
            return;
        }

        // Track menu state even when not using gamepad, so IsHandlingGamepadInput works
        bool menuChanged = !ReferenceEquals(currentState, _lastBrowserMenu);
        if (menuChanged)
        {
            _lastBrowserMenu = currentState;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            _initialFocusFramesRemaining = 60; // Wait longer for async mod loading
            _currentFocusIndex = 0;
            _currentModButtonIndex = 0;
            _currentRegion = FocusRegion.ModList;
            _announcedFirstMod = false;
            _previousModCount = 0;
            _lastScrollAnnouncedModIndex = -1;
            _lastScrollPosition = -1f;
            Mod.Logger.Info("[DownloadMods] Entered Download Mods menu");
        }

        // Process gamepad navigation even if not strictly "UsingGamepadUI" - check for actual gamepad input
        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        if (!hasGamepadInput)
        {
            return;
        }

        try
        {
            ConfigureGamepadPoints(currentState);
            HandleManualNavigation(currentState);
            HandleActionButton(currentState);
            HandleScrollAnnouncements(currentState);
            AnnounceCurrentFocus(currentState);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[DownloadMods] Failed to configure gamepad points: {ex}");
        }
    }

    private static void ConfigureGamepadPoints(object browser)
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
        object? modListObj = _modListField?.GetValue(browser);
        UIElement? modList = modListObj as UIElement;

        // Get filter/category buttons
        IList? categoryButtons = _categoryButtonsField?.GetValue(browser) as IList;

        // Get tag filter toggle (not in CategoryButtons)
        UIElement? tagFilterToggle = _tagFilterToggleField?.GetValue(browser) as UIElement;

        // Get action buttons
        UIElement? reloadButton = _reloadButtonField?.GetValue(browser) as UIElement;
        UIElement? backButton = _backButtonField?.GetValue(browser) as UIElement;
        UIElement? clearButton = _clearButtonField?.GetValue(browser) as UIElement;
        UIElement? downloadAllButton = _downloadAllButtonField?.GetValue(browser) as UIElement;
        UIElement? updateAllButton = _updateAllButtonField?.GetValue(browser) as UIElement;

        // Get the parent UIElement to check HasChild for conditional buttons
        UIElement? browserElement = browser as UIElement;

        // Create bindings for category/filter buttons (top row)
        var filterBindings = new List<PointBinding>();
        if (categoryButtons is not null)
        {
            string[] filterLabels = GetFilterLabels(browser);

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
            string tagLabel = GetTagFilterLabel(browser);

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
                UIElement? moreInfoButton = _moreInfoButtonField?.GetValue(item) as UIElement;
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
                UIElement? tMLUpdateButton = _tMLUpdateRequiredField?.GetValue(item) as UIElement;
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
                UIElement? updateButton = _updateButtonField?.GetValue(item) as UIElement;
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
                UIElement? updateWithDepsButton = _updateWithDepsButtonField?.GetValue(item) as UIElement;
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
            var binding = CreateButtonBinding(ref nextId, reloadButton, GetReloadButtonLabel(browser), PointType.ActionButton);
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

        // Log what we created
        if (Main.GameUpdateCount % 60 == 0) // Log once per second
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[DownloadMods] Bindings: {filterBindings.Count} filters, {modBindings.Count} mods, {topActionBindings.Count} top actions, {bottomActionBindings.Count} bottom actions");
        }

        // Check if mods just became available (async loading completed)
        int currentModCount = modBindings.Count;
        if (currentModCount > 0 && _previousModCount == 0 && !_announcedFirstMod)
        {
            _announcedFirstMod = true;
            _currentRegion = FocusRegion.ModList;
            _currentFocusIndex = 0;
            _currentModButtonIndex = 0;

            // Announce the mod count and first mod
            string loadedMessage = $"Loaded {currentModCount} mods. ";
            string firstModLabel = modBindings[0].Label;
            string announcement = $"{loadedMessage}{firstModLabel}, 1 of {currentModCount}";

            ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Mods loaded, announcing: {announcement}");
            ScreenReaderService.Announce(announcement, force: true);

            // Update tracking to prevent re-announcing
            _lastAnnouncedPointId = modBindings[0].Id;
            _lastSeenPointId = modBindings[0].Id;
        }
        _previousModCount = currentModCount;

        // Create all link points
        foreach (PointBinding binding in bindings)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
            linkPoint.Unlink();
        }

        // Connect filter buttons horizontally (no escape to back button - only mod list can reach back)
        for (int i = 0; i < filterBindings.Count - 1; i++)
        {
            ConnectHorizontal(filterBindings[i], filterBindings[i + 1]);
        }

        // Connect mod items vertically
        for (int i = 0; i < modBindings.Count - 1; i++)
        {
            ConnectVertical(modBindings[i], modBindings[i + 1]);
        }

        // Mod items no longer escape left to back button - use down at bottom instead

        // Connect top action buttons horizontally
        for (int i = 0; i < topActionBindings.Count - 1; i++)
        {
            ConnectHorizontal(topActionBindings[i], topActionBindings[i + 1]);
        }

        // Connect bottom action buttons horizontally
        for (int i = 0; i < bottomActionBindings.Count - 1; i++)
        {
            ConnectHorizontal(bottomActionBindings[i], bottomActionBindings[i + 1]);
        }

        // Connect filter row down to first mod item or top action buttons
        if (filterBindings.Count > 0)
        {
            if (modBindings.Count > 0)
            {
                foreach (var filter in filterBindings)
                {
                    UILinkPoint filterPoint = EnsureLinkPoint(filter.Id);
                    filterPoint.Down = modBindings[0].Id;
                }
                UILinkPoint firstModPoint = EnsureLinkPoint(modBindings[0].Id);
                firstModPoint.Up = filterBindings[filterBindings.Count / 2].Id;
            }
            else if (topActionBindings.Count > 0)
            {
                foreach (var filter in filterBindings)
                {
                    UILinkPoint filterPoint = EnsureLinkPoint(filter.Id);
                    filterPoint.Down = topActionBindings[0].Id;
                }
            }
        }

        // Connect last mod item down to bottom action buttons (Back, etc.)
        if (modBindings.Count > 0 && bottomActionBindings.Count > 0)
        {
            UILinkPoint lastModPoint = EnsureLinkPoint(modBindings[^1].Id);
            lastModPoint.Down = bottomActionBindings[0].Id;

            foreach (var action in bottomActionBindings)
            {
                UILinkPoint actionPoint = EnsureLinkPoint(action.Id);
                actionPoint.Up = modBindings[^1].Id;
            }
        }

        // Connect top action buttons down to bottom action buttons
        if (topActionBindings.Count > 0 && bottomActionBindings.Count > 0)
        {
            for (int i = 0; i < topActionBindings.Count; i++)
            {
                UILinkPoint topPoint = EnsureLinkPoint(topActionBindings[i].Id);
                topPoint.Down = bottomActionBindings[Math.Min(i, bottomActionBindings.Count - 1)].Id;
            }

            for (int i = 0; i < bottomActionBindings.Count; i++)
            {
                UILinkPoint bottomPoint = EnsureLinkPoint(bottomActionBindings[i].Id);
                bottomPoint.Up = topActionBindings[Math.Min(i, topActionBindings.Count - 1)].Id;
            }
        }

        // If no mod items, connect filter directly to top action
        if (modBindings.Count == 0 && filterBindings.Count > 0 && topActionBindings.Count > 0)
        {
            foreach (var filter in filterBindings)
            {
                UILinkPoint filterPoint = EnsureLinkPoint(filter.Id);
                filterPoint.Down = topActionBindings[0].Id;
            }

            foreach (var action in topActionBindings)
            {
                UILinkPoint actionPoint = EnsureLinkPoint(action.Id);
                actionPoint.Up = filterBindings[filterBindings.Count / 2].Id;
            }
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Force initial focus - only after mods are loaded OR if we need to fallback to filter/action buttons
        if (PlayerInput.UsingGamepadUI && _initialFocusFramesRemaining > 0)
        {
            // If mods are available, set focus to first mod
            if (modBindings.Count > 0)
            {
                int defaultPointId = modBindings[0].Id;
                UILinkPointNavigator.ChangePoint(defaultPointId);
                _currentRegion = FocusRegion.ModList;
                _currentFocusIndex = 0;
                _initialFocusFramesRemaining = 0; // Stop trying once we have mods
            }
            // Otherwise, fall back after enough frames have passed (in case mods never load)
            else if (_initialFocusFramesRemaining <= 5)
            {
                int defaultPointId = filterBindings.Count > 0 ? filterBindings[0].Id :
                                     topActionBindings.Count > 0 ? topActionBindings[0].Id :
                                     bottomActionBindings.Count > 0 ? bottomActionBindings[0].Id :
                                     bindings[0].Id;

                UILinkPointNavigator.ChangePoint(defaultPointId);
                _initialFocusFramesRemaining = 0;
            }
            else
            {
                _initialFocusFramesRemaining--;
            }
        }

        // Scrolling is handled by right analog stick, not automatic when navigating
        // HandleModListScrolling(modList, browser);
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
            // It uses yield return, so it's NOT an IList - we need to enumerate it
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

    private static void HandleModListScrolling(UIElement? modList, object browser)
    {
        if (modList is null)
        {
            return;
        }

        // Try to get the scrollbar
        Type browserType = browser.GetType();
        UIScrollbar? scrollbar = null;

        // The scrollbar is inside _backgroundElement, not directly accessible
        // We can try to find it through the mod list's scrollbar property
        if (modList is UIList uiList)
        {
            // UIList has a _scrollbar field
            FieldInfo? scrollbarField = typeof(UIList).GetField("_scrollbar", BindingFlags.NonPublic | BindingFlags.Instance);
            scrollbar = scrollbarField?.GetValue(uiList) as UIScrollbar;
        }

        if (scrollbar is null || ModItemBindings.Count == 0)
        {
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        ModItemBinding? currentBinding = ModItemBindings.FirstOrDefault(b => b.Id == currentPoint);

        if (currentBinding is null || currentBinding.Value.Id == 0)
        {
            return;
        }

        // Scroll to ensure the item is visible
        if (currentBinding.Value.ModItem is UIElement modItem)
        {
            CalculatedStyle listDims = modList.GetInnerDimensions();
            CalculatedStyle itemDims = modItem.GetDimensions();

            float itemRelativeTop = itemDims.Y - listDims.Y;
            float itemHeight = itemDims.Height;
            float viewHeight = listDims.Height;
            float currentScroll = scrollbar.ViewPosition;

            // If item is above visible area, scroll up
            if (itemRelativeTop < currentScroll)
            {
                scrollbar.ViewPosition = Math.Max(0, itemRelativeTop - 10);
            }
            // If item is below visible area, scroll down
            else if (itemRelativeTop + itemHeight > currentScroll + viewHeight)
            {
                scrollbar.ViewPosition = itemRelativeTop + itemHeight - viewHeight + 10;
            }
        }
    }

    // Analog stick previous state for debouncing
    private static bool _stickLeftWasPressed;
    private static bool _stickRightWasPressed;
    private static bool _stickUpWasPressed;
    private static bool _stickDownWasPressed;

    // A button state
    private static bool _aButtonWasPressed;

    // Right analog scroll tracking
    private static int _lastScrollAnnouncedModIndex = -1;
    private static float _lastScrollPosition = -1f;

    private static void HandleManualNavigation(object browser)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        // Detect new presses (transition from not pressed to pressed) for D-pad
        bool leftPressed = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
        bool rightPressed = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
        bool upPressed = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
        bool downPressed = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

        // Update D-pad previous state
        _leftWasPressed = gpState.DPad.Left == ButtonState.Pressed;
        _rightWasPressed = gpState.DPad.Right == ButtonState.Pressed;
        _upWasPressed = gpState.DPad.Up == ButtonState.Pressed;
        _downWasPressed = gpState.DPad.Down == ButtonState.Pressed;

        // Check analog stick with deadzone AND proper debouncing
        Vector2 stick = gpState.ThumbSticks.Left;
        const float threshold = 0.5f;

        bool stickLeftNow = stick.X < -threshold;
        bool stickRightNow = stick.X > threshold;
        bool stickUpNow = stick.Y > threshold;
        bool stickDownNow = stick.Y < -threshold;

        // Only trigger on new stick movement
        if (!leftPressed && stickLeftNow && !_stickLeftWasPressed)
        {
            leftPressed = true;
        }
        if (!rightPressed && stickRightNow && !_stickRightWasPressed)
        {
            rightPressed = true;
        }
        if (!upPressed && stickUpNow && !_stickUpWasPressed)
        {
            upPressed = true;
        }
        if (!downPressed && stickDownNow && !_stickDownWasPressed)
        {
            downPressed = true;
        }

        // Update analog stick previous state
        _stickLeftWasPressed = stickLeftNow;
        _stickRightWasPressed = stickRightNow;
        _stickUpWasPressed = stickUpNow;
        _stickDownWasPressed = stickDownNow;

        if (!leftPressed && !rightPressed && !upPressed && !downPressed)
        {
            return;
        }

        // Log the input
        string direction = leftPressed ? "LEFT" : rightPressed ? "RIGHT" : upPressed ? "UP" : "DOWN";
        ScreenReaderMod.Instance?.Logger.Debug($"[DownloadMods] Input: {direction}, current region: {_currentRegion}, index: {_currentFocusIndex}");

        // Only sync from UILinkPointNavigator if we haven't initialized yet
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (currentPoint < BaseLinkId || _currentFocusIndex < 0)
        {
            // Not our point or not initialized, force to first mod item or first available binding
            if (ModBindingsList.Count > 0)
            {
                _currentRegion = FocusRegion.ModList;
                _currentFocusIndex = 0;
                int newPoint = ModBindingsList[0].Id;
                UILinkPointNavigator.ChangePoint(newPoint);
                ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Force focus to mod list, point {newPoint}");
            }
            else if (BottomActionBindingsList.Count > 0)
            {
                _currentRegion = FocusRegion.BottomActionButtons;
                _currentFocusIndex = 0;
                int newPoint = BottomActionBindingsList[0].Id;
                UILinkPointNavigator.ChangePoint(newPoint);
                ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Force focus to back button, point {newPoint}");
            }
            return;
        }

        // Process navigation
        bool navigated = false;

        if (leftPressed)
        {
            navigated = NavigateLeft();
        }
        else if (rightPressed)
        {
            navigated = NavigateRight();
        }
        else if (upPressed)
        {
            navigated = NavigateUp();
        }
        else if (downPressed)
        {
            navigated = NavigateDown();
        }

        if (navigated)
        {
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
                ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Navigated to region {_currentRegion}, index {_currentFocusIndex}, point {newPointId.Value}");
            }
        }
        else
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[DownloadMods] Navigation blocked - at edge of region {_currentRegion}");
        }
    }

    private static void HandleActionButton(object browser)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        // Check for A button press (confirm/action button)
        bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
        bool aJustPressed = aPressed && !_aButtonWasPressed;
        _aButtonWasPressed = aPressed;

        if (aJustPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                if (binding.Element is UIElement element)
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Clicking: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(element, Main.MouseScreen);
                        element.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        ScreenReaderMod.Instance?.Logger.Warn($"[DownloadMods] Click failed: {ex.Message}");
                    }
                }
            }
        }
    }

    private static void HandleScrollAnnouncements(object browser)
    {
        // Only handle scroll announcements when in mod list region
        if (_currentRegion != FocusRegion.ModList)
        {
            _lastScrollAnnouncedModIndex = -1;
            _lastScrollPosition = -1f;
            return;
        }

        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        if (!gpState.IsConnected)
        {
            return;
        }

        // Check right analog stick vertical movement
        float rightStickY = gpState.ThumbSticks.Right.Y;
        const float scrollThreshold = 0.3f;

        if (Math.Abs(rightStickY) < scrollThreshold)
        {
            return; // No significant scrolling happening
        }

        // Get the mod list and scrollbar
        object? modListObj = _modListField?.GetValue(browser);
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

        float currentScroll = scrollbar.ViewPosition;

        // Check if scroll position changed significantly
        if (Math.Abs(currentScroll - _lastScrollPosition) < 5f && _lastScrollPosition >= 0)
        {
            return; // Not enough scroll change
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

        // If the centered mod changed, announce it
        if (closestModIndex >= 0 && closestModIndex != _lastScrollAnnouncedModIndex)
        {
            _lastScrollAnnouncedModIndex = closestModIndex;

            // Get the mod item to announce
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

                    // Update the D-pad focus to match scroll position
                    _currentFocusIndex = closestModIndex;
                    _currentModButtonIndex = 0;

                    ScreenReaderMod.Instance?.Logger.Debug($"[DownloadMods] Scroll announced: {announcement}");
                    break;
                }
                currentIndex++;
            }
        }
    }

    private static int? GetCurrentPointId()
    {
        // For mod list, we need to consider the button index
        if (_currentRegion == FocusRegion.ModList)
        {
            if (_currentFocusIndex < 0 || _currentFocusIndex >= ModItemButtonGroups.Count)
            {
                // Fall back to ModBindingsList if button groups aren't set up yet
                if (_currentFocusIndex >= 0 && _currentFocusIndex < ModBindingsList.Count)
                {
                    return ModBindingsList[_currentFocusIndex].Id;
                }
                return null;
            }

            var buttonGroup = ModItemButtonGroups[_currentFocusIndex];
            return buttonGroup.GetButtonIdAtIndex(_currentModButtonIndex);
        }

        var list = GetCurrentRegionList();
        if (list is null || list.Count == 0 || _currentFocusIndex < 0 || _currentFocusIndex >= list.Count)
        {
            return null;
        }

        return list[_currentFocusIndex].Id;
    }

    private static List<PointBinding>? GetCurrentRegionList()
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

    private static bool NavigateLeft()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                // Only navigate within filter buttons, don't leave to back button
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                // Only navigate between buttons within a mod item, don't leave to back button
                if (_currentModButtonIndex > 0)
                {
                    _currentModButtonIndex--;
                    ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] LEFT in mod list -> button index {_currentModButtonIndex}");
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool NavigateRight()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                if (_currentFocusIndex < FilterBindings.Count - 1)
                {
                    _currentFocusIndex++;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                if (_currentFocusIndex >= 0 && _currentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[_currentFocusIndex];
                    int maxButtonIndex = buttonGroup.ButtonCount - 1;

                    if (_currentModButtonIndex < maxButtonIndex)
                    {
                        _currentModButtonIndex++;
                        ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] RIGHT in mod list -> button index {_currentModButtonIndex}");
                        return true;
                    }
                }
                break;

            case FocusRegion.TopActionButtons:
                if (_currentFocusIndex < TopActionBindingsList.Count - 1)
                {
                    _currentFocusIndex++;
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                if (_currentFocusIndex < BottomActionBindingsList.Count - 1)
                {
                    _currentFocusIndex++;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool NavigateUp()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                break;

            case FocusRegion.ModList:
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    _currentFocusIndex = FilterBindings.Count / 2;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    _currentFocusIndex = ModBindingsList.Count - 1;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    _currentFocusIndex = Math.Min(_currentFocusIndex, FilterBindings.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                // Go back up to mod list (or filter buttons if no mods)
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    _currentFocusIndex = ModBindingsList.Count - 1; // Go to last mod
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    _currentFocusIndex = FilterBindings.Count / 2;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool NavigateDown()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    _currentFocusIndex = 0;
                    _currentModButtonIndex = 0;
                    return true;
                }
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    _currentFocusIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                if (_currentFocusIndex < ModBindingsList.Count - 1)
                {
                    _currentFocusIndex++;
                    _currentModButtonIndex = 0;
                    return true;
                }
                // At bottom of mod list, go directly to bottom action buttons (Back, etc.)
                if (BottomActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.BottomActionButtons;
                    _currentFocusIndex = 0;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                if (BottomActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.BottomActionButtons;
                    _currentFocusIndex = Math.Min(_currentFocusIndex, BottomActionBindingsList.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                break;
        }

        return false;
    }

    private static PointBinding CreateButtonBinding(ref int nextId, UIElement element, string label, PointType type)
    {
        CalculatedStyle dims = element.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        return new PointBinding(nextId++, center, label, string.Empty, element, type);
    }

    private static string GetModDisplayName(object modItem)
    {
        try
        {
            ModDownloadItem? modData = _modDownloadField?.GetValue(modItem) as ModDownloadItem;
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
            ModDownloadItem? modData = _modDownloadField?.GetValue(modItem) as ModDownloadItem;
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
            ModDownloadItem? modData = _modDownloadField?.GetValue(modItem) as ModDownloadItem;
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
        // CategoryButtons order: SortMode, TimePeriod, Update, ModSide, Search
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
            // Check if Loading property is true
            PropertyInfo? loadingProperty = browser.GetType().GetProperty("Loading", BindingFlags.Public | BindingFlags.Instance);
            bool isLoading = (bool)(loadingProperty?.GetValue(browser) ?? false);

            return isLoading ? "Cancel loading" : "Reload browser";
        }
        catch
        {
            return "Reload";
        }
    }

    private static void AnnounceCurrentFocus(object browser)
    {
        int? expectedPoint = GetCurrentPointId();
        int currentPoint = expectedPoint ?? UILinkPointNavigator.CurrentPoint;

        if (currentPoint < BaseLinkId)
        {
            return;
        }

        // Only announce when the point has been stable for at least one frame
        bool isStable = currentPoint == _lastSeenPointId;
        bool alreadyAnnounced = currentPoint == _lastAnnouncedPointId;

        _lastSeenPointId = currentPoint;

        if (!isStable || alreadyAnnounced)
        {
            return;
        }

        if (!BindingById.TryGetValue(currentPoint, out PointBinding binding))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[DownloadMods] No binding found for point {currentPoint}");
            return;
        }

        string announcement = BuildAnnouncement(binding);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);

        ScreenReaderMod.Instance?.Logger.Info($"[DownloadMods] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private static string BuildAnnouncement(PointBinding binding)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        switch (binding.Type)
        {
            case PointType.FilterButton:
                int filterIndex = _currentFocusIndex + 1;
                int filterTotal = FilterBindings.Count;
                return $"Filter: {label}, {filterIndex} of {filterTotal}";

            case PointType.ModItem:
                int modIndex = _currentFocusIndex + 1;
                int modTotal = ModBindingsList.Count;
                return $"{label}, {modIndex} of {modTotal}";

            case PointType.ModItemButton:
                if (_currentFocusIndex >= 0 && _currentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[_currentFocusIndex];
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

    private static void ConnectHorizontal(PointBinding left, PointBinding right)
    {
        UILinkPoint leftPoint = EnsureLinkPoint(left.Id);
        UILinkPoint rightPoint = EnsureLinkPoint(right.Id);

        leftPoint.Right = right.Id;
        rightPoint.Left = left.Id;
    }

    private static void ConnectVertical(PointBinding up, PointBinding down)
    {
        UILinkPoint upPoint = EnsureLinkPoint(up.Id);
        UILinkPoint downPoint = EnsureLinkPoint(down.Id);

        upPoint.Down = down.Id;
        downPoint.Up = up.Id;
    }

    private static UILinkPoint EnsureLinkPoint(int id)
    {
        if (!UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? linkPoint))
        {
            linkPoint = new UILinkPoint(id, true, -1, -1, -1, -1);
            UILinkPointNavigator.Points[id] = linkPoint;
        }

        return linkPoint;
    }

    private enum PointType
    {
        FilterButton,
        ModItem,
        ModItemButton,
        ActionButton,
        BackButton
    }

    private readonly record struct PointBinding(int Id, Vector2 Position, string Label, string Description, UIElement? Element, PointType Type);

    private readonly record struct ModItemBinding(int Id, object ModItem, int ListIndex);

    /// <summary>
    /// Tracks the buttons available for a mod download item.
    /// </summary>
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
                int count = 1; // Main is always present
                if (MoreInfoId.HasValue) count++;
                if (TMLUpdateId.HasValue) count++;
                if (UpdateId.HasValue) count++;
                if (UpdateWithDepsId.HasValue) count++;
                return count;
            }
        }
    }
}
