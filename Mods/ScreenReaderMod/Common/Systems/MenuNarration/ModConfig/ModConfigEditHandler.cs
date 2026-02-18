#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Handles navigation and narration for UIModConfig (config element editing screen).
/// </summary>
internal sealed class ModConfigEditHandler
{
    private readonly AnnouncementGate _gate;
    private readonly SliderRepeatState _sliderState = new();

    // Navigation state
    private int _currentElementIndex = -1;
    private List<UIElement>? _navigableElements;
    private int _configElementCount; // Excludes action buttons
    private int _skipInputFrames;

    public ModConfigEditHandler(AnnouncementGate gate)
    {
        _gate = gate;
    }

    /// <summary>
    /// Process the config edit screen for this frame.
    /// </summary>
    public void Process(
        UIState state,
        bool justEntered,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (justEntered)
        {
            _currentElementIndex = -1;
            _navigableElements = null;
            _configElementCount = 0;
            _skipInputFrames = 5; // Skip initial frames
            _sliderState.Reset();
        }

        // Get config list
        UIElement? configList = ReflectionCache.UIModConfig.MainConfigList?.GetValue(state) as UIElement
                              ?? ReflectionCache.UIModConfig.UIList?.GetValue(state) as UIElement;

        if (configList is null)
        {
            configList = FindUIList(state);
        }

        if (configList is null)
        {
            return;
        }

        // Collect navigable elements if needed
        if (_navigableElements is null || justEntered)
        {
            _navigableElements = CollectConfigElements(configList);
            _configElementCount = _navigableElements.Count;

            // Add action buttons
            List<UIElement> actionButtons = CollectActionButtons(state);
            _navigableElements.AddRange(actionButtons);

            ScreenReaderMod.Instance?.Logger.Debug(
                $"[ModConfigEditHandler] Found {_navigableElements.Count} elements " +
                $"({_configElementCount} config + {actionButtons.Count} buttons)");

            if (_navigableElements.Count > 0)
            {
                _currentElementIndex = 0;

                if (justEntered)
                {
                    AnnounceCurrentElement(isMenuContext, menuEventSink);
                    return; // Skip input on first frame
                }
            }
            else
            {
                _currentElementIndex = -1;
            }
        }
        else
        {
            // Refresh action buttons (they may change)
            RefreshActionButtons(state);
        }

        // Skip input during cooldown
        if (_skipInputFrames > 0)
        {
            _skipInputFrames--;
            return;
        }

        // Handle navigation
        HandleNavigation(isMenuContext, menuEventSink);
        HandleAction(isMenuContext, menuEventSink);
    }

