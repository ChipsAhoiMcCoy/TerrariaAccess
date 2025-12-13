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

    // Type references for tModLoader config UI
    private static readonly Type? ModConfigListType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfigList, tModLoader");
    private static readonly Type? ModConfigStateType = Type.GetType("Terraria.ModLoader.Config.UI.UIModConfig, tModLoader");
    private static readonly Type? ModType = Type.GetType("Terraria.ModLoader.Mod, tModLoader");
    private static readonly Type? ModConfigType = Type.GetType("Terraria.ModLoader.Config.ModConfig, tModLoader");
    private static readonly Type? ConfigElementType = Type.GetType("Terraria.ModLoader.Config.UI.ConfigElement, tModLoader");
    private static readonly Type? UIModConfigItemType = Type.GetType("Terraria.ModLoader.UI.UIModConfigItem, tModLoader");

    // Field accessors for UIModConfigList
    private static readonly FieldInfo? SelectedModField = ModConfigListType?.GetField("selectedMod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ModListField = ModConfigListType?.GetField("modList", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ConfigListField = ModConfigListType?.GetField("configList", BindingFlags.Instance | BindingFlags.NonPublic);

    // Field accessors for UIModConfig
    private static readonly FieldInfo? EditingModField = ModConfigStateType?.GetField("mod", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ActiveConfigField = ModConfigStateType?.GetField("modConfig", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MainConfigListField = ModConfigStateType?.GetField("mainConfigList", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? UiListField = ModConfigStateType?.GetField("uIList", BindingFlags.Instance | BindingFlags.NonPublic);

    // Property accessors
    private static readonly PropertyInfo? ModDisplayNameProperty = ModType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModInternalNameProperty = ModType?.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModConfigDisplayNameProperty = ModConfigType?.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? ModConfigFullNameProperty = ModConfigType?.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? LocalizedValueProperty = typeof(LocalizedText).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

    // Cached reflection info for config elements
    private static readonly Dictionary<Type, ConfigElementAccessors> ConfigElementAccessorCache = new();
    private static bool _loggedTypes;

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
        _uiTracker.Reset();
    }

    private static void AddEvent(ICollection<MenuNarrationEvent> target, string? text, bool force, MenuNarrationEventKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        target.Add(new MenuNarrationEvent(text, force, kind));
    }

    public bool TryBuildMenuEvents(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        if (context.MenuMode != MenuID.FancyUI)
        {
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
            _stateChanged = PrepareForState(state, alignCursor);
            HandleListState(state, uiContext, announce, enableNavigation);
            _lastState = state;
            return true;
        }

        if (ModConfigStateType is not null && ModConfigStateType.IsAssignableFrom(stateType))
        {
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
            _uiTracker.Reset();
            _lastHoverAnnouncement = null;
            _currentElementIndex = -1;
            _navigableElements = null;
            _lastNavigatedElement = null;

            if (alignCursor)
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
            string intro = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.ListIntro", "Mod configuration list. Use Up and Down to navigate mods, Enter to select.");
            announce(intro, true, MenuNarrationEventKind.ModConfig);
            _listIntroAnnounced = true;
            _detailIntroAnnounced = false;
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
        // Get navigable mod items
        UIElement? modList = ModListField?.GetValue(state) as UIElement;
        if (modList is null)
        {
            return;
        }

        // Collect navigable items if not already done
        if (_navigableElements is null || _stateChanged)
        {
            _navigableElements = CollectNavigableElements(modList);
            _currentElementIndex = _navigableElements.Count > 0 ? 0 : -1;

            // Announce first item on entry if there are items
            if (_currentElementIndex >= 0 && _stateChanged)
            {
                AnnounceElementAtIndex(_currentElementIndex, announce);
            }
        }

        // Handle D-pad navigation
        if (HandleDpadNavigation(announce))
        {
            return;
        }
    }

    private void TryAnnounceListHover(UIState state, UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce)
    {
        object? selectedMod = SelectedModField?.GetValue(state);
        string modName = DescribeMod(selectedMod);

        if (string.IsNullOrWhiteSpace(modName))
        {
            return;
        }

        if (string.Equals(modName, _lastModName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string template = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.SelectedMod", "Selected mod: {0}");
        announce(string.Format(template, modName), true, MenuNarrationEventKind.ModConfig);
        _lastModName = modName;
    }

    #endregion

    #region Config State (UIModConfig)

    private void HandleConfigState(UIState state, UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce, bool enableNavigation)
    {
        object? mod = EditingModField?.GetValue(state);
        object? config = ActiveConfigField?.GetValue(state);

        string modName = DescribeMod(mod);
        string configName = DescribeConfig(config);

        // Announce intro when entering config
        if (!_detailIntroAnnounced || _stateChanged || !string.Equals(configName, _lastConfigLabel, StringComparison.OrdinalIgnoreCase))
        {
            string message = ComposeConfigAnnouncement(configName, modName);
            string navHint = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ModConfigMenu.NavHint", "Use Up and Down to navigate options.");
            announce($"{message} {navHint}", true, MenuNarrationEventKind.ModConfig);
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

            // Log element discovery
            ScreenReaderMod.Instance?.Logger.Debug($"[ModConfig] Found {_navigableElements.Count} navigable config elements");

            if (_navigableElements.Count > 0)
            {
                _currentElementIndex = 0;

                // Announce first item on entry
                if (_stateChanged)
                {
                    AnnounceConfigElementAtIndex(_currentElementIndex, announce);
                }
            }
            else
            {
                _currentElementIndex = -1;
            }
        }

        // Handle D-pad navigation
        HandleConfigDpadNavigation(configList, announce);
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

            // Play tick sound
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
    }

    private void TryAnnounceConfigHover(UserInterface? uiContext, Action<string, bool, MenuNarrationEventKind> announce)
    {
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
        if (typeName is not null &&
            (typeName.Contains("UIModConfigItem", StringComparison.Ordinal) ||
             typeName.Contains("UIPanel", StringComparison.Ordinal) ||
             typeName.Contains("UITextPanel", StringComparison.Ordinal)))
        {
            if (IsInteractable(current))
            {
                elements.Add(current);
            }
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
                // Each item in the list should be a config element
                if (IsConfigElement(item))
                {
                    elements.Add(item);
                }
                else
                {
                    // Check children
                    foreach (UIElement child in item.Children)
                    {
                        if (IsConfigElement(child))
                        {
                            elements.Add(child);
                        }
                    }
                }
            }
        }
        else
        {
            // Fallback: collect from children directly
            CollectConfigElementsRecursive(configList, elements, depth: 0);
        }

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

        // Check if it's a ConfigElement or derived type
        if (ConfigElementType is not null && ConfigElementType.IsAssignableFrom(type))
        {
            return true;
        }

        // Check by type name patterns
        string? typeName = type.FullName;
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
               typeName.Contains("HeaderElement", StringComparison.Ordinal);
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
            string positionInfo = $"{index + 1} of {_navigableElements.Count}";
            announce($"{description}, {positionInfo}", false, MenuNarrationEventKind.ModConfig);
        }
    }

    private static string DescribeModListElement(UIElement element)
    {
        // Try to extract mod name or text from the element
        return ExtractElementText(element);
    }

    private static string DescribeConfigElement(UIElement element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        Type type = element.GetType();
        ConfigElementAccessors accessors = GetConfigElementAccessors(type);

        // Try to get label
        string label = TryExtractConfigLabel(element, accessors);

        // Try to get value
        string value = TryExtractConfigValue(element, accessors);

        // Try to get tooltip
        string tooltip = TryExtractTooltip(element, accessors);

        // Build description
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(label))
        {
            parts.Add(TextSanitizer.Clean(label));
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(TextSanitizer.Clean(value));
        }

        if (parts.Count == 0)
        {
            // Fallback to generic extraction
            string extracted = ExtractElementText(element);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                parts.Add(extracted);
            }
            else
            {
                parts.Add(type.Name);
            }
        }

        return string.Join(": ", parts);
    }

    private static string TryExtractConfigLabel(UIElement element, ConfigElementAccessors accessors)
    {
        // Try Label property/field first
        if (accessors.LabelProperty?.GetValue(element) is object labelValue)
        {
            string text = ConvertToText(labelValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (accessors.LabelField?.GetValue(element) is object labelFieldValue)
        {
            string text = ConvertToText(labelFieldValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Try TextDisplayFunction delegate
        if (accessors.TextDisplayFunction?.GetValue(element) is Delegate textFunc)
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

        return string.Empty;
    }

    private static string TryExtractConfigValue(UIElement element, ConfigElementAccessors accessors)
    {
        // Try Value property/field
        if (accessors.ValueProperty?.GetValue(element) is object valueObj)
        {
            return FormatConfigValue(valueObj);
        }

        if (accessors.ValueField?.GetValue(element) is object valueFieldObj)
        {
            return FormatConfigValue(valueFieldObj);
        }

        // Try GetValue method
        if (accessors.GetValueMethod is not null)
        {
            try
            {
                object? result = accessors.GetValueMethod.Invoke(element, Array.Empty<object>());
                return FormatConfigValue(result);
            }
            catch
            {
                // Ignore method invocation failures
            }
        }

        return string.Empty;
    }

    private static string TryExtractTooltip(UIElement element, ConfigElementAccessors accessors)
    {
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

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var accessors = new ConfigElementAccessors
        {
            LabelProperty = FindProperty(type, flags, "Label", "DisplayName", "Name"),
            LabelField = FindField(type, flags, "_label", "_text", "label", "text"),
            ValueProperty = FindProperty(type, flags, "Value", "CurrentValue"),
            ValueField = FindField(type, flags, "_value", "value"),
            TooltipProperty = FindProperty(type, flags, "Tooltip", "TooltipText"),
            TooltipField = FindField(type, flags, "_tooltip", "tooltip"),
            TextDisplayFunction = FindField(type, flags, "_TextDisplayFunction", "TextDisplayFunction"),
            GetValueMethod = type.GetMethod("GetValue", flags, null, Type.EmptyTypes, null),
        };

        ConfigElementAccessorCache[type] = accessors;
        return accessors;
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
        // Try to find and invoke adjustment methods
        Type type = element.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // For boolean toggles
        MethodInfo? toggleMethod = type.GetMethod("Toggle", flags)
                                 ?? type.GetMethod("OnClick", flags);

        if (toggleMethod is not null)
        {
            try
            {
                toggleMethod.Invoke(element, Array.Empty<object>());

                // Re-announce with new value
                string newDescription = DescribeConfigElement(element);
                announce(newDescription, false, MenuNarrationEventKind.ModConfig);
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }
            catch
            {
                // Ignore
            }
        }

        // For sliders/numeric values
        string adjustMethodName = direction > 0 ? "Increase" : "Decrease";
        MethodInfo? adjustMethod = type.GetMethod(adjustMethodName, flags);

        if (adjustMethod is not null)
        {
            try
            {
                adjustMethod.Invoke(element, Array.Empty<object>());

                // Re-announce with new value
                string newDescription = DescribeConfigElement(element);
                announce(newDescription, false, MenuNarrationEventKind.ModConfig);
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            catch
            {
                // Ignore
            }
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

                viewPositionProp.SetValue(container, new Vector2(0, targetY));
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
        public FieldInfo? TextDisplayFunction { get; init; }
        public MethodInfo? GetValueMethod { get; init; }
    }
}
