#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

/// <summary>
/// Provides screen reader narration and gamepad navigation for the tModLoader Mod Configuration menu.
/// Handles both the mod selection list (UIModConfigList) and individual config editing (UIModConfig).
/// </summary>
internal sealed class ModConfigMenuNarrator
{
    private readonly MenuUiSelectionTracker _uiTracker = new();

    /// <summary>
    /// Returns true if this narrator is currently handling a mod config screen.
    /// Used by DefaultMenuNarrationHandler to suppress hover announcements.
    /// </summary>
    public static bool IsHandlingGamepadInput { get; private set; }

    // Type references for tModLoader config UI
    private static readonly Type? ModConfigListType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfigList, tModLoader");
    private static readonly Type? ModConfigStateType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfig, tModLoader");
    private static readonly Type? ModType = Type.GetType("Terraria.ModLoader.Mod, tModLoader");
    private static readonly Type? ModConfigType = Type.GetType("Terraria.ModLoader.Config.ModConfig, tModLoader");
    private static readonly Type? ConfigElementType = Type.GetType("Terraria.ModLoader.Config.UI.ConfigElement, tModLoader");
    private static readonly Type? UIModConfigItemType = Type.GetType("Terraria.ModLoader.UI.UIModConfigItem, tModLoader");
    private static readonly Type? UIButtonType = Type.GetType("Terraria.ModLoader.UI.UIButton`1, tModLoader");