    private void HandleNavigation(
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_navigableElements is null || _navigableElements.Count == 0)
        {
            return;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        TriggersSet current = PlayerInput.Triggers.Current;

        // Check for left/right held (sliders)
        bool leftHeld = current.MenuLeft;
        bool rightHeld = current.MenuRight;

        // Also check analog stick
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        if (gpState.IsConnected)
        {
            Vector2 stick = gpState.ThumbSticks.Left;
            const float threshold = 0.5f;

            if (stick.X < -threshold)
            {
                leftHeld = true;
            }
            if (stick.X > threshold)
            {
                rightHeld = true;
            }
        }

        // Handle slider adjustment
        int currentDirection = leftHeld ? -1 : (rightHeld ? 1 : 0);

        if (currentDirection != 0 && _currentElementIndex >= 0 && _currentElementIndex < _navigableElements.Count)
        {
            if (_sliderState.UpdateAndCheckShouldAdjust(currentDirection))
            {
                TryAdjustCurrentValue(currentDirection, isMenuContext, menuEventSink);
            }
            return;
        }
        else
        {
            _sliderState.UpdateAndCheckShouldAdjust(0);
        }

        // Handle up/down navigation
        int newIndex = _currentElementIndex;

        if (justPressed.MenuUp)
        {
            newIndex = _currentElementIndex > 0 ? _currentElementIndex - 1 : _navigableElements.Count - 1;
        }
        else if (justPressed.MenuDown)
        {
            newIndex = _currentElementIndex < _navigableElements.Count - 1 ? _currentElementIndex + 1 : 0;
        }

        if (newIndex != _currentElementIndex && newIndex >= 0 && newIndex < _navigableElements.Count)
        {
            _currentElementIndex = newIndex;
            _sliderState.ClearElementTracking();
            _gate.ClearFrameSuppression();
            AnnounceCurrentElement(isMenuContext, menuEventSink);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
    }

    private void HandleAction(
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_navigableElements is null || _navigableElements.Count == 0 || _currentElementIndex < 0)
        {
            return;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        if (!justPressed.MouseLeft)
        {
            return;
        }

        UIElement element = _navigableElements[_currentElementIndex];

        // Try toggle boolean
        if (ConfigSliderHandler.TryToggleBoolean(element))
        {
            AnnounceValueOnly(element, isMenuContext, menuEventSink);
            _gate.SuppressForFrames(30);
            return;
        }

        // Try cycle enum
        if (ConfigSliderHandler.TryCycleEnum(element))
        {
            AnnounceValueOnly(element, isMenuContext, menuEventSink);
            _gate.SuppressForFrames(30);
            return;
        }

        // Default: click
        if (ConfigSliderHandler.TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            AnnounceCurrentElement(isMenuContext, menuEventSink);
            _gate.SuppressForFrames(30);
        }
    }

    private void TryAdjustCurrentValue(
        int direction,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_navigableElements is null || _currentElementIndex < 0 || _currentElementIndex >= _navigableElements.Count)
        {
            return;
        }

        UIElement element = _navigableElements[_currentElementIndex];

        if (ConfigSliderHandler.TryAdjustSlider(element, direction, out float newPercent))
        {
            string valueOnly = $"{newPercent:0} percent";
            _gate.TryAnnounce(valueOnly, true, isMenuContext, menuEventSink);
            _sliderState.TrackElement(_currentElementIndex, newPercent);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        // For booleans, toggle on left/right
        Type type = element.GetType();
        string? typeName = type.FullName;
        if (typeName is not null && typeName.Contains("BooleanElement", StringComparison.Ordinal))
        {
            if (ConfigSliderHandler.TryInvokeClick(element))
            {
                AnnounceValueOnly(element, isMenuContext, menuEventSink);
            }
        }
    }

    private void AnnounceCurrentElement(
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_navigableElements is null || _currentElementIndex < 0 || _currentElementIndex >= _navigableElements.Count)
        {
            return;
        }

        UIElement element = _navigableElements[_currentElementIndex];
        string description = ConfigElementDescriber.DescribeConfigElement(element);

        if (!string.IsNullOrWhiteSpace(description))
        {
            _gate.TryAnnounce(description, false, isMenuContext, menuEventSink);
        }
    }

    private void AnnounceValueOnly(
        UIElement element,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        string valueOnly = ConfigElementDescriber.GetValueOnly(element);

        if (!string.IsNullOrWhiteSpace(valueOnly))
        {
            _gate.TryAnnounce(valueOnly, true, isMenuContext, menuEventSink);
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
                UIElement? configElement = GetConfigElementFromContainer(item);
                if (configElement is not null)
                {
                    elements.Add(configElement);
                }
                else if (IsConfigElement(item))
                {
                    elements.Add(item);
                }
            }
        }
        else
        {
            CollectConfigElementsRecursive(configList, elements, 0);
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
            return;
        }

        if (IsConfigElement(current))
        {
            elements.Add(current);
            return;
        }

