#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
using Terraria.UI;
using Terraria.UI.Gamepad;
using ScreenReaderMod.Common.Systems.ModBrowser;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Manage Mods (UIMods) menu.
/// Uses reflection since UIMods and UIModItem are internal types.
/// </summary>
public sealed class ManageModsAccessibilitySystem : ModSystem
{
    private const int BaseLinkId = 3100;

    private static int _lastAnnouncedPointId = -1;
    private static int _lastSeenPointId = -1;
    private static object? _lastModsMenu;
    private static int _initialFocusFramesRemaining;

    // Navigation state tracking
    private static int _currentFocusIndex;
    private static FocusRegion _currentRegion = FocusRegion.ModList;
    private static bool _leftWasPressed;
    private static bool _rightWasPressed;
    private static bool _upWasPressed;
    private static bool _downWasPressed;

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

    // Type references
    private static Type? _uiModsType;
    private static Type? _uiModItemType;
    private static Type? _localModType;

    // UIMods field references
    private static FieldInfo? _modListField;
    private static FieldInfo? _itemsField;
    private static FieldInfo? _categoryButtonsField;
    private static FieldInfo? _buttonEAField;
    private static FieldInfo? _buttonDAField;
    private static FieldInfo? _buttonRMField;
    private static FieldInfo? _buttonBField;
    private static FieldInfo? _buttonOMFField;
    private static FieldInfo? _buttonCLField;
    private static FieldInfo? _uiScrollbarField;
    private static FieldInfo? _sortModeField;
    private static FieldInfo? _enabledFilterModeField;
    private static FieldInfo? _modSideFilterModeField;
    private static FieldInfo? _searchFilterModeField;

    // UIMods dialog field references
    private static FieldInfo? _blockInputField;
    private static FieldInfo? _confirmDialogYesButtonField;
    private static FieldInfo? _confirmDialogNoButtonField;
    private static FieldInfo? _confirmDialogTextField;
    private static MethodInfo? _closeConfirmDialogMethod;

    // UIModItem field references
    private static FieldInfo? _modField;
    private static PropertyInfo? _displayNameCleanProperty;
    private static PropertyInfo? _modNameProperty;
    private static FieldInfo? _uiModStateTextField;
    private static FieldInfo? _moreInfoButtonField;
    private static FieldInfo? _deleteModButtonField;
    private static FieldInfo? _configButtonField;

    // UIModItem dialog field references (for delete confirmation)
    private static FieldInfo? _modItemDialogYesButtonField;
    private static FieldInfo? _modItemDialogNoButtonField;
    private static FieldInfo? _modItemDialogTextField;

    // LocalMod field for enabled status
    private static PropertyInfo? _enabledProperty;

    // Track binding for announcement lookup
    private static readonly Dictionary<int, PointBinding> BindingById = new();
    private static readonly List<ModItemBinding> ModItemBindings = new();

    // Track mod item buttons (More Info, Delete, Config) for each mod
    private static readonly List<ModItemButtonGroup> ModItemButtonGroups = new();

    // Current button index within a mod item (0 = mod toggle, 1+ = buttons to the right)
    private static int _currentModButtonIndex;

    // Dialog state tracking
    private static bool _isDialogActive;
    private static int _dialogFocusIndex; // 0 = Yes, 1 = No
    private static readonly List<PointBinding> DialogBindings = new();
    private static string? _dialogText; // The dialog text to announce with the first button
    private static bool _dialogTextAnnounced; // Whether the dialog text has been announced
    private static int _dialogActionCooldown; // Frames to wait after dialog closes before allowing actions
    private static int _savedFocusPointBeforeDialog; // Store focus point before dialog opens
    private const int DialogYesPointId = 3200; // Safe link point IDs for dialog buttons
    private const int DialogNoPointId = 3201;

