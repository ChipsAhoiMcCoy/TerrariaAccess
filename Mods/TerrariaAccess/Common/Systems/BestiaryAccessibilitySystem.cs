#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Systems.ModBrowser;
using TerrariaAccess.Common.Systems.ModMenuAccessibility;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Provides full keyboard/gamepad navigation and screen reader announcements for the Bestiary menu.
/// Works from both the title menu and in-game pause menu by hooking UIBestiaryTest.Draw directly.
/// </summary>
public sealed class BestiaryAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Constants

    private const string BestiaryMenuTypeName = "Terraria.GameContent.UI.States.UIBestiaryTest";
    private const string ContextKeyScreen = "bestiary:screen";

    protected override int BaseLinkId => LinkIdRegistry.Bestiary;
    protected override string MenuTypeName => BestiaryMenuTypeName;
    protected override string SystemLogName => "Bestiary";
    protected override bool UseDrawMenuHook => false;

    #endregion

    #region Hook Delegates & Fields

    private delegate void DrawDelegate(UIState self, SpriteBatch spriteBatch);
    private delegate void OnOpenPageDelegate(UIState self);

    private static Hook? _drawHook;
    private static Hook? _onOpenPageHook;

    private static BestiaryAccessibilitySystem? _instance;

    #endregion

    #region Navigation State

    private enum NavigationRegion
    {
        NavButtons,     // BackPage, NextPage
        ActionButtons,  // Sort, Filter, Search buttons
        EntryGrid,      // Creature grid
        FilterGrid,     // Filter overlay
        SortGrid,       // Sort overlay
        ExitButton      // Back/Exit button
    }

    private NavigationRegion _currentRegion = NavigationRegion.EntryGrid;
    private int _currentEntryIndex;
    private int _currentNavIndex;
    private int _currentActionIndex;
    private int _currentFilterIndex;
    private int _currentSortIndex;

    // Grid dimensions (recalculated each frame)
    private int _gridColumns;
    private int _gridRows;

    // Bindings
    private readonly List<PointBinding> _navBindings = new();
    private readonly List<PointBinding> _actionBindings = new();
    private readonly List<EntryBinding> _entryBindings = new();
    private readonly List<PointBinding> _filterBindings = new();
    private readonly List<PointBinding> _sortBindings = new();
    private PointBinding? _exitBinding;

    // Overlay state tracking
    private bool _wasSortOverlayOpen;
    private bool _wasFilterOverlayOpen;

    // Page change tracking
    private int _lastPageOffset = -1;

    // Search text tracking for audio feedback
    private string? _lastSearchText;

    #endregion

    #region Public Properties

    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            return IsBestiaryMenuActive();
        }
    }

    private static bool IsBestiaryMenuActive()
    {
        string? menuTypeName = Main.MenuUI?.CurrentState?.GetType().FullName;
        if (menuTypeName == BestiaryMenuTypeName)
        {
            return true;
        }

        string? inGameTypeName = Main.InGameUI?.CurrentState?.GetType().FullName;
        if (inGameTypeName == BestiaryMenuTypeName)
        {
            return true;
        }

        return false;
    }

    protected override object? GetActiveMenuState()
    {
        object? state = Main.MenuUI?.CurrentState;
        if (state is not null && state.GetType().FullName == BestiaryMenuTypeName)
        {
            return state;
        }

        state = Main.InGameUI?.CurrentState;
        if (state is not null && state.GetType().FullName == BestiaryMenuTypeName)
        {
            return state;
        }

        return null;
    }

    #endregion

    #region Lifecycle

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        _instance = this;

        Type? bestiaryMenuType = ReflectionCache.UIBestiaryTest.Type;
        if (bestiaryMenuType is null)
        {
            Mod.Logger.Warn($"[{SystemLogName}] UIBestiaryTest type not found, system disabled");
            return;
        }

        base.Load();

        MethodInfo? drawMethod = bestiaryMenuType.GetMethod(
            "Draw",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(SpriteBatch) },
            null);
        if (drawMethod is not null)
        {
            _drawHook = new Hook(drawMethod, OnDraw);
        }

        MethodInfo? onOpenPageMethod = bestiaryMenuType.GetMethod(
            "OnOpenPage",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (onOpenPageMethod is not null)
        {
            _onOpenPageHook = new Hook(onOpenPageMethod, OnOpenPage);
        }
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        base.Unload();

        _drawHook?.Dispose();
        _drawHook = null;
        _onOpenPageHook?.Dispose();
        _onOpenPageHook = null;

        _navBindings.Clear();
        _actionBindings.Clear();
        _entryBindings.Clear();
        _filterBindings.Clear();
        _sortBindings.Clear();
        _exitBinding = null;
        BindingById.Clear();
        _lastPageOffset = -1;
        _lastSearchText = null;

        ScreenReaderService.ClearContexts("bestiary:");
        _instance = null;
    }

    #endregion

    #region Hooks

    private static void OnOpenPage(OnOpenPageDelegate orig, UIState self)
    {
        orig(self);

        if (_instance is null || self.GetType().FullName != BestiaryMenuTypeName)
        {
            return;
        }

        _instance.ResetMenuTracking();
        _instance.ResetBestiaryState();
        _instance.Mod.Logger.Info($"[{_instance.SystemLogName}] Menu activated");
    }

    private static void OnDraw(DrawDelegate orig, UIState self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (_instance is null)
        {
            return;
        }

        _instance.ProcessMenuAccessibility();
    }

    #endregion

    #region Menu Processing

    protected override void OnMenuEntered(object menuState)
    {
        ResetBestiaryState();
        ScreenReaderService.ClearContexts("bestiary:");
    }

    protected override void OnMenuExited()
    {
        _navBindings.Clear();
        _actionBindings.Clear();
        _entryBindings.Clear();
        _filterBindings.Clear();
        _sortBindings.Clear();
        _exitBinding = null;
        BindingById.Clear();
        ScreenReaderService.ClearContexts("bestiary:");
    }

    protected override int GetInitialFocusFrameCount() => 15;

    protected override bool ShouldProcessInput(object menuState)
    {
        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        bool wasSearchActive = SearchModeManager.IsSearchModeActive;
        SearchModeManager.Update();
        bool isSearchActive = SearchModeManager.IsSearchModeActive;

        HandleSearchBarSync(menuState, wasSearchActive, isSearchActive);

        if (isSearchActive && !wasSearchActive)
        {
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
        }

        if (!isSearchActive && wasSearchActive)
        {
            ResetInputState();
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            Mod.Logger.Info($"[{SystemLogName}] Exited search mode, reset input state");

            try
            {
                ConfigureGamepadPoints(menuState);
            }
            catch (Exception ex)
            {
                Mod.Logger.Warn($"[{SystemLogName}] Error configuring points on search exit: {ex}");
            }

            return false;
        }

        return hasGamepadInput || ShouldProcessKeyboardInput();
    }

    private void ResetBestiaryState()
    {
        _currentRegion = NavigationRegion.EntryGrid;
        _currentEntryIndex = 0;
        _currentNavIndex = 0;
        _currentActionIndex = 0;
        _currentFilterIndex = 0;
        _currentSortIndex = 0;
        _wasSortOverlayOpen = false;
        _wasFilterOverlayOpen = false;
        _lastPageOffset = -1;
        _lastSearchText = null;
    }

    private void DetectPageAndOverlayTransitions(object menuState)
    {
        int currentPageOffset = GetCurrentPageOffset(menuState);
        if (_lastPageOffset >= 0 && currentPageOffset >= 0 && currentPageOffset != _lastPageOffset)
        {
            string currentRangeText = GetRangeText();
            if (!string.IsNullOrEmpty(currentRangeText))
            {
                ScreenReaderService.EnqueuePrefix($"Page {currentRangeText}.");
            }

            _currentRegion = NavigationRegion.EntryGrid;
            _currentEntryIndex = 0;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;

            Mod.Logger.Info($"[{SystemLogName}] Page changed to offset {currentPageOffset}");
        }
        _lastPageOffset = currentPageOffset;

        bool isSortOpen = IsSortOverlayOpen(menuState);
        bool isFilterOpen = IsFilterOverlayOpen(menuState);

        if (isSortOpen && !_wasSortOverlayOpen)
        {
            _currentRegion = NavigationRegion.SortGrid;
            _currentSortIndex = 0;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            ScreenReaderService.EnqueuePrefix("Sort options.");
            Mod.Logger.Info($"[{SystemLogName}] Sort overlay opened");
        }
        else if (!isSortOpen && _wasSortOverlayOpen)
        {
            _currentRegion = NavigationRegion.ActionButtons;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            Mod.Logger.Info($"[{SystemLogName}] Sort overlay closed");
        }

        if (isFilterOpen && !_wasFilterOverlayOpen)
        {
            _currentRegion = NavigationRegion.FilterGrid;
            _currentFilterIndex = 0;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            ScreenReaderService.EnqueuePrefix("Filter options.");
            Mod.Logger.Info($"[{SystemLogName}] Filter overlay opened");
        }
        else if (!isFilterOpen && _wasFilterOverlayOpen)
        {
            _currentRegion = NavigationRegion.ActionButtons;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            Mod.Logger.Info($"[{SystemLogName}] Filter overlay closed");
        }

        _wasSortOverlayOpen = isSortOpen;
        _wasFilterOverlayOpen = isFilterOpen;
    }

    private void HandleSearchBarSync(object menuState, bool wasSearchActive, bool isSearchActive)
    {
        UISearchBar? searchBar = GetSearchBar(menuState);
        if (searchBar is null)
        {
            return;
        }

        if (isSearchActive && !wasSearchActive)
        {
            // Entering search mode: activate the search bar's text input
            if (!searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
                Mod.Logger.Info($"[{SystemLogName}] Activated search bar text input");
            }
            _lastSearchText = null;

            // SearchModeManager.Toggle() enqueued a prefix, but we return early
            // during search mode so no announcement would consume it. Clear it
            // and announce directly instead.
            ScreenReaderService.ClearAllPrefixes();
            string searchAnnouncement = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.SearchMode.SearchEnabled",
                "Search mode. Type to filter. Press Tab to return to navigation");
            ScreenReaderService.Announce(searchAnnouncement, force: true);
        }
        else if (!isSearchActive && wasSearchActive)
        {
            // Exiting search mode: deactivate the search bar's text input
            if (searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
                Mod.Logger.Info($"[{SystemLogName}] Deactivated search bar text input");
            }
        }
        else if (isSearchActive && !searchBar.IsWritingText)
        {
            // Desync: search bar was deactivated externally (e.g., Escape key in search bar)
            SearchModeManager.ExitSearchMode();
            Mod.Logger.Info($"[{SystemLogName}] Search bar desynced, exiting search mode");
        }
        else if (!isSearchActive && searchBar.IsWritingText)
        {
            // Desync: search bar still active but search mode ended
            searchBar.ToggleTakingText();
            Mod.Logger.Info($"[{SystemLogName}] Deactivated orphaned search bar");
        }

        // Track search text changes for audio feedback while searching
        if (SearchModeManager.IsSearchModeActive)
        {
            string? currentText = GetSearchString(menuState);
            if (_lastSearchText is not null && !string.Equals(currentText, _lastSearchText, StringComparison.Ordinal))
            {
                global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
            }
            _lastSearchText = currentText;
        }
        else
        {
            _lastSearchText = null;
        }
    }

    private UISearchBar? GetSearchBar(object menuState)
    {
        try
        {
            return ReflectionCache.UIBestiaryTest.SearchBar?.GetValue(menuState) as UISearchBar;
        }
        catch
        {
            return null;
        }
    }

    private string? GetSearchString(object menuState)
    {
        try
        {
            return ReflectionCache.UIBestiaryTest.SearchString?.GetValue(menuState) as string;
        }
        catch
        {
            return null;
        }
    }

    protected override bool ShouldProcessKeyboardInput()
    {
        return !SearchModeManager.IsSearchModeActive;
    }

    #endregion

    #region Overlay Detection

    private bool IsSortOverlayOpen(object menuState)
    {
        try
        {
            object? sortingGrid = ReflectionCache.UIBestiaryTest.SortingGrid?.GetValue(menuState);
            if (sortingGrid is UIElement sortElement && sortElement.Parent is not null)
            {
                return true;
            }
        }
        catch
        {
            // Best effort
        }
        return false;
    }

    private bool IsFilterOverlayOpen(object menuState)
    {
        try
        {
            object? filteringGrid = ReflectionCache.UIBestiaryTest.FilteringGrid?.GetValue(menuState);
            if (filteringGrid is UIElement filterElement && filterElement.Parent is not null)
            {
                return true;
            }
        }
        catch
        {
            // Best effort
        }
        return false;
    }

    #endregion

    #region Gamepad Point Configuration

    protected override void ConfigureGamepadPoints(object menuState)
    {
        BindingById.Clear();
        _navBindings.Clear();
        _actionBindings.Clear();
        _entryBindings.Clear();
        _filterBindings.Clear();
        _sortBindings.Clear();
        _exitBinding = null;
        _gridColumns = 0;
        _gridRows = 0;

        int nextId = BaseLinkId;

        ConfigureNavButtons(menuState, ref nextId);
        ConfigureActionButtons(menuState, ref nextId);

        if (IsSortOverlayOpen(menuState))
        {
            ConfigureSortGrid(menuState, ref nextId);
        }
        else if (IsFilterOverlayOpen(menuState))
        {
            ConfigureFilterGrid(menuState, ref nextId);
        }
        else
        {
            ConfigureEntryGrid(menuState, ref nextId);
        }

        ConfigureExitButton(menuState, ref nextId);

        // Set up link points
        foreach (var binding in BindingById.Values)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
            linkPoint.Unlink();
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Handle initial focus
        if (InitialFocusFramesRemaining > 0)
        {
            int defaultPointId = _entryBindings.Count > 0
                ? _entryBindings[0].Binding.Id
                : (_exitBinding?.Id ?? BaseLinkId);

            UILinkPointNavigator.ChangePoint(defaultPointId);
            InitialFocusFramesRemaining--;
        }

        // Handle search mode -> navigation mode transition
        if (SearchModeManager.ConsumeFocusFirstModRequest() && _entryBindings.Count > 0)
        {
            _currentRegion = NavigationRegion.EntryGrid;
            _currentEntryIndex = 0;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;

            int pointId = _entryBindings[0].Binding.Id;
            UILinkPointNavigator.ChangePoint(pointId);
        }

        // Always keep UILinkPointNavigator synced to our current point.
        // Terraria's SetupGamepadPoints (which runs in orig Draw before us) calls
        // MoveToVisuallyClosestPoint when CurrentPoint >= its max ID, hijacking
        // focus to a Terraria-managed point based on mouse position. We must
        // override this every frame to prevent Terraria's gamepad navigation
        // from processing D-pad input on the wrong entry.
        if (InitialFocusFramesRemaining <= 0)
        {
            int? syncPointId = GetCurrentPointId();
            if (syncPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(syncPointId.Value);
            }
        }
    }

    private void ConfigureNavButtons(object menuState, ref int nextId)
    {
        try
        {
            // Look for BackPage and NextPage snap points on the menu state
            if (menuState is not UIElement menuElement)
            {
                return;
            }

            // Find snap points by name
            var snapPoints = GetSnapPoints(menuElement);

            foreach (var sp in snapPoints)
            {
                string name = sp.Name;
                if (name != "BackPage" && name != "NextPage")
                {
                    continue;
                }

                string label = name == "BackPage" ? "Previous Page" : "Next Page";
                Vector2 position = sp.Position;
                var binding = new PointBinding(nextId++, position, label, string.Empty, null, PointType.NavButton);
                _navBindings.Add(binding);
                BindingById[binding.Id] = binding;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring nav buttons: {ex.Message}");
        }
    }

    private void ConfigureActionButtons(object menuState, ref int nextId)
    {
        try
        {
            if (menuState is not UIElement menuElement)
            {
                return;
            }

            var snapPoints = GetSnapPoints(menuElement);

            string[] actionNames = { "SortButton", "FilterButton", "SearchButton" };
            string[] actionLabels = { "Sort", "Filter", "Search" };

            for (int i = 0; i < actionNames.Length; i++)
            {
                foreach (var sp in snapPoints)
                {
                    if (sp.Name != actionNames[i])
                    {
                        continue;
                    }

                    string label = actionLabels[i];

                    // Append current sort/filter text
                    if (actionNames[i] == "SortButton")
                    {
                        string? sortText = GetSortText(menuState);
                        if (!string.IsNullOrEmpty(sortText))
                        {
                            label = $"Sort: {sortText}";
                        }
                    }
                    else if (actionNames[i] == "FilterButton")
                    {
                        string? filterText = GetFilterText(menuState);
                        if (!string.IsNullOrEmpty(filterText))
                        {
                            label = $"Filter: {filterText}";
                        }
                    }

                    Vector2 position = sp.Position;
                    var binding = new PointBinding(nextId++, position, label, string.Empty, null, PointType.ActionButton);
                    _actionBindings.Add(binding);
                    BindingById[binding.Id] = binding;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring action buttons: {ex.Message}");
        }
    }

    private void ConfigureEntryGrid(object menuState, ref int nextId)
    {
        try
        {
            object? entryGrid = ReflectionCache.UIBestiaryTest.EntryGrid?.GetValue(menuState);
            if (entryGrid is not UIElement gridElement)
            {
                return;
            }

            // Get grid dimensions
            MethodInfo? getEntries = BestiaryReflectionCache.GetEntryGridGetEntriesToShowMethod(entryGrid.GetType());
            if (getEntries is not null)
            {
                object?[] args = new object?[3];
                getEntries.Invoke(entryGrid, args);
                if (args[0] is int cols && args[1] is int rows)
                {
                    _gridColumns = cols;
                    _gridRows = rows;
                }
            }

            // Get page offset for position context
            int pageOffset = 0;
            if (ReflectionCache.UIBestiaryEntryGrid.AtEntryIndex?.GetValue(entryGrid) is int offset)
            {
                pageOffset = offset;
            }

            int totalEntries = 0;
            if (ReflectionCache.UIBestiaryEntryGrid.LastEntry?.GetValue(entryGrid) is int last)
            {
                totalEntries = last;
            }

            // Iterate visible child buttons
            int gridIndex = 0;
            foreach (UIElement child in gridElement.Children)
            {
                if (child.GetType().Name != "UIBestiaryEntryButton")
                {
                    continue;
                }

                string entryName = GetEntryButtonName(child);
                bool isUnlocked = entryName != "???";
                int absoluteIndex = pageOffset + gridIndex;

                CalculatedStyle dims = child.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var binding = new PointBinding(nextId++, center, entryName, string.Empty, child, PointType.EntryButton);
                BindingById[binding.Id] = binding;

                _entryBindings.Add(new EntryBinding
                {
                    Binding = binding,
                    Name = entryName,
                    IsUnlocked = isUnlocked,
                    AbsoluteIndex = absoluteIndex,
                    TotalEntries = totalEntries,
                    GridIndex = gridIndex,
                    EntryButton = child
                });

                gridIndex++;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring entry grid: {ex.Message}");
        }
    }

    private void ConfigureFilterGrid(object menuState, ref int nextId)
    {
        try
        {
            object? filteringGrid = ReflectionCache.UIBestiaryTest.FilteringGrid?.GetValue(menuState);
            if (filteringGrid is not UIElement filterElement)
            {
                return;
            }

            var snapPoints = GetSnapPoints(filterElement);

            foreach (var sp in snapPoints)
            {
                if (sp.Name != "Filters")
                {
                    continue;
                }

                // Try to get filter label from the button
                string label = GetFilterButtonLabel(sp.Element) ?? $"Filter {sp.Id + 1}";

                Vector2 position = sp.Position;
                var binding = new PointBinding(nextId++, position, label, string.Empty, sp.Element, PointType.FilterButton);
                _filterBindings.Add(binding);
                BindingById[binding.Id] = binding;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring filter grid: {ex.Message}");
        }
    }

    private void ConfigureSortGrid(object menuState, ref int nextId)
    {
        try
        {
            object? sortingGrid = ReflectionCache.UIBestiaryTest.SortingGrid?.GetValue(menuState);
            if (sortingGrid is not UIElement sortElement)
            {
                return;
            }

            var snapPoints = GetSnapPoints(sortElement);

            foreach (var sp in snapPoints)
            {
                if (sp.Name != "SortSteps")
                {
                    continue;
                }

                string label = GetSortButtonLabel(sp.Element) ?? $"Sort {sp.Id + 1}";

                // Check if this sort is currently active
                bool isActive = false;
                try
                {
                    PropertyInfo? isOnProp = sp.Element is null
                        ? null
                        : BestiaryReflectionCache.GetIsOnProperty(sp.Element.GetType());
                    if (isOnProp?.GetValue(sp.Element) is bool on)
                    {
                        isActive = on;
                    }
                }
                catch
                {
                    // Best effort
                }

                if (isActive)
                {
                    label = $"{label}, Selected";
                }

                Vector2 position = sp.Position;
                var binding = new PointBinding(nextId++, position, label, string.Empty, sp.Element, PointType.SortButton);
                _sortBindings.Add(binding);
                BindingById[binding.Id] = binding;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring sort grid: {ex.Message}");
        }
    }

    private void ConfigureExitButton(object menuState, ref int nextId)
    {
        try
        {
            if (menuState is not UIElement menuElement)
            {
                return;
            }

            var snapPoints = GetSnapPoints(menuElement);

            foreach (var sp in snapPoints)
            {
                if (sp.Name != "ExitButton")
                {
                    continue;
                }

                string backLabel = Language.GetTextValue("UI.Back");
                if (string.IsNullOrWhiteSpace(backLabel)) backLabel = "Back";

                Vector2 position = sp.Position;
                var binding = new PointBinding(nextId++, position, backLabel, string.Empty, sp.Element, PointType.BackButton);
                _exitBinding = binding;
                BindingById[binding.Id] = binding;
                break;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring exit button: {ex.Message}");
        }
    }

    #endregion

    #region Navigation

    protected override void HandleNavigation(object menuState)
    {
        DetectPageAndOverlayTransitions(menuState);

        if (!CurrentInput.HasNavigation)
        {
            return;
        }

        bool navigated = false;

        switch (_currentRegion)
        {
            case NavigationRegion.NavButtons:
                navigated = HandleNavButtonNavigation();
                break;
            case NavigationRegion.ActionButtons:
                navigated = HandleActionButtonNavigation();
                break;
            case NavigationRegion.EntryGrid:
                navigated = HandleEntryGridNavigation();
                break;
            case NavigationRegion.FilterGrid:
                navigated = HandleFilterGridNavigation();
                break;
            case NavigationRegion.SortGrid:
                navigated = HandleSortGridNavigation();
                break;
            case NavigationRegion.ExitButton:
                navigated = HandleExitButtonNavigation();
                break;
        }

        if (navigated)
        {
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
            }
        }
    }

    private bool HandleNavButtonNavigation()
    {
        if (_navBindings.Count == 0)
        {
            return false;
        }

        if (CurrentInput.Left && _currentNavIndex > 0)
        {
            _currentNavIndex--;
            return true;
        }
        if (CurrentInput.Right && _currentNavIndex < _navBindings.Count - 1)
        {
            _currentNavIndex++;
            return true;
        }
        if (CurrentInput.Right && _currentNavIndex >= _navBindings.Count - 1 && _actionBindings.Count > 0)
        {
            _currentRegion = NavigationRegion.ActionButtons;
            _currentActionIndex = 0;
            return true;
        }
        if (CurrentInput.Down)
        {
            if (_entryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = Math.Min(_currentEntryIndex, _entryBindings.Count - 1);
                return true;
            }
            if (_exitBinding.HasValue)
            {
                _currentRegion = NavigationRegion.ExitButton;
                return true;
            }
        }

        return false;
    }

    private bool HandleActionButtonNavigation()
    {
        if (_actionBindings.Count == 0)
        {
            return false;
        }

        if (CurrentInput.Left)
        {
            if (_currentActionIndex > 0)
            {
                _currentActionIndex--;
                return true;
            }
            if (_navBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.NavButtons;
                _currentNavIndex = _navBindings.Count - 1;
                return true;
            }
        }
        if (CurrentInput.Right && _currentActionIndex < _actionBindings.Count - 1)
        {
            _currentActionIndex++;
            return true;
        }
        if (CurrentInput.Down)
        {
            if (_entryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = Math.Min(_currentEntryIndex, _entryBindings.Count - 1);
                return true;
            }
            if (_exitBinding.HasValue)
            {
                _currentRegion = NavigationRegion.ExitButton;
                return true;
            }
        }
        if (CurrentInput.Up)
        {
            // No region above action buttons
        }

        return false;
    }

    private bool HandleEntryGridNavigation()
    {
        if (_entryBindings.Count == 0)
        {
            return false;
        }

        int cols = _gridColumns > 0 ? _gridColumns : 1;

        if (CurrentInput.Left)
        {
            if (_currentEntryIndex > 0)
            {
                _currentEntryIndex--;
                return true;
            }
        }
        if (CurrentInput.Right)
        {
            if (_currentEntryIndex < _entryBindings.Count - 1)
            {
                _currentEntryIndex++;
                return true;
            }
        }
        if (CurrentInput.Up)
        {
            int newIndex = _currentEntryIndex - cols;
            if (newIndex >= 0)
            {
                _currentEntryIndex = newIndex;
                return true;
            }
            // Move to top bar
            if (_actionBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.ActionButtons;
                _currentActionIndex = Math.Min(_currentActionIndex, _actionBindings.Count - 1);
                return true;
            }
            if (_navBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.NavButtons;
                _currentNavIndex = Math.Min(_currentNavIndex, _navBindings.Count - 1);
                return true;
            }
        }
        if (CurrentInput.Down)
        {
            int newIndex = _currentEntryIndex + cols;
            if (newIndex < _entryBindings.Count)
            {
                _currentEntryIndex = newIndex;
                return true;
            }
            if (_exitBinding.HasValue)
            {
                _currentRegion = NavigationRegion.ExitButton;
                return true;
            }
        }

        return false;
    }

    private bool HandleFilterGridNavigation()
    {
        if (_filterBindings.Count == 0)
        {
            return false;
        }

        // Filter grid is typically 12 per row
        const int filterCols = 12;

        if (CurrentInput.Left && _currentFilterIndex > 0)
        {
            _currentFilterIndex--;
            return true;
        }
        if (CurrentInput.Right && _currentFilterIndex < _filterBindings.Count - 1)
        {
            _currentFilterIndex++;
            return true;
        }
        if (CurrentInput.Up)
        {
            int newIndex = _currentFilterIndex - filterCols;
            if (newIndex >= 0)
            {
                _currentFilterIndex = newIndex;
                return true;
            }
        }
        if (CurrentInput.Down)
        {
            int newIndex = _currentFilterIndex + filterCols;
            if (newIndex < _filterBindings.Count)
            {
                _currentFilterIndex = newIndex;
                return true;
            }
        }

        return false;
    }

    private bool HandleSortGridNavigation()
    {
        if (_sortBindings.Count == 0)
        {
            return false;
        }

        if (CurrentInput.Up && _currentSortIndex > 0)
        {
            _currentSortIndex--;
            return true;
        }
        if (CurrentInput.Down && _currentSortIndex < _sortBindings.Count - 1)
        {
            _currentSortIndex++;
            return true;
        }

        return false;
    }

    private bool HandleExitButtonNavigation()
    {
        if (CurrentInput.Up)
        {
            if (_entryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = Math.Min(_currentEntryIndex, _entryBindings.Count - 1);
                return true;
            }
            if (_actionBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.ActionButtons;
                _currentActionIndex = 0;
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Action Handling

    protected override void HandleAction(object menuState)
    {
        if (CurrentInput.ActionPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                // For entry buttons, simulate a click to select the entry
                if (binding.Element is UIElement buttonElement)
                {
                    Mod.Logger.Info($"[{SystemLogName}] Clicking: {binding.Label}");
                    global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();

                    try
                    {
                        CalculatedStyle dims = buttonElement.GetDimensions();
                        Vector2 clickPos = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);

                        // Move mouse to button position to trigger hover effects
                        Main.mouseX = (int)clickPos.X;
                        Main.mouseY = (int)clickPos.Y;
                        Main.lastMouseX = Main.mouseX;
                        Main.lastMouseY = Main.mouseY;

                        var clickEvent = new UIMouseEvent(buttonElement, clickPos);
                        global::TerrariaAccess.Common.Services.ProgrammaticUiClickInvoker.LeftClick(buttonElement, clickEvent);

                        Main.mouseLeft = false;
                        Main.mouseLeftRelease = false;
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[{SystemLogName}] Click failed: {ex.Message}");
                    }

                    // For entry buttons, announce selected creature info
                    if (binding.Type == PointType.EntryButton)
                    {
                        LastAnnouncedPointId = -1;
                        LastSeenPointId = -1;
                    }

                    // For sort/filter buttons, force re-announcement
                    if (binding.Type == PointType.SortButton || binding.Type == PointType.FilterButton)
                    {
                        LastAnnouncedPointId = -1;
                        LastSeenPointId = -1;
                    }
                }
                else
                {
                    // For snap-point based buttons without element refs, use UILinkPointNavigator
                    Mod.Logger.Info($"[{SystemLogName}] Activating via snap point: {binding.Label}");
                    global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
                    global::TerrariaAccess.Common.Services.NativeSoundSuppression.RequestDeferredSuppressionForCurrentFrame();

                    // Move mouse to the snap point position
                    Main.mouseX = (int)binding.Position.X;
                    Main.mouseY = (int)binding.Position.Y;
                    Main.lastMouseX = Main.mouseX;
                    Main.lastMouseY = Main.mouseY;
                    Main.mouseLeft = true;
                    Main.mouseLeftRelease = true;
                }
            }
        }

        if (CurrentInput.BackPressed)
        {
            // If in sort/filter overlay, close it by clicking the sort/filter button again
            if (_currentRegion == NavigationRegion.SortGrid || _currentRegion == NavigationRegion.FilterGrid)
            {
                Mod.Logger.Info($"[{SystemLogName}] B button pressed in overlay, returning to grid");
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = Math.Min(_currentEntryIndex, Math.Max(0, _entryBindings.Count - 1));
                LastAnnouncedPointId = -1;
                LastSeenPointId = -1;
                return;
            }

            // Otherwise click the exit button
            if (_exitBinding?.Element is UIElement exitButton)
            {
                Mod.Logger.Info($"[{SystemLogName}] B button pressed, clicking Back");
                global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();

                try
                {
                    var clickEvent = new UIMouseEvent(exitButton, Main.MouseScreen);
                    global::TerrariaAccess.Common.Services.ProgrammaticUiClickInvoker.LeftClick(exitButton, clickEvent);
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warn($"[{SystemLogName}] Back click failed: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Announcement

    protected override int? GetCurrentPointId()
    {
        switch (_currentRegion)
        {
            case NavigationRegion.NavButtons:
                if (_navBindings.Count == 0 || _currentNavIndex < 0 || _currentNavIndex >= _navBindings.Count)
                    return null;
                return _navBindings[_currentNavIndex].Id;

            case NavigationRegion.ActionButtons:
                if (_actionBindings.Count == 0 || _currentActionIndex < 0 || _currentActionIndex >= _actionBindings.Count)
                    return null;
                return _actionBindings[_currentActionIndex].Id;

            case NavigationRegion.EntryGrid:
                if (_entryBindings.Count == 0 || _currentEntryIndex < 0 || _currentEntryIndex >= _entryBindings.Count)
                    return null;
                return _entryBindings[_currentEntryIndex].Binding.Id;

            case NavigationRegion.FilterGrid:
                if (_filterBindings.Count == 0 || _currentFilterIndex < 0 || _currentFilterIndex >= _filterBindings.Count)
                    return null;
                return _filterBindings[_currentFilterIndex].Id;

            case NavigationRegion.SortGrid:
                if (_sortBindings.Count == 0 || _currentSortIndex < 0 || _currentSortIndex >= _sortBindings.Count)
                    return null;
                return _sortBindings[_currentSortIndex].Id;

            case NavigationRegion.ExitButton:
                return _exitBinding?.Id;

            default:
                return null;
        }
    }

    protected override string BuildAnnouncement(PointBinding binding, object menuState)
    {
        bool isFirstAnnouncement = !ScreenReaderService.WasContextAnnounced(ContextKeyScreen);
        string header = string.Empty;

        if (isFirstAnnouncement)
        {
            ScreenReaderService.MarkContextAnnounced(ContextKeyScreen);

            string progressText = GetProgressText(menuState);
            header = !string.IsNullOrEmpty(progressText)
                ? $"Bestiary, {progressText}. Press Tab to search. "
                : "Bestiary. Press Tab to search. ";
        }

        switch (_currentRegion)
        {
            case NavigationRegion.NavButtons:
                return BuildNavButtonAnnouncement(header);
            case NavigationRegion.ActionButtons:
                return BuildActionButtonAnnouncement(header);
            case NavigationRegion.EntryGrid:
                return BuildEntryAnnouncement(header, menuState);
            case NavigationRegion.FilterGrid:
                return BuildFilterAnnouncement(header);
            case NavigationRegion.SortGrid:
                return BuildSortAnnouncement(header);
            case NavigationRegion.ExitButton:
                return BuildExitButtonAnnouncement(header);
            default:
                return string.Empty;
        }
    }

    private string BuildNavButtonAnnouncement(string header)
    {
        if (_currentNavIndex < 0 || _currentNavIndex >= _navBindings.Count)
        {
            return string.Empty;
        }

        var binding = _navBindings[_currentNavIndex];
        string label = TextSanitizer.Clean(binding.Label);

        // Add page range text
        string rangeText = GetRangeText();
        if (!string.IsNullOrEmpty(rangeText))
        {
            label = $"{label}, {rangeText}";
        }

        return $"{header}{label}";
    }

    private string BuildActionButtonAnnouncement(string header)
    {
        if (_currentActionIndex < 0 || _currentActionIndex >= _actionBindings.Count)
        {
            return string.Empty;
        }

        var binding = _actionBindings[_currentActionIndex];
        string label = TextSanitizer.Clean(binding.Label);
        int position = _currentActionIndex + 1;
        int total = _actionBindings.Count;

        return $"{header}{label}, button {position} of {total}";
    }

    private string BuildEntryAnnouncement(string header, object menuState)
    {
        if (_currentEntryIndex < 0 || _currentEntryIndex >= _entryBindings.Count)
        {
            return string.Empty;
        }

        var entry = _entryBindings[_currentEntryIndex];
        string name = TextSanitizer.Clean(entry.Name);

        var parts = new List<string>();
        parts.Add(name);

        if (!entry.IsUnlocked)
        {
            parts.Add("Undiscovered");
        }

        // Try to get selected entry details if this entry is selected
        string details = GetSelectedEntryDetails(menuState, entry);
        if (!string.IsNullOrEmpty(details))
        {
            parts.Add(details);
        }

        int absolutePos = entry.AbsoluteIndex + 1;
        int totalEntries = entry.TotalEntries;
        if (totalEntries > 0)
        {
            parts.Add($"entry {absolutePos} of {totalEntries}");
        }

        return $"{header}{string.Join(", ", parts)}";
    }

    private string BuildFilterAnnouncement(string header)
    {
        if (_currentFilterIndex < 0 || _currentFilterIndex >= _filterBindings.Count)
        {
            return string.Empty;
        }

        var binding = _filterBindings[_currentFilterIndex];
        string label = TextSanitizer.Clean(binding.Label);
        int position = _currentFilterIndex + 1;
        int total = _filterBindings.Count;

        return $"{header}{label}, filter {position} of {total}";
    }

    private string BuildSortAnnouncement(string header)
    {
        if (_currentSortIndex < 0 || _currentSortIndex >= _sortBindings.Count)
        {
            return string.Empty;
        }

        var binding = _sortBindings[_currentSortIndex];
        string label = TextSanitizer.Clean(binding.Label);
        int position = _currentSortIndex + 1;
        int total = _sortBindings.Count;

        return $"{header}{label}, sort {position} of {total}";
    }

    private string BuildExitButtonAnnouncement(string header)
    {
        if (!_exitBinding.HasValue)
        {
            return string.Empty;
        }

        string label = TextSanitizer.Clean(_exitBinding.Value.Label);
        return $"{header}{label}";
    }

    #endregion

    #region Data Extraction Helpers

    private string GetEntryButtonName(UIElement entryButton)
    {
        try
        {
            // Get the UIBestiaryEntryIcon from the button, then call GetHoverText
            FieldInfo? iconField = BestiaryReflectionCache.GetEntryButtonIconField(entryButton.GetType());
            object? icon = iconField?.GetValue(entryButton);
            if (icon is not null)
            {
                MethodInfo? getHoverText = BestiaryReflectionCache.GetIconHoverTextMethod(icon.GetType());
                if (getHoverText is not null)
                {
                    object? hoverText = getHoverText.Invoke(icon, null);
                    if (hoverText is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            // Fallback: try Entry property
            PropertyInfo? entryProp = BestiaryReflectionCache.GetEntryButtonEntryProperty(entryButton.GetType());
            object? bestiaryEntry = entryProp?.GetValue(entryButton);
            if (bestiaryEntry is BestiaryEntry be)
            {
                return GetEntryName(be) ?? "???";
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting entry name: {ex.Message}");
        }

        return "???";
    }

    private string? GetEntryName(BestiaryEntry entry)
    {
        try
        {
            foreach (var info in entry.Info)
            {
                if (ReflectionCache.BestiaryInfoElements.NamePlateType is not null &&
                    ReflectionCache.BestiaryInfoElements.NamePlateType.IsInstanceOfType(info))
                {
                    string? key = ReflectionCache.BestiaryInfoElements.NamePlateKey?.GetValue(info) as string;
                    if (!string.IsNullOrEmpty(key))
                    {
                        return Language.GetTextValue(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting entry name from info: {ex.Message}");
        }

        return null;
    }

    private string GetSelectedEntryDetails(object menuState, EntryBinding entry)
    {
        try
        {
            // Check if this entry's button is the currently selected one
            object? selectedButton = ReflectionCache.UIBestiaryTest.SelectedEntryButton?.GetValue(menuState);
            if (selectedButton is null || !ReferenceEquals(selectedButton, entry.EntryButton))
            {
                return string.Empty;
            }

            if (!entry.IsUnlocked)
            {
                return string.Empty;
            }

            // Get BestiaryEntry from the button
            PropertyInfo? entryProp = BestiaryReflectionCache.GetEntryButtonEntryProperty(entry.EntryButton.GetType());
            object? bestiaryEntry = entryProp?.GetValue(entry.EntryButton);
            if (bestiaryEntry is not BestiaryEntry be)
            {
                return string.Empty;
            }

            var detailParts = new List<string>();

            foreach (var info in be.Info)
            {
                // Stats
                if (ReflectionCache.BestiaryInfoElements.StatsType is not null &&
                    ReflectionCache.BestiaryInfoElements.StatsType.IsInstanceOfType(info))
                {
                    int? hp = ReflectionCache.BestiaryInfoElements.StatsLifeMax?.GetValue(info) as int?;
                    int? attack = ReflectionCache.BestiaryInfoElements.StatsDamage?.GetValue(info) as int?;
                    int? defense = ReflectionCache.BestiaryInfoElements.StatsDefense?.GetValue(info) as int?;

                    if (hp.HasValue) detailParts.Add($"HP {hp.Value}");
                    if (attack.HasValue) detailParts.Add($"Attack {attack.Value}");
                    if (defense.HasValue) detailParts.Add($"Defense {defense.Value}");

                    float? knockback = ReflectionCache.BestiaryInfoElements.StatsKnockbackResist?.GetValue(info) as float?;
                    if (knockback.HasValue) detailParts.Add($"Knockback resist {knockback.Value:P0}");

                    float? coinValue = ReflectionCache.BestiaryInfoElements.StatsMonetaryValue?.GetValue(info) as float?;
                    if (coinValue.HasValue && coinValue.Value > 0f)
                    {
                        detailParts.Add($"Worth {FormatCoinValue((long)coinValue.Value)}");
                    }
                }

                // Flavor text
                if (ReflectionCache.BestiaryInfoElements.FlavorTextType is not null &&
                    ReflectionCache.BestiaryInfoElements.FlavorTextType.IsInstanceOfType(info))
                {
                    string? key = ReflectionCache.BestiaryInfoElements.FlavorTextKey?.GetValue(info) as string;
                    if (!string.IsNullOrEmpty(key))
                    {
                        string flavorText = Language.GetTextValue(key);
                        if (!string.IsNullOrWhiteSpace(flavorText) && flavorText != key)
                        {
                            detailParts.Add(flavorText.TrimEnd('.'));
                        }
                    }
                }

                // Item drops
                if (ReflectionCache.BestiaryInfoElements.ItemDropType is not null &&
                    ReflectionCache.BestiaryInfoElements.ItemDropType.IsInstanceOfType(info))
                {
                    string? dropText = GetItemDropText(info);
                    if (!string.IsNullOrEmpty(dropText))
                    {
                        detailParts.Add(dropText);
                    }
                }

                // Kill count
                if (ReflectionCache.BestiaryInfoElements.KillCounterType is not null &&
                    ReflectionCache.BestiaryInfoElements.KillCounterType.IsInstanceOfType(info))
                {
                    string? killText = GetKillCountText(info);
                    if (!string.IsNullOrEmpty(killText))
                    {
                        detailParts.Add(killText);
                    }
                }
            }

            return string.Join(", ", detailParts);
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting entry details: {ex.Message}");
            return string.Empty;
        }
    }

    private string? GetItemDropText(object dropInfo)
    {
        try
        {
            object? dropRateInfo = ReflectionCache.BestiaryInfoElements.ItemDropDropRateInfo?.GetValue(dropInfo);
            if (dropRateInfo is null)
            {
                return null;
            }

            // DropRateInfo is a struct with public fields: itemId, dropRate, stackMin, stackMax
            var drType = dropRateInfo.GetType();
            FieldInfo? itemIdField = BestiaryReflectionCache.GetDropRateInfoItemIdField(drType);
            FieldInfo? dropRateField = BestiaryReflectionCache.GetDropRateInfoDropRateField(drType);
            FieldInfo? stackMinField = BestiaryReflectionCache.GetDropRateInfoStackMinField(drType);
            FieldInfo? stackMaxField = BestiaryReflectionCache.GetDropRateInfoStackMaxField(drType);

            int? itemId = itemIdField?.GetValue(dropRateInfo) as int?;
            float? dropRate = dropRateField?.GetValue(dropRateInfo) as float?;

            if (!itemId.HasValue || !dropRate.HasValue)
            {
                Mod.Logger.Debug($"[{SystemLogName}] Drop info missing fields. Available: {string.Join(", ", Array.ConvertAll(drType.GetFields(BindingFlags.Public | BindingFlags.Instance), f => $"{f.Name}:{f.FieldType.Name}"))}");
                return null;
            }

            string itemName = Lang.GetItemNameValue(itemId.Value);
            if (string.IsNullOrEmpty(itemName))
                itemName = $"Item {itemId.Value}";

            string rateText;
            if (dropRate.Value >= 1f)
                rateText = "100%";
            else if (dropRate.Value <= 0f)
                rateText = "0%";
            else
                rateText = $"{dropRate.Value:P2}".TrimEnd('0').TrimEnd('.');

            int? stackMin = stackMinField?.GetValue(dropRateInfo) as int?;
            int? stackMax = stackMaxField?.GetValue(dropRateInfo) as int?;

            string stackText = string.Empty;
            if (stackMin.HasValue && stackMax.HasValue && (stackMin.Value > 1 || stackMax.Value > 1))
            {
                stackText = stackMin.Value == stackMax.Value
                    ? $" x{stackMin.Value}"
                    : $" x{stackMin.Value}-{stackMax.Value}";
            }

            return $"Drops {itemName}{stackText} ({rateText})";
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting drop info: {ex.Message}");
        }
        return null;
    }

    private string? GetKillCountText(object killInfo)
    {
        try
        {
            int? npcNetId = ReflectionCache.BestiaryInfoElements.KillCounterNpcId?.GetValue(killInfo) as int?;
            if (npcNetId.HasValue)
            {
                // Convert net ID to NPC type, then to banner index for kill count lookup
                int npcType = npcNetId.Value < 0 ? NPCID.FromNetId(npcNetId.Value) : npcNetId.Value;
                int bannerId = Item.NPCtoBanner(npcType);
                if (bannerId > 0 && bannerId < NPC.killCount.Length)
                {
                    int killCount = NPC.killCount[bannerId];
                    if (killCount > 0)
                        return $"Killed {killCount} times";
                    else
                        return "Never killed";
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting kill count: {ex.Message}");
        }
        return null;
    }

    private static string FormatCoinValue(long copper)
    {
        long platinum = copper / 1000000;
        copper %= 1000000;
        long gold = copper / 10000;
        copper %= 10000;
        long silver = copper / 100;
        copper %= 100;

        var parts = new List<string>();
        if (platinum > 0) parts.Add($"{platinum} platinum");
        if (gold > 0) parts.Add($"{gold} gold");
        if (silver > 0) parts.Add($"{silver} silver");
        if (copper > 0) parts.Add($"{copper} copper");

        return parts.Count > 0 ? string.Join(" ", parts) : "nothing";
    }

    private string GetProgressText(object menuState)
    {
        try
        {
            object? progressReport = ReflectionCache.UIBestiaryTest.ProgressReport?.GetValue(menuState);
            if (progressReport is null)
            {
                return string.Empty;
            }

            PropertyInfo? completionProp = BestiaryReflectionCache.GetProgressReportCompletionProperty(progressReport.GetType());
            if (completionProp?.GetValue(progressReport) is float percent)
            {
                return $"{percent:P0} complete";
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting progress: {ex.Message}");
        }

        return string.Empty;
    }

    private int GetCurrentPageOffset(object menuState)
    {
        try
        {
            object? entryGrid = ReflectionCache.UIBestiaryTest.EntryGrid?.GetValue(menuState);
            if (entryGrid is null) return -1;

            if (ReflectionCache.UIBestiaryEntryGrid.AtEntryIndex?.GetValue(entryGrid) is int offset)
            {
                return offset;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting page offset: {ex.Message}");
        }
        return -1;
    }

    private string GetRangeText()
    {
        try
        {
            object? menuState = GetActiveMenuState();
            if (menuState is null) return string.Empty;

            object? rangeText = ReflectionCache.UIBestiaryTest.IndexesRangeText?.GetValue(menuState);
            if (rangeText is not null)
            {
                PropertyInfo? textProp = BestiaryReflectionCache.GetTextProperty(rangeText.GetType());
                if (textProp?.GetValue(rangeText) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch
        {
            // Best effort
        }

        return string.Empty;
    }

    private string? GetSortText(object menuState)
    {
        try
        {
            object? sortText = ReflectionCache.UIBestiaryTest.SortingText?.GetValue(menuState);
            if (sortText is not null)
            {
                PropertyInfo? textProp = BestiaryReflectionCache.GetTextProperty(sortText.GetType());
                if (textProp?.GetValue(sortText) is string text)
                {
                    return TextSanitizer.Clean(text);
                }
            }
        }
        catch
        {
            // Best effort
        }

        return null;
    }

    private string? GetFilterText(object menuState)
    {
        try
        {
            object? filterText = ReflectionCache.UIBestiaryTest.FilteringText?.GetValue(menuState);
            if (filterText is not null)
            {
                PropertyInfo? textProp = BestiaryReflectionCache.GetTextProperty(filterText.GetType());
                if (textProp?.GetValue(filterText) is string text)
                {
                    return TextSanitizer.Clean(text);
                }
            }
        }
        catch
        {
            // Best effort
        }

        return null;
    }

    private string? GetFilterButtonLabel(UIElement? button)
    {
        if (button is null) return null;

        try
        {
            // GroupOptionButton has a _title field or GetInnerDimensions text
            PropertyInfo? hoverTextProp = BestiaryReflectionCache.GetHoverTextProperty(button.GetType());
            if (hoverTextProp?.GetValue(button) is string hoverText && !string.IsNullOrWhiteSpace(hoverText))
            {
                // Check if the filter is active
                bool isOn = false;
                PropertyInfo? isOnProp = BestiaryReflectionCache.GetIsOnProperty(button.GetType());
                if (isOnProp?.GetValue(button) is bool on)
                {
                    isOn = on;
                }

                return isOn ? $"{hoverText}, Active" : hoverText;
            }

            // Fallback: try _title field
            FieldInfo? titleField = BestiaryReflectionCache.GetTitleField(button.GetType());
            if (titleField?.GetValue(button) is string title && !string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // Best effort
        }

        return null;
    }

    private string? GetSortButtonLabel(UIElement? button)
    {
        if (button is null) return null;

        try
        {
            PropertyInfo? hoverTextProp = BestiaryReflectionCache.GetHoverTextProperty(button.GetType());
            if (hoverTextProp?.GetValue(button) is string hoverText && !string.IsNullOrWhiteSpace(hoverText))
            {
                return hoverText;
            }

            FieldInfo? titleField = BestiaryReflectionCache.GetTitleField(button.GetType());
            if (titleField?.GetValue(button) is string title && !string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // Best effort
        }

        return null;
    }

    #endregion

    #region Snap Point Helpers

    private static List<SnapPointInfo> GetSnapPoints(UIElement element)
    {
        var results = new List<SnapPointInfo>();

        try
        {
            MethodInfo? getSnapPoints = BestiaryReflectionCache.GetSnapPointsMethod(element.GetType());

            if (getSnapPoints is null)
            {
                return results;
            }

            object? snapPointList = getSnapPoints.Invoke(element, null);
            if (snapPointList is IList list)
            {
                foreach (object? sp in list)
                {
                    if (sp is not SnapPoint snapPoint)
                    {
                        continue;
                    }

                    results.Add(new SnapPointInfo
                    {
                        Name = snapPoint.Name,
                        Id = snapPoint.Id,
                        Position = snapPoint.Position,
                        Element = GetSnapPointElement(snapPoint)
                    });
                }
            }
        }
        catch
        {
            // Best effort
        }

        return results;
    }

    private static UIElement? GetSnapPointElement(SnapPoint snapPoint)
    {
        try
        {
            // SnapPoint has a reference to its parent UIElement
            FieldInfo? elementField = BestiaryReflectionCache.SnapPointElementField;
            return elementField?.GetValue(snapPoint) as UIElement;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Helper Types

    private sealed class EntryBinding
    {
        public PointBinding Binding { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsUnlocked { get; init; }
        public int AbsoluteIndex { get; init; }
        public int TotalEntries { get; init; }
        public int GridIndex { get; init; }
        public UIElement EntryButton { get; init; } = null!;
    }

    private struct SnapPointInfo
    {
        public string Name;
        public int Id;
        public Vector2 Position;
        public UIElement? Element;
    }

    private static class BestiaryReflectionCache
    {
        private static readonly Dictionary<Type, MethodInfo?> EntryGridGetEntriesToShowMethods = new();
        private static readonly Dictionary<Type, PropertyInfo?> IsOnProperties = new();
        private static readonly Dictionary<Type, FieldInfo?> EntryButtonIconFields = new();
        private static readonly Dictionary<Type, MethodInfo?> IconHoverTextMethods = new();
        private static readonly Dictionary<Type, PropertyInfo?> EntryButtonEntryProperties = new();
        private static readonly Dictionary<Type, FieldInfo?> DropRateInfoItemIdFields = new();
        private static readonly Dictionary<Type, FieldInfo?> DropRateInfoDropRateFields = new();
        private static readonly Dictionary<Type, FieldInfo?> DropRateInfoStackMinFields = new();
        private static readonly Dictionary<Type, FieldInfo?> DropRateInfoStackMaxFields = new();
        private static readonly Dictionary<Type, PropertyInfo?> CompletionPercentProperties = new();
        private static readonly Dictionary<Type, PropertyInfo?> TextProperties = new();
        private static readonly Dictionary<Type, PropertyInfo?> HoverTextProperties = new();
        private static readonly Dictionary<Type, FieldInfo?> TitleFields = new();
        private static readonly Dictionary<Type, MethodInfo?> SnapPointsMethods = new();

        internal static readonly FieldInfo? SnapPointElementField =
            typeof(SnapPoint).GetField("_element", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static MethodInfo? GetEntryGridGetEntriesToShowMethod(Type type) =>
            GetCachedMember(EntryGridGetEntriesToShowMethods, type,
                static t => t.GetMethod("GetEntriesToShow", BindingFlags.Public | BindingFlags.Instance));

        internal static PropertyInfo? GetIsOnProperty(Type type) =>
            GetCachedMember(IsOnProperties, type,
                static t => t.GetProperty("IsOn", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetEntryButtonIconField(Type type) =>
            GetCachedMember(EntryButtonIconFields, type,
                static t => t.GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic));

        internal static MethodInfo? GetIconHoverTextMethod(Type type) =>
            GetCachedMember(IconHoverTextMethods, type,
                static t => t.GetMethod("GetHoverText", BindingFlags.Public | BindingFlags.Instance));

        internal static PropertyInfo? GetEntryButtonEntryProperty(Type type) =>
            GetCachedMember(EntryButtonEntryProperties, type,
                static t => t.GetProperty("Entry", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetDropRateInfoItemIdField(Type type) =>
            GetCachedMember(DropRateInfoItemIdFields, type,
                static t => t.GetField("itemId", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetDropRateInfoDropRateField(Type type) =>
            GetCachedMember(DropRateInfoDropRateFields, type,
                static t => t.GetField("dropRate", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetDropRateInfoStackMinField(Type type) =>
            GetCachedMember(DropRateInfoStackMinFields, type,
                static t => t.GetField("stackMin", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetDropRateInfoStackMaxField(Type type) =>
            GetCachedMember(DropRateInfoStackMaxFields, type,
                static t => t.GetField("stackMax", BindingFlags.Public | BindingFlags.Instance));

        internal static PropertyInfo? GetProgressReportCompletionProperty(Type type) =>
            GetCachedMember(CompletionPercentProperties, type,
                static t => t.GetProperty("CompletionPercent", BindingFlags.Public | BindingFlags.Instance));

        internal static PropertyInfo? GetTextProperty(Type type) =>
            GetCachedMember(TextProperties, type,
                static t => t.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance));

        internal static PropertyInfo? GetHoverTextProperty(Type type) =>
            GetCachedMember(HoverTextProperties, type,
                static t => t.GetProperty("HoverText", BindingFlags.Public | BindingFlags.Instance));

        internal static FieldInfo? GetTitleField(Type type) =>
            GetCachedMember(TitleFields, type,
                static t => t.GetField("_title", BindingFlags.Instance | BindingFlags.NonPublic));

        internal static MethodInfo? GetSnapPointsMethod(Type type) =>
            GetCachedMember(SnapPointsMethods, type,
                static t => t.GetMethod("GetSnapPoints", BindingFlags.Public | BindingFlags.Instance));

        private static TMember? GetCachedMember<TMember>(
            Dictionary<Type, TMember?> cache,
            Type type,
            Func<Type, TMember?> factory)
            where TMember : MemberInfo
        {
            if (!cache.TryGetValue(type, out TMember? member))
            {
                member = factory(type);
                cache[type] = member;
            }

            return member;
        }
    }

    #endregion
}