        foreach (UIElement child in current.Children)
        {
            CollectConfigElementsRecursive(child, elements, depth + 1);
        }
    }

    private static UIElement? GetConfigElementFromContainer(UIElement container)
    {
        string typeName = container.GetType().Name;
        if (typeName.Contains("SortableElement", StringComparison.Ordinal) ||
            typeName.Contains("Container", StringComparison.Ordinal))
        {
            foreach (UIElement child in container.Children)
            {
                if (IsConfigElement(child))
                {
                    return child;
                }
            }
        }

        foreach (UIElement child in container.Children)
        {
            if (IsConfigElement(child))
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsConfigElement(UIElement element)
    {
        Type type = element.GetType();
        string? typeName = type.FullName;

        // Exclude headers
        if (typeName is not null && typeName.Contains("HeaderElement", StringComparison.Ordinal))
        {
            return false;
        }

        if (ReflectionCache.ConfigElement.Type is not null &&
            ReflectionCache.ConfigElement.Type.IsAssignableFrom(type))
        {
            return true;
        }

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

    private static bool TryGetListItems(UIElement list, out List<UIElement>? items)
    {
        items = null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        Type type = list.GetType();
        FieldInfo? itemsField = type.GetField("_items", flags) ?? type.GetField("items", flags);

        if (itemsField?.GetValue(list) is IEnumerable<UIElement> enumerable)
        {
            items = enumerable.ToList();
            return true;
        }

        if (list.Children.Any())
        {
            items = list.Children.ToList();
            return true;
        }

        return false;
    }

    private static UIElement? FindUIList(UIState state)
    {
        foreach (UIElement child in state.Children)
        {
            if (child.GetType().Name.Contains("UIList", StringComparison.Ordinal))
            {
                return child;
            }

            UIElement? found = FindUIListRecursive(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static UIElement? FindUIListRecursive(UIElement parent)
    {
        foreach (UIElement child in parent.Children)
        {
            if (child.GetType().Name.Contains("UIList", StringComparison.Ordinal))
            {
                return child;
            }

            UIElement? found = FindUIListRecursive(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static List<UIElement> CollectActionButtons(UIState state)
    {
        var buttons = new List<UIElement>();

        if (ReflectionCache.UIModConfig.BackButton?.GetValue(state) is UIElement backButton && IsButtonVisible(backButton))
        {
            buttons.Add(backButton);
        }

        if (ReflectionCache.UIModConfig.SaveConfigButton?.GetValue(state) is UIElement saveButton && IsButtonVisible(saveButton))
        {
            buttons.Add(saveButton);
        }

        if (ReflectionCache.UIModConfig.RevertConfigButton?.GetValue(state) is UIElement revertButton && IsButtonVisible(revertButton))
        {
            buttons.Add(revertButton);
        }

        if (ReflectionCache.UIModConfig.RestoreDefaultsConfigButton?.GetValue(state) is UIElement restoreButton && IsButtonVisible(restoreButton))
        {
            buttons.Add(restoreButton);
        }

        return buttons;
    }

    private static bool IsButtonVisible(UIElement button)
    {
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

        // Remove old action buttons
        while (_navigableElements.Count > _configElementCount)
        {
            _navigableElements.RemoveAt(_navigableElements.Count - 1);
        }

        // Re-collect
        List<UIElement> actionButtons = CollectActionButtons(state);
        _navigableElements.AddRange(actionButtons);

        // Adjust index
        if (_currentElementIndex >= _navigableElements.Count)
        {
            _currentElementIndex = _navigableElements.Count - 1;
        }
    }

    /// <summary>
    /// Reset all state.
    /// </summary>
    public void Reset()
    {
        _currentElementIndex = -1;
        _navigableElements = null;
        _configElementCount = 0;
        _skipInputFrames = 0;
        _sliderState.Reset();
    }
}

/// <summary>
/// Tracks slider hold-to-repeat state.
/// </summary>
internal sealed class SliderRepeatState
{
    public const int RepeatDelay = 25;
    public const int RepeatRate = 3;

    private int _repeatDirection;
    private int _repeatTimer;
    private int _lastElementIndex = -1;
    private float _lastValue = -1f;

    public bool UpdateAndCheckShouldAdjust(int currentDirection)
    {
        if (currentDirection == 0)
        {
            _repeatDirection = 0;
            _repeatTimer = 0;
            return false;
        }

        if (currentDirection != _repeatDirection)
        {
            _repeatDirection = currentDirection;
            _repeatTimer = 0;
            return true;
        }

        _repeatTimer++;

        if (_repeatTimer >= RepeatDelay)
        {
            int framesSinceDelay = _repeatTimer - RepeatDelay;
            return framesSinceDelay % RepeatRate == 0;
        }

        return false;
    }

    public void TrackElement(int elementIndex, float value)
    {
        _lastElementIndex = elementIndex;
        _lastValue = value;
    }

    public void ClearElementTracking()
    {
        _lastElementIndex = -1;
        _lastValue = -1f;
    }

    public void Reset()
    {
        _repeatDirection = 0;
        _repeatTimer = 0;
        _lastElementIndex = -1;
        _lastValue = -1f;
    }
}