    /// <summary>
    /// Returns true if the Manage Mods menu is currently active and handling gamepad input.
    /// Used by MenuNarration to suppress hover announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (_lastModsMenu is null || !PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            // Verify we're still in the UIMods UI state
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null || _uiModsType is null || currentState.GetType() != _uiModsType)
            {
                _lastModsMenu = null;
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
        _uiModsType = Type.GetType("Terraria.ModLoader.UI.UIMods, tModLoader");
        _uiModItemType = Type.GetType("Terraria.ModLoader.UI.UIModItem, tModLoader");
        _localModType = Type.GetType("Terraria.ModLoader.Core.LocalMod, tModLoader");

        Mod.Logger.Info($"[ManageMods] Load: UIMods type found: {_uiModsType is not null}");
        Mod.Logger.Info($"[ManageMods] Load: UIModItem type found: {_uiModItemType is not null}");
        Mod.Logger.Info($"[ManageMods] Load: LocalMod type found: {_localModType is not null}");

        if (_uiModsType is null)
        {
            Mod.Logger.Warn("[ManageMods] Could not find UIMods type");
            return;
        }

        // Hook into DrawMenu to process during menu rendering (PostUpdateEverything only runs in-game)
        On_Main.DrawMenu += HandleDrawMenu;

        // Get UIMods fields
        _modListField = _uiModsType.GetField("modList", BindingFlags.NonPublic | BindingFlags.Instance);
        _itemsField = _uiModsType.GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
        _categoryButtonsField = _uiModsType.GetField("_categoryButtons", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonEAField = _uiModsType.GetField("buttonEA", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonDAField = _uiModsType.GetField("buttonDA", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonRMField = _uiModsType.GetField("buttonRM", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonBField = _uiModsType.GetField("buttonB", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonOMFField = _uiModsType.GetField("buttonOMF", BindingFlags.NonPublic | BindingFlags.Instance);
        _buttonCLField = _uiModsType.GetField("buttonCL", BindingFlags.NonPublic | BindingFlags.Instance);
        _uiScrollbarField = _uiModsType.GetField("uIScrollbar", BindingFlags.NonPublic | BindingFlags.Instance);
        _sortModeField = _uiModsType.GetField("sortMode", BindingFlags.Public | BindingFlags.Instance);
        _enabledFilterModeField = _uiModsType.GetField("enabledFilterMode", BindingFlags.Public | BindingFlags.Instance);
        _modSideFilterModeField = _uiModsType.GetField("modSideFilterMode", BindingFlags.Public | BindingFlags.Instance);
        _searchFilterModeField = _uiModsType.GetField("searchFilterMode", BindingFlags.Public | BindingFlags.Instance);

        // Get UIMods dialog fields
        _blockInputField = _uiModsType.GetField("_blockInput", BindingFlags.NonPublic | BindingFlags.Instance);
        _confirmDialogYesButtonField = _uiModsType.GetField("_confirmDialogYesButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _confirmDialogNoButtonField = _uiModsType.GetField("_confirmDialogNoButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _confirmDialogTextField = _uiModsType.GetField("_confirmDialogText", BindingFlags.NonPublic | BindingFlags.Instance);
        _closeConfirmDialogMethod = _uiModsType.GetMethod("CloseConfirmDialog", BindingFlags.NonPublic | BindingFlags.Instance);

        // Get UIModItem fields
        if (_uiModItemType is not null)
        {
            _modField = _uiModItemType.GetField("_mod", BindingFlags.NonPublic | BindingFlags.Instance);
            _displayNameCleanProperty = _uiModItemType.GetProperty("DisplayNameClean", BindingFlags.Public | BindingFlags.Instance);
            _modNameProperty = _uiModItemType.GetProperty("ModName", BindingFlags.Public | BindingFlags.Instance);
            _uiModStateTextField = _uiModItemType.GetField("_uiModStateText", BindingFlags.NonPublic | BindingFlags.Instance);
            _moreInfoButtonField = _uiModItemType.GetField("_moreInfoButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _deleteModButtonField = _uiModItemType.GetField("_deleteModButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _configButtonField = _uiModItemType.GetField("_configButton", BindingFlags.NonPublic | BindingFlags.Instance);

            // UIModItem dialog fields (for delete confirmation)
            _modItemDialogYesButtonField = _uiModItemType.GetField("_dialogYesButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _modItemDialogNoButtonField = _uiModItemType.GetField("_dialogNoButton", BindingFlags.NonPublic | BindingFlags.Instance);
            _modItemDialogTextField = _uiModItemType.GetField("_dialogText", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // Get LocalMod fields
        if (_localModType is not null)
        {
            _enabledProperty = _localModType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
        }

    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Remove hook
        if (_uiModsType is not null)
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
        DialogBindings.Clear();
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _lastScrollAnnouncedModIndex = -1;
        _lastScrollPosition = -1f;
        _lastModsMenu = null;
        _initialFocusFramesRemaining = 0;
        _currentFocusIndex = 0;
        _currentModButtonIndex = 0;
        _currentRegion = FocusRegion.ModList;
        _isDialogActive = false;
        _dialogFocusIndex = 0;
        _dialogText = null;
        _dialogTextAnnounced = false;
        _lastDialogAnnouncedIndex = -1;
        _dialogActionCooldown = 0;
        _savedFocusPointBeforeDialog = 0;
    }

    private void HandleDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        // CRITICAL: Consume mouse input BEFORE native processing if dialog is active
        // This prevents _blockInput.OnLeftMouseDown from closing the dialog
        if (_isDialogActive)
        {
            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;
        }

        // Call original
        orig(self, gameTime);

        // Then process menu accessibility
        TryProcessModsMenu();
    }

    private void TryProcessModsMenu()
    {
        if (_uiModsType is null)
        {
            return;
        }

        object? currentState = Main.MenuUI?.CurrentState;

        // Only log state mismatch once when transitioning away from UIMods, not continuously
        // This prevents log spam when on other menu screens like UIModConfigList

        // Compare by type name since Type objects might differ across assemblies
        bool isUIMods = currentState is not null &&
                        currentState.GetType().FullName == "Terraria.ModLoader.UI.UIMods";

        if (!isUIMods)
        {
            // Clear menu reference if we've left UIMods
            if (_lastModsMenu is not null)
            {
                Mod.Logger.Info("[ManageMods] Left Manage Mods menu");
                _lastModsMenu = null;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                _lastScrollAnnouncedModIndex = -1;
                _lastScrollPosition = -1f;
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
        bool menuChanged = !ReferenceEquals(currentState, _lastModsMenu);
        if (menuChanged)
        {
            _lastModsMenu = currentState;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            _lastScrollAnnouncedModIndex = -1;
            _lastScrollPosition = -1f;
            _initialFocusFramesRemaining = 10; // Increased from 5 to give more time
            _currentFocusIndex = 0;
            _currentModButtonIndex = 0;
            _currentRegion = FocusRegion.ModList;
            Mod.Logger.Info("[ManageMods] Entered Manage Mods menu");
        }

        // Update search mode manager (handles Tab key toggle)
        SearchModeManager.Update();

        // If user pressed Enter to exit search mode, focus the first mod
        if (SearchModeManager.ConsumeFocusFirstModRequest())
        {
            _currentFocusIndex = 0;
            _currentModButtonIndex = 0;
            _currentRegion = FocusRegion.ModList;
            _lastAnnouncedPointId = -1; // Reset to trigger announcement
        }

        // Process navigation - support both gamepad and keyboard input
        // Keyboard navigation only works when not in search mode
        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;
        bool hasKeyboardNavigation = !SearchModeManager.IsSearchModeActive;

        if (!hasGamepadInput && !hasKeyboardNavigation)
        {
            return;
        }

        try
        {
            // Check if a confirmation dialog is active
            // currentState is guaranteed non-null here (checked via isUIMods above)
            bool dialogActive = IsConfirmDialogActive(currentState!);

            if (dialogActive != _isDialogActive)
            {
                if (dialogActive)
                {
                    // Dialog just opened
                    _isDialogActive = true;
                    _dialogFocusIndex = 0; // Default to "Yes" button
                    _lastAnnouncedPointId = -1;
                    _lastDialogAnnouncedIndex = -1;
                    _dialogText = GetDialogText(currentState!);
                    _dialogTextAnnounced = false;

                    // Save current focus point so we can restore properly later
                    _savedFocusPointBeforeDialog = UILinkPointNavigator.CurrentPoint;

                    // CRITICAL: Set up dialog link points in UILinkPointNavigator to prevent
                    // native gamepad handling from clicking the Delete button while dialog is open
                    SetupDialogLinkPoints();

                    // IMPORTANT: Mark A button as already pressed so the press that opened
                    // the dialog doesn't immediately activate the Yes button
                    GamePadState gpState = GamePad.GetState(PlayerIndex.One);
                    _dialogAWasPressed = gpState.Buttons.A == ButtonState.Pressed;

                    // CRITICAL: Add a cooldown to prevent accidental Yes clicks
                    // This gives the user time to release and intentionally re-press A
                    _dialogActionCooldown = 45; // ~0.75 seconds at 60fps

                    Mod.Logger.Info("[ManageMods] Confirmation dialog opened");
                }
                else
                {
                    // Dialog just closed
                    _isDialogActive = false;
                    DialogBindings.Clear();
                    _dialogText = null;
                    _dialogTextAnnounced = false;
                    _lastDialogAnnouncedIndex = -1;
                    _dialogFocusIndex = 0;

                    // Reset button index to mod toggle so we don't re-trigger Delete button
                    _currentModButtonIndex = 0;

                    // CRITICAL: Remove dialog link points and restore focus to a safe point
                    CleanupDialogLinkPoints();

                    // IMPORTANT: Mark A button as already pressed so the press that closed
                    // the dialog doesn't trigger another action on the underlying menu
                    GamePadState gpState = GamePad.GetState(PlayerIndex.One);
                    _aButtonWasPressed = gpState.Buttons.A == ButtonState.Pressed;

                    // Set a cooldown to prevent accidental re-triggering of actions
                    _dialogActionCooldown = 15; // About 0.25 seconds at 60fps

                    Mod.Logger.Info("[ManageMods] Confirmation dialog closed, cooldown started");
                }
            }

            if (_isDialogActive)
            {
                // CRITICAL: Consume mouse input EVERY FRAME while dialog is active
                // This prevents native Terraria UI from triggering _blockInput.OnLeftMouseDown
                // which would close the dialog when the gamepad A button generates mouseLeft
                Main.mouseLeft = false;
                Main.mouseLeftRelease = false;

                // Keep main menu button state in sync while dialog is active
                // This prevents stale state when transitioning back to menu
                GamePadState gpState = GamePad.GetState(PlayerIndex.One);
                _aButtonWasPressed = gpState.Buttons.A == ButtonState.Pressed;

                // Handle dialog navigation
                ConfigureDialogPoints(currentState!);
                HandleDialogNavigation();
                HandleDialogAction(currentState!);
                AnnounceDialogFocus();
            }
            else
            {
                // Handle normal menu navigation
                ConfigureGamepadPoints(currentState!);
                HandleManualNavigation(currentState!);
                HandleActionButton(currentState!);
                AnnounceCurrentFocus(currentState!);
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ManageMods] Failed to configure gamepad points: {ex}");
        }
    }

    private static void ConfigureGamepadPoints(object mods)
    {
        BindingById.Clear();
        ModItemBindings.Clear();
        FilterBindings.Clear();
        ModBindingsList.Clear();
        TopActionBindingsList.Clear();
        BottomActionBindingsList.Clear();

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>();

        // Get mod list and items
        UIList? modList = _modListField?.GetValue(mods) as UIList;
        IList? items = _itemsField?.GetValue(mods) as IList;
        UIScrollbar? scrollbar = _uiScrollbarField?.GetValue(mods) as UIScrollbar;

        // Get filter/category buttons
        IList? categoryButtons = _categoryButtonsField?.GetValue(mods) as IList;

        // Get action buttons
        UIElement? buttonEA = _buttonEAField?.GetValue(mods) as UIElement;
        UIElement? buttonDA = _buttonDAField?.GetValue(mods) as UIElement;
        UIElement? buttonRM = _buttonRMField?.GetValue(mods) as UIElement;
        UIElement? buttonB = _buttonBField?.GetValue(mods) as UIElement;
        UIElement? buttonOMF = _buttonOMFField?.GetValue(mods) as UIElement;
        UIElement? buttonCL = _buttonCLField?.GetValue(mods) as UIElement;

        // Get the parent UIElement to check HasChild
        UIElement? modsElement = mods as UIElement;

        // Create bindings for category/filter buttons (top row)
        var filterBindings = new List<PointBinding>();
        if (categoryButtons is not null)
        {
            string[] filterLabels = new[]
            {
                GetSortModeLabel(mods),
                GetEnabledFilterLabel(mods),
                GetModSideFilterLabel(mods),
                GetRamUsageLabel(),
                GetSearchFilterLabel(mods)
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

        // Create bindings for ALL mod items (not just visible ones)
        // We'll use the items list which contains all mods, then handle scrolling separately
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
                    UIElement? toggleElement = _uiModStateTextField?.GetValue(item) as UIElement;
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

                    // Get More Info button (always present, but verify it's attached to UI)
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

                    // Get Delete button (only present when mod not loaded AND can be deleted)
                    UIElement? deleteButton = _deleteModButtonField?.GetValue(item) as UIElement;
                    if (deleteButton?.Parent is not null)
                    {
                        CalculatedStyle deleteDims = deleteButton.GetDimensions();
                        Vector2 deleteCenter = new(deleteDims.X + deleteDims.Width / 2f, deleteDims.Y + deleteDims.Height / 2f);
                        var deleteBinding = new PointBinding(nextId++, deleteCenter, "Delete", string.Empty, deleteButton, PointType.ModItemButton);
                        bindings.Add(deleteBinding);
                        BindingById[deleteBinding.Id] = deleteBinding;
                        deleteId = deleteBinding.Id;
                    }

                    // Get Config button (only present when mod has config)
                    UIElement? configButton = _configButtonField?.GetValue(item) as UIElement;
                    if (configButton?.Parent is not null)
                    {
                        CalculatedStyle configDims = configButton.GetDimensions();
                        Vector2 configCenter = new(configDims.X + configDims.Width / 2f, configDims.Y + configDims.Height / 2f);
                        var configBinding = new PointBinding(nextId++, configCenter, "Config", string.Empty, configButton, PointType.ModItemButton);
                        bindings.Add(configBinding);
                        BindingById[configBinding.Id] = configBinding;
                        configId = configBinding.Id;
                    }

                    // Store button group for navigation
                    ModItemButtonGroups.Add(new ModItemButtonGroup(modIndex, toggleId, moreInfoId, deleteId, configId));
                    modIndex++;
                }
            }

        }

        // Create bindings for top row action buttons (Enable All, Disable All, Force Reload)
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

        // Create bindings for bottom row action buttons (Back, Open Mods Folder, Mod Config)
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

        // Log what we created
        if (Main.GameUpdateCount % 60 == 0) // Log once per second
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Bindings: {filterBindings.Count} filters, {modBindings.Count} mods, {topActionBindings.Count} top actions, {bottomActionBindings.Count} bottom actions");
        }

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
                // Connect all filters down to first mod
                foreach (var filter in filterBindings)
                {
                    UILinkPoint filterPoint = EnsureLinkPoint(filter.Id);
                    filterPoint.Down = modBindings[0].Id;
                }
                // Connect first mod up to middle filter
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

        // Connect last mod item down to top action buttons
        if (modBindings.Count > 0 && topActionBindings.Count > 0)
        {
            UILinkPoint lastModPoint = EnsureLinkPoint(modBindings[^1].Id);
            lastModPoint.Down = topActionBindings[topActionBindings.Count / 2].Id;

            foreach (var action in topActionBindings)
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

        // Force initial focus
        if (PlayerInput.UsingGamepadUI && _initialFocusFramesRemaining > 0)
        {
            // Default to first mod item if available, otherwise first filter button
            int defaultPointId = modBindings.Count > 0 ? modBindings[0].Id :
                                 filterBindings.Count > 0 ? filterBindings[0].Id :
                                 topActionBindings.Count > 0 ? topActionBindings[0].Id :
                                 bindings[0].Id;

            UILinkPointNavigator.ChangePoint(defaultPointId);
            _initialFocusFramesRemaining--;
        }

        // Handle right stick scrolling (from virtual stick via OKLS keys)
        HandleRightStickScroll(modList, scrollbar, items);

        // Handle scrolling when navigating mod list with D-pad
        HandleModListScrolling(modList, scrollbar, items);
    }

    private static void HandleModListScrolling(UIList? modList, UIScrollbar? scrollbar, IList? items)
    {
        if (modList is null || scrollbar is null || items is null || items.Count == 0)
        {
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        ModItemBinding currentModBinding = ModItemBindings.FirstOrDefault(b => b.Id == currentPoint);

        if (currentModBinding.Id == 0) // default struct value means not found
        {
            return;
        }

        // Find the index of the current mod item
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

        // Scroll to ensure the item is visible
        if (items[modIndex] is UIElement modItem)
        {
            float itemTop = modItem.Top.Pixels;
            float itemHeight = modItem.GetOuterDimensions().Height;
            float viewHeight = modList.GetInnerDimensions().Height;
            float currentScroll = scrollbar.ViewPosition;

            // If item is above visible area, scroll up
            if (itemTop < currentScroll)
            {
                scrollbar.ViewPosition = itemTop;
            }
            // If item is below visible area, scroll down
            else if (itemTop + itemHeight > currentScroll + viewHeight)
            {
                scrollbar.ViewPosition = itemTop + itemHeight - viewHeight;
            }
        }
    }

    private static void HandleRightStickScroll(UIList? modList, UIScrollbar? scrollbar, IList? items)
    {
        // Only handle scroll when in mod list region
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

        // Check right analog stick vertical movement (from virtual stick or real gamepad)
        // PlayerInput.GamepadThumbstickRight is set by VirtualStickService from OKLS keys
        float rightStickY = PlayerInput.GamepadThumbstickRight.Y;
        const float scrollThreshold = 0.1f;

        // Apply scroll if stick is deflected
        if (Math.Abs(rightStickY) >= scrollThreshold)
        {
            // Scroll speed similar to vanilla (16 pixels per frame at full deflection)
            // Negative because stick up (positive Y) should scroll up (decrease ViewPosition)
            float scrollAmount = -rightStickY * 16f;
            scrollbar.ViewPosition += scrollAmount;
        }

        float currentScroll = scrollbar.ViewPosition;

        // Check if scroll position changed significantly for announcement
        if (Math.Abs(currentScroll - _lastScrollPosition) < 5f && _lastScrollPosition >= 0)
        {
            return; // Not enough scroll change for announcement
        }

        _lastScrollPosition = currentScroll;

        // Find the mod item that's currently in the center of the viewport
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

        // If the centered mod changed, announce it
        if (closestModIndex >= 0 && closestModIndex != _lastScrollAnnouncedModIndex)
        {
            _lastScrollAnnouncedModIndex = closestModIndex;

            // Get the mod item to announce
            if (items[closestModIndex] is object modItem)
            {
                string modName = GetModDisplayName(modItem);
                string modStatus = GetModStatus(modItem);
                string announcement = string.IsNullOrEmpty(modStatus)
                    ? $"{modName}, {closestModIndex + 1} of {items.Count}"
                    : $"{modName}, {modStatus}, {closestModIndex + 1} of {items.Count}";

                SoundEngine.PlaySound(SoundID.MenuTick);
                ScreenReaderService.Announce(announcement, force: true);

                // Update the focus to match scroll position
                _currentFocusIndex = closestModIndex;
                _currentModButtonIndex = 0;

                ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Scroll announced: {announcement}");
            }
        }
    }

    // Analog stick previous state for debouncing
    private static bool _stickLeftWasPressed;
    private static bool _stickRightWasPressed;
    private static bool _stickUpWasPressed;
    private static bool _stickDownWasPressed;

    // A button state for toggle re-announcement
    private static bool _aButtonWasPressed;

    // Keyboard navigation state tracking
    private static bool _keyLeftWasPressed;
    private static bool _keyRightWasPressed;
    private static bool _keyUpWasPressed;
    private static bool _keyDownWasPressed;
    private static bool _keyEnterWasPressed;
    private static bool _keySpaceWasPressed;

    private static void HandleManualNavigation(object mods)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        KeyboardState kbState = Main.keyState;

        bool leftPressed = false;
        bool rightPressed = false;
        bool upPressed = false;
        bool downPressed = false;

        // Handle gamepad input
        if (gpState.IsConnected)
        {
            // Detect new presses (transition from not pressed to pressed) for D-pad
            leftPressed = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
            rightPressed = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
            upPressed = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
            downPressed = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

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

            // Only trigger on new stick movement (transition from not pressed to pressed)
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
        }

        // Handle keyboard input when not in search mode
        if (!SearchModeManager.IsSearchModeActive)
        {
            // Check arrow keys and WASD
            bool keyLeftNow = kbState.IsKeyDown(Keys.Left) || kbState.IsKeyDown(Keys.A);
            bool keyRightNow = kbState.IsKeyDown(Keys.Right) || kbState.IsKeyDown(Keys.D);
            bool keyUpNow = kbState.IsKeyDown(Keys.Up) || kbState.IsKeyDown(Keys.W);
            bool keyDownNow = kbState.IsKeyDown(Keys.Down) || kbState.IsKeyDown(Keys.S);

            // Detect new key presses
            if (!leftPressed && keyLeftNow && !_keyLeftWasPressed)
            {
                leftPressed = true;
            }
            if (!rightPressed && keyRightNow && !_keyRightWasPressed)
            {
                rightPressed = true;
            }
            if (!upPressed && keyUpNow && !_keyUpWasPressed)
            {
                upPressed = true;
            }
            if (!downPressed && keyDownNow && !_keyDownWasPressed)
            {
                downPressed = true;
            }

            // Update keyboard previous state
            _keyLeftWasPressed = keyLeftNow;
            _keyRightWasPressed = keyRightNow;
            _keyUpWasPressed = keyUpNow;
            _keyDownWasPressed = keyDownNow;
        }

        if (!leftPressed && !rightPressed && !upPressed && !downPressed)
        {
            return;
        }

        // Log the input
        string direction = leftPressed ? "LEFT" : rightPressed ? "RIGHT" : upPressed ? "UP" : "DOWN";
        ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Input: {direction}, current region: {_currentRegion}, index: {_currentFocusIndex}");

        // Only sync from UILinkPointNavigator if we haven't initialized yet
        // Don't sync on every input - that causes double-movement because native navigation also moves
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
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Force focus to mod list, point {newPoint}");
            }
            else if (BottomActionBindingsList.Count > 0)
            {
                _currentRegion = FocusRegion.BottomActionButtons;
                _currentFocusIndex = 0;
                int newPoint = BottomActionBindingsList[0].Id;
                UILinkPointNavigator.ChangePoint(newPoint);
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Force focus to back button, point {newPoint}");
            }
            return;
        }

        // Don't sync from UILinkPointNavigator - trust our own state tracking
        // Native navigation also processes input, which would cause double-movement

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
            // Update the UILinkPointNavigator to the new focus
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Navigated to region {_currentRegion}, index {_currentFocusIndex}, point {newPointId.Value}");
            }
        }
        else
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Navigation blocked - at edge of region {_currentRegion}");
        }
    }

    // Track the last known state for each mod to detect changes
    private static readonly Dictionary<object, bool> _lastKnownModStates = new();

    // Track when we toggled to avoid double-announcement
    private static int _toggleCooldownFrames;

    private static void HandleActionButton(object mods)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        KeyboardState kbState = Main.keyState;

        // Decrement cooldowns
        if (_toggleCooldownFrames > 0)
        {
            _toggleCooldownFrames--;
        }
        if (_dialogActionCooldown > 0)
        {
            _dialogActionCooldown--;
        }

        bool actionJustPressed = false;

        // Check for A button press (gamepad)
        if (gpState.IsConnected)
        {
            bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
            bool aJustPressed = aPressed && !_aButtonWasPressed;
            _aButtonWasPressed = aPressed;

            if (aJustPressed)
            {
                actionJustPressed = true;
            }
        }

        // Check for Enter/Space key (keyboard) when not in search mode
        if (!SearchModeManager.IsSearchModeActive)
        {
            bool enterNow = kbState.IsKeyDown(Keys.Enter);
            bool spaceNow = kbState.IsKeyDown(Keys.Space);

            bool enterJustPressed = enterNow && !_keyEnterWasPressed;
            bool spaceJustPressed = spaceNow && !_keySpaceWasPressed;

            _keyEnterWasPressed = enterNow;
            _keySpaceWasPressed = spaceNow;

            if (enterJustPressed || spaceJustPressed)
            {
                actionJustPressed = true;
            }
        }

        // When action is pressed on a mod item, toggle it and announce
        // Also check dialog cooldown to prevent accidental re-triggering after closing dialog
        if (actionJustPressed && _currentRegion == FocusRegion.ModList && _toggleCooldownFrames == 0 && _dialogActionCooldown == 0)
        {
            // Check if we're on a mod item button (More Info, Delete, Config)
            if (_currentModButtonIndex > 0)
            {
                // Get the current button point and click it
                int? currentPointId = GetCurrentPointId();
                if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
                {
                    if (binding.Element is UIElement buttonElement)
                    {
                        ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Clicking mod button: {binding.Label}");
                        SoundEngine.PlaySound(SoundID.MenuTick);

                        // Simulate a click on the button
                        try
                        {
                            // Get button center position for accurate click
                            CalculatedStyle dims = buttonElement.GetDimensions();
                            Vector2 buttonCenter = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);

                            // Create mouse event at button's actual position
                            var clickEvent = new UIMouseEvent(buttonElement, buttonCenter);

                            // For Delete button, invoke the QuickModDelete method directly on the UIModItem
                            if (binding.Label == "Delete" && _currentFocusIndex >= 0 && _currentFocusIndex < ModItemBindings.Count)
                            {
                                var modItemBinding = ModItemBindings[_currentFocusIndex];
                                object? modItem = modItemBinding.ModItem;
                                if (modItem is not null && _uiModItemType is not null)
                                {
                                    // Find and invoke QuickModDelete method
                                    var quickModDeleteMethod = _uiModItemType.GetMethod("QuickModDelete", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (quickModDeleteMethod is not null)
                                    {
                                        ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Invoking QuickModDelete directly");
                                        quickModDeleteMethod.Invoke(modItem, new object[] { clickEvent, buttonElement });

                                        // Log state immediately after invoke
                                        UIElement? blockInputAfter = _blockInputField?.GetValue(mods) as UIElement;
                                        bool hasBlockInput = blockInputAfter is not null;
                                        bool isChild = hasBlockInput && (mods as UIElement)?.HasChild(blockInputAfter!) == true;
                                        ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] After QuickModDelete: blockInput exists={hasBlockInput}, isChild={isChild}");

                                        // CRITICAL: Consume input to prevent native UI from closing the dialog
                                        // The dialog's _blockInput has OnLeftMouseDown = CloseConfirmDialog, so if
                                        // native gamepad handling also processes the A button as a click, it will
                                        // immediately close the dialog we just opened
                                        Main.mouseLeft = false;
                                        Main.mouseLeftRelease = false;

                                        // Add cooldown to prevent rapid re-clicking before dialog is detected
                                        _dialogActionCooldown = 30; // Half second cooldown
                                    }
                                    else
                                    {
                                        ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] QuickModDelete method not found");
                                        buttonElement.LeftClick(clickEvent);
                                    }
                                }
                            }
                            else
                            {
                                // For other buttons, use LeftClick
                                buttonElement.LeftClick(clickEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] Button click failed: {ex.Message}");
                        }
                    }
                }
                return;
            }

            // Find the current mod for toggle
            ModItemBinding? foundBinding = null;

            if (_currentFocusIndex >= 0 && _currentFocusIndex < ModItemBindings.Count)
            {
                foundBinding = ModItemBindings[_currentFocusIndex];
            }

            ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] A pressed, focusIndex={_currentFocusIndex}, foundBinding={foundBinding is not null}");

            if (foundBinding is not null)
            {
                object? modItem = foundBinding.Value.ModItem;
                if (modItem is not null)
                {
                    // Get current state and toggle
                    bool currentState = GetModEnabledState(modItem);
                    bool newState = !currentState;

                    try
                    {
                        object? localMod = _modField?.GetValue(modItem);
                        if (localMod is not null && _enabledProperty is not null)
                        {
                            // Set the new enabled state
                            _enabledProperty.SetValue(localMod, newState);

                            // Update the UI to reflect the change
                            var updateMethod = _uiModItemType?.GetMethod("UpdateUIForEnabledChange",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            updateMethod?.Invoke(modItem, null);

                            // Don't play sound here - native UI click will play it via ToggleEnabled

                            // Announce the new state
                            string modDisplayName = GetModDisplayName(modItem);
                            string stateText = newState ? "Enabled" : "Disabled";
                            string announcement = $"{modDisplayName} {stateText}";

                            ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Toggled: {announcement}");
                            ScreenReaderService.Announce(announcement, force: true);

                            // Set cooldown to prevent native double-toggle from triggering again
                            // The native UI will also toggle via Main.mouseLeft, so we need to
                            // toggle back to counteract it (or we can just accept the final state)
                            _toggleCooldownFrames = 10;
                            _lastKnownModStates[modItem] = newState;
                        }
                    }
                    catch (Exception ex)
                    {
                        ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] Toggle failed: {ex.Message}");
                    }
                }
            }
        }

        // Monitor for native toggles and re-toggle to counteract them
        // This handles the case where native UI also toggles via Main.mouseLeft
        if (_toggleCooldownFrames > 0 && _toggleCooldownFrames <= 8)
        {
            foreach (var binding in ModItemBindings)
            {
                object? modItem = binding.ModItem;
                if (modItem is not null && _lastKnownModStates.TryGetValue(modItem, out bool expectedState))
                {
                    bool actualState = GetModEnabledState(modItem);
                    if (actualState != expectedState)
                    {
                        // Native toggle changed our state back! Toggle again to restore
                        try
                        {
                            object? localMod = _modField?.GetValue(modItem);
                            if (localMod is not null && _enabledProperty is not null)
                            {
                                _enabledProperty.SetValue(localMod, expectedState);
                                var updateMethod = _uiModItemType?.GetMethod("UpdateUIForEnabledChange",
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                updateMethod?.Invoke(modItem, null);
                                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Counteracted native toggle, restored to {expectedState}");
                            }
                        }
                        catch { }
                    }
                }
            }
        }
    }

    private static bool GetModEnabledState(object? modItem)
    {
        if (modItem is null)
        {
            return false;
        }

        try
        {
            object? localMod = _modField?.GetValue(modItem);
            if (localMod is null || _enabledProperty is null)
            {
                return false;
            }

            return (bool)(_enabledProperty.GetValue(localMod) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static void SyncRegionFromCurrentPoint(int currentPoint)
    {
        // Check each region to find where the current point is
        for (int i = 0; i < FilterBindings.Count; i++)
        {
            if (FilterBindings[i].Id == currentPoint)
            {
                _currentRegion = FocusRegion.FilterButtons;
                _currentFocusIndex = i;
                return;
            }
        }

        for (int i = 0; i < ModBindingsList.Count; i++)
        {
            if (ModBindingsList[i].Id == currentPoint)
            {
                _currentRegion = FocusRegion.ModList;
                _currentFocusIndex = i;
                return;
            }
        }

        for (int i = 0; i < TopActionBindingsList.Count; i++)
        {
            if (TopActionBindingsList[i].Id == currentPoint)
            {
                _currentRegion = FocusRegion.TopActionButtons;
                _currentFocusIndex = i;
                return;
            }
        }

        for (int i = 0; i < BottomActionBindingsList.Count; i++)
        {
            if (BottomActionBindingsList[i].Id == currentPoint)
            {
                _currentRegion = FocusRegion.BottomActionButtons;
                _currentFocusIndex = i;
                return;
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
                // Only navigate within filter buttons, don't escape to back button
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    return true;
                }
                break;

            case FocusRegion.ModList:
                // LEFT only navigates back through mod item buttons, doesn't escape to back button
                if (_currentModButtonIndex > 0)
                {
                    _currentModButtonIndex--;
                    ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] LEFT in mod list -> button index {_currentModButtonIndex}");
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
                // RIGHT navigates through mod item buttons (More Info, Delete, Config)
                if (_currentFocusIndex >= 0 && _currentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[_currentFocusIndex];
                    int maxButtonIndex = buttonGroup.ButtonCount - 1;

                    if (_currentModButtonIndex < maxButtonIndex)
                    {
                        _currentModButtonIndex++;
                        ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] RIGHT in mod list -> button index {_currentModButtonIndex}");
                        return true;
                    }
                }
                break;

            case FocusRegion.TopActionButtons:
                // RIGHT moves between top action buttons only
                if (_currentFocusIndex < TopActionBindingsList.Count - 1)
                {
                    _currentFocusIndex++;
                    return true;
                }
                // Don't jump to mod list - user must press UP to return to mod list
                break;

            case FocusRegion.BottomActionButtons:
                // RIGHT moves between bottom action buttons only
                if (_currentFocusIndex < BottomActionBindingsList.Count - 1)
                {
                    _currentFocusIndex++;
                    return true;
                }
                // Don't jump to mod list - user must press UP to navigate up
                break;
        }

        return false;
    }

    private static bool NavigateUp()
    {
        switch (_currentRegion)
        {
            case FocusRegion.FilterButtons:
                // No up navigation from filter buttons
                break;

            case FocusRegion.ModList:
                if (_currentFocusIndex > 0)
                {
                    _currentFocusIndex--;
                    _currentModButtonIndex = 0; // Reset button index when moving to different mod
                    return true;
                }
                // UP from first mod goes to filter buttons
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    _currentFocusIndex = FilterBindings.Count / 2;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                // UP from top action goes to last mod item
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    _currentFocusIndex = ModBindingsList.Count - 1;
                    _currentModButtonIndex = 0;
                    return true;
                }
                // Or to filter buttons if no mods
                if (FilterBindings.Count > 0)
                {
                    _currentRegion = FocusRegion.FilterButtons;
                    _currentFocusIndex = Math.Min(_currentFocusIndex, FilterBindings.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                // UP from bottom action goes to top action
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    _currentFocusIndex = Math.Min(_currentFocusIndex, TopActionBindingsList.Count - 1);
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
                // DOWN from filters goes to first mod
                if (ModBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.ModList;
                    _currentFocusIndex = 0;
                    _currentModButtonIndex = 0;
                    return true;
                }
                // Or to top action buttons if no mods
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
                    _currentModButtonIndex = 0; // Reset button index when moving to different mod
                    return true;
                }
                // DOWN from last mod goes to top action buttons
                if (TopActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.TopActionButtons;
                    _currentFocusIndex = TopActionBindingsList.Count / 2;
                    _currentModButtonIndex = 0;
                    return true;
                }
                break;

            case FocusRegion.TopActionButtons:
                // DOWN from top action goes to bottom action
                if (BottomActionBindingsList.Count > 0)
                {
                    _currentRegion = FocusRegion.BottomActionButtons;
                    _currentFocusIndex = Math.Min(_currentFocusIndex, BottomActionBindingsList.Count - 1);
                    return true;
                }
                break;

            case FocusRegion.BottomActionButtons:
                // No down navigation from bottom buttons
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
        // Try to get display name from the property
        string? displayName = _displayNameCleanProperty?.GetValue(modItem) as string;
        if (!string.IsNullOrEmpty(displayName))
        {
            return displayName;
        }

        // Fallback to mod name
        return _modNameProperty?.GetValue(modItem) as string ?? "Unknown Mod";
    }

    private static string GetModStatus(object modItem)
    {
        try
        {
            object? localMod = _modField?.GetValue(modItem);
            if (localMod is null || _enabledProperty is null)
            {
                return string.Empty;
            }

            bool enabled = (bool)(_enabledProperty.GetValue(localMod) ?? false);
            return enabled ? Language.GetTextValue("GameUI.Enabled") : Language.GetTextValue("GameUI.Disabled");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetSortModeLabel(object mods)
    {
        object? sortMode = _sortModeField?.GetValue(mods);
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
        object? filterMode = _enabledFilterModeField?.GetValue(mods);
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
        object? filterMode = _modSideFilterModeField?.GetValue(mods);
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
        object? filterMode = _searchFilterModeField?.GetValue(mods);
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

    private static void AnnounceCurrentFocus(object mods)
    {
        // Get the point we're supposed to be at based on our navigation state
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
            ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] No binding found for point {currentPoint}");
            return;
        }

        // In search mode, only announce mod items (not filters or action buttons)
        // This lets users hear the first mod that matches their search
        if (SearchModeManager.IsSearchModeActive &&
            binding.Type != PointType.ModItem &&
            binding.Type != PointType.ModItemButton)
        {
            return;
        }

        string announcement = BuildAnnouncement(binding);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);

        ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private static string BuildAnnouncement(PointBinding binding)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // Add context based on type
        switch (binding.Type)
        {
            case PointType.FilterButton:
                int filterIndex = _currentFocusIndex + 1;
                int filterTotal = FilterBindings.Count;
                return $"Filter: {label}, {filterIndex} of {filterTotal}";

            case PointType.ModItem:
                // Add position in list
                int modIndex = _currentFocusIndex + 1;
                int modTotal = ModBindingsList.Count;
                return $"{label}, {modIndex} of {modTotal}";

            case PointType.ModItemButton:
                // Announce the button with context about available buttons
                if (_currentFocusIndex >= 0 && _currentFocusIndex < ModItemButtonGroups.Count)
                {
                    var buttonGroup = ModItemButtonGroups[_currentFocusIndex];
                    int buttonCount = buttonGroup.ButtonCount;
                    // Button index 0 is toggle, so buttons are index 1+
                    int buttonNumber = _currentModButtonIndex;
                    int totalButtons = buttonCount - 1; // Exclude toggle from button count
                    return $"{label}, button {buttonNumber} of {totalButtons}";
                }
                return label;

            case PointType.BackButton:
                return label;

            case PointType.ActionButton:
                return label;

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

    #region Dialog Handling

    /// <summary>
    /// Sets up UILinkPointNavigator with dialog-specific link points.
    /// This prevents native gamepad handling from clicking menu buttons while the dialog is open.
    /// </summary>
    private static void SetupDialogLinkPoints()
    {
        // Create link points for the dialog Yes/No buttons
        UILinkPoint yesPoint = EnsureLinkPoint(DialogYesPointId);
        UILinkPoint noPoint = EnsureLinkPoint(DialogNoPointId);

        // Link them vertically (Yes above No)
        yesPoint.Up = -1;
        yesPoint.Down = DialogNoPointId;
        yesPoint.Left = -1;
        yesPoint.Right = -1;

        noPoint.Up = DialogYesPointId;
        noPoint.Down = -1;
        noPoint.Left = -1;
        noPoint.Right = -1;

        // Set current point to Yes button (or No if focus is 1)
        int dialogPoint = _dialogFocusIndex == 0 ? DialogYesPointId : DialogNoPointId;
        UILinkPointNavigator.ChangePoint(dialogPoint);

        ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Dialog link points set up, focus on {dialogPoint}");
    }

    /// <summary>
    /// Cleans up dialog link points and restores focus to a safe menu point.
    /// </summary>
    private static void CleanupDialogLinkPoints()
    {
        // Remove dialog link points
        UILinkPointNavigator.Points.Remove(DialogYesPointId);
        UILinkPointNavigator.Points.Remove(DialogNoPointId);

        // Restore focus to a safe point - the mod toggle button (not Delete)
        // We use _currentFocusIndex which points to the mod item, and button index 0 (toggle)
        if (_currentFocusIndex >= 0 && _currentFocusIndex < ModBindingsList.Count)
        {
            int safePointId = ModBindingsList[_currentFocusIndex].Id;
            UILinkPointNavigator.ChangePoint(safePointId);
            ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Restored focus to mod toggle point {safePointId}");
        }
        else if (BottomActionBindingsList.Count > 0)
        {
            // Fallback to Back button
            int backPointId = BottomActionBindingsList[0].Id;
            UILinkPointNavigator.ChangePoint(backPointId);
            ScreenReaderMod.Instance?.Logger.Debug($"[ManageMods] Restored focus to Back button {backPointId}");
        }
    }

    /// <summary>
    /// Checks if a confirmation dialog is currently active by looking for _blockInput.
    /// </summary>
    private static bool IsConfirmDialogActive(object mods)
    {
        if (_blockInputField is null)
        {
            // Only log once per second to avoid spam
            if (Main.GameUpdateCount % 60 == 0)
                ScreenReaderMod.Instance?.Logger.Debug("[ManageMods] Dialog check: _blockInputField is null");
            return false;
        }

        try
        {
            UIElement? blockInput = _blockInputField.GetValue(mods) as UIElement;
            if (blockInput is null)
            {
                return false;
            }

            // Check if blockInput is currently a child of the mods menu
            if (mods is UIElement modsElement)
            {
                bool hasChild = modsElement.HasChild(blockInput);
                if (hasChild)
                {
                    ScreenReaderMod.Instance?.Logger.Debug("[ManageMods] Dialog check: _blockInput IS a child - dialog active");
                }
                return hasChild;
            }
            else
            {
                ScreenReaderMod.Instance?.Logger.Debug("[ManageMods] Dialog check: mods is not UIElement");
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] Dialog check exception: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Gets the dialog text for announcement.
    /// </summary>
    private static string? GetDialogText(object mods)
    {
        try
        {
            // First try the UIMods confirm dialog text
            UIElement? dialogText = _confirmDialogTextField?.GetValue(mods) as UIElement;
            if (dialogText is UIText uiText)
            {
                return uiText.Text;
            }

            // Also check the mod items for their dialog text
            IList? items = _itemsField?.GetValue(mods) as IList;
            if (items is not null)
            {
                foreach (object? item in items)
                {
                    if (item is not null)
                    {
                        UIElement? modItemDialogText = _modItemDialogTextField?.GetValue(item) as UIElement;
                        if (modItemDialogText is UIText modItemUiText && !string.IsNullOrEmpty(modItemUiText.Text))
                        {
                            return modItemUiText.Text;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return Language.GetTextValue("tModLoader.DeleteModConfirm");
    }

    /// <summary>
    /// Configures navigation points for the confirmation dialog.
    /// </summary>
    private static void ConfigureDialogPoints(object mods)
    {
        DialogBindings.Clear();

        try
        {
            // Try to find the Yes/No buttons from UIMods first
            UIElement? yesButton = _confirmDialogYesButtonField?.GetValue(mods) as UIElement;
            UIElement? noButton = _confirmDialogNoButtonField?.GetValue(mods) as UIElement;

            // If not found, check each mod item for its dialog buttons
            if (yesButton is null || noButton is null)
            {
                IList? items = _itemsField?.GetValue(mods) as IList;
                if (items is not null)
                {
                    foreach (object? item in items)
                    {
                        if (item is not null)
                        {
                            UIElement? modYesButton = _modItemDialogYesButtonField?.GetValue(item) as UIElement;
                            UIElement? modNoButton = _modItemDialogNoButtonField?.GetValue(item) as UIElement;

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
            ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] Failed to configure dialog points: {ex.Message}");
        }
    }

    // Dialog navigation input state
    private static bool _dialogUpWasPressed;
    private static bool _dialogDownWasPressed;
    private static bool _dialogAWasPressed;

    /// <summary>
    /// Handles UP/DOWN navigation in the dialog.
    /// </summary>
    private static void HandleDialogNavigation()
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        // Check D-pad (UP/DOWN for consistency with other menus)
        bool upPressed = gpState.DPad.Up == ButtonState.Pressed && !_dialogUpWasPressed;
        bool downPressed = gpState.DPad.Down == ButtonState.Pressed && !_dialogDownWasPressed;

        _dialogUpWasPressed = gpState.DPad.Up == ButtonState.Pressed;
        _dialogDownWasPressed = gpState.DPad.Down == ButtonState.Pressed;

        // Check analog stick
        Vector2 stick = gpState.ThumbSticks.Left;
        const float threshold = 0.5f;

        if (!upPressed && stick.Y > threshold && !_stickUpWasPressed)
        {
            upPressed = true;
        }
        if (!downPressed && stick.Y < -threshold && !_stickDownWasPressed)
        {
            downPressed = true;
        }

        if (upPressed && _dialogFocusIndex > 0)
        {
            _dialogFocusIndex--;
            _lastDialogAnnouncedIndex = -1; // Reset to trigger new announcement
            // Sync UILinkPointNavigator to prevent native handling issues
            UILinkPointNavigator.ChangePoint(_dialogFocusIndex == 0 ? DialogYesPointId : DialogNoPointId);
            ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Dialog: UP -> index {_dialogFocusIndex}");
        }
        else if (downPressed && _dialogFocusIndex < DialogBindings.Count - 1)
        {
            _dialogFocusIndex++;
            _lastDialogAnnouncedIndex = -1; // Reset to trigger new announcement
            // Sync UILinkPointNavigator to prevent native handling issues
            UILinkPointNavigator.ChangePoint(_dialogFocusIndex == 0 ? DialogYesPointId : DialogNoPointId);
            ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Dialog: DOWN -> index {_dialogFocusIndex}");
        }
    }

    /// <summary>
    /// Handles A button press in the dialog.
    /// </summary>
    private static void HandleDialogAction(object mods)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
        bool aJustPressed = aPressed && !_dialogAWasPressed;
        _dialogAWasPressed = aPressed;

        if (aJustPressed && _dialogFocusIndex >= 0 && _dialogFocusIndex < DialogBindings.Count)
        {
            var binding = DialogBindings[_dialogFocusIndex];
            if (binding.Element is UIElement button)
            {
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Dialog: Clicking {binding.Label} (index {_dialogFocusIndex})");
                SoundEngine.PlaySound(SoundID.MenuTick);

                try
                {
                    // Use LeftClick for both Yes and No buttons - the native button handlers
                    // will properly close the dialog. We've already set up UILinkPointNavigator
                    // with dialog-specific points to prevent the Delete button from being
                    // re-triggered by native gamepad handling.
                    var clickEvent = new UIMouseEvent(button, Main.MouseScreen);
                    button.LeftClick(clickEvent);

                    // Consume mouse input to prevent native UI from also processing this click
                    Main.mouseLeft = false;
                    Main.mouseLeftRelease = false;
                }
                catch (Exception ex)
                {
                    ScreenReaderMod.Instance?.Logger.Warn($"[ManageMods] Dialog click failed: {ex.Message}");
                }
            }
        }
    }

    // Track last dialog announcement to avoid repeats
    private static int _lastDialogAnnouncedIndex = -1;

    /// <summary>
    /// Announces the current dialog focus.
    /// </summary>
    private static void AnnounceDialogFocus()
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

            // If dialog text hasn't been announced yet, bundle it with the button
            if (!_dialogTextAnnounced && !string.IsNullOrEmpty(_dialogText))
            {
                string dialogText = TextSanitizer.Clean(_dialogText);
                string announcement = $"{dialogText} {buttonLabel}";
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Announcing dialog with button: {announcement}");
                ScreenReaderService.Announce(announcement, force: true);
                _dialogTextAnnounced = true;
            }
            else
            {
                // Just announce the button
                ScreenReaderMod.Instance?.Logger.Info($"[ManageMods] Announcing dialog button: {buttonLabel}");
                ScreenReaderService.Announce(buttonLabel, force: true);
            }
        }
    }

    #endregion

    private enum PointType
    {
        FilterButton,
        ModItem,
        ModItemButton,  // More Info, Delete, Config buttons within a mod item
        ActionButton,
        BackButton
    }

    private readonly record struct PointBinding(int Id, Vector2 Position, string Label, string Description, UIElement? Element, PointType Type);

    private readonly record struct ModItemBinding(int Id, object ModItem);

    /// <summary>
    /// Tracks the buttons available for a mod item (toggle, more info, delete, config).
    /// </summary>
    private readonly record struct ModItemButtonGroup(
        int ModIndex,           // Index in ModBindingsList
        int ToggleId,           // ID for the mod toggle button
        int? MoreInfoId,        // ID for the More Info button (always present)
        int? DeleteId,          // ID for the Delete button (only when mod not loaded)
        int? ConfigId           // ID for the Config button (only when mod has config)
    )
    {
        /// <summary>
        /// Returns the button ID at the given index (0 = toggle, 1 = more info, 2+ = delete/config).
        /// </summary>
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

        /// <summary>
        /// Returns the total number of navigable buttons for this mod item.
        /// </summary>
        public int ButtonCount
        {
            get
            {
                int count = 1; // Toggle is always present
                if (MoreInfoId.HasValue) count++;
                if (DeleteId.HasValue) count++;
                if (ConfigId.HasValue) count++;
                return count;
            }
        }
    }
}
