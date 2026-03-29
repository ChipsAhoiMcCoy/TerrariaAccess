#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Handles navigation and narration for UIModConfigList (mod selection screen).
/// Manages dual-list navigation: mod list on left, config list on right.
/// </summary>
internal sealed class ModConfigListHandler
{
    private readonly AnnouncementGate _gate;

    // Navigation state
    private int _modListIndex = -1;
    private int _configListIndex = -1;
    private List<UIElement>? _modListElements;
    private List<UIElement>? _configListElements;
    private bool _pendingConfigListNavigation;
    private bool _isConfigListFocused;
    private bool _justEntered;

    public ModConfigListHandler(AnnouncementGate gate)
    {
        _gate = gate;
    }

    /// <summary>
    /// Whether we're currently focused on the config list (right side).
    /// </summary>
    public bool IsConfigListFocused => _isConfigListFocused;

    /// <summary>
    /// Process the list screen for this frame.
    /// </summary>
    /// <param name="state">The UIModConfigList state.</param>
    /// <param name="justEntered">True if we just entered this screen.</param>
    /// <param name="isMenuContext">True for menu context, false for in-game.</param>
    /// <param name="menuEventSink">Event sink for menu context.</param>
    public void Process(
        UIState state,
        bool justEntered,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        _justEntered = justEntered;

        if (justEntered)
        {
            _isConfigListFocused = false;
            _modListIndex = -1;
            _configListIndex = -1;
            _modListElements = null;
            _configListElements = null;
            _pendingConfigListNavigation = false;
        }

        // Get list elements
        UIElement? modList = ReflectionCache.UIModConfigList.ModList?.GetValue(state) as UIElement;
        UIElement? configList = ReflectionCache.UIModConfigList.ConfigList?.GetValue(state) as UIElement;

        TerrariaAccess.Instance?.Logger.Debug(
            $"[ModConfigListHandler] modList={modList?.GetType().Name ?? "null"}, configList={configList?.GetType().Name ?? "null"}");

        if (modList is null)
        {
            TerrariaAccess.Instance?.Logger.Debug("[ModConfigListHandler] modList is null, returning");
            return;
        }

        // Collect mod list elements
        if (_modListElements is null || justEntered)
        {
            _modListElements = CollectNavigableElements(modList);
            _modListIndex = _modListElements.Count > 0 ? 0 : -1;

            TerrariaAccess.Instance?.Logger.Debug(
                $"[ModConfigListHandler] Collected {_modListElements.Count} mod list elements, index={_modListIndex}");

            if (_modListIndex >= 0 && justEntered)
            {
                TerrariaAccess.Instance?.Logger.Debug("[ModConfigListHandler] Announcing first element on entry");
                AnnounceModListElement(_modListIndex, isMenuContext, menuEventSink);
                return; // Skip input on first frame
            }
        }

        // Always refresh config list (it changes when a mod is selected)
        if (configList is not null)
        {
            var newConfigElements = CollectNavigableElements(configList);

            bool configListChanged = _configListElements is null ||
                                     _configListElements.Count != newConfigElements.Count ||
                                     (_configListElements.Count > 0 && newConfigElements.Count > 0 &&
                                      !ReferenceEquals(_configListElements[0], newConfigElements[0]));

            if (configListChanged)
            {
                _configListElements = newConfigElements;

                if (_isConfigListFocused && _configListElements.Count > 0)
                {
                    _configListIndex = 0;
                }
                else if (_configListElements.Count == 0)
                {
                    _configListIndex = -1;
                }
            }

            // Handle pending auto-navigation after selecting a mod
            if (_pendingConfigListNavigation)
            {
                if (_configListElements is not null && _configListElements.Count > 0)
                {
                    _pendingConfigListNavigation = false;
                    _isConfigListFocused = true;
                    _configListIndex = 0;
                    AnnounceConfigListElement(0, isMenuContext, menuEventSink);
                    return;
                }
                else
                {
                    _pendingConfigListNavigation = false;
                    string noConfigs = LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.ModConfigMenu.NoConfigsForMod",
                        "This mod has no configurations.");
                    _gate.TryAnnounce(noConfigs, false, isMenuContext, menuEventSink);
                }
            }
        }

