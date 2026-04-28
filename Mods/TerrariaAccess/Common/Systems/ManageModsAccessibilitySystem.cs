#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
using Terraria.UI;
using Terraria.UI.Gamepad;
using TerrariaAccess.Common.Systems.ModBrowser;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Manage Mods (UIMods) menu.
/// Uses reflection since UIMods and UIModItem are internal types.
/// </summary>
public sealed class ManageModsAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Base Class Implementation

    protected override int BaseLinkId => LinkIdRegistry.ManageMods;
    protected override string MenuTypeName => "Terraria.ModLoader.UI.UIMods";
    protected override string SystemLogName => "ManageMods";

    #endregion

    #region Menu-Specific State

    // Navigation state tracking
    private static FocusRegion _currentRegion = FocusRegion.ModList;

    // Right stick scroll tracking
    private static int _lastScrollAnnouncedModIndex = -1;
    private static float _lastScrollPosition = -1f;

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

    // Track mod item bindings for toggle operations
    private static readonly List<ModItemBinding> ModItemBindings = new();

    // Track mod item buttons (More Info, Delete, Config) for each mod
    private static readonly List<ModItemButtonGroup> ModItemButtonGroups = new();

    // Current button index within a mod item (0 = mod toggle, 1+ = buttons to the right)
    private static int _currentModButtonIndex;

    // Dialog state tracking
    private static bool _isDialogActive;
    private static int _dialogFocusIndex; // 0 = Yes, 1 = No
    private static readonly List<PointBinding> DialogBindings = new();
    private static int _savedFocusPointBeforeDialog; // Store focus point before dialog opens

    // Track the last known state for each mod to detect changes
    private static readonly Dictionary<object, bool> _lastKnownModStates = new();

    // Cooldown and context keys for speech queue system
    private const string CooldownKeyToggle = "managemods:toggle";
    private const string CooldownKeyDialogAction = "managemods:dialog-action";
    private const string ContextKeyDialogText = "managemods:dialog";

    // Track last dialog announcement to avoid repeats
    private static int _lastDialogAnnouncedIndex = -1;

    #endregion

    #region Public API

    /// <summary>
    /// Returns true if the Manage Mods menu is currently active and handling gamepad input.
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

            // Verify we're still in the UIMods UI state
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null || ReflectionCache.UIMods.Type is null || currentState.GetType() != ReflectionCache.UIMods.Type)
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
        Mod.Logger.Info($"[ManageMods] Load: UIMods type found: {ReflectionCache.UIMods.Type is not null}");
        Mod.Logger.Info($"[ManageMods] Load: UIModItem type found: {ReflectionCache.UIModItem.Type is not null}");
        Mod.Logger.Info($"[ManageMods] Load: LocalMod type found: {ReflectionCache.LocalMod.Type is not null}");

        if (ReflectionCache.UIMods.Type is null)
        {
            Mod.Logger.Warn("[ManageMods] Could not find UIMods type");
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

        if (ReflectionCache.UIMods.Type is not null)
        {
            base.Unload();
        }

        ModItemBindings.Clear();
        ModItemButtonGroups.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();
        DialogBindings.Clear();
        _lastScrollAnnouncedModIndex = -1;
        _lastScrollPosition = -1f;
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
        _isDialogActive = false;
        _dialogFocusIndex = 0;
        _lastDialogAnnouncedIndex = -1;
        _savedFocusPointBeforeDialog = 0;
        _lastKnownModStates.Clear();
        // Clear speech queue state related to manage mods
        ScreenReaderService.ClearContexts("managemods:");
        ScreenReaderService.ClearCooldown(CooldownKeyToggle);
        ScreenReaderService.ClearCooldown(CooldownKeyDialogAction);
    }

    protected override void OnMenuEntered(object menuState)
    {
        _lastScrollAnnouncedModIndex = -1;
        _lastScrollPosition = -1f;
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
    }

    protected override void OnMenuExited()
    {
        FilterBindings.Clear();
        ModBindingsList.Clear();
        ModItemBindings.Clear();
        ModItemButtonGroups.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();
        _currentModButtonIndex = 0;
        // Drop cached states so re-entering the menu rebuilds baselines and
        // doesn't announce stale mod entries that no longer exist.
        _lastKnownModStates.Clear();
        // Clear speech queue state when leaving the menu
        ScreenReaderService.ClearContexts("managemods:");
        ScreenReaderService.ClearCooldown(CooldownKeyToggle);
        ScreenReaderService.ClearCooldown(CooldownKeyDialogAction);
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
        // CRITICAL: Handle dialog state BEFORE native processing
        if (_isDialogActive)
        {
            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;
        }

        // Check if a confirmation dialog is active
        bool dialogActive = IsConfirmDialogActive(menuState);

        if (dialogActive != _isDialogActive)
        {
            HandleDialogStateChange(dialogActive, menuState);
        }

        if (_isDialogActive)
        {
            // CRITICAL: Consume mouse input EVERY FRAME while dialog is active
            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;

            // Handle dialog navigation and action
            ConfigureDialogPoints(menuState);
            return; // Skip normal configuration
        }

        // Normal menu configuration
        BindingById.Clear();
        ModItemBindings.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>();

        // Get mod list and items
        UIList? modList = ReflectionCache.UIMods.ModList?.GetValue(menuState) as UIList;
        IList? items = ReflectionCache.UIMods.Items?.GetValue(menuState) as IList;
        UIScrollbar? scrollbar = ReflectionCache.UIMods.UiScrollbar?.GetValue(menuState) as UIScrollbar;

        // Get filter/category buttons
        IList? categoryButtons = ReflectionCache.UIMods.CategoryButtons?.GetValue(menuState) as IList;

        // Get action buttons
        UIElement? buttonEA = ReflectionCache.UIMods.ButtonEA?.GetValue(menuState) as UIElement;
        UIElement? buttonDA = ReflectionCache.UIMods.ButtonDA?.GetValue(menuState) as UIElement;
        UIElement? buttonRM = ReflectionCache.UIMods.ButtonRM?.GetValue(menuState) as UIElement;
        UIElement? buttonB = ReflectionCache.UIMods.ButtonB?.GetValue(menuState) as UIElement;
        UIElement? buttonOMF = ReflectionCache.UIMods.ButtonOMF?.GetValue(menuState) as UIElement;
        UIElement? buttonCL = ReflectionCache.UIMods.ButtonCL?.GetValue(menuState) as UIElement;

        // Create bindings for category/filter buttons (top row)
        var filterBindings = new List<PointBinding>();
        if (categoryButtons is not null)
        {
            string[] filterLabels = new[]
            {
                GetSortModeLabel(menuState),
                GetEnabledFilterLabel(menuState),
                GetModSideFilterLabel(menuState),
                GetRamUsageLabel(),
                GetSearchFilterLabel(menuState)
            };

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

        // Create bindings for ALL mod items
        var modBindings = new List<PointBinding>();
        ModItemButtonGroups.Clear();

        if (items is not null && items.Count > 0)
        {
            int modIndex = 0;
            foreach (object? item in items)
            {
                if (item is UIElement modItemElement)
                {
                    // Get the toggle button element for accurate click positioning
                    UIElement? toggleElement = GetVisibleModToggleElement(item);
                    UIElement targetElement = toggleElement ?? modItemElement;

                    CalculatedStyle dims = targetElement.GetDimensions();
                    Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);

                    string modDisplayName = GetModDisplayName(item);
                    string modStatus = GetModStatus(item);
                    string fullLabel = string.IsNullOrEmpty(modStatus) ? modDisplayName : $"{modDisplayName}, {modStatus}";

                    var binding = new PointBinding(nextId++, center, fullLabel, string.Empty, targetElement, PointType.ModItem);
                    modBindings.Add(binding);
                    bindings.Add(binding);
                    BindingById[binding.Id] = binding;

                    ModItemBindings.Add(new ModItemBinding(binding.Id, item));

                    // Track button IDs for this mod item
                    int toggleId = binding.Id;
                    int? moreInfoId = null;
                    int? deleteId = null;
                    int? configId = null;

                    // Get More Info button
                    UIElement? moreInfoButton = ReflectionCache.UIModItem.MoreInfoButton?.GetValue(item) as UIElement;
                    if (moreInfoButton?.Parent is not null)
                    {
                        CalculatedStyle moreInfoDims = moreInfoButton.GetDimensions();
                        Vector2 moreInfoCenter = new(moreInfoDims.X + moreInfoDims.Width / 2f, moreInfoDims.Y + moreInfoDims.Height / 2f);
                        var moreInfoBinding = new PointBinding(nextId++, moreInfoCenter, "More Info", string.Empty, moreInfoButton, PointType.ModItemButton);
                        bindings.Add(moreInfoBinding);
                        BindingById[moreInfoBinding.Id] = moreInfoBinding;
                        moreInfoId = moreInfoBinding.Id;
                    }

                    // Get Delete button
                    UIElement? deleteButton = ReflectionCache.UIModItem.DeleteModButton?.GetValue(item) as UIElement;
                    if (deleteButton?.Parent is not null)
                    {
                        CalculatedStyle deleteDims = deleteButton.GetDimensions();
                        Vector2 deleteCenter = new(deleteDims.X + deleteDims.Width / 2f, deleteDims.Y + deleteDims.Height / 2f);
                        var deleteBinding = new PointBinding(nextId++, deleteCenter, "Delete", string.Empty, deleteButton, PointType.ModItemButton);
                        bindings.Add(deleteBinding);
                        BindingById[deleteBinding.Id] = deleteBinding;
                        deleteId = deleteBinding.Id;
                    }

                    // Get Config button
                    UIElement? configButton = ReflectionCache.UIModItem.ConfigButton?.GetValue(item) as UIElement;
                    if (configButton?.Parent is not null)
                    {
                        CalculatedStyle configDims = configButton.GetDimensions();
                        Vector2 configCenter = new(configDims.X + configDims.Width / 2f, configDims.Y + configDims.Height / 2f);
                        var configBinding = new PointBinding(nextId++, configCenter, "Config", string.Empty, configButton, PointType.ModItemButton);
                        bindings.Add(configBinding);
                        BindingById[configBinding.Id] = configBinding;
                        configId = configBinding.Id;
                    }

                    ModItemButtonGroups.Add(new ModItemButtonGroup(modIndex, toggleId, moreInfoId, deleteId, configId));
                    modIndex++;
                }
            }
        }

        // Create bindings for top row action buttons
        var topActionBindings = new List<PointBinding>();
        if (buttonEA is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonEA, Language.GetTextValue("tModLoader.ModsEnableAll"), PointType.ActionButton);
            topActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        if (buttonDA is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonDA, Language.GetTextValue("tModLoader.ModsDisableAll"), PointType.ActionButton);
            topActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        if (buttonRM is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonRM, Language.GetTextValue("tModLoader.ModsForceReload"), PointType.ActionButton);
            topActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Create bindings for bottom row action buttons
        var bottomActionBindings = new List<PointBinding>();
        if (buttonB is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonB, Language.GetTextValue("UI.Back"), PointType.BackButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        if (buttonOMF is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonOMF, Language.GetTextValue("tModLoader.ModsOpenModsFolders"), PointType.ActionButton);
            bottomActionBindings.Add(binding);
            bindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        if (buttonCL is not null)
        {
            var binding = CreateButtonBinding(ref nextId, buttonCL, Language.GetTextValue("tModLoader.ModConfiguration"), PointType.ActionButton);
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

        // Create all link points but leave them UNLINKED
        foreach (PointBinding binding in bindings)
        {
            SetupLinkPoint(binding);
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Force initial focus
        if (PlayerInput.UsingGamepadUI && InitialFocusFramesRemaining > 0)
        {
            int defaultPointId = modBindings.Count > 0 ? modBindings[0].Id :
                                 filterBindings.Count > 0 ? filterBindings[0].Id :
                                 topActionBindings.Count > 0 ? topActionBindings[0].Id :
                                 bindings[0].Id;

            UILinkPointNavigator.ChangePoint(defaultPointId);
            InitialFocusFramesRemaining--;
        }

        // Handle right stick scrolling
        HandleRightStickScroll(modList, scrollbar, items);

        // Handle scrolling when navigating mod list with D-pad
        HandleModListScrolling(modList, scrollbar, items);
    }

    #endregion

    #region Navigation

    protected override void HandleNavigation(object menuState)
    {
        if (_isDialogActive)
        {
            HandleDialogNavigation();
            return;
        }

        if (!CurrentInput.HasNavigation)
        {
            return;
        }

        // Log the input
        string direction = CurrentInput.Left ? "LEFT" : CurrentInput.Right ? "RIGHT" : CurrentInput.Up ? "UP" : "DOWN";
        Mod.Logger.Debug($"[ManageMods] Input: {direction}, current region: {_currentRegion}, index: {CurrentFocusIndex}");

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
                Mod.Logger.Info($"[ManageMods] Navigated to region {_currentRegion}, index {CurrentFocusIndex}, point {newPointId.Value}");
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
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    CurrentFocusIndex = Math.Min(CurrentFocusIndex, TopActionBindingsList.Count - 1);
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
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    CurrentFocusIndex = TopActionBindingsList.Count / 2;
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
        if (_isDialogActive)
        {
            return _dialogFocusIndex == 0 ? LinkIdRegistry.DialogYes : LinkIdRegistry.DialogNo;
        }

        if (_currentRegion == FocusRegion.ModList)
        {
            if (CurrentFocusIndex < 0 || CurrentFocusIndex >= ModItemButtonGroups.Count)
            {
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
        if (_isDialogActive)
        {
            HandleDialogAction(menuState);
            AnnounceDialogFocus();
            return;
        }

        // Run every frame: detect any mod whose enabled state changed since last
        // frame (whether from our reflection toggle, a native click via the user's
        // MouseLeft keybind, dependency cascades, etc.) and announce it. Mods
        // whose state changed this frame are returned so we can avoid re-toggling
        // them below.
        DetectAndAnnounceStateChanges(out HashSet<object> changedThisFrame);

        if (!CurrentInput.ActionPressed)
        {
            return;
        }

        int? currentPointId = GetCurrentPointId();
        if (!currentPointId.HasValue || !BindingById.TryGetValue(currentPointId.Value, out var binding))
        {
            return;
        }

        if (_currentRegion == FocusRegion.ModList)
        {
            // Don't initiate a delete confirmation while the dialog cooldown is active.
            if (ScreenReaderService.IsOnCooldown(CooldownKeyDialogAction))
            {
                return;
            }

            // Check if we're on a mod item button (More Info, Delete, Config)
            if (_currentModButtonIndex > 0)
            {
                HandleModItemButtonAction(menuState);
                return;
            }

            // If a native click already toggled the focused mod this frame, don't
            // toggle again — DetectAndAnnounceStateChanges already announced it.
            if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModItemBindings.Count)
            {
                object? focusedMod = ModItemBindings[CurrentFocusIndex].ModItem;
                if (focusedMod is not null && changedThisFrame.Contains(focusedMod))
                {
                    return;
                }
            }

            HandleModToggle();
            return;
        }

        HandleFocusedButtonAction(binding);
    }

    private void HandleFocusedButtonAction(PointBinding binding)
    {
        if (binding.Element is not UIElement element)
        {
            return;
        }

        Mod.Logger.Info($"[ManageMods] Clicking: {binding.Label}");
        SoundEngine.PlaySound(SoundID.MenuTick);

        try
        {
            CalculatedStyle dims = element.GetDimensions();
            Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
            var clickEvent = new UIMouseEvent(element, center);
            element.LeftClick(clickEvent);

            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Click failed: {ex.Message}");
        }
    }

    private void HandleModItemButtonAction(object menuState)
    {
        int? currentPointId = GetCurrentPointId();
        if (!currentPointId.HasValue || !BindingById.TryGetValue(currentPointId.Value, out var binding))
        {
            return;
        }

        if (binding.Element is not UIElement buttonElement)
        {
            return;
        }

        Mod.Logger.Info($"[ManageMods] Clicking mod button: {binding.Label} (type: {buttonElement.GetType().Name})");
        SoundEngine.PlaySound(SoundID.MenuTick);

        try
        {
            CalculatedStyle dims = buttonElement.GetDimensions();
            Vector2 buttonCenter = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
            var clickEvent = new UIMouseEvent(buttonElement, buttonCenter);

            // For More Info and Delete buttons, invoke the handler method directly on the UIModItem
            // to avoid click propagation via UIElement.LeftClick which bubbles up to the parent
            // UIModItem panel and can interfere with the screen transition.
            if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModItemBindings.Count)
            {
                var modItemBinding = ModItemBindings[CurrentFocusIndex];
                object? modItem = modItemBinding.ModItem;

                if (modItem is not null && ReflectionCache.UIModItem.Type is not null)
                {
                    // More Info button: invoke ShowMoreInfo directly
                    if (binding.Label == "More Info" && ReflectionCache.UIModItem.ShowMoreInfo is { } showMoreInfoMethod)
                    {
                        Mod.Logger.Info($"[ManageMods] Invoking ShowMoreInfo directly to avoid click propagation");
                        showMoreInfoMethod.Invoke(modItem, new object[] { clickEvent, buttonElement });

                        Main.mouseLeft = false;
                        Main.mouseLeftRelease = false;
                        return;
                    }

                    // Delete button: invoke QuickModDelete directly
                    if (binding.Label == "Delete")
                    {
                        var quickModDeleteMethod = ReflectionCache.UIModItem.Type.GetMethod("QuickModDelete", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (quickModDeleteMethod is not null)
                        {
                            Mod.Logger.Info($"[ManageMods] Invoking QuickModDelete directly");
                            quickModDeleteMethod.Invoke(modItem, new object[] { clickEvent, buttonElement });

                            Main.mouseLeft = false;
                            Main.mouseLeftRelease = false;
                            ScreenReaderService.SetCooldown(CooldownKeyDialogAction, 30);
                            return;
                        }
                    }
                }
            }

            // Fallback: use LeftClick for other buttons (Config, etc.)
            Mod.Logger.Info($"[ManageMods] Using LeftClick fallback for button: {binding.Label}");
            buttonElement.LeftClick(clickEvent);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Button click failed: {ex.Message}");
        }
    }

    private void HandleModToggle()
    {
        if (CurrentFocusIndex < 0 || CurrentFocusIndex >= ModItemBindings.Count)
        {
            return;
        }

        var foundBinding = ModItemBindings[CurrentFocusIndex];
        object? modItem = foundBinding.ModItem;

        if (modItem is null)
        {
            return;
        }

        UIElement? toggleElement = GetVisibleModToggleElement(modItem);
        if (toggleElement is null)
        {
            string modDisplayName = GetModDisplayName(modItem);
            Mod.Logger.Info($"[ManageMods] Toggle ignored because {modDisplayName} does not expose an enabled-state button");
            ScreenReaderService.Announce($"{modDisplayName} cannot be toggled", force: true);
            return;
        }

        try
        {
            if (ReflectionCache.UIModItem.ToggleEnabled is { } toggleEnabledMethod)
            {
                CalculatedStyle dims = toggleElement.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var clickEvent = new UIMouseEvent(toggleElement, center);
                toggleEnabledMethod.Invoke(modItem, new object[] { clickEvent, toggleElement });

                Mod.Logger.Info($"[ManageMods] Toggled {GetModDisplayName(modItem)} via reflection");
                Main.mouseLeft = false;
                Main.mouseLeftRelease = false;
                return;
            }

            bool currentState = GetModEnabledState(modItem);
            object? localMod = ReflectionCache.UIModItem.Mod?.GetValue(modItem);
            bool fallbackState = !currentState;
            if (localMod is not null && ReflectionCache.LocalMod.Enabled is not null)
            {
                ReflectionCache.LocalMod.Enabled.SetValue(localMod, fallbackState);
                ReflectionCache.UIModItem.UpdateUiForEnabledChange?.Invoke(modItem, null);

                Mod.Logger.Warn("[ManageMods] ToggleEnabled reflection was unavailable; used direct enabled-state fallback");
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Toggle failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Compares each tracked mod's current enabled state against the cached value.
    /// Announces every change (no matter the source — our reflection toggle, a
    /// native click from a user-bound MouseLeft key, or a dependency cascade) and
    /// updates the cache. Returns the set of mod items whose state changed this
    /// frame so callers can avoid re-toggling something that just got toggled
    /// natively.
    /// </summary>
    private void DetectAndAnnounceStateChanges(out HashSet<object> changedThisFrame)
    {
        changedThisFrame = new HashSet<object>();

        foreach (var binding in ModItemBindings)
        {
            object? modItem = binding.ModItem;
            if (modItem is null)
            {
                continue;
            }

            bool actualState = GetModEnabledState(modItem);

            if (!_lastKnownModStates.TryGetValue(modItem, out bool lastState))
            {
                // First time we've seen this mod — establish a baseline without
                // announcing.
                _lastKnownModStates[modItem] = actualState;
                continue;
            }

            if (actualState == lastState)
            {
                continue;
            }

            _lastKnownModStates[modItem] = actualState;
            changedThisFrame.Add(modItem);

            string modDisplayName = GetModDisplayName(modItem);
            string stateText = actualState ? "Enabled" : "Disabled";
            string announcement = $"{modDisplayName} {stateText}";

            Mod.Logger.Info($"[ManageMods] State change detected: {announcement}");
            ScreenReaderService.Announce(announcement, force: true);
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

    private void HandleModListScrolling(UIList? modList, UIScrollbar? scrollbar, IList? items)
    {
        if (modList is null || scrollbar is null || items is null || items.Count == 0)
        {
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        ModItemBinding currentModBinding = ModItemBindings.FirstOrDefault(b => b.Id == currentPoint);

        if (currentModBinding.Id == 0)
        {
            return;
        }

        int modIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], currentModBinding.ModItem))
            {
                modIndex = i;
                break;
            }
        }

        if (modIndex < 0)
        {
            return;
        }

        if (items[modIndex] is UIElement modItem)
        {
            float itemTop = modItem.Top.Pixels;
            float itemHeight = modItem.GetOuterDimensions().Height;
            float viewHeight = modList.GetInnerDimensions().Height;
            float currentScroll = scrollbar.ViewPosition;

            if (itemTop < currentScroll)
            {
                scrollbar.ViewPosition = itemTop;
            }
            else if (itemTop + itemHeight > currentScroll + viewHeight)
            {
                scrollbar.ViewPosition = itemTop + itemHeight - viewHeight;
            }
        }
    }

    private void HandleRightStickScroll(UIList? modList, UIScrollbar? scrollbar, IList? items)
    {
        if (_currentRegion != FocusRegion.ModList)
        {
            _lastScrollAnnouncedModIndex = -1;
            _lastScrollPosition = -1f;
            return;
        }

        if (modList is null || scrollbar is null || items is null || items.Count == 0)
        {
            return;
        }

        float rightStickY = PlayerInput.GamepadThumbstickRight.Y;
        const float scrollThreshold = 0.1f;

        bool isActivelyScrolling = Math.Abs(rightStickY) >= scrollThreshold;

        if (!isActivelyScrolling)
        {
            _lastScrollPosition = scrollbar.ViewPosition;
            return;
        }

        float scrollAmount = -rightStickY * 16f;
        scrollbar.ViewPosition += scrollAmount;

        float currentScroll = scrollbar.ViewPosition;

        if (Math.Abs(currentScroll - _lastScrollPosition) < 5f && _lastScrollPosition >= 0)
        {
            return;
        }

        _lastScrollPosition = currentScroll;

        CalculatedStyle listDims = modList.GetInnerDimensions();
        float viewportCenter = listDims.Y + listDims.Height / 2f;

        int closestModIndex = -1;
        float closestDistance = float.MaxValue;

        int index = 0;
        foreach (object? item in items)
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

            if (items[closestModIndex] is object modItem)
            {
                string modName = GetModDisplayName(modItem);
                string modStatus = GetModStatus(modItem);
                string announcement = string.IsNullOrEmpty(modStatus)
                    ? $"{modName}, {closestModIndex + 1} of {items.Count}"
                    : $"{modName}, {modStatus}, {closestModIndex + 1} of {items.Count}";

                SoundEngine.PlaySound(SoundID.MenuTick);
                ScreenReaderService.Announce(announcement, force: true);

                CurrentFocusIndex = closestModIndex;
                _currentModButtonIndex = 0;
            }
        }
    }

    #endregion

    #region Dialog Handling

    private void HandleDialogStateChange(bool dialogActive, object menuState)
    {
        if (dialogActive)
        {
            _isDialogActive = true;
            _dialogFocusIndex = 0;
            LastAnnouncedPointId = -1;
            _lastDialogAnnouncedIndex = -1;

            // Get dialog text and enqueue it as a prefix for the first button announcement
            string? dialogText = GetDialogText(menuState);
            if (!string.IsNullOrEmpty(dialogText))
            {
                ScreenReaderService.EnqueuePrefix(dialogText);
            }

            _savedFocusPointBeforeDialog = UILinkPointNavigator.CurrentPoint;
            SetupDialogLinkPoints();

            ScreenReaderService.SetCooldown(CooldownKeyDialogAction, 45);

            Mod.Logger.Info("[ManageMods] Confirmation dialog opened");
        }
        else
        {
            _isDialogActive = false;
            DialogBindings.Clear();
            _lastDialogAnnouncedIndex = -1;
            _dialogFocusIndex = 0;

            _currentModButtonIndex = 0;
            CleanupDialogLinkPoints();

            // Clear dialog-related speech state and set cooldown
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearContexts("managemods:dialog");
            ScreenReaderService.SetCooldown(CooldownKeyDialogAction, 15);

            Mod.Logger.Info("[ManageMods] Confirmation dialog closed, cooldown started");
        }
    }

    private void SetupDialogLinkPoints()
    {
        UILinkPoint yesPoint = EnsureLinkPoint(LinkIdRegistry.DialogYes);
        UILinkPoint noPoint = EnsureLinkPoint(LinkIdRegistry.DialogNo);

        yesPoint.Up = -1;
        yesPoint.Down = LinkIdRegistry.DialogNo;
        yesPoint.Left = -1;
        yesPoint.Right = -1;

        noPoint.Up = LinkIdRegistry.DialogYes;
        noPoint.Down = -1;
        noPoint.Left = -1;
        noPoint.Right = -1;

        int dialogPoint = _dialogFocusIndex == 0 ? LinkIdRegistry.DialogYes : LinkIdRegistry.DialogNo;
        UILinkPointNavigator.ChangePoint(dialogPoint);
    }

    private void CleanupDialogLinkPoints()
    {
        UILinkPointNavigator.Points.Remove(LinkIdRegistry.DialogYes);
        UILinkPointNavigator.Points.Remove(LinkIdRegistry.DialogNo);

        if (CurrentFocusIndex >= 0 && CurrentFocusIndex < ModBindingsList.Count)
        {
            int safePointId = ModBindingsList[CurrentFocusIndex].Id;
            UILinkPointNavigator.ChangePoint(safePointId);
        }
        else if (BottomActionBindingsList.Count > 0)
        {
            int backPointId = BottomActionBindingsList[0].Id;
            UILinkPointNavigator.ChangePoint(backPointId);
        }
    }

    private bool IsConfirmDialogActive(object mods)
    {
        if (ReflectionCache.UIMods.BlockInput is null)
        {
            return false;
        }

        try
        {
            UIElement? blockInput = ReflectionCache.UIMods.BlockInput.GetValue(mods) as UIElement;
            if (blockInput is null)
            {
                return false;
            }

            if (mods is UIElement modsElement)
            {
                return modsElement.HasChild(blockInput);
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Dialog check exception: {ex.Message}");
        }

        return false;
    }

    private string? GetDialogText(object mods)
    {
        try
        {
            UIElement? dialogText = ReflectionCache.UIMods.ConfirmDialogText?.GetValue(mods) as UIElement;
            if (dialogText is UIText uiText)
            {
                return uiText.Text;
            }

            IList? items = ReflectionCache.UIMods.Items?.GetValue(mods) as IList;
            if (items is not null)
            {
                foreach (object? item in items)
                {
                    if (item is not null)
                    {
                        UIElement? modItemDialogText = ReflectionCache.UIModItem.DialogText?.GetValue(item) as UIElement;
                        if (modItemDialogText is UIText modItemUiText && !string.IsNullOrEmpty(modItemUiText.Text))
                        {
                            return modItemUiText.Text;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Failed to read confirmation dialog text: {ex.Message}");
        }

        return Language.GetTextValue("tModLoader.DeleteModConfirm");
    }

    private void ConfigureDialogPoints(object mods)
    {
        DialogBindings.Clear();

        try
        {
            UIElement? yesButton = ReflectionCache.UIMods.ConfirmDialogYesButton?.GetValue(mods) as UIElement;
            UIElement? noButton = ReflectionCache.UIMods.ConfirmDialogNoButton?.GetValue(mods) as UIElement;

            if (yesButton is null || noButton is null)
            {
                IList? items = ReflectionCache.UIMods.Items?.GetValue(mods) as IList;
                if (items is not null)
                {
                    foreach (object? item in items)
                    {
                        if (item is not null)
                        {
                            UIElement? modYesButton = ReflectionCache.UIModItem.DialogYesButton?.GetValue(item) as UIElement;
                            UIElement? modNoButton = ReflectionCache.UIModItem.DialogNoButton?.GetValue(item) as UIElement;

                            if (modYesButton is not null && modNoButton is not null)
                            {
                                yesButton = modYesButton;
                                noButton = modNoButton;
                                break;
                            }
                        }
                    }
                }
            }

            if (yesButton is not null)
            {
                CalculatedStyle dims = yesButton.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                DialogBindings.Add(new PointBinding(0, center, Language.GetTextValue("LegacyMenu.104"), string.Empty, yesButton, PointType.ActionButton));
            }

            if (noButton is not null)
            {
                CalculatedStyle dims = noButton.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                DialogBindings.Add(new PointBinding(1, center, Language.GetTextValue("LegacyMenu.105"), string.Empty, noButton, PointType.ActionButton));
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Failed to configure dialog points: {ex.Message}");
        }
    }

    private void HandleDialogNavigation()
    {
        if (DialogBindings.Count == 0 || !CurrentInput.HasNavigation)
        {
            return;
        }

        if ((CurrentInput.Left || CurrentInput.Up) && _dialogFocusIndex > 0)
        {
            _dialogFocusIndex--;
            _lastDialogAnnouncedIndex = -1;
            UILinkPointNavigator.ChangePoint(LinkIdRegistry.DialogYes);
        }
        else if ((CurrentInput.Right || CurrentInput.Down) && _dialogFocusIndex < DialogBindings.Count - 1)
        {
            _dialogFocusIndex++;
            _lastDialogAnnouncedIndex = -1;
            UILinkPointNavigator.ChangePoint(LinkIdRegistry.DialogNo);
        }
    }

    private void HandleDialogAction(object mods)
    {
        if (CurrentInput.BackPressed && DialogBindings.Count > 1)
        {
            _dialogFocusIndex = 1;
            UILinkPointNavigator.ChangePoint(LinkIdRegistry.DialogNo);
            ClickDialogBinding(DialogBindings[_dialogFocusIndex]);
            return;
        }

        if (CurrentInput.ActionPressed && _dialogFocusIndex >= 0 && _dialogFocusIndex < DialogBindings.Count)
        {
            ClickDialogBinding(DialogBindings[_dialogFocusIndex]);
        }
    }

    private void ClickDialogBinding(PointBinding binding)
    {
        if (binding.Element is not UIElement button)
        {
            return;
        }

        Mod.Logger.Info($"[ManageMods] Dialog: Clicking {binding.Label}");
        SoundEngine.PlaySound(SoundID.MenuTick);

        try
        {
            CalculatedStyle dims = button.GetDimensions();
            Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
            var clickEvent = new UIMouseEvent(button, center);
            button.LeftClick(clickEvent);

            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Dialog click failed: {ex.Message}");
        }
    }

    private void AnnounceDialogFocus()
    {
        if (_dialogFocusIndex == _lastDialogAnnouncedIndex)
        {
            return;
        }

        if (_dialogFocusIndex >= 0 && _dialogFocusIndex < DialogBindings.Count)
        {
            var binding = DialogBindings[_dialogFocusIndex];
            string buttonLabel = TextSanitizer.Clean(binding.Label);

            _lastDialogAnnouncedIndex = _dialogFocusIndex;
            SoundEngine.PlaySound(SoundID.MenuTick);

            // The dialog text was enqueued as a prefix and will be automatically
            // prepended by the speech controller to the first announcement
            ScreenReaderService.Announce(buttonLabel, force: true);
        }
    }

    #endregion

    #region Helper Methods

    private static bool GetModEnabledState(object? modItem)
    {
        if (modItem is null)
        {
            return false;
        }

        try
        {
            object? localMod = ReflectionCache.UIModItem.Mod?.GetValue(modItem);
            if (localMod is null || ReflectionCache.LocalMod.Enabled is null)
            {
                return false;
            }

            return (bool)(ReflectionCache.LocalMod.Enabled.GetValue(localMod) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static UIElement? GetVisibleModToggleElement(object? modItem)
    {
        if (modItem is null)
        {
            return null;
        }

        try
        {
            UIElement? toggleElement = ReflectionCache.UIModItem.UiModStateText?.GetValue(modItem) as UIElement;
            return toggleElement?.Parent is not null ? toggleElement : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetModDisplayName(object modItem)
    {
        string? displayName = ReflectionCache.UIModItem.DisplayNameClean?.GetValue(modItem) as string;
        if (!string.IsNullOrEmpty(displayName))
        {
            return displayName;
        }

        return ReflectionCache.UIModItem.ModName?.GetValue(modItem) as string ?? "Unknown Mod";
    }

    private static string GetModStatus(object modItem)
    {
        try
        {
            object? localMod = ReflectionCache.UIModItem.Mod?.GetValue(modItem);
            if (localMod is null || ReflectionCache.LocalMod.Enabled is null)
            {
                return string.Empty;
            }

            bool enabled = (bool)(ReflectionCache.LocalMod.Enabled.GetValue(localMod) ?? false);
            return enabled ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetSortModeLabel(object mods)
    {
        object? sortMode = ReflectionCache.UIMods.SortMode?.GetValue(mods);
        if (sortMode is null)
        {
            return "Sort";
        }

        return sortMode.ToString() switch
        {
            "RecentlyUpdated" => Language.GetTextValue("tModLoader.ModsSortRecently"),
            "DisplayNameAtoZ" => Language.GetTextValue("tModLoader.ModsSortNamesAlph"),
            "DisplayNameZtoA" => Language.GetTextValue("tModLoader.ModsSortNamesReverseAlph"),
            _ => "Sort"
        };
    }

    private static string GetEnabledFilterLabel(object mods)
    {
        object? filterMode = ReflectionCache.UIMods.EnabledFilterMode?.GetValue(mods);
        if (filterMode is null)
        {
            return "Filter";
        }

        return filterMode.ToString() switch
        {
            "All" => Language.GetTextValue("tModLoader.ModsShowAllMods"),
            "EnabledOnly" => Language.GetTextValue("tModLoader.ModsShowEnabledMods"),
            "DisabledOnly" => Language.GetTextValue("tModLoader.ModsShowDisabledMods"),
            _ => "Filter"
        };
    }

    private static string GetModSideFilterLabel(object mods)
    {
        object? filterMode = ReflectionCache.UIMods.ModSideFilterMode?.GetValue(mods);
        if (filterMode is null)
        {
            return "Side Filter";
        }

        return filterMode.ToString() switch
        {
            "All" => Language.GetTextValue("tModLoader.ModsShowAllMods"),
            "Both" => Language.GetTextValue("tModLoader.ModsShowMSBoth"),
            "Client" => Language.GetTextValue("tModLoader.ModsShowMSClient"),
            "Server" => Language.GetTextValue("tModLoader.ModsShowMSServer"),
            "NoSync" => Language.GetTextValue("tModLoader.ModsShowMSNoSync"),
            _ => "Side Filter"
        };
    }

    private static string GetRamUsageLabel()
    {
        return "RAM Usage Toggle";
    }

    private static string GetSearchFilterLabel(object mods)
    {
        object? filterMode = ReflectionCache.UIMods.SearchFilterMode?.GetValue(mods);
        if (filterMode is null)
        {
            return "Search Filter";
        }

        return filterMode.ToString() switch
        {
            "Name" => Language.GetTextValue("tModLoader.ModsSearchByModName"),
            "Author" => Language.GetTextValue("tModLoader.ModsSearchByAuthor"),
            _ => "Search Filter"
        };
    }

    #endregion

    #region Nested Types

    private readonly record struct ModItemBinding(int Id, object ModItem);

    private readonly record struct ModItemButtonGroup(
        int ModIndex,
        int ToggleId,
        int? MoreInfoId,
        int? DeleteId,
        int? ConfigId
    )
    {
        public int? GetButtonIdAtIndex(int index)
        {
            return index switch
            {
                0 => ToggleId,
                1 => MoreInfoId,
                2 => DeleteId ?? ConfigId,
                3 => DeleteId.HasValue ? ConfigId : null,
                _ => null
            };
        }

        public int ButtonCount
        {
            get
            {
                int count = 1;
                if (MoreInfoId.HasValue) count++;
                if (DeleteId.HasValue) count++;
                if (ConfigId.HasValue) count++;
                return count;
            }
        }
    }

    #endregion
}