    // Field accessors for UIModConfigList
    private static readonly FieldInfo? SelectedModField = ModConfigListType?.GetField("selectedMod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ModListField = ModConfigListType?.GetField("modList", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ConfigListField = ModConfigListType?.GetField("configList", BindingFlags.Instance | BindingFlags.NonPublic);

    // Field accessors for UIModConfig
    private static readonly FieldInfo? EditingModField = ModConfigStateType?.GetField("mod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ActiveConfigField = ModConfigStateType?.GetField("modConfig", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MainConfigListField = ModConfigStateType?.GetField("mainConfigList", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? UiListField = ModConfigStateType?.GetField("uIList", BindingFlags.Instance | BindingFlags.NonPublic);

    // Field accessors for UIModConfig action buttons
    private static readonly FieldInfo? SaveConfigButtonField = ModConfigStateType?.GetField("saveConfigButton", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? BackButtonField = ModConfigStateType?.GetField("backButton", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? RevertConfigButtonField = ModConfigStateType?.GetField("revertConfigButton", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? RestoreDefaultsButtonField = ModConfigStateType?.GetField("restoreDefaultsConfigButton", BindingFlags.Instance | BindingFlags.NonPublic);

    // Property accessors
    private static readonly PropertyInfo? ModDisplayNameProperty = ModType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModInternalNameProperty = ModType?.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModConfigDisplayNameProperty = ModConfigType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModConfigFullNameProperty = ModConfigType?.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? LocalizedValueProperty = typeof(LocalizedText).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

    // Cached reflection info for config elements
    private static readonly Dictionary<Type, ConfigElementAccessors> ConfigElementAccessorCache = new();
    private static bool _loggedTypes;
    private static bool _loggedTypeResolution;

    // Navigation state
    private UIState? _lastState;
    private string? _lastModName;
    private string? _lastConfigLabel;
    private bool _listIntroAnnounced;
    private bool _detailIntroAnnounced;
    private string? _lastHoverAnnouncement;
    private int _currentElementIndex = -1;
    private List<UIElement>? _navigableElements;
    private UIElement? _lastNavigatedElement;
    private bool _stateChanged;
    private int _suppressHoverFrames; // Frames to suppress hover announcements after keyboard nav
    private int _configElementCount; // Number of config elements (excluding action buttons)

    // Dual-list navigation state for UIModConfigList (mod list on left, config list on right)
    private enum ListFocus { ModList, ConfigList }
    private ListFocus _currentListFocus = ListFocus.ModList;
    private int _modListIndex = -1;
    private int _configListIndex = -1;
    private List<UIElement>? _modListElements;
    private List<UIElement>? _configListElements;
    private bool _pendingConfigListNavigation; // Set when a mod is selected, cleared after navigating to config list

    public void Reset()
    {
        _lastState = null;
        _lastModName = null;
        _lastConfigLabel = null;
        _listIntroAnnounced = false;
        _detailIntroAnnounced = false;
        _lastHoverAnnouncement = null;
        _currentElementIndex = -1;
        _navigableElements = null;
        _lastNavigatedElement = null;
        _stateChanged = false;
        _suppressHoverFrames = 0;
        _uiTracker.Reset();

        // Reset dual-list state
        _currentListFocus = ListFocus.ModList;
        _modListIndex = -1;
        _configListIndex = -1;
        _modListElements = null;
        _configListElements = null;
        _pendingConfigListNavigation = false;

        // Clear the flag so DefaultMenuNarrationHandler knows we're not handling input
        IsHandlingGamepadInput = false;
    }

    private static void AddEvent(ICollection<MenuNarrationEvent> target, string? text, bool force, MenuNarrationEventKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        target.Add(new MenuNarrationEvent(text, force, kind));
    }

    // Menu mode constants for mod config screens (from Interface.cs)
    private const int ModConfigListMenuMode = 10027;
    private const int ModConfigEditMenuMode = 10024;

    public bool TryBuildMenuEvents(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        // Accept FancyUI (in-game settings overlay) or the mod config menu modes from main menu
        bool isValidMenuMode = context.MenuMode == MenuID.FancyUI ||
                               context.MenuMode == ModConfigListMenuMode ||
                               context.MenuMode == ModConfigEditMenuMode;

        if (!isValidMenuMode)
        {
            if (_lastState is not null)
            {
                ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] Menu mode changed, calling Reset()");
            }
            Reset();
            return false;
        }

        return TryHandleState(
            context.UiState,
            Main.MenuUI,
            alignCursor: false,
            enableNavigation: true,
            (text, force, kind) => AddEvent(events, text, force, kind));
    }

    public bool TryHandleIngameUi(UserInterface? inGameUi, bool requiresPause)
    {
        UIState? currentState = inGameUi?.CurrentState;
        if (currentState is null)
        {
            Reset();
            return false;
        }

        // Check if we're on a mod config screen - these should work even without pause (for multiplayer)
        Type stateType = currentState.GetType();
        bool isModConfigScreen = (ModConfigListType is not null && ModConfigListType.IsAssignableFrom(stateType)) ||
                                  (ModConfigStateType is not null && ModConfigStateType.IsAssignableFrom(stateType));

        if (!isModConfigScreen)
        {
            // For non-mod-config screens, respect the pause requirement
            if (!requiresPause)
            {
                Reset();
                return false;
            }
        }

        // Only skip mod config when TryBuildMenuEvents will handle it (when MenuMode is FancyUI)
        // This prevents double processing from the two ModConfigMenuNarrator instances
        if (isModConfigScreen && Main.menuMode == MenuID.FancyUI)
        {
            return false;
        }

        return TryHandleState(
            currentState,
            inGameUi,
            alignCursor: true,
            enableNavigation: true,
            (text, force, kind) => ScreenReaderService.Announce(text, force));
    }

    private bool TryHandleState(
        UIState? state,
        UserInterface? uiContext,
        bool alignCursor,
        bool enableNavigation,
        Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (state is null)
        {
            Reset();
            return false;
        }

        Type stateType = state.GetType();

        if (ModConfigListType is not null && ModConfigListType.IsAssignableFrom(stateType))
        {
            // Set flag so DefaultMenuNarrationHandler suppresses hover announcements
            IsHandlingGamepadInput = enableNavigation && PlayerInput.UsingGamepadUI;
            _stateChanged = PrepareForState(state, alignCursor);
            HandleListState(state, uiContext, announce, enableNavigation);
            _lastState = state;
            return true;
        }

        if (ModConfigStateType is not null && ModConfigStateType.IsAssignableFrom(stateType))
        {
            // Set flag so DefaultMenuNarrationHandler suppresses hover announcements
            IsHandlingGamepadInput = enableNavigation && PlayerInput.UsingGamepadUI;
            _stateChanged = PrepareForState(state, alignCursor);
            HandleConfigState(state, uiContext, announce, enableNavigation);
            _lastState = state;
            return true;
        }

        Reset();
        return false;
    }

    private bool PrepareForState(UIState state, bool alignCursor)
    {
        bool stateChanged = !ReferenceEquals(_lastState, state);

        if (stateChanged)
        {
            string lastTypeName = _lastState?.GetType().Name ?? "null";
            string currentTypeName = state?.GetType().Name ?? "null";
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] PrepareForState: state changed from {lastTypeName} to {currentTypeName}");

            _uiTracker.Reset();
            _lastHoverAnnouncement = null;
            _currentElementIndex = -1;
            _navigableElements = null;
            _lastNavigatedElement = null;

            if (alignCursor && state is not null)
            {
                PositionCursorAtStateCenter(state);
            }
        }

        return stateChanged;
    }

    #region Mod List State (UIModConfigList)

    private void HandleListState(UIState state, UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce, bool enableNavigation)
    {
        if (!_listIntroAnnounced || _stateChanged)
        {
            // Skip title announcement - the first item will be announced by navigation
            _listIntroAnnounced = true;
            _detailIntroAnnounced = false;

            // Log type resolution for debugging
            LogTypeResolutionStatus();
        }

        // Handle gamepad navigation for the mod list
        if (enableNavigation)
        {
            HandleListNavigation(state, announce);
        }

        // Also track mouse hover for mixed input
        TryAnnounceListHover(state, uiContext, announce);
    }

    private void HandleListNavigation(UIState state, Action<string, bool, MenuNarrationEventKind> announce)
    {
        // Get both list elements
        UIElement? modList = ModListField?.GetValue(state) as UIElement;
        UIElement? configList = ConfigListField?.GetValue(state) as UIElement;

        if (modList is null)
        {
            ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] modList field is null - cannot navigate");
            return;
        }

        // Collect navigable items from both lists if needed
        if (_modListElements is null || _stateChanged)
        {
            _modListElements = CollectNavigableElements(modList);
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Collected {_modListElements.Count} navigable elements from modList (stateChanged={_stateChanged})");
            _modListIndex = _modListElements.Count > 0 ? 0 : -1;
            _currentListFocus = ListFocus.ModList;

            // Move cursor to and announce first item on entry if there are items
            if (_modListIndex >= 0 && _stateChanged)
            {
                MoveCursorToElement(_modListElements[_modListIndex]);
                AnnounceModListElement(_modListIndex, announce);
                _suppressHoverFrames = 30; // Suppress hover to prevent double announcement

                // Skip input processing on the first frame to avoid the A button press
                // that was used to enter the menu from triggering a mod selection
                return;
            }
        }

        // Always refresh config list elements (they change when a mod is selected)
        if (configList is not null)
        {
            var newConfigElements = CollectNavigableElements(configList);

            // Check if config list has changed (by count or by reference)
            bool configListChanged = _configListElements is null ||
                                     _configListElements.Count != newConfigElements.Count ||
                                     (_configListElements.Count > 0 && newConfigElements.Count > 0 &&
                                      !ReferenceEquals(_configListElements[0], newConfigElements[0]));

            if (configListChanged)
            {
                _configListElements = newConfigElements;
                ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Collected {_configListElements.Count} navigable elements from configList");

                // If we were focused on config list and it's now populated, reset index
                if (_currentListFocus == ListFocus.ConfigList && _configListElements.Count > 0)
                {
                    _configListIndex = 0;
                }
                else if (_configListElements.Count == 0)
                {
                    _configListIndex = -1;
                }
            }

            // Handle pending auto-navigation after selecting a mod
            // This must be checked every frame, not just when configListChanged
            if (_pendingConfigListNavigation)
            {
                if (_configListElements is not null && _configListElements.Count > 0)
                {
                    ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Auto-navigating to config list with {_configListElements.Count} configs");
                    _pendingConfigListNavigation = false;
                    _currentListFocus = ListFocus.ConfigList;
                    _configListIndex = 0;
                    MoveCursorToElement(_configListElements[0]);

                    // Only announce if there are multiple configs to choose from
                    // If there's only one, user will immediately enter it and hear the first config element
                    if (_configListElements.Count > 1)
                    {
                        AnnounceConfigListElement(0, announce);
                    }
                    return;
                }
                else
                {
                    // No configs available for this mod
                    ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] pendingConfigListNavigation but config list is empty");
                    _pendingConfigListNavigation = false;
                    string noConfigs = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.NoConfigsForMod", "This mod has no configurations.");
                    announce(noConfigs, false, MenuNarrationEventKind.ModConfig);
                }
            }
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        // Handle left/right to switch between lists
        if (justPressed.MenuLeft && _currentListFocus == ListFocus.ConfigList)
        {
            // Switch to mod list
            _currentListFocus = ListFocus.ModList;
            if (_modListIndex >= 0 && _modListElements is not null && _modListIndex < _modListElements.Count)
            {
                MoveCursorToElement(_modListElements[_modListIndex]);
                string listName = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ModListName", "Mod list");
                announce(listName, false, MenuNarrationEventKind.ModConfig);
                AnnounceModListElement(_modListIndex, announce);
            }
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        if (justPressed.MenuRight && _currentListFocus == ListFocus.ModList)
        {
            // Switch to config list if it has items
            if (_configListElements is not null && _configListElements.Count > 0)
            {
                _currentListFocus = ListFocus.ConfigList;
                if (_configListIndex < 0)
                {
                    _configListIndex = 0;
                }
                MoveCursorToElement(_configListElements[_configListIndex]);
                // Announce the first item directly (skip title announcement)
                AnnounceConfigListElement(_configListIndex, announce);
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            else
            {
                // No configs available for selected mod
                string noConfigs = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.NoConfigs", "No configurations available. Select a mod first.");
                announce(noConfigs, false, MenuNarrationEventKind.ModConfig);
            }
            return;
        }

        // Handle up/down navigation within current list
        if (_currentListFocus == ListFocus.ModList)
        {
            if (HandleModListDpadNavigation(announce))
            {
                return;
            }

            // Handle A button to select mod
            if (HandleModListSelectButton(state, announce))
            {
                return;
            }
        }
        else // ConfigList focus
        {
            if (HandleConfigListDpadNavigation(announce))
            {
                return;
            }

            // Handle A button to open config
            if (HandleConfigListSelectButton(announce))
            {
                return;
            }
        }
    }

    private bool HandleModListDpadNavigation(Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_modListElements is null || _modListElements.Count == 0 || _modListIndex < 0)
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        int newIndex = _modListIndex;

        if (justPressed.MenuUp)
        {
            newIndex = _modListIndex > 0 ? _modListIndex - 1 : _modListElements.Count - 1;
        }
        else if (justPressed.MenuDown)
        {
            newIndex = _modListIndex < _modListElements.Count - 1 ? _modListIndex + 1 : 0;
        }
        else
        {
            return false;
        }

        if (newIndex != _modListIndex)
        {
            _modListIndex = newIndex;
            MoveCursorToElement(_modListElements[newIndex]);
            AnnounceModListElement(newIndex, announce);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return true;
        }

        return false;
    }

    private bool HandleConfigListDpadNavigation(Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_configListElements is null || _configListElements.Count == 0 || _configListIndex < 0)
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        int newIndex = _configListIndex;

        if (justPressed.MenuUp)
        {
            newIndex = _configListIndex > 0 ? _configListIndex - 1 : _configListElements.Count - 1;
        }
        else if (justPressed.MenuDown)
        {
            newIndex = _configListIndex < _configListElements.Count - 1 ? _configListIndex + 1 : 0;
        }
        else
        {
            return false;
        }

        if (newIndex != _configListIndex)
        {
            _configListIndex = newIndex;
            MoveCursorToElement(_configListElements[newIndex]);
            AnnounceConfigListElement(newIndex, announce);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return true;
        }

        return false;
    }

    private bool HandleModListSelectButton(UIState state, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_modListElements is null || _modListElements.Count == 0 || _modListIndex < 0)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] HandleModListSelectButton early return: elements={_modListElements?.Count ?? -1}, index={_modListIndex}");
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        if (!justPressed.MouseLeft)
        {
            return false;
        }

        UIElement element = _modListElements[_modListIndex];
        ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] A button pressed, attempting click on element at index {_modListIndex}, type={element.GetType().Name}");