        // Handle navigation input
        HandleNavigation(state, isMenuContext, menuEventSink);
    }

    private void HandleNavigation(
        UIState state,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        // Handle left/right to switch between lists
        if (justPressed.MenuLeft && _isConfigListFocused)
        {
            _isConfigListFocused = false;
            if (_modListIndex >= 0 && _modListElements is not null && _modListIndex < _modListElements.Count)
            {
                string listName = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.ModConfigMenu.ModListName", "Mod list");
                _gate.TryAnnounce(listName, false, isMenuContext, menuEventSink);
                AnnounceModListElement(_modListIndex, isMenuContext, menuEventSink);
            }
            SoundEngine.PlaySound(SoundID.MenuTick);
            return;
        }

        if (justPressed.MenuRight && !_isConfigListFocused)
        {
            if (_configListElements is not null && _configListElements.Count > 0)
            {
                _isConfigListFocused = true;
                if (_configListIndex < 0)
                {
                    _configListIndex = 0;
                }
                AnnounceConfigListElement(_configListIndex, isMenuContext, menuEventSink);
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            else
            {
                string noConfigs = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.ModConfigMenu.NoConfigs",
                    "No configurations available. Select a mod first.");
                _gate.TryAnnounce(noConfigs, false, isMenuContext, menuEventSink);
            }
            return;
        }

        // Handle up/down navigation within current list
        if (_isConfigListFocused)
        {
            if (HandleConfigListNavigation(justPressed, isMenuContext, menuEventSink))
            {
                return;
            }
            HandleConfigListSelect(justPressed, isMenuContext, menuEventSink);
        }
        else
        {
            if (HandleModListNavigation(justPressed, isMenuContext, menuEventSink))
            {
                return;
            }
            HandleModListSelect(state, justPressed, isMenuContext, menuEventSink);
        }
    }

    private bool HandleModListNavigation(
        TriggersSet justPressed,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_modListElements is null || _modListElements.Count == 0 || _modListIndex < 0)
        {
            return false;
        }

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
            AnnounceModListElement(newIndex, isMenuContext, menuEventSink);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return true;
        }

        return false;
    }

    private bool HandleConfigListNavigation(
        TriggersSet justPressed,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_configListElements is null || _configListElements.Count == 0 || _configListIndex < 0)
        {
            return false;
        }

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
            AnnounceConfigListElement(newIndex, isMenuContext, menuEventSink);
            SoundEngine.PlaySound(SoundID.MenuTick);
            return true;
        }

        return false;
    }

    private void HandleModListSelect(
        UIState state,
        TriggersSet justPressed,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_modListElements is null || _modListElements.Count == 0 || _modListIndex < 0)
        {
            return;
        }

        if (!justPressed.MouseLeft)
        {
            return;
        }

        UIElement element = _modListElements[_modListIndex];

        if (ConfigSliderHandler.TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            _pendingConfigListNavigation = true;
        }
    }

    private void HandleConfigListSelect(
        TriggersSet justPressed,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_configListElements is null || _configListElements.Count == 0 || _configListIndex < 0)
        {
            return;
        }

        if (!justPressed.MouseLeft)
        {
            return;
        }

        UIElement element = _configListElements[_configListIndex];

        if (ConfigSliderHandler.TryInvokeClick(element))
        {
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }
    }

    private void AnnounceModListElement(
        int index,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
    {
        if (_modListElements is null || index < 0 || index >= _modListElements.Count)
        {
            return;
        }

        UIElement element = _modListElements[index];
        string description = ConfigElementDescriber.DescribeModListElement(element);

        if (!string.IsNullOrWhiteSpace(description))
        {
            string positionInfo = $"{index + 1} of {_modListElements.Count}";
            string announcement = $"{description}, {positionInfo}";
            _gate.TryAnnounce(announcement, false, isMenuContext, menuEventSink);
                    }
    }

    private void AnnounceConfigListElement(
        int index,
        bool isMenuContext,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink)
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
        string announcement = $"{description}, {positionInfo}";
        _gate.TryAnnounce(announcement, false, isMenuContext, menuEventSink);
            }

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
        Type type = current.GetType();
        string? typeName = type.FullName;

        if (typeName is not null &&
            (typeName.Contains("UIButton", StringComparison.Ordinal) ||
             typeName.Contains("UIModConfigItem", StringComparison.Ordinal) ||
             typeName.Contains("UITextPanel", StringComparison.Ordinal)))
        {
            elements.Add(current);
        }

        foreach (UIElement child in current.Children)
        {
            CollectNavigableElementsRecursive(child, elements);
        }
    }

    private static string ExtractElementText(UIElement element)
    {
        return ConfigElementDescriber.DescribeModListElement(element);
    }

    /// <summary>
    /// Reset all state.
    /// </summary>
    public void Reset()
    {
        _modListIndex = -1;
        _configListIndex = -1;
        _modListElements = null;
        _configListElements = null;
        _pendingConfigListNavigation = false;
        _isConfigListFocused = false;
        _justEntered = false;
    }
}
