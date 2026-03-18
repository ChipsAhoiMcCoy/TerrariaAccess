#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.ModMenuAccessibility;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Mod Info (UIModInfo) screen.
/// This screen is shown when clicking "More Info" on a mod in the Manage Mods menu.
/// Uses reflection since UIModInfo is an internal type.
/// </summary>
public sealed class ModInfoAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Base Class Implementation

    protected override int BaseLinkId => LinkIdRegistry.ModInfo;
    protected override string MenuTypeName => "Terraria.ModLoader.UI.UIModInfo";
    protected override string SystemLogName => "ModInfo";

    #endregion

    #region Menu-Specific State

    // Navigation state for two-row layout
    private readonly List<PointBinding> _topRowBindings = new();
    private readonly List<PointBinding> _bottomRowBindings = new();
    private bool _inTopRow;

    // Context key for one-time description announcement
    private const string ContextKeyDescription = "modinfo:description";

    #endregion

    #region Public Properties

    /// <summary>
    /// Returns true if the Mod Info menu is currently active and handling gamepad input.
    /// This check is aggressive - it returns true as soon as UIModInfo is detected,
    /// even before we've fully configured our gamepad points.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            // Check the UI state directly
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null)
            {
                return false;
            }

            // Check by type name since we might not have the type reference yet
            string? typeName = currentState.GetType().FullName;
            return typeName == "Terraria.ModLoader.UI.UIModInfo";
        }
    }

    #endregion

    #region Lifecycle

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Log type availability (types are now cached in ReflectionCache)
        Mod.Logger.Info($"[ModInfo] Load: UIModInfo type found: {ReflectionCache.UIModInfo.Type is not null}");

        if (ReflectionCache.UIModInfo.Type is null)
        {
            Mod.Logger.Warn("[ModInfo] Could not find UIModInfo type");
            return;
        }

        // Let base class set up the DrawMenu hook
        base.Load();
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Clear menu-specific state
        _topRowBindings.Clear();
        _bottomRowBindings.Clear();
        _inTopRow = false;
        ScreenReaderService.ClearContexts("modinfo:");

        base.Unload();
    }

    #endregion

    #region Abstract Method Implementations

    protected override void OnMenuEntered(object menuState)
    {
        _inTopRow = false; // Start on bottom row (Back button)
        ScreenReaderService.ClearContexts("modinfo:");
    }

    protected override void OnMenuExited()
    {
        _topRowBindings.Clear();
        _bottomRowBindings.Clear();
        ScreenReaderService.ClearContexts("modinfo:");
    }

    protected override int GetInitialFocusFrameCount() => 15;

    protected override void ConfigureGamepadPoints(object menuState)
    {
        BindingById.Clear();
        _topRowBindings.Clear();
        _bottomRowBindings.Clear();

        int nextId = BaseLinkId;

        // Get the main container element to check which buttons are attached
        UIElement? mainContainer = ReflectionCache.UIModInfo.UIElement?.GetValue(menuState) as UIElement;

        if (mainContainer is null)
        {
            Mod.Logger.Debug("[ModInfo] ConfigureGamepadPoints: mainContainer is null, UI may not be initialized yet");
        }

        // Get button references
        UIElement? homepageButton = ReflectionCache.UIModInfo.ModHomepageButton?.GetValue(menuState) as UIElement;
        UIElement? steamButton = ReflectionCache.UIModInfo.ModSteamButton?.GetValue(menuState) as UIElement;
        UIElement? extractLocButton = ReflectionCache.UIModInfo.ExtractLocalizationButton?.GetValue(menuState) as UIElement;
        UIElement? fakeExtractLocButton = ReflectionCache.UIModInfo.FakeExtractLocalizationButton?.GetValue(menuState) as UIElement;
        UIElement? extractButton = ReflectionCache.UIModInfo.ExtractButton?.GetValue(menuState) as UIElement;
        UIElement? deleteButton = ReflectionCache.UIModInfo.DeleteButton?.GetValue(menuState) as UIElement;
        UIElement? fakeDeleteButton = ReflectionCache.UIModInfo.FakeDeleteButton?.GetValue(menuState) as UIElement;

        // Top row buttons (at Top = -65f): Homepage (center), Steam (left), Extract Localization (right)
        // These are conditionally shown based on mod properties

        // Check if homepage button is visible (has a parent = is attached to UI)
        if (homepageButton is not null && IsElementVisible(mainContainer, homepageButton))
        {
            var binding = CreateBinding(ref nextId, homepageButton,
                Language.GetTextValue("tModLoader.ModInfoVisitHomepage"), PointType.ActionButton);
            _topRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Steam button
        if (steamButton is not null && IsElementVisible(mainContainer, steamButton))
        {
            var binding = CreateBinding(ref nextId, steamButton,
                Language.GetTextValue("tModLoader.ModInfoVisitSteampage"), PointType.ActionButton);
            _topRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Extract Localization button (either active or fake/disabled version)
        if (extractLocButton is not null && IsElementVisible(mainContainer, extractLocButton))
        {
            var binding = CreateBinding(ref nextId, extractLocButton,
                Language.GetTextValue("tModLoader.ModInfoExtractLocalization"), PointType.ActionButton);
            _topRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }
        else if (fakeExtractLocButton is not null && IsElementVisible(mainContainer, fakeExtractLocButton))
        {
            var binding = CreateBinding(ref nextId, fakeExtractLocButton,
                Language.GetTextValue("tModLoader.ModInfoExtractLocalization") + " (disabled)", PointType.DisabledButton);
            _topRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Bottom row buttons (at Top = -20f): Back (left), Extract (center), Delete (right)
        // Back button is a local variable in OnInitialize, we need to find it by searching children
        UIElement? backButton = FindBackButton(mainContainer);
        if (backButton is not null)
        {
            var binding = CreateBinding(ref nextId, backButton,
                Language.GetTextValue("UI.Back"), PointType.BackButton);
            _bottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Extract button
        if (extractButton is not null && IsElementVisible(mainContainer, extractButton))
        {
            var binding = CreateBinding(ref nextId, extractButton,
                Language.GetTextValue("tModLoader.ModInfoExtract"), PointType.ActionButton);
            _bottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Delete button (either active or fake/disabled version)
        if (deleteButton is not null && IsElementVisible(mainContainer, deleteButton))
        {
            var binding = CreateBinding(ref nextId, deleteButton,
                Language.GetTextValue("UI.Delete"), PointType.ActionButton);
            _bottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }
        else if (fakeDeleteButton is not null && IsElementVisible(mainContainer, fakeDeleteButton))
        {
            var binding = CreateBinding(ref nextId, fakeDeleteButton,
                Language.GetTextValue("UI.Delete") + " (disabled)", PointType.DisabledButton);
            _bottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Sort bindings by X position for proper left/right navigation
        _topRowBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
        _bottomRowBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));

        Mod.Logger.Debug($"[ModInfo] ConfigureGamepadPoints: {_topRowBindings.Count} top buttons, {_bottomRowBindings.Count} bottom buttons, {BindingById.Count} total bindings");

        if (BindingById.Count == 0)
        {
            Mod.Logger.Warn("[ModInfo] ConfigureGamepadPoints: No bindings found - UI buttons may not be populated yet");
            return;
        }

        // Create all link points
        var allBindings = new List<PointBinding>();
        allBindings.AddRange(_topRowBindings);
        allBindings.AddRange(_bottomRowBindings);

        foreach (PointBinding binding in allBindings)
        {
            SetupLinkPoint(binding);
        }

        // Connect top row horizontally
        for (int i = 0; i < _topRowBindings.Count - 1; i++)
        {
            ConnectHorizontal(_topRowBindings[i], _topRowBindings[i + 1]);
        }

        // Connect bottom row horizontally
        for (int i = 0; i < _bottomRowBindings.Count - 1; i++)
        {
            ConnectHorizontal(_bottomRowBindings[i], _bottomRowBindings[i + 1]);
        }

        // Connect rows vertically
        if (_topRowBindings.Count > 0 && _bottomRowBindings.Count > 0)
        {
            // Connect each top row button to nearest bottom row button
            foreach (var topBinding in _topRowBindings)
            {
                var nearest = FindNearestHorizontal(topBinding, _bottomRowBindings);
                if (nearest.HasValue)
                {
                    UILinkPoint topPoint = EnsureLinkPoint(topBinding.Id);
                    UILinkPoint bottomPoint = EnsureLinkPoint(nearest.Value.Id);
                    topPoint.Down = nearest.Value.Id;
                    bottomPoint.Up = topBinding.Id;
                }
            }
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Force initial focus to Back button
        if (PlayerInput.UsingGamepadUI && InitialFocusFramesRemaining > 0)
        {
            int defaultPointId = _bottomRowBindings.Count > 0 ? _bottomRowBindings[0].Id :
                                 _topRowBindings.Count > 0 ? _topRowBindings[0].Id :
                                 BaseLinkId;

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

        var currentList = _inTopRow ? _topRowBindings : _bottomRowBindings;

        bool navigated = false;

        if (CurrentInput.Left && CurrentFocusIndex > 0)
        {
            CurrentFocusIndex--;
            navigated = true;
        }
        else if (CurrentInput.Right && CurrentFocusIndex < currentList.Count - 1)
        {
            CurrentFocusIndex++;
            navigated = true;
        }
        else if (CurrentInput.Up && !_inTopRow && _topRowBindings.Count > 0)
        {
            _inTopRow = true;
            CurrentFocusIndex = Math.Min(CurrentFocusIndex, _topRowBindings.Count - 1);
            navigated = true;
        }
        else if (CurrentInput.Down && _inTopRow && _bottomRowBindings.Count > 0)
        {
            _inTopRow = false;
            CurrentFocusIndex = Math.Min(CurrentFocusIndex, _bottomRowBindings.Count - 1);
            navigated = true;
        }

        if (navigated)
        {
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
                Mod.Logger.Info($"[ModInfo] Navigated to row {(_inTopRow ? "top" : "bottom")}, index {CurrentFocusIndex}, point {newPointId.Value}");
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
                // Don't click disabled buttons
                if (binding.Type == PointType.DisabledButton)
                {
                    Mod.Logger.Info($"[ModInfo] Button is disabled: {binding.Label}");
                    ScreenReaderService.Announce("Button disabled", force: true);
                    return;
                }

                if (binding.Element is UIElement buttonElement)
                {
                    Mod.Logger.Info($"[ModInfo] Clicking button: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(buttonElement, Main.MouseScreen);
                        buttonElement.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModInfo] Button click failed: {ex.Message}");
                    }
                }
            }
        }

        // B button goes back
        if (CurrentInput.BackPressed)
        {
            // Find and click the Back button
            foreach (var binding in _bottomRowBindings)
            {
                if (binding.Type == PointType.BackButton && binding.Element is UIElement backButton)
                {
                    Mod.Logger.Info("[ModInfo] B button pressed, clicking Back");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(backButton, Main.MouseScreen);
                        backButton.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModInfo] Back button click failed: {ex.Message}");
                    }
                    return;
                }
            }
        }
    }

    protected override int? GetCurrentPointId()
    {
        var currentList = _inTopRow ? _topRowBindings : _bottomRowBindings;
        if (currentList.Count == 0 || CurrentFocusIndex < 0 || CurrentFocusIndex >= currentList.Count)
        {
            return null;
        }

        return currentList[CurrentFocusIndex].Id;
    }

    protected override string BuildAnnouncement(PointBinding binding, object menuState)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // On first focus, announce the mod name and description
        if (!ScreenReaderService.WasContextAnnounced(ContextKeyDescription))
        {
            ScreenReaderService.MarkContextAnnounced(ContextKeyDescription);

            string? modDisplayName = ReflectionCache.UIModInfo.ModDisplayName?.GetValue(menuState) as string;
            string? description = ReflectionCache.UIModInfo.Info?.GetValue(menuState) as string;

            if (!string.IsNullOrEmpty(modDisplayName))
            {
                string header = $"Mod Info: {modDisplayName}";
                if (!string.IsNullOrEmpty(description))
                {
                    // Truncate very long descriptions
                    if (description.Length > 500)
                    {
                        description = description.Substring(0, 500) + "...";
                    }
                    header = $"{header}. {TextSanitizer.Clean(description)}";
                }

                // Add button position context
                string positionContext = BuildPositionContext();
                return $"{header}. {label}{positionContext}";
            }
        }

        // For subsequent announcements, include button position context
        string position = BuildPositionContext();
        return $"{label}{position}";
    }

    private string BuildPositionContext()
    {
        var currentList = _inTopRow ? _topRowBindings : _bottomRowBindings;
        if (currentList.Count <= 1)
        {
            return string.Empty;
        }

        int buttonIndex = CurrentFocusIndex + 1;
        int buttonTotal = currentList.Count;
        string rowName = _inTopRow ? "top row" : "bottom row";

        return $", {buttonIndex} of {buttonTotal} in {rowName}";
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a button binding with centered position (adapts to base class PointBinding).
    /// </summary>
    private static PointBinding CreateBinding(ref int nextId, UIElement element, string label, PointType type)
    {
        CalculatedStyle dims = element.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        return new PointBinding(nextId++, center, label, string.Empty, element, type);
    }

    private static bool IsElementVisible(UIElement? parent, UIElement element)
    {
        if (parent is null)
        {
            return false;
        }

        return parent.HasChild(element);
    }

    private static UIElement? FindBackButton(UIElement? container)
    {
        if (container is null)
        {
            return null;
        }

        // The Back button is a UIAutoScaleTextTextPanel<string> with "Back" text
        // Prioritize text matching for reliability

        try
        {
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(container) is not List<UIElement> elements)
            {
                return null;
            }

            string backText = Language.GetTextValue("UI.Back");

            // First pass: find by text content (most reliable)
            foreach (var element in elements)
            {
                string typeName = element.GetType().Name;
                if (!typeName.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                string? text = textProperty?.GetValue(element) as string;

                if (text == backText)
                {
                    return element;
                }
            }

            // Fallback: find by position (VAlign ≈ 1f, HAlign ≈ 0, Top ≈ -20f)
            foreach (var element in elements)
            {
                string typeName = element.GetType().Name;
                if (!typeName.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                // Check position with wider tolerances
                if (element.VAlign < 0.85f) continue;
                if (element.HAlign > 0.2f) continue;
                if (element.Top.Pixels < -40f || element.Top.Pixels > 0f) continue;

                TerrariaAccess.Instance?.Logger.Debug($"[ModInfo] Found Back button by position fallback");
                return element;
            }

            TerrariaAccess.Instance?.Logger.Debug("[ModInfo] Back button not found");
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[ModInfo] Error finding Back button: {ex.Message}");
        }

        return null;
    }

    private static PointBinding? FindNearestHorizontal(PointBinding origin, List<PointBinding> bindings)
    {
        PointBinding? nearest = null;
        float minDistance = float.MaxValue;

        foreach (var binding in bindings)
        {
            float distance = Math.Abs(binding.Position.X - origin.Position.X);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = binding;
            }
        }

        return nearest;
    }

    #endregion
}