        // Invoke the click on the mod element
        if (TryInvokeClick(element))
        {
            ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] Click succeeded, setting pendingConfigListNavigation=true");
            SoundEngine.PlaySound(SoundID.MenuTick);

            // Set flag to auto-navigate to config list on next frame
            // The config list will be populated by the click handler
            _pendingConfigListNavigation = true;

            return true;
        }

        ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] TryInvokeClick returned false");
        return false;
    }

    private bool HandleConfigListSelectButton(Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_configListElements is null || _configListElements.Count == 0 || _configListIndex < 0)
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        if (!justPressed.MouseLeft)
        {
            return false;
        }

        UIElement element = _configListElements[_configListIndex];

        // Invoke the click to open the config editing screen
        if (TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuOpen);
            return true;
        }

        return false;
    }

    private void AnnounceModListElement(int index, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_modListElements is null || index < 0 || index >= _modListElements.Count)
        {
            return;
        }

        UIElement element = _modListElements[index];
        string description = DescribeModListElement(element);

        if (!string.IsNullOrWhiteSpace(description))
        {
            string positionInfo = $"{index + 1} of {_modListElements.Count}";
            announce($"{description}, {positionInfo}", false, MenuNarrationEventKind.ModConfig);
        }
    }

    private void AnnounceConfigListElement(int index, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_configListElements is null || index < 0 || index >= _configListElements.Count)
        {
            return;
        }

        UIElement element = _configListElements[index];
        string description = ExtractElementText(element);

        if (string.IsNullOrWhiteSpace(description))
        {
            description = element.GetType().Name;
        }

        string positionInfo = $"{index + 1} of {_configListElements.Count}";
        announce($"{description}, {positionInfo}", false, MenuNarrationEventKind.ModConfig);
    }

    private void TryAnnounceListHover(UIState state, UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce)
    {
        // Track selected mod internally without announcing - gamepad navigation already announces the focused mod
        object? selectedMod = SelectedModField?.GetValue(state);
        string modName = DescribeMod(selectedMod);

        if (!string.IsNullOrWhiteSpace(modName))
        {
            _lastModName = modName;
        }
    }

    #endregion

    #region Config State (UIModConfig)

    private void HandleConfigState(UIState state, UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce, bool enableNavigation)
    {
        object? config = ActiveConfigField?.GetValue(state);
        string configName = DescribeConfig(config);

        // Track state changes without announcing title - the first config element will be announced by navigation
        if (!_detailIntroAnnounced || _stateChanged || !string.Equals(configName, _lastConfigLabel, StringComparison.OrdinalIgnoreCase))
        {
            _detailIntroAnnounced = true;
            _lastConfigLabel = configName;

            // Log available types for debugging on first run
            if (!_loggedTypes)
            {
                LogConfigElementTypes(state);
                _loggedTypes = true;
            }
        }

        _listIntroAnnounced = false;

        // Handle gamepad navigation
        if (enableNavigation)
        {
            HandleConfigNavigation(state, announce);
        }

        // Also handle mouse hover
        TryAnnounceConfigHover(uiContext, announce);
    }

    private void HandleConfigNavigation(UIState state, Action<string, bool, MenuNarrationEventKind> announce)
    {
        // Get the main config list containing all config elements
        UIElement? configList = MainConfigListField?.GetValue(state) as UIElement
                              ?? UiListField?.GetValue(state) as UIElement;

        if (configList is null)
        {
            // Fallback: try to find a UIList in the state's children
            configList = FindUIList(state);
        }

        if (configList is null)
        {
            return;
        }

        // Collect navigable config elements if not already done
        if (_navigableElements is null || _stateChanged)
        {
            _navigableElements = CollectConfigElements(configList);
            _configElementCount = _navigableElements.Count;

            // Add action buttons at the end of navigable elements
            List<UIElement> actionButtons = CollectActionButtons(state);
            _navigableElements.AddRange(actionButtons);

            // Log element discovery
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Found {_navigableElements.Count} navigable elements ({_configElementCount} config + {actionButtons.Count} buttons)");

            if (_navigableElements.Count > 0)
            {
                _currentElementIndex = 0;

                // Announce first item on entry
                if (_stateChanged)
                {
                    AnnounceConfigElementAtIndex(_currentElementIndex, announce);
                    _suppressHoverFrames = 30; // Suppress hover to prevent double announcement
                }
            }
            else
            {
                _currentElementIndex = -1;
            }
        }
        else
        {
            // Refresh action buttons in case they changed (Save/Revert appear after changes)
            RefreshActionButtons(state);
        }

        // Handle D-pad navigation
        HandleConfigDpadNavigation(configList, announce);

        // Handle A button (gamepad select) to click the current config element
        HandleConfigSelectButton(announce);
    }

    private static List<UIElement> CollectActionButtons(UIState state)
    {
        List<UIElement> buttons = new();

        // Collect action buttons in order: Back, Save Config, Revert Changes, Restore Defaults
        // Note: Save Config and Revert Changes only appear when there are pending changes

        if (BackButtonField?.GetValue(state) is UIElement backButton && IsButtonVisible(backButton))
        {
            buttons.Add(backButton);
        }

        if (SaveConfigButtonField?.GetValue(state) is UIElement saveButton && IsButtonVisible(saveButton))
        {
            buttons.Add(saveButton);
        }

        if (RevertConfigButtonField?.GetValue(state) is UIElement revertButton && IsButtonVisible(revertButton))
        {
            buttons.Add(revertButton);
        }

        if (RestoreDefaultsButtonField?.GetValue(state) is UIElement restoreButton && IsButtonVisible(restoreButton))
        {
            buttons.Add(restoreButton);
        }

        return buttons;
    }

    private static bool IsButtonVisible(UIElement button)
    {
        // Check if the button has a parent (meaning it's been added to the UI)
        // The Save Config and Revert Changes buttons are only appended when there are pending changes
        try
        {
            PropertyInfo? parentProp = typeof(UIElement).GetProperty("Parent", BindingFlags.Instance | BindingFlags.Public);
            object? parent = parentProp?.GetValue(button);
            return parent is not null;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshActionButtons(UIState state)
    {
        if (_navigableElements is null)
        {
            return;
        }

        // Remove old action buttons (everything after config elements)
        while (_navigableElements.Count > _configElementCount)
        {
            _navigableElements.RemoveAt(_navigableElements.Count - 1);
        }

        // Re-collect action buttons
        List<UIElement> actionButtons = CollectActionButtons(state);
        _navigableElements.AddRange(actionButtons);

        // Adjust current index if it's now out of bounds
        if (_currentElementIndex >= _navigableElements.Count)
        {
            _currentElementIndex = _navigableElements.Count - 1;
        }
    }

    private void HandleConfigDpadNavigation(UIElement configList, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_navigableElements is null || _navigableElements.Count == 0)
        {
            return;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        int newIndex = _currentElementIndex;

        if (justPressed.MenuUp)
        {
            newIndex = _currentElementIndex > 0 ? _currentElementIndex - 1 : _navigableElements.Count - 1;
        }
        else if (justPressed.MenuDown)
        {
            newIndex = _currentElementIndex < _navigableElements.Count - 1 ? _currentElementIndex + 1 : 0;
        }
        else if (justPressed.MenuLeft)
        {
            // For sliders or cycle elements, try to decrease value
            if (_currentElementIndex >= 0 && _currentElementIndex < _navigableElements.Count)
            {
                TryAdjustConfigValue(_navigableElements[_currentElementIndex], -1, announce);
            }
            return;
        }
        else if (justPressed.MenuRight)
        {
            // For sliders or cycle elements, try to increase value
            if (_currentElementIndex >= 0 && _currentElementIndex < _navigableElements.Count)
            {
                TryAdjustConfigValue(_navigableElements[_currentElementIndex], 1, announce);
            }
            return;
        }

        if (newIndex != _currentElementIndex && newIndex >= 0 && newIndex < _navigableElements.Count)
        {
            _currentElementIndex = newIndex;
            UIElement element = _navigableElements[newIndex];

            // Move cursor to the element
            MoveCursorToElement(element);

            // Scroll the element into view
            ScrollElementIntoView(configList, element);

            // Announce the element
            AnnounceConfigElementAtIndex(newIndex, announce);

            // Suppress hover announcements for a short time to prevent double-speak
            _suppressHoverFrames = 15;

            // Play tick sound
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
    }

    // Button texts to ignore in config menu hover announcements
    private static readonly HashSet<string> ConfigMenuButtonsToIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        "Revert Changes",
        "Save Config",
        "Back",
        "Restore Defaults",
        "<",
        ">",
    };

    private void TryAnnounceConfigHover(UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce)
    {
        // Decrement and check suppression counter
        if (_suppressHoverFrames > 0)
        {
            _suppressHoverFrames--;
            return;
        }
        if (uiContext is null)
        {
            return;
        }

        if (!_uiTracker.TryGetHoverLabel(uiContext, out MenuUiLabel hover))
        {
            return;
        }

        if (!hover.IsNew)
        {
            return;
        }

        string announcement = BuildHoverAnnouncement(hover);
        if (string.IsNullOrWhiteSpace(announcement) || string.Equals(announcement, _lastHoverAnnouncement, StringComparison.Ordinal))
        {
            return;
        }

        // Filter out UI button announcements (Save, Revert, Back, etc.)
        if (ConfigMenuButtonsToIgnore.Contains(announcement.Trim()))
        {
            return;
        }

        _lastHoverAnnouncement = announcement;
        announce(announcement, false, MenuNarrationEventKind.Hover);
    }

    #endregion

    #region Element Collection

    private static List<UIElement> CollectNavigableElements(UIElement parent)
    {
        var elements = new List<UIElement>();
        CollectNavigableElementsRecursive(parent, elements);

        // Sort by vertical position
        elements.Sort((a, b) =>
        {
            float ay = a.GetDimensions().Y;
            float by = b.GetDimensions().Y;
            return ay.CompareTo(by);
        });

        return elements;
    }

    private static void CollectNavigableElementsRecursive(UIElement current, List<UIElement> elements)
    {
        // Check if this is a clickable/interactable element
        Type type = current.GetType();
        string? typeName = type.FullName;

        // Include common interactable types
        // Note: We don't check IsInteractable() here because newly added elements
        // may not have valid dimensions yet (layout happens during Draw, after Update).
        // Type-based filtering is sufficient for mod config menus.
        if (typeName is not null &&
            (typeName.Contains("UIButton", StringComparison.Ordinal) ||
             typeName.Contains("UIModConfigItem", StringComparison.Ordinal) ||
             typeName.Contains("UITextPanel", StringComparison.Ordinal)))
        {
            elements.Add(current);
        }

        // Recurse into children
        foreach (UIElement child in current.Children)
        {
            CollectNavigableElementsRecursive(child, elements);
        }
    }

    private static List<UIElement> CollectConfigElements(UIElement configList)
    {
        var elements = new List<UIElement>();

        // Try to get items from UIList
        if (TryGetListItems(configList, out List<UIElement>? listItems) && listItems is not null)
        {
            foreach (UIElement item in listItems)
            {
                // Items in UIModConfig's mainConfigList are wrapped in UISortableElement containers
                // The actual ConfigElement is the first child of the UISortableElement
                UIElement? configElement = GetConfigElementFromContainer(item);
                if (configElement is not null)
                {
                    elements.Add(configElement);
                }
                else if (IsConfigElement(item))
                {
                    // Direct ConfigElement (shouldn't happen but handle it)
                    elements.Add(item);
                }
            }
        }
        else
        {
            // Fallback: collect from children directly
            CollectConfigElementsRecursive(configList, elements, depth: 0);
        }

        ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Collected {elements.Count} config elements, types: {string.Join(", ", elements.Take(5).Select(e => e.GetType().Name))}");

        // Sort by vertical position
        elements.Sort((a, b) =>
        {
            try
            {
                float ay = a.GetDimensions().Y;
                float by = b.GetDimensions().Y;
                return ay.CompareTo(by);
            }
            catch
            {
                return 0;
            }
        });

        return elements;
    }

    /// <summary>
    /// Extracts the ConfigElement from a UISortableElement container.
    /// In UIModConfig, config elements are wrapped in UISortableElement for sorting.
    /// </summary>
    private static UIElement? GetConfigElementFromContainer(UIElement container)
    {
        // Check if this is a UISortableElement or similar wrapper
        string typeName = container.GetType().Name;
        if (typeName.Contains("SortableElement", StringComparison.Ordinal) ||
            typeName.Contains("Container", StringComparison.Ordinal))
        {
            // The ConfigElement is the first child
            foreach (UIElement child in container.Children)
            {
                if (IsConfigElement(child))
                {
                    return child;
                }
            }
        }

        // Check if any child is a ConfigElement
        foreach (UIElement child in container.Children)
        {
            if (IsConfigElement(child))
            {
                return child;
            }
        }

        return null;
    }

    private static void CollectConfigElementsRecursive(UIElement current, List<UIElement> elements, int depth)
    {
        if (depth > 10)
        {
            return; // Prevent infinite recursion
        }

        if (IsConfigElement(current))
        {
            elements.Add(current);
            return; // Don't recurse into config elements
        }

        foreach (UIElement child in current.Children)
        {
            CollectConfigElementsRecursive(child, elements, depth + 1);
        }
    }

    private static bool IsConfigElement(UIElement element)
    {
        Type type = element.GetType();
        string? typeName = type.FullName;

        // Exclude header elements - they are non-interactive section titles
        if (typeName is not null && typeName.Contains("HeaderElement", StringComparison.Ordinal))
        {
            return false;
        }

        // Check if it's a ConfigElement or derived type
        if (ConfigElementType is not null && ConfigElementType.IsAssignableFrom(type))
        {
            return true;
        }

        // Check by type name patterns
        if (typeName is null)
        {
            return false;
        }

        return typeName.Contains("ConfigElement", StringComparison.Ordinal) ||
               typeName.Contains("BooleanElement", StringComparison.Ordinal) ||
               typeName.Contains("IntInputElement", StringComparison.Ordinal) ||
               typeName.Contains("FloatElement", StringComparison.Ordinal) ||
               typeName.Contains("StringInputElement", StringComparison.Ordinal) ||
               typeName.Contains("EnumElement", StringComparison.Ordinal) ||
               typeName.Contains("ColorElement", StringComparison.Ordinal) ||
               typeName.Contains("ItemDefinitionElement", StringComparison.Ordinal) ||
               typeName.Contains("ListElement", StringComparison.Ordinal);
    }

    private static bool IsInteractable(UIElement element)
    {
        // Check if element has non-zero dimensions
        try
        {
            CalculatedStyle dims = element.GetDimensions();
            return dims.Width > 0 && dims.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetListItems(UIElement list, out List<UIElement>? items)
    {
        items = null;

        // Try to access _items field (common in UIList)
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        Type type = list.GetType();
        FieldInfo? itemsField = type.GetField("_items", flags) ?? type.GetField("items", flags);

        if (itemsField?.GetValue(list) is IEnumerable<UIElement> enumerable)
        {
            items = enumerable.ToList();
            return true;
        }

        // Try Children property
        if (list.Children.Any())
        {
            items = list.Children.ToList();
            return true;
        }

        return false;
    }

    private static UIElement? FindUIList(UIState state)
    {
        return FindElementByType(state, "UIList");
    }

    private static UIElement? FindElementByType(UIElement parent, string typeNamePart)
    {
        foreach (UIElement child in parent.Children)
        {
            if (child.GetType().Name.Contains(typeNamePart, StringComparison.Ordinal))
            {
                return child;
            }

            UIElement? found = FindElementByType(child, typeNamePart);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    #endregion

    #region Element Announcement

    private void AnnounceElementAtIndex(int index, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_navigableElements is null || index < 0 || index >= _navigableElements.Count)
        {
            return;
        }

        UIElement element = _navigableElements[index];
        string description = DescribeModListElement(element);

        if (!string.IsNullOrWhiteSpace(description))
        {
            string positionInfo = $"{index + 1} of {_navigableElements.Count}";
            announce($"{description}, {positionInfo}", false, MenuNarrationEventKind.ModConfig);
        }
    }

    private void AnnounceConfigElementAtIndex(int index, Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_navigableElements is null || index < 0 || index >= _navigableElements.Count)
        {
            return;
        }

        UIElement element = _navigableElements[index];
        _lastNavigatedElement = element;

        string description = DescribeConfigElement(element);

        if (!string.IsNullOrWhiteSpace(description))
        {
            // Just announce the setting without position info
            announce(description, false, MenuNarrationEventKind.ModConfig);
        }
    }

    private static string DescribeModListElement(UIElement element)
    {
        // Try to extract mod name or text from the element
        // UIButton<T> inherits from UIAutoScaleTextTextPanel<T> which has a Text property
        string text = ExtractTextFromTypeHierarchy(element);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return TextSanitizer.Clean(text);
        }

        // Fallback to generic extraction
        return ExtractElementText(element);
    }

    /// <summary>
    /// Extracts text by searching the type hierarchy for common text properties.
    /// This handles cases where properties are defined in base classes.
    /// </summary>
    private static string ExtractTextFromTypeHierarchy(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Type? type = element.GetType();

        // Walk up the type hierarchy
        while (type is not null && type != typeof(object))
        {
            // Try Text property first (UIAutoScaleTextTextPanel has this)
            PropertyInfo? textProp = type.GetProperty("Text", flags | BindingFlags.DeclaredOnly);
            if (textProp is not null)
            {
                try
                {
                    object? value = textProp.GetValue(element);
                    if (value is string str && !string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
                catch
                {
                    // Ignore property access errors
                }
            }

            // Try _text field
            FieldInfo? textField = type.GetField("_text", flags | BindingFlags.DeclaredOnly);
            if (textField is not null)
            {
                try
                {
                    object? value = textField.GetValue(element);
                    string? str = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
                }
                catch
                {
                    // Ignore field access errors
                }
            }

            type = type.BaseType;
        }

        return string.Empty;
    }

    private static string DescribeConfigElement(UIElement element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        Type type = element.GetType();
        string? typeName = type.FullName;

        // Skip header elements - they are non-interactive section titles
        // and should not be navigated to or announced
        if (typeName is not null && typeName.Contains("HeaderElement", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Check if this is an action button (UITextPanel used for Back, Save, etc.)
        if (typeName is not null && typeName.Contains("UITextPanel", StringComparison.Ordinal))
        {
            string buttonText = ExtractTextFromTypeHierarchy(element);
            if (!string.IsNullOrWhiteSpace(buttonText))
            {
                string cleanButtonText = TextSanitizer.Clean(buttonText);
                return $"{cleanButtonText} button";
            }
        }

        ConfigElementAccessors accessors = GetConfigElementAccessors(type);

        // Try to get label (this is the primary text from TextDisplayFunction)
        string label = TryExtractConfigLabel(element, accessors);

        // Try to get value
        string value = TryExtractConfigValue(element, accessors);

        // Clean up the label and value
        string cleanLabel = !string.IsNullOrWhiteSpace(label) ? TextSanitizer.Clean(label) : string.Empty;
        string cleanValue = !string.IsNullOrWhiteSpace(value) ? TextSanitizer.Clean(value) : string.Empty;

        // Check if the label already contains the value (common for sliders/floats)
        // TextDisplayFunction for sliders typically returns "Label: value" already
        bool labelContainsValue = !string.IsNullOrWhiteSpace(cleanValue) &&
                                   !string.IsNullOrWhiteSpace(cleanLabel) &&
                                   cleanLabel.Contains(cleanValue, StringComparison.OrdinalIgnoreCase);

        // Build description - don't duplicate value if already in label
        if (!string.IsNullOrWhiteSpace(cleanLabel))
        {
            if (labelContainsValue || string.IsNullOrWhiteSpace(cleanValue))
            {
                // Label already has the value or no value to add
                return cleanLabel;
            }
            else
            {
                // Add value separately (for toggles: "Setting Name: On")
                return $"{cleanLabel}: {cleanValue}";
            }
        }

        // Fallback to generic extraction
        string extracted = ExtractElementText(element);
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            return TextSanitizer.Clean(extracted);
        }

        return type.Name;
    }

    private static string TryExtractConfigLabel(UIElement element, ConfigElementAccessors accessors)
    {
        // Try TextDisplayFunction property first (Func<string> delegate in ConfigElement)
        // This is the primary way ConfigElement provides its display text
        if (accessors.TextDisplayFunctionProperty?.GetValue(element) is Delegate textFunc)
        {
            try
            {
                object? result = textFunc.DynamicInvoke();
                string text = ConvertToText(result);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
                // Ignore delegate invocation failures
            }
        }

        // Try Label field (protected field in ConfigElement)
        if (accessors.LabelField?.GetValue(element) is object labelFieldValue)
        {
            string text = ConvertToText(labelFieldValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Try Label property
        if (accessors.LabelProperty?.GetValue(element) is object labelValue)
        {
            string text = ConvertToText(labelValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string TryExtractConfigValue(UIElement element, ConfigElementAccessors accessors)
    {
        // Try GetObject method first (ConfigElement's primary value getter)
        if (accessors.GetObjectMethod is not null)
        {
            try
            {
                object? result = accessors.GetObjectMethod.Invoke(element, Array.Empty<object>());
                return FormatConfigValue(result);
            }
            catch
            {
                // Ignore method invocation failures
            }
        }

        // Try Value property (ConfigElement<T> exposes Value)
        if (accessors.ValueProperty?.GetValue(element) is object valueObj)
        {
            return FormatConfigValue(valueObj);
        }

        if (accessors.ValueField?.GetValue(element) is object valueFieldObj)
        {
            return FormatConfigValue(valueFieldObj);
        }

        return string.Empty;
    }

    private static string TryExtractTooltip(UIElement element, ConfigElementAccessors accessors)
    {
        // Try TooltipFunction property first (Func<string> delegate in ConfigElement)
        if (accessors.TooltipFunctionProperty?.GetValue(element) is Delegate tooltipFunc)
        {
            try
            {
                object? result = tooltipFunc.DynamicInvoke();
                string text = ConvertToText(result);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
                // Ignore delegate invocation failures
            }
        }

        if (accessors.TooltipProperty?.GetValue(element) is object tooltipValue)
        {
            return ConvertToText(tooltipValue);
        }

        if (accessors.TooltipField?.GetValue(element) is object tooltipFieldValue)
        {
            return ConvertToText(tooltipFieldValue);
        }

        return string.Empty;
    }

    private static string FormatConfigValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        // Handle collection types - don't show raw type names like "System.Collections.Generic.List`1[...]"
        if (value is System.Collections.ICollection collection)
        {
            int count = collection.Count;
            return count == 1
                ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.OneItem", "1 item")
                : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ItemCount", "{0} items").Replace("{0}", count.ToString());
        }

        // Handle IEnumerable (but not string which is also IEnumerable)
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            // Check if it looks like a collection type name
            string typeName = value.GetType().Name;
            if (typeName.Contains("List", StringComparison.Ordinal) ||
                typeName.Contains("Collection", StringComparison.Ordinal) ||
                typeName.Contains("Array", StringComparison.Ordinal) ||
                typeName.Contains("Set", StringComparison.Ordinal))
            {
                int count = 0;
                foreach (object? _ in enumerable)
                {
                    count++;
                    if (count > 100) break; // Limit counting
                }
                return count == 1
                    ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.OneItem", "1 item")
                    : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ItemCount", "{0} items").Replace("{0}", count.ToString());
            }
        }

        return value switch
        {
            bool b => b
                ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOn", "On")
                : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.Controls.ToggleOff", "Off"),
            float f => f.ToString("0.##", CultureInfo.CurrentCulture),
            double d => d.ToString("0.##", CultureInfo.CurrentCulture),
            Enum e => e.ToString(),
            _ => ConvertToText(value),
        };
    }

    private static string ExtractElementText(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = element.GetType();

        // Try common text properties
        string[] textMembers = { "Text", "_text", "Label", "_label", "DisplayName", "Name" };

        foreach (string memberName in textMembers)
        {
            PropertyInfo? prop = type.GetProperty(memberName, flags);
            if (prop?.GetValue(element) is object propValue)
            {
                string text = ConvertToText(propValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            FieldInfo? field = type.GetField(memberName, flags);
            if (field?.GetValue(element) is object fieldValue)
            {
                string text = ConvertToText(fieldValue);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        // Recurse into first child with text
        foreach (UIElement child in element.Children)
        {
            string childText = ExtractElementText(child);
            if (!string.IsNullOrWhiteSpace(childText))
            {
                return childText;
            }
        }

        return string.Empty;
    }

    private static string ConvertToText(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string s)
        {
            return TextSanitizer.Clean(s);
        }

        if (value is LocalizedText localized)
        {
            return TextSanitizer.Clean(localized.Value ?? string.Empty);
        }

        return TextSanitizer.Clean(value.ToString() ?? string.Empty);
    }

    private static ConfigElementAccessors GetConfigElementAccessors(Type type)
    {
        if (ConfigElementAccessorCache.TryGetValue(type, out ConfigElementAccessors? cached))
        {
            return cached;
        }

        // Use FlattenHierarchy to find inherited members from base classes like ConfigElement
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        var accessors = new ConfigElementAccessors
        {
            // Label is a protected field in ConfigElement with uppercase 'L'
            LabelProperty = FindProperty(type, flags, "Label", "DisplayName", "Name"),
            LabelField = FindField(type, flags, "Label", "_label", "_text"),
            // Value property for ConfigElement<T>
            ValueProperty = FindProperty(type, flags, "Value", "CurrentValue"),
            ValueField = FindField(type, flags, "_value", "value"),
            // Tooltip
            TooltipProperty = FindProperty(type, flags, "Tooltip", "TooltipText"),
            TooltipField = FindField(type, flags, "_tooltip", "tooltip"),
            // TextDisplayFunction is a property (Func<string>) in ConfigElement
            TextDisplayFunctionProperty = FindProperty(type, flags, "TextDisplayFunction"),
            // TooltipFunction is also a property (Func<string>) in ConfigElement
            TooltipFunctionProperty = FindProperty(type, flags, "TooltipFunction"),
            // GetObject method in ConfigElement returns the current value - search hierarchy
            GetObjectMethod = FindMethod(type, flags, "GetObject"),
            // Slider-specific properties for RangeElement types
            ProportionProperty = FindProperty(type, flags, "Proportion"),
            MinProperty = FindProperty(type, flags, "Min"),
            MaxProperty = FindProperty(type, flags, "Max"),
            IncrementProperty = FindProperty(type, flags, "Increment"),
            TickIncrementProperty = FindProperty(type, flags, "TickIncrement"),
        };

        ConfigElementAccessorCache[type] = accessors;
        return accessors;
    }

    private static MethodInfo? FindMethod(Type type, BindingFlags flags, string name)
    {
        // Search through type hierarchy for the method
        Type? currentType = type;
        while (currentType is not null && currentType != typeof(object))
        {
            MethodInfo? method = currentType.GetMethod(name, flags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method is not null)
            {
                return method;
            }
            currentType = currentType.BaseType;
        }
        return null;
    }

    private static PropertyInfo? FindProperty(Type type, BindingFlags flags, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? prop = type.GetProperty(name, flags);
            if (prop is not null)
            {
                return prop;
            }
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, BindingFlags flags, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo? field = type.GetField(name, flags);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    #endregion

    #region Navigation Helpers

    /// <summary>
    /// Handles the gamepad A button (select) to click the currently focused element in the mod list.
    /// </summary>
    private bool HandleSelectButton()
    {
        if (_navigableElements is null || _navigableElements.Count == 0 || _currentElementIndex < 0)
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        // A button on gamepad maps to MouseLeft in menu contexts
        if (!justPressed.MouseLeft)
        {
            return false;
        }

        UIElement element = _navigableElements[_currentElementIndex];

        // Invoke the click on the element
        if (TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuOpen);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles the gamepad A button (select) to interact with the currently focused config element.
    /// For boolean toggles, this toggles the value. For other elements, it invokes the click.
    /// </summary>
    private void HandleConfigSelectButton(Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_navigableElements is null || _navigableElements.Count == 0 || _currentElementIndex < 0)
        {
            return;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        // A button on gamepad maps to MouseLeft in menu contexts
        if (!justPressed.MouseLeft)
        {
            return;
        }

        UIElement element = _navigableElements[_currentElementIndex];

        // First try to toggle if it's a boolean element
        if (TryToggleConfigElement(element, announce))
        {
            // Suppress hover announcements after toggle to prevent "Revert Changes" etc.
            _suppressHoverFrames = 30;
            return;
        }

        // Otherwise invoke the click (for expandable elements, buttons, etc.)
        if (TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);

            // Suppress hover announcements after click
            _suppressHoverFrames = 30;

            // Re-announce with potentially new state
            string description = DescribeConfigElement(element);
            if (!string.IsNullOrWhiteSpace(description))
            {
                announce(description, false, MenuNarrationEventKind.ModConfig);
            }
        }
    }

    /// <summary>
    /// Attempts to invoke a left click on the given UI element.
    /// </summary>
    private static bool TryInvokeClick(UIElement element)
    {
        try
        {
            // Create a UIMouseEvent for the click
            CalculatedStyle dims = element.GetDimensions();
            var mousePosition = new Vector2(dims.X + dims.Width / 2, dims.Y + dims.Height / 2);
            var evt = new UIMouseEvent(element, mousePosition);

            // Invoke the LeftClick method
            element.LeftClick(evt);

            return true;
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Failed to invoke click: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to toggle a boolean config element.
    /// </summary>
    private static bool TryToggleConfigElement(UIElement element, Action<string, bool, MenuNarrationEventKind> announce)
    {
        Type type = element.GetType();
        string? typeName = type.FullName;

        // Check if this is a boolean element
        if (typeName is null || !typeName.Contains("BooleanElement", StringComparison.Ordinal))
        {
            return false;
        }

        // Try to invoke the click which toggles the boolean
        if (TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);

            // Re-announce with new value
            string description = DescribeConfigElement(element);
            if (!string.IsNullOrWhiteSpace(description))
            {
                announce(description, false, MenuNarrationEventKind.ModConfig);
            }
            return true;
        }

        return false;
    }

    private bool HandleDpadNavigation(Action<string, bool, MenuNarrationEventKind> announce)
    {
        if (_navigableElements is null || _navigableElements.Count == 0)
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        int newIndex = _currentElementIndex;

        if (justPressed.MenuUp)
        {
            newIndex = _currentElementIndex > 0 ? _currentElementIndex - 1 : _navigableElements.Count - 1;
        }
        else if (justPressed.MenuDown)
        {
            newIndex = _currentElementIndex < _navigableElements.Count - 1 ? _currentElementIndex + 1 : 0;
        }
        else
        {
            return false;
        }

        if (newIndex != _currentElementIndex)
        {
            _currentElementIndex = newIndex;
            MoveCursorToElement(_navigableElements[newIndex]);
            AnnounceElementAtIndex(newIndex, announce);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return true;
        }

        return false;
    }

    private static void TryAdjustConfigValue(UIElement element, int direction, Action<string, bool, MenuNarrationEventKind> announce)
    {
        Type type = element.GetType();
        ConfigElementAccessors accessors = GetConfigElementAccessors(type);

        // Try slider adjustment first (RangeElement types have Proportion property)
        if (TryAdjustSlider(element, accessors, direction))
        {
            // Re-announce with new value
            string newDescription = DescribeConfigElement(element);
            announce(newDescription, false, MenuNarrationEventKind.ModConfig);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        // For boolean toggles - clicking toggles the value
        string? typeName = type.FullName;
        if (typeName is not null && typeName.Contains("BooleanElement", StringComparison.Ordinal))
        {
            if (TryInvokeClick(element))
            {
                // Re-announce with new value
                string newDescription = DescribeConfigElement(element);
                announce(newDescription, false, MenuNarrationEventKind.ModConfig);
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }
    }

    private static bool TryAdjustSlider(UIElement element, ConfigElementAccessors accessors, int direction)
    {
        // Check if this is a slider element (has Proportion and TickIncrement properties)
        if (accessors.ProportionProperty is null || !accessors.ProportionProperty.CanRead || !accessors.ProportionProperty.CanWrite)
        {
            return false;
        }

        try
        {
            // Get current proportion (0.0 to 1.0)
            object? proportionObj = accessors.ProportionProperty.GetValue(element);
            if (proportionObj is not float currentProportion)
            {
                return false;
            }

            // Get tick increment (how much to change per step)
            float tickIncrement = 0.05f; // Default 5% step
            if (accessors.TickIncrementProperty?.GetValue(element) is float tick && tick > 0f)
            {
                tickIncrement = tick;
            }

            // Calculate new proportion
            float newProportion = currentProportion + (direction * tickIncrement);
            newProportion = Math.Clamp(newProportion, 0f, 1f);

            // Only update if value actually changed
            if (Math.Abs(newProportion - currentProportion) < 0.0001f)
            {
                return false;
            }

            // Set the new proportion
            accessors.ProportionProperty.SetValue(element, newProportion);
            return true;
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Failed to adjust slider: {ex.Message}");
            return false;
        }
    }

    private static void MoveCursorToElement(UIElement element)
    {
        try
        {
            CalculatedStyle dims = element.GetDimensions();
            float x = dims.X + (dims.Width * 0.5f);
            float y = dims.Y + (dims.Height * 0.5f);

            int clampedX = (int)MathHelper.Clamp(x, 0f, Main.screenWidth - 1);
            int clampedY = (int)MathHelper.Clamp(y, 0f, Main.screenHeight - 1);

            Main.mouseX = clampedX;
            Main.mouseY = clampedY;
            PlayerInput.MouseX = clampedX;
            PlayerInput.MouseY = clampedY;
        }
        catch
        {
            // Ignore dimension calculation failures
        }
    }

    private static void ScrollElementIntoView(UIElement container, UIElement element)
    {
        // Try to scroll the container so the element is visible
        Type containerType = container.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Look for scroll-related methods
        MethodInfo? scrollToMethod = containerType.GetMethod("ScrollIntoView", flags)
                                   ?? containerType.GetMethod("ScrollTo", flags);

        if (scrollToMethod is not null)
        {
            try
            {
                scrollToMethod.Invoke(container, new object[] { element });
            }
            catch
            {
                // Ignore scroll failures
            }
        }

        // Fallback: try to set ViewPosition directly
        PropertyInfo? viewPositionProp = containerType.GetProperty("ViewPosition", flags);
        if (viewPositionProp is not null && viewPositionProp.CanWrite)
        {
            try
            {
                CalculatedStyle containerDims = container.GetDimensions();
                CalculatedStyle elementDims = element.GetDimensions();

                // Calculate scroll position to center the element
                float targetY = elementDims.Y - containerDims.Y - (containerDims.Height / 2) + (elementDims.Height / 2);
                targetY = Math.Max(0, targetY);

                // ViewPosition is a float, not Vector2
                viewPositionProp.SetValue(container, targetY);
            }
            catch
            {
                // Ignore
            }
        }
    }

    private static void PositionCursorAtStateCenter(UIState state)
    {
        CalculatedStyle dims;
        try
        {
            dims = state.GetInnerDimensions();
            if (dims.Width <= 0f || dims.Height <= 0f)
            {
                dims = state.GetDimensions();
            }
        }
        catch
        {
            dims = new CalculatedStyle(0f, 0f, Main.screenWidth, Main.screenHeight);
        }

        float width = dims.Width > 0f ? dims.Width : Main.screenWidth;
        float height = dims.Height > 0f ? dims.Height : Main.screenHeight;
        float x = dims.Width > 0f ? dims.X : 0f;
        float y = dims.Height > 0f ? dims.Y : 0f;

        float centerX = x + (width * 0.5f);
        float centerY = y + (height * 0.5f);

        int clampedX = (int)Math.Clamp(centerX, 0f, Main.screenWidth - 1);
        int clampedY = (int)Math.Clamp(centerY, 0f, Main.screenHeight - 1);

        Main.mouseX = clampedX;
        Main.mouseY = clampedY;
        PlayerInput.MouseX = clampedX;
        PlayerInput.MouseY = clampedY;
    }

    #endregion

    #region Description Helpers

    private static string BuildHoverAnnouncement(MenuUiLabel hover)
    {
        string cleaned = TextSanitizer.Clean(hover.Text);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        if (hover.Element is null)
        {
            return string.Empty;
        }

        return DescribeConfigElement(hover.Element);
    }

    private static string ComposeConfigAnnouncement(string configName, string modName)
    {
        if (string.IsNullOrWhiteSpace(configName))
        {
            configName = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigFallback", "Configuration");
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            string template = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigIntroNoMod", "{0} configuration.");
            return string.Format(template, configName);
        }

        string combinedTemplate = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ConfigIntro", "{0} from {1}");
        return string.Format(combinedTemplate, configName, modName);
    }

    private static string DescribeMod(object? mod)
    {
        if (mod is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ModDisplayNameProperty?.GetValue(mod);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ModInternalNameProperty?.GetValue(mod) is string name && !string.IsNullOrWhiteSpace(name))
            {
                return TextSanitizer.Clean(name);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to describe mod: {ex.Message}");
        }

        return string.Empty;
    }

    private static string DescribeConfig(object? config)
    {
        if (config is null)
        {
            return string.Empty;
        }

        try
        {
            object? localized = ModConfigDisplayNameProperty?.GetValue(config);
            string display = ExtractLocalized(localized);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            if (ModConfigFullNameProperty?.GetValue(config) is string fullName && !string.IsNullOrWhiteSpace(fullName))
            {
                return TextSanitizer.Clean(fullName);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[MenuNarration] Failed to describe config: {ex.Message}");
        }

        return string.Empty;
    }

    private static string ExtractLocalized(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is LocalizedText localized)
        {
            return TextSanitizer.Clean(localized.Value ?? string.Empty);
        }

        if (LocalizedValueProperty is not null && value.GetType() == typeof(LocalizedText))
        {
            try
            {
                object? textValue = LocalizedValueProperty.GetValue(value);
                if (textValue is string str)
                {
                    return TextSanitizer.Clean(str);
                }
            }
            catch
            {
                // ignored
            }
        }

        return TextSanitizer.Clean(value.ToString() ?? string.Empty);
    }

    #endregion

    #region Debugging

    private static void LogTypeResolutionStatus()
    {
        if (_loggedTypeResolution)
        {
            return;
        }
        _loggedTypeResolution = true;

        ScreenReaderMod.Instance?.Logger.Info("[ModConfig] Type resolution status:");
        ScreenReaderMod.Instance?.Logger.Info($"  ModConfigListType: {(ModConfigListType is not null ? "resolved" : "NULL")}");
        ScreenReaderMod.Instance?.Logger.Info($"  ModConfigStateType: {(ModConfigStateType is not null ? "resolved" : "NULL")}");
        ScreenReaderMod.Instance?.Logger.Info($"  ModType: {(ModType is not null ? "resolved" : "NULL")}");
        ScreenReaderMod.Instance?.Logger.Info($"  UIButtonType: {(UIButtonType is not null ? "resolved" : "NULL")}");
        ScreenReaderMod.Instance?.Logger.Info($"  ModListField: {(ModListField is not null ? "resolved" : "NULL")}");
        ScreenReaderMod.Instance?.Logger.Info($"  SelectedModField: {(SelectedModField is not null ? "resolved" : "NULL")}");
    }

    private static void LogConfigElementTypes(UIState state)
    {
        ScreenReaderMod.Instance?.Logger.Debug("[ModConfig] Logging config element types for state: " + state.GetType().FullName);

        // Log the state's structure
        LogElementTree(state, 0);
    }

    private static void LogElementTree(UIElement element, int depth)
    {
        if (depth > 5)
        {
            return;
        }

        string indent = new string(' ', depth * 2);
        string typeName = element.GetType().FullName ?? element.GetType().Name;

        ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] {indent}{typeName}");

        foreach (UIElement child in element.Children)
        {
            LogElementTree(child, depth + 1);
        }
    }

    #endregion

    private sealed class ConfigElementAccessors
    {
        public PropertyInfo? LabelProperty { get; init; }
        public FieldInfo? LabelField { get; init; }
        public PropertyInfo? ValueProperty { get; init; }
        public FieldInfo? ValueField { get; init; }
        public PropertyInfo? TooltipProperty { get; init; }
        public FieldInfo? TooltipField { get; init; }
        public PropertyInfo? TextDisplayFunctionProperty { get; init; }
        public PropertyInfo? TooltipFunctionProperty { get; init; }
        public MethodInfo? GetObjectMethod { get; init; }
        // Slider-specific properties for RangeElement types
        public PropertyInfo? ProportionProperty { get; init; }
        public PropertyInfo? MinProperty { get; init; }
        public PropertyInfo? MaxProperty { get; init; }
        public PropertyInfo? IncrementProperty { get; init; }
        public PropertyInfo? TickIncrementProperty { get; init; }
    }
}
