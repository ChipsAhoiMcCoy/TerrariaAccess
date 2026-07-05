#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.ModMenuAccessibility;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Mod Sources (UIModSources) screen.
/// This screen is for mod developers to build and manage their mod source projects.
/// </summary>
public sealed class ModSourcesAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Base Class Implementation

    protected override int BaseLinkId => LinkIdRegistry.ModSources;
    protected override string MenuTypeName => "Terraria.ModLoader.UI.UIModSources";
    protected override string SystemLogName => "ModSources";

    #endregion

    #region Public Properties

    /// <summary>
    /// Returns true if the Mod Sources menu is currently active and handling gamepad input.
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

            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null)
            {
                return false;
            }

            return currentState.GetType().FullName == "Terraria.ModLoader.UI.UIModSources";
        }
    }

    #endregion

    #region Menu-Specific State

    private enum NavigationRegion
    {
        SourceList,
        BottomButtons
    }

    private NavigationRegion _currentRegion = NavigationRegion.BottomButtons;

    // Source list navigation
    private readonly List<ModSourceItemBindings> _sourceBindings = new();
    private int _currentSourceIndex;
    private int _currentButtonIndex;

    // Bottom action buttons
    private readonly List<PointBinding> _bottomButtonBindings = new();

    // Context key for screen announcement
    private const string ContextKeyScreen = "modsources:screen";

    #endregion

    #region Nested Types

    /// <summary>
    /// Holds all navigable bindings for a single mod source item.
    /// </summary>
    private sealed class ModSourceItemBindings
    {
        public UIElement SourceItem { get; init; } = null!;
        public string ModName { get; init; } = string.Empty;
        public bool HasBuiltMod { get; init; }
        public List<PointBinding> Buttons { get; } = new();
    }

    #endregion

    #region Lifecycle

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        Mod.Logger.Info($"[ModSources] Load: UIModSources type found: {ReflectionCache.UIModSources.Type is not null}");

        if (ReflectionCache.UIModSources.Type is null)
        {
            Mod.Logger.Warn("[ModSources] Could not find UIModSources type");
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

        _sourceBindings.Clear();
        _bottomButtonBindings.Clear();
        ScreenReaderService.ClearContexts("modsources:");

        base.Unload();
    }

    #endregion

    #region Abstract Method Implementations

    protected override void OnMenuEntered(object menuState)
    {
        _currentRegion = NavigationRegion.BottomButtons;
        _currentSourceIndex = 0;
        _currentButtonIndex = 0;
        ScreenReaderService.ClearContexts("modsources:");
    }

    protected override void OnMenuExited()
    {
        _sourceBindings.Clear();
        _bottomButtonBindings.Clear();
        ScreenReaderService.ClearContexts("modsources:");
    }

    protected override int GetInitialFocusFrameCount() => 30; // Allow time for async source loading

    protected override void ConfigureGamepadPoints(object menuState)
    {
        BindingById.Clear();
        _sourceBindings.Clear();
        _bottomButtonBindings.Clear();

        int nextId = BaseLinkId;

        // Get the mod source list
        UIList? modList = ReflectionCache.UIModSources.ModList?.GetValue(menuState) as UIList;

        // Build source item bindings
        if (modList is not null)
        {
            foreach (UIElement item in modList)
            {
                if (item.GetType().Name != "UIModSourceItem")
                {
                    continue;
                }

                ModSourceItemBindings sourceBindings = CreateSourceBindings(item, ref nextId);
                if (sourceBindings.Buttons.Count > 0)
                {
                    _sourceBindings.Add(sourceBindings);
                }
            }
        }

        // Find bottom action buttons by searching the main container
        UIElement? mainElement = ReflectionCache.UIModSources.UIElement?.GetValue(menuState) as UIElement;
        if (mainElement is not null)
        {
            FindBottomButtons(mainElement, ref nextId);
        }

        if (BindingById.Count == 0)
        {
            return;
        }

        // Set up all link points
        foreach (var binding in BindingById.Values)
        {
            SetupLinkPoint(binding);
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Handle initial focus
        if (PlayerInput.UsingGamepadUI && InitialFocusFramesRemaining > 0)
        {
            int defaultPointId = _bottomButtonBindings.Count > 0
                ? _bottomButtonBindings[0].Id
                : BaseLinkId;

            UILinkPointNavigator.ChangePoint(defaultPointId);
            InitialFocusFramesRemaining--;
        }
    }

    protected override void HandleNavigation(object menuState)
    {
        if (!CurrentInput.HasNavigation)
        {
            return;
        }

        bool navigated = false;

        switch (_currentRegion)
        {
            case NavigationRegion.SourceList:
                navigated = HandleSourceListNavigation();
                break;
            case NavigationRegion.BottomButtons:
                navigated = HandleBottomButtonNavigation();
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

    protected override void HandleAction(object menuState)
    {
        if (CurrentInput.ActionPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                if (binding.Type == PointType.DisabledButton)
                {
                    ScreenReaderService.Announce("Button disabled", force: true);
                    return;
                }

                if (binding.Element is UIElement buttonElement)
                {
                    Mod.Logger.Info($"[ModSources] Clicking: {binding.Label}");
                    global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();

                    try
                    {
                        var clickEvent = new UIMouseEvent(buttonElement, Main.MouseScreen);
                        global::TerrariaAccess.Common.Services.ProgrammaticUiClickInvoker.LeftClick(buttonElement, clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModSources] Click failed: {ex.Message}");
                    }
                }
            }
        }

        if (CurrentInput.BackPressed)
        {
            // Find and click the Back button
            foreach (var binding in _bottomButtonBindings)
            {
                if (binding.Type == PointType.BackButton && binding.Element is UIElement backButton)
                {
                    Mod.Logger.Info("[ModSources] B button pressed, clicking Back");
                    global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();

                    try
                    {
                        var clickEvent = new UIMouseEvent(backButton, Main.MouseScreen);
                        global::TerrariaAccess.Common.Services.ProgrammaticUiClickInvoker.LeftClick(backButton, clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModSources] Back click failed: {ex.Message}");
                    }
                    return;
                }
            }
        }
    }

    protected override int? GetCurrentPointId()
    {
        switch (_currentRegion)
        {
            case NavigationRegion.SourceList:
                if (_sourceBindings.Count == 0 ||
                    _currentSourceIndex < 0 ||
                    _currentSourceIndex >= _sourceBindings.Count)
                {
                    return null;
                }
                var source = _sourceBindings[_currentSourceIndex];
                if (_currentButtonIndex < 0 || _currentButtonIndex >= source.Buttons.Count)
                {
                    return null;
                }
                return source.Buttons[_currentButtonIndex].Id;

            case NavigationRegion.BottomButtons:
                if (_bottomButtonBindings.Count == 0 ||
                    CurrentFocusIndex < 0 ||
                    CurrentFocusIndex >= _bottomButtonBindings.Count)
                {
                    return null;
                }
                return _bottomButtonBindings[CurrentFocusIndex].Id;

            default:
                return null;
        }
    }

    protected override string BuildAnnouncement(PointBinding binding, object menuState)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // On first focus, announce the screen name
        if (!ScreenReaderService.WasContextAnnounced(ContextKeyScreen))
        {
            ScreenReaderService.MarkContextAnnounced(ContextKeyScreen);
            int sourceCount = _sourceBindings.Count;
            string header = sourceCount == 0
                ? "Mod Sources. No mod sources found."
                : $"Mod Sources. {sourceCount} mod source{(sourceCount == 1 ? "" : "s")} available.";
            return $"{header} {label}";
        }

        // For source buttons, include mod name context and button position
        if (_currentRegion == NavigationRegion.SourceList &&
            _currentSourceIndex >= 0 &&
            _currentSourceIndex < _sourceBindings.Count)
        {
            var source = _sourceBindings[_currentSourceIndex];
            int sourceIndex = _currentSourceIndex + 1;
            int sourceTotal = _sourceBindings.Count;
            int buttonIndex = _currentButtonIndex + 1;
            int buttonTotal = source.Buttons.Count;

            // If this is the first button, announce full source info
            if (_currentButtonIndex == 0)
            {
                string status = source.HasBuiltMod ? "Built" : "Not built";
                return $"{source.ModName}. {status}. {label}, button {buttonIndex} of {buttonTotal}, source {sourceIndex} of {sourceTotal}";
            }

            // For subsequent buttons, just announce button position
            return $"{label}, button {buttonIndex} of {buttonTotal}";
        }

        // For bottom buttons, announce position
        if (_currentRegion == NavigationRegion.BottomButtons &&
            CurrentFocusIndex >= 0 &&
            CurrentFocusIndex < _bottomButtonBindings.Count)
        {
            int buttonIndex = CurrentFocusIndex + 1;
            int buttonTotal = _bottomButtonBindings.Count;
            if (buttonTotal > 1)
            {
                return $"{label}, {buttonIndex} of {buttonTotal}";
            }
        }

        return label;
    }

    #endregion

    #region Navigation Helpers

    private bool HandleSourceListNavigation()
    {
        if (_sourceBindings.Count == 0)
        {
            return false;
        }

        var currentSource = _sourceBindings[_currentSourceIndex];
        bool navigated = false;

        if (CurrentInput.Left && _currentButtonIndex > 0)
        {
            _currentButtonIndex--;
            navigated = true;
        }
        else if (CurrentInput.Right && _currentButtonIndex < currentSource.Buttons.Count - 1)
        {
            _currentButtonIndex++;
            navigated = true;
        }
        else if (CurrentInput.Up)
        {
            if (_currentSourceIndex > 0)
            {
                _currentSourceIndex--;
                _currentButtonIndex = Math.Min(_currentButtonIndex, _sourceBindings[_currentSourceIndex].Buttons.Count - 1);
                navigated = true;
            }
        }
        else if (CurrentInput.Down)
        {
            if (_currentSourceIndex < _sourceBindings.Count - 1)
            {
                _currentSourceIndex++;
                _currentButtonIndex = Math.Min(_currentButtonIndex, _sourceBindings[_currentSourceIndex].Buttons.Count - 1);
                navigated = true;
            }
            else if (_bottomButtonBindings.Count > 0)
            {
                // Move to bottom buttons
                _currentRegion = NavigationRegion.BottomButtons;
                CurrentFocusIndex = 0;
                navigated = true;
            }
        }

        return navigated;
    }

    private bool HandleBottomButtonNavigation()
    {
        bool navigated = false;

        if (CurrentInput.Left && CurrentFocusIndex > 0)
        {
            CurrentFocusIndex--;
            navigated = true;
        }
        else if (CurrentInput.Right && CurrentFocusIndex < _bottomButtonBindings.Count - 1)
        {
            CurrentFocusIndex++;
            navigated = true;
        }
        else if (CurrentInput.Up && _sourceBindings.Count > 0)
        {
            // Move to source list
            _currentRegion = NavigationRegion.SourceList;
            _currentSourceIndex = _sourceBindings.Count - 1;
            _currentButtonIndex = 0;
            navigated = true;
        }

        return navigated;
    }

    #endregion

    #region Binding Creation

    private ModSourceItemBindings CreateSourceBindings(UIElement sourceItem, ref int nextId)
    {
        string modName = ReflectionCache.UIModSourceItem.ModName?.GetValue(sourceItem) as string ?? "Unknown";
        object? builtMod = ReflectionCache.UIModSourceItem.BuiltMod?.GetValue(sourceItem);

        var bindings = new ModSourceItemBindings
        {
            SourceItem = sourceItem,
            ModName = modName,
            HasBuiltMod = builtMod is not null
        };

        // Find buttons by searching children
        // The buttons are: Build, Build + Reload, and optionally Publish or Rebuild Required
        try
        {
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(sourceItem) is not List<UIElement> elements)
            {
                return bindings;
            }

            string buildText = Language.GetTextValue("tModLoader.MSBuild");
            string buildReloadText = Language.GetTextValue("tModLoader.MSBuildReload");
            string publishText = Language.GetTextValue("tModLoader.MSPublish");
            string rebuildText = Language.GetTextValue("tModLoader.MSRebuildRequired");

            // Track found buttons to avoid duplicates
            var foundButtons = new HashSet<UIElement>();

            // First pass: find buttons by text content (most reliable)
            foreach (var element in elements)
            {
                if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                string? text = textProperty?.GetValue(element) as string;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                string label;
                PointType type = PointType.ActionButton;

                if (text == buildText)
                {
                    label = buildText;
                }
                else if (text == buildReloadText)
                {
                    label = buildReloadText;
                }
                else if (text == publishText)
                {
                    label = publishText;
                }
                else if (text == rebuildText)
                {
                    label = rebuildText;
                    type = PointType.DisabledButton; // Can't click rebuild required
                }
                else
                {
                    continue;
                }

                foundButtons.Add(element);
                AddSourceButtonBinding(bindings, element, label, type, ref nextId);
            }

            // Fallback: find buttons by position if text matching failed
            if (foundButtons.Count == 0)
            {
                Mod.Logger.Debug($"[ModSources] Using position fallback for {modName}");
                foreach (var element in elements)
                {
                    if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                    {
                        continue;
                    }

                    // Check Top position - buttons are at Top ≈ 40f (use wider tolerance)
                    if (Math.Abs(element.Top.Pixels - 40f) > 20f)
                    {
                        continue;
                    }

                    // Try to determine which button by position (use wider tolerances)
                    float leftPixels = element.Left.Pixels;
                    string label;
                    PointType type = PointType.ActionButton;

                    if (leftPixels < 50f)
                    {
                        // Build button (typically at ~10f)
                        label = buildText;
                    }
                    else if (leftPixels < 200f)
                    {
                        // Build + Reload button (typically at ~150f)
                        label = buildReloadText;
                    }
                    else if (leftPixels > 300f)
                    {
                        // Publish or Rebuild Required (typically at ~360-390f)
                        var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                        string? text = textProperty?.GetValue(element) as string;

                        if (text == rebuildText)
                        {
                            label = rebuildText;
                            type = PointType.DisabledButton;
                        }
                        else
                        {
                            label = publishText;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    AddSourceButtonBinding(bindings, element, label, type, ref nextId);
                }
            }

            // Sort buttons by X position
            bindings.Buttons.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));

            if (bindings.Buttons.Count == 0)
            {
                Mod.Logger.Debug($"[ModSources] No buttons found for {modName}");
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ModSources] Error finding source buttons for {modName}: {ex.Message}");
        }

        return bindings;
    }

    private void AddSourceButtonBinding(ModSourceItemBindings bindings, UIElement button, string label, PointType type, ref int nextId)
    {
        CalculatedStyle dims = button.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        var binding = new PointBinding(nextId++, center, label, string.Empty, button, type);
        bindings.Buttons.Add(binding);
        BindingById[binding.Id] = binding;
    }

    private void FindBottomButtons(UIElement container, ref int nextId)
    {
        try
        {
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(container) is not List<UIElement> elements)
            {
                return;
            }

            string backText = Language.GetTextValue("UI.Back");
            string openSourcesText = Language.GetTextValue("tModLoader.MSOpenSources");
            string buildAllText = Language.GetTextValue("tModLoader.MSBuildAll");
            string buildReloadAllText = Language.GetTextValue("tModLoader.MSBuildReloadAll");
            string createModText = Language.GetTextValue("tModLoader.MSCreateMod");

            foreach (var element in elements)
            {
                if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                // Bottom buttons have VAlign = 1f and specific Top values
                if (element.VAlign < 0.9f)
                {
                    continue;
                }

                // Try to get the Text property
                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                string? text = textProperty?.GetValue(element) as string;

                PointType buttonType = PointType.ActionButton;
                string label;

                if (text == backText)
                {
                    buttonType = PointType.BackButton;
                    label = backText;
                }
                else if (text == openSourcesText)
                {
                    label = openSourcesText;
                }
                else if (text == buildAllText)
                {
                    label = buildAllText;
                }
                else if (text == buildReloadAllText)
                {
                    label = buildReloadAllText;
                }
                else if (text == createModText)
                {
                    label = createModText;
                }
                else if (text is not null)
                {
                    label = text;
                }
                else
                {
                    continue;
                }

                CalculatedStyle dims = element.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var binding = new PointBinding(nextId++, center, label, string.Empty, element, buttonType);
                _bottomButtonBindings.Add(binding);
                BindingById[binding.Id] = binding;
            }

            // Sort by X position
            _bottomButtonBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[ModSources] Error finding bottom buttons: {ex.Message}");
        }
    }

    #endregion
}
