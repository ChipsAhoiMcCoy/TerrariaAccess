#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.ModBrowser;
using ScreenReaderMod.Common.Systems.ModMenuAccessibility;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides full keyboard/gamepad navigation and screen reader announcements for the Bestiary menu.
/// Works from both the title menu and in-game pause menu by hooking UIBestiaryTest.Draw directly.
/// </summary>
public sealed class BestiaryAccessibilitySystem : ModSystem
{
    #region Constants

    private const int BaseLinkId = LinkIdRegistry.Bestiary;
    private const string SystemLogName = "Bestiary";
    private const string BestiaryMenuTypeName = "Terraria.GameContent.UI.States.UIBestiaryTest";
    private const string ContextKeyScreen = "bestiary:screen";

    #endregion

    #region Hook Delegates & Fields

    private delegate void DrawDelegate(UIState self, SpriteBatch spriteBatch);
    private delegate void OnActivateDelegate(UIState self);

    private static Hook? _drawHook;
    private static Hook? _onActivateHook;

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
    private readonly Dictionary<int, PointBinding> _bindingById = new();

    // Stability tracking
    private int _lastAnnouncedPointId = -1;
    private int _lastSeenPointId = -1;
    private object? _lastMenuState;
    private int _initialFocusFramesRemaining;

    // Overlay state tracking
    private bool _wasSortOverlayOpen;
    private bool _wasFilterOverlayOpen;

    #endregion

    #region Input State

    private bool _leftWasPressed;
    private bool _rightWasPressed;
    private bool _upWasPressed;
    private bool _downWasPressed;

    private bool _stickLeftWasPressed;
    private bool _stickRightWasPressed;
    private bool _stickUpWasPressed;
    private bool _stickDownWasPressed;

    private bool _aButtonWasPressed;
    private bool _bButtonWasPressed;

    private bool _keyLeftWasPressed;
    private bool _keyRightWasPressed;
    private bool _keyUpWasPressed;
    private bool _keyDownWasPressed;
    private bool _keyEnterWasPressed;
    private bool _keySpaceWasPressed;

    private NavigationInput _currentInput;

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

    private static object? GetActiveMenuState()
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

        Mod.Logger.Info($"[{SystemLogName}] Loading accessibility system");

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

        MethodInfo? onActivateMethod = bestiaryMenuType.GetMethod(
            "OnActivate",
            BindingFlags.Instance | BindingFlags.Public);
        if (onActivateMethod is not null)
        {
            _onActivateHook = new Hook(onActivateMethod, OnActivate);
        }
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        _drawHook?.Dispose();
        _drawHook = null;
        _onActivateHook?.Dispose();
        _onActivateHook = null;

        _navBindings.Clear();
        _actionBindings.Clear();
        _entryBindings.Clear();
        _filterBindings.Clear();
        _sortBindings.Clear();
        _exitBinding = null;
        _bindingById.Clear();
        _lastMenuState = null;
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _initialFocusFramesRemaining = 0;

        ScreenReaderService.ClearContexts("bestiary:");

        ResetInputState();
        _instance = null;
    }

    #endregion

    #region Hooks

    private static void OnActivate(OnActivateDelegate orig, UIState self)
    {
        orig(self);

        if (_instance is null)
        {
            return;
        }

        _instance._lastMenuState = null;
        _instance._lastAnnouncedPointId = -1;
        _instance._lastSeenPointId = -1;
        _instance._initialFocusFramesRemaining = 15;
        _instance._currentRegion = NavigationRegion.EntryGrid;
        _instance._currentEntryIndex = 0;
        _instance._currentNavIndex = 0;
        _instance._currentActionIndex = 0;
        _instance._currentFilterIndex = 0;
        _instance._currentSortIndex = 0;
        _instance._wasSortOverlayOpen = false;
        _instance._wasFilterOverlayOpen = false;

        _instance._aButtonWasPressed = true;
        _instance._bButtonWasPressed = true;

        ScreenReaderService.ClearContexts("bestiary:");

        _instance.Mod.Logger.Info($"[{SystemLogName}] Menu activated");
    }

    private static void OnDraw(DrawDelegate orig, UIState self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (_instance is null)
        {
            return;
        }

        _instance.ProcessMenu(self);
    }

    #endregion

    #region Menu Processing

    private void ProcessMenu(object menuState)
    {
        bool menuChanged = !ReferenceEquals(menuState, _lastMenuState);
        if (menuChanged)
        {
            _lastMenuState = menuState;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            _initialFocusFramesRemaining = 15;
            _currentEntryIndex = 0;
            _currentNavIndex = 0;
            _currentActionIndex = 0;
            _currentFilterIndex = 0;
            _currentSortIndex = 0;
            _currentRegion = NavigationRegion.EntryGrid;
            _wasSortOverlayOpen = false;
            _wasFilterOverlayOpen = false;

            _aButtonWasPressed = true;
            _bButtonWasPressed = true;

            ScreenReaderService.ClearContexts("bestiary:");
            Mod.Logger.Info($"[{SystemLogName}] Entered menu");
        }

        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        if (!hasGamepadInput && !ShouldProcessKeyboardInput())
        {
            return;
        }

        try
        {
            bool wasSearchActive = SearchModeManager.IsSearchModeActive;
            SearchModeManager.Update();
            bool isSearchActive = SearchModeManager.IsSearchModeActive;

            if (isSearchActive && !wasSearchActive)
            {
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
            }

            UpdateInputState();
            ConfigureGamepadPoints(menuState);

            // Detect overlay state transitions
            bool isSortOpen = IsSortOverlayOpen(menuState);
            bool isFilterOpen = IsFilterOverlayOpen(menuState);

            if (isSortOpen && !_wasSortOverlayOpen)
            {
                _currentRegion = NavigationRegion.SortGrid;
                _currentSortIndex = 0;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                ScreenReaderService.EnqueuePrefix("Sort options.");
                Mod.Logger.Info($"[{SystemLogName}] Sort overlay opened");
            }
            else if (!isSortOpen && _wasSortOverlayOpen)
            {
                _currentRegion = NavigationRegion.ActionButtons;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                Mod.Logger.Info($"[{SystemLogName}] Sort overlay closed");
            }

            if (isFilterOpen && !_wasFilterOverlayOpen)
            {
                _currentRegion = NavigationRegion.FilterGrid;
                _currentFilterIndex = 0;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                ScreenReaderService.EnqueuePrefix("Filter options.");
                Mod.Logger.Info($"[{SystemLogName}] Filter overlay opened");
            }
            else if (!isFilterOpen && _wasFilterOverlayOpen)
            {
                _currentRegion = NavigationRegion.ActionButtons;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                Mod.Logger.Info($"[{SystemLogName}] Filter overlay closed");
            }

            _wasSortOverlayOpen = isSortOpen;
            _wasFilterOverlayOpen = isFilterOpen;

            HandleNavigation(menuState);
            HandleAction(menuState);
            AnnounceCurrentFocus(menuState);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error processing menu: {ex}");
        }
    }

    private bool ShouldProcessKeyboardInput()
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

    #region Input Handling

    private void UpdateInputState()
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        KeyboardState kbState = Main.keyState;

        var input = new NavigationInput();

        if (gpState.IsConnected)
        {
            input.Left = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
            input.Right = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
            input.Up = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
            input.Down = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

            _leftWasPressed = gpState.DPad.Left == ButtonState.Pressed;
            _rightWasPressed = gpState.DPad.Right == ButtonState.Pressed;
            _upWasPressed = gpState.DPad.Up == ButtonState.Pressed;
            _downWasPressed = gpState.DPad.Down == ButtonState.Pressed;

            Vector2 stick = gpState.ThumbSticks.Left;
            const float threshold = 0.5f;

            bool stickLeftNow = stick.X < -threshold;
            bool stickRightNow = stick.X > threshold;
            bool stickUpNow = stick.Y > threshold;
            bool stickDownNow = stick.Y < -threshold;

            if (!input.Left && stickLeftNow && !_stickLeftWasPressed) input.Left = true;
            if (!input.Right && stickRightNow && !_stickRightWasPressed) input.Right = true;
            if (!input.Up && stickUpNow && !_stickUpWasPressed) input.Up = true;
            if (!input.Down && stickDownNow && !_stickDownWasPressed) input.Down = true;

            _stickLeftWasPressed = stickLeftNow;
            _stickRightWasPressed = stickRightNow;
            _stickUpWasPressed = stickUpNow;
            _stickDownWasPressed = stickDownNow;

            bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
            input.ActionPressed = aPressed && !_aButtonWasPressed;
            _aButtonWasPressed = aPressed;

            bool bPressed = gpState.Buttons.B == ButtonState.Pressed;
            input.BackPressed = bPressed && !_bButtonWasPressed;
            _bButtonWasPressed = bPressed;
        }

        if (ShouldProcessKeyboardInput())
        {
            bool keyLeftNow = kbState.IsKeyDown(Keys.Left) || kbState.IsKeyDown(Keys.A);
            bool keyRightNow = kbState.IsKeyDown(Keys.Right) || kbState.IsKeyDown(Keys.D);
            bool keyUpNow = kbState.IsKeyDown(Keys.Up) || kbState.IsKeyDown(Keys.W);
            bool keyDownNow = kbState.IsKeyDown(Keys.Down) || kbState.IsKeyDown(Keys.S);

            if (!input.Left && keyLeftNow && !_keyLeftWasPressed) input.Left = true;
            if (!input.Right && keyRightNow && !_keyRightWasPressed) input.Right = true;
            if (!input.Up && keyUpNow && !_keyUpWasPressed) input.Up = true;
            if (!input.Down && keyDownNow && !_keyDownWasPressed) input.Down = true;

            _keyLeftWasPressed = keyLeftNow;
            _keyRightWasPressed = keyRightNow;
            _keyUpWasPressed = keyUpNow;
            _keyDownWasPressed = keyDownNow;

            bool enterNow = kbState.IsKeyDown(Keys.Enter);
            bool spaceNow = kbState.IsKeyDown(Keys.Space);

            if (!input.ActionPressed)
            {
                if ((enterNow && !_keyEnterWasPressed) || (spaceNow && !_keySpaceWasPressed))
                {
                    input.ActionPressed = true;
                }
            }

            _keyEnterWasPressed = enterNow;
            _keySpaceWasPressed = spaceNow;
        }

        input.HasNavigation = input.Left || input.Right || input.Up || input.Down;
        _currentInput = input;
    }

    private void ResetInputState()
    {
        _leftWasPressed = false;
        _rightWasPressed = false;
        _upWasPressed = false;
        _downWasPressed = false;
        _stickLeftWasPressed = false;
        _stickRightWasPressed = false;
        _stickUpWasPressed = false;
        _stickDownWasPressed = false;
        _aButtonWasPressed = false;
        _bButtonWasPressed = false;
        _keyLeftWasPressed = false;
        _keyRightWasPressed = false;
        _keyUpWasPressed = false;
        _keyDownWasPressed = false;
        _keyEnterWasPressed = false;
        _keySpaceWasPressed = false;
    }

    #endregion

    #region Gamepad Point Configuration

    private void ConfigureGamepadPoints(object menuState)
    {
        _bindingById.Clear();
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
        foreach (var binding in _bindingById.Values)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
            linkPoint.Unlink();
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Handle initial focus
        if (_initialFocusFramesRemaining > 0)
        {
            int defaultPointId = _entryBindings.Count > 0
                ? _entryBindings[0].Binding.Id
                : (_exitBinding?.Id ?? BaseLinkId);

            UILinkPointNavigator.ChangePoint(defaultPointId);
            _initialFocusFramesRemaining--;
        }

        // Handle search mode -> navigation mode transition
        if (SearchModeManager.ConsumeFocusFirstModRequest() && _entryBindings.Count > 0)
        {
            _currentRegion = NavigationRegion.EntryGrid;
            _currentEntryIndex = 0;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;

            int pointId = _entryBindings[0].Binding.Id;
            UILinkPointNavigator.ChangePoint(pointId);
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
                _bindingById[binding.Id] = binding;
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
                    _bindingById[binding.Id] = binding;
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
            var getEntries = entryGrid.GetType().GetMethod("GetEntriesToShow",
                BindingFlags.Public | BindingFlags.Instance);
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
                _bindingById[binding.Id] = binding;

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
                _bindingById[binding.Id] = binding;
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
                    var isOnProp = sp.Element?.GetType().GetProperty("IsOn", BindingFlags.Public | BindingFlags.Instance);
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
                _bindingById[binding.Id] = binding;
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
                _bindingById[binding.Id] = binding;
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

    private void HandleNavigation(object menuState)
    {
        if (!_currentInput.HasNavigation)
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

        if (_currentInput.Left && _currentNavIndex > 0)
        {
            _currentNavIndex--;
            return true;
        }
        if (_currentInput.Right && _currentNavIndex < _navBindings.Count - 1)
        {
            _currentNavIndex++;
            return true;
        }
        if (_currentInput.Right && _currentNavIndex >= _navBindings.Count - 1 && _actionBindings.Count > 0)
        {
            _currentRegion = NavigationRegion.ActionButtons;
            _currentActionIndex = 0;
            return true;
        }
        if (_currentInput.Down)
        {
            if (_entryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = 0;
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

        if (_currentInput.Left)
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
        if (_currentInput.Right && _currentActionIndex < _actionBindings.Count - 1)
        {
            _currentActionIndex++;
            return true;
        }
        if (_currentInput.Down)
        {
            if (_entryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = 0;
                return true;
            }
            if (_exitBinding.HasValue)
            {
                _currentRegion = NavigationRegion.ExitButton;
                return true;
            }
        }
        if (_currentInput.Up)
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

        if (_currentInput.Left)
        {
            if (_currentEntryIndex > 0)
            {
                _currentEntryIndex--;
                return true;
            }
        }
        if (_currentInput.Right)
        {
            if (_currentEntryIndex < _entryBindings.Count - 1)
            {
                _currentEntryIndex++;
                return true;
            }
        }
        if (_currentInput.Up)
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
        if (_currentInput.Down)
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

        if (_currentInput.Left && _currentFilterIndex > 0)
        {
            _currentFilterIndex--;
            return true;
        }
        if (_currentInput.Right && _currentFilterIndex < _filterBindings.Count - 1)
        {
            _currentFilterIndex++;
            return true;
        }
        if (_currentInput.Up)
        {
            int newIndex = _currentFilterIndex - filterCols;
            if (newIndex >= 0)
            {
                _currentFilterIndex = newIndex;
                return true;
            }
        }
        if (_currentInput.Down)
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

        if (_currentInput.Up && _currentSortIndex > 0)
        {
            _currentSortIndex--;
            return true;
        }
        if (_currentInput.Down && _currentSortIndex < _sortBindings.Count - 1)
        {
            _currentSortIndex++;
            return true;
        }

        return false;
    }

    private bool HandleExitButtonNavigation()
    {
        if (_currentInput.Up)
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

    private void HandleAction(object menuState)
    {
        if (_currentInput.ActionPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && _bindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                // For entry buttons, simulate a click to select the entry
                if (binding.Element is UIElement buttonElement)
                {
                    Mod.Logger.Info($"[{SystemLogName}] Clicking: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

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
                        buttonElement.LeftClick(clickEvent);

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
                        _lastAnnouncedPointId = -1;
                        _lastSeenPointId = -1;
                    }

                    // For sort/filter buttons, force re-announcement
                    if (binding.Type == PointType.SortButton || binding.Type == PointType.FilterButton)
                    {
                        _lastAnnouncedPointId = -1;
                        _lastSeenPointId = -1;
                    }
                }
                else
                {
                    // For snap-point based buttons without element refs, use UILinkPointNavigator
                    Mod.Logger.Info($"[{SystemLogName}] Activating via snap point: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

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

        if (_currentInput.BackPressed)
        {
            // If in sort/filter overlay, close it by clicking the sort/filter button again
            if (_currentRegion == NavigationRegion.SortGrid || _currentRegion == NavigationRegion.FilterGrid)
            {
                Mod.Logger.Info($"[{SystemLogName}] B button pressed in overlay, returning to grid");
                _currentRegion = NavigationRegion.EntryGrid;
                _currentEntryIndex = Math.Min(_currentEntryIndex, Math.Max(0, _entryBindings.Count - 1));
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                return;
            }

            // Otherwise click the exit button
            if (_exitBinding?.Element is UIElement exitButton)
            {
                Mod.Logger.Info($"[{SystemLogName}] B button pressed, clicking Back");
                SoundEngine.PlaySound(SoundID.MenuTick);

                try
                {
                    var clickEvent = new UIMouseEvent(exitButton, Main.MouseScreen);
                    exitButton.LeftClick(clickEvent);
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

    private int? GetCurrentPointId()
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

    private void AnnounceCurrentFocus(object menuState)
    {
        int? expectedPoint = GetCurrentPointId();
        if (!expectedPoint.HasValue)
        {
            return;
        }

        int currentPoint = expectedPoint.Value;

        if (currentPoint < BaseLinkId)
        {
            return;
        }

        bool isStable = currentPoint == _lastSeenPointId;
        bool alreadyAnnounced = currentPoint == _lastAnnouncedPointId;

        _lastSeenPointId = currentPoint;

        if (!isStable || alreadyAnnounced)
        {
            return;
        }

        string announcement = BuildAnnouncement(menuState);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);
        Mod.Logger.Info($"[{SystemLogName}] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private string BuildAnnouncement(object menuState)
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
            var iconField = entryButton.GetType().GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic);
            object? icon = iconField?.GetValue(entryButton);
            if (icon is not null)
            {
                var getHoverText = icon.GetType().GetMethod("GetHoverText", BindingFlags.Public | BindingFlags.Instance);
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
            var entryProp = entryButton.GetType().GetProperty("Entry", BindingFlags.Public | BindingFlags.Instance);
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
            var entryProp = entry.EntryButton.GetType().GetProperty("Entry", BindingFlags.Public | BindingFlags.Instance);
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
            }

            return string.Join(", ", detailParts);
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error getting entry details: {ex.Message}");
            return string.Empty;
        }
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

            var completionProp = progressReport.GetType().GetProperty("CompletionPercent",
                BindingFlags.Public | BindingFlags.Instance);
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

    private string GetRangeText()
    {
        try
        {
            object? menuState = GetActiveMenuState();
            if (menuState is null) return string.Empty;

            object? rangeText = ReflectionCache.UIBestiaryTest.IndexesRangeText?.GetValue(menuState);
            if (rangeText is not null)
            {
                var textProp = rangeText.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
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
                var textProp = sortText.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
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
                var textProp = filterText.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
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
            var hoverTextProp = button.GetType().GetProperty("HoverText", BindingFlags.Public | BindingFlags.Instance);
            if (hoverTextProp?.GetValue(button) is string hoverText && !string.IsNullOrWhiteSpace(hoverText))
            {
                // Check if the filter is active
                bool isOn = false;
                var isOnProp = button.GetType().GetProperty("IsOn", BindingFlags.Public | BindingFlags.Instance);
                if (isOnProp?.GetValue(button) is bool on)
                {
                    isOn = on;
                }

                return isOn ? $"{hoverText}, Active" : hoverText;
            }

            // Fallback: try _title field
            var titleField = button.GetType().GetField("_title", BindingFlags.Instance | BindingFlags.NonPublic);
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
            var hoverTextProp = button.GetType().GetProperty("HoverText", BindingFlags.Public | BindingFlags.Instance);
            if (hoverTextProp?.GetValue(button) is string hoverText && !string.IsNullOrWhiteSpace(hoverText))
            {
                return hoverText;
            }

            var titleField = button.GetType().GetField("_title", BindingFlags.Instance | BindingFlags.NonPublic);
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
            var getSnapPoints = element.GetType().GetMethod("GetSnapPoints",
                BindingFlags.Public | BindingFlags.Instance);

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
            var elementField = typeof(SnapPoint).GetField("_element", BindingFlags.Instance | BindingFlags.NonPublic);
            return elementField?.GetValue(snapPoint) as UIElement;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Helper Types

    private enum PointType
    {
        NavButton,
        ActionButton,
        EntryButton,
        FilterButton,
        SortButton,
        BackButton
    }

    private readonly record struct PointBinding(
        int Id,
        Vector2 Position,
        string Label,
        string Description,
        UIElement? Element,
        PointType Type
    );

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

    private struct NavigationInput
    {
        public bool Left;
        public bool Right;
        public bool Up;
        public bool Down;
        public bool ActionPressed;
        public bool BackPressed;
        public bool HasNavigation;
    }

    private struct SnapPointInfo
    {
        public string Name;
        public int Id;
        public Vector2 Position;
        public UIElement? Element;
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

    #endregion
}
