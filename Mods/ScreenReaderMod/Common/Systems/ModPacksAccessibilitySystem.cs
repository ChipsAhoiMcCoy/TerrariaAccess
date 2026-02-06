#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.ModMenuAccessibility;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Mod Packs (UIModPacks) screen.
/// This screen is accessed from the main mod menu and allows managing mod pack collections.
/// </summary>
public sealed class ModPacksAccessibilitySystem : ModMenuAccessibilityBase
{
    #region Base Class Implementation

    protected override int BaseLinkId => LinkIdRegistry.ModPacks;
    protected override string MenuTypeName => "Terraria.ModLoader.UI.UIModPacks";
    protected override string SystemLogName => "ModPacks";

    #endregion

    #region Public Properties

    /// <summary>
    /// Returns true if the Mod Packs menu is currently active and handling gamepad input.
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

            return currentState.GetType().FullName == "Terraria.ModLoader.UI.UIModPacks";
        }
    }

    #endregion

    #region Menu-Specific State

    // Navigation regions
    private enum NavigationRegion
    {
        ModPackList,
        BottomButtons
    }

    private NavigationRegion _currentRegion = NavigationRegion.BottomButtons;

    // Mod pack list navigation
    private readonly List<ModPackItemBindings> _modPackBindings = new();
    private int _currentPackIndex;
    private int _currentButtonIndex;

    // Bottom action buttons
    private readonly List<PointBinding> _bottomButtonBindings = new();

    // Context key for screen announcement
    private const string ContextKeyScreen = "modpacks:screen";

    #endregion

    #region Nested Types

    /// <summary>
    /// Holds all navigable bindings for a single mod pack item.
    /// </summary>
    private sealed class ModPackItemBindings
    {
        public UIElement PackItem { get; init; } = null!;
        public string PackName { get; init; } = string.Empty;
        public int NumMods { get; init; }
        public int NumEnabled { get; init; }
        public int NumDisabled { get; init; }
        public int NumMissing { get; init; }
        public bool IsLegacy { get; init; }
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

        Mod.Logger.Info($"[ModPacks] Load: UIModPacks type found: {ReflectionCache.UIModPacks.Type is not null}");

        if (ReflectionCache.UIModPacks.Type is null)
        {
            Mod.Logger.Warn("[ModPacks] Could not find UIModPacks type");
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

        _modPackBindings.Clear();
        _bottomButtonBindings.Clear();
        ScreenReaderService.ClearContexts("modpacks:");

        base.Unload();
    }

    #endregion

    #region Abstract Method Implementations

    protected override void OnMenuEntered(object menuState)
    {
        _currentRegion = NavigationRegion.BottomButtons;
        _currentPackIndex = 0;
        _currentButtonIndex = 0;
        ScreenReaderService.ClearContexts("modpacks:");
    }

    protected override void OnMenuExited()
    {
        _modPackBindings.Clear();
        _bottomButtonBindings.Clear();
        ScreenReaderService.ClearContexts("modpacks:");
    }

    protected override int GetInitialFocusFrameCount() => 30; // Allow time for async mod pack loading

    protected override void ConfigureGamepadPoints(object menuState)
    {
        BindingById.Clear();
        _modPackBindings.Clear();
        _bottomButtonBindings.Clear();

        int nextId = BaseLinkId;

        // Get the mod pack list
        UIList? modPackList = ReflectionCache.UIModPacks.ModPacks?.GetValue(menuState) as UIList;

        // Build mod pack item bindings
        if (modPackList is not null)
        {
            foreach (UIElement item in modPackList)
            {
                if (item.GetType().Name != "UIModPackItem")
                {
                    continue;
                }

                ModPackItemBindings packBindings = CreateModPackBindings(item, ref nextId);
                if (packBindings.Buttons.Count > 0)
                {
                    _modPackBindings.Add(packBindings);
                }
            }
        }

        // Find bottom action buttons by searching the main container
        UIElement? mainContainer = FindMainContainer(menuState);
        if (mainContainer is not null)
        {
            FindBottomButtons(mainContainer, ref nextId);
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
            case NavigationRegion.ModPackList:
                navigated = HandleModPackListNavigation();
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
                    Mod.Logger.Info($"[ModPacks] Clicking: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(buttonElement, Main.MouseScreen);
                        buttonElement.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModPacks] Click failed: {ex.Message}");
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
                    Mod.Logger.Info("[ModPacks] B button pressed, clicking Back");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(backButton, Main.MouseScreen);
                        backButton.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[ModPacks] Back click failed: {ex.Message}");
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
            case NavigationRegion.ModPackList:
                if (_modPackBindings.Count == 0 ||
                    _currentPackIndex < 0 ||
                    _currentPackIndex >= _modPackBindings.Count)
                {
                    return null;
                }
                var pack = _modPackBindings[_currentPackIndex];
                if (_currentButtonIndex < 0 || _currentButtonIndex >= pack.Buttons.Count)
                {
                    return null;
                }
                return pack.Buttons[_currentButtonIndex].Id;

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
            int packCount = _modPackBindings.Count;
            string header = packCount == 0
                ? "Mod Packs. No mod packs found."
                : $"Mod Packs. {packCount} mod pack{(packCount == 1 ? "" : "s")} available.";
            return $"{header} {label}";
        }

        // For mod pack buttons, include pack context and button position
        if (_currentRegion == NavigationRegion.ModPackList &&
            _currentPackIndex >= 0 &&
            _currentPackIndex < _modPackBindings.Count)
        {
            var pack = _modPackBindings[_currentPackIndex];
            int packIndex = _currentPackIndex + 1;
            int packTotal = _modPackBindings.Count;
            int buttonIndex = _currentButtonIndex + 1;
            int buttonTotal = pack.Buttons.Count;

            // If this is the first button, announce full pack info
            if (_currentButtonIndex == 0)
            {
                string status = BuildPackStatus(pack);
                return $"{pack.PackName}. {status}. {label}, button {buttonIndex} of {buttonTotal}, pack {packIndex} of {packTotal}";
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

    private bool HandleModPackListNavigation()
    {
        if (_modPackBindings.Count == 0)
        {
            return false;
        }

        var currentPack = _modPackBindings[_currentPackIndex];
        bool navigated = false;

        if (CurrentInput.Left && _currentButtonIndex > 0)
        {
            _currentButtonIndex--;
            navigated = true;
        }
        else if (CurrentInput.Right && _currentButtonIndex < currentPack.Buttons.Count - 1)
        {
            _currentButtonIndex++;
            navigated = true;
        }
        else if (CurrentInput.Up)
        {
            if (_currentPackIndex > 0)
            {
                _currentPackIndex--;
                _currentButtonIndex = Math.Min(_currentButtonIndex, _modPackBindings[_currentPackIndex].Buttons.Count - 1);
                navigated = true;
            }
        }
        else if (CurrentInput.Down)
        {
            if (_currentPackIndex < _modPackBindings.Count - 1)
            {
                _currentPackIndex++;
                _currentButtonIndex = Math.Min(_currentButtonIndex, _modPackBindings[_currentPackIndex].Buttons.Count - 1);
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
        else if (CurrentInput.Up && _modPackBindings.Count > 0)
        {
            // Move to mod pack list
            _currentRegion = NavigationRegion.ModPackList;
            _currentPackIndex = _modPackBindings.Count - 1;
            _currentButtonIndex = 0;
            navigated = true;
        }

        return navigated;
    }

    #endregion

    #region Binding Creation

    private ModPackItemBindings CreateModPackBindings(UIElement packItem, ref int nextId)
    {
        var bindings = new ModPackItemBindings
        {
            PackItem = packItem,
            PackName = ReflectionCache.UIModPackItem.Filename?.GetValue(packItem) as string ?? "Unknown",
            NumMods = (int)(ReflectionCache.UIModPackItem.NumMods?.GetValue(packItem) ?? 0),
            NumEnabled = (int)(ReflectionCache.UIModPackItem.NumModsEnabled?.GetValue(packItem) ?? 0),
            NumDisabled = (int)(ReflectionCache.UIModPackItem.NumModsDisabled?.GetValue(packItem) ?? 0),
            NumMissing = GetMissingCount(packItem),
            IsLegacy = (bool)(ReflectionCache.UIModPackItem.Legacy?.GetValue(packItem) ?? false)
        };

        // Get all the button fields and create bindings for visible ones
        AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.EnableListOnlyButton,
            Language.GetTextValue("tModLoader.ModPackEnableOnlyThisList"), PointType.ActionButton, ref nextId);

        AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.EnableListButton,
            Language.GetTextValue("tModLoader.ModPackEnableThisList"), PointType.ActionButton, ref nextId);

        // View List button - need to find it by iterating children since it's a local variable
        AddViewListButton(bindings, packItem, ref nextId);

        AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.ViewInModBrowserButton,
            Language.GetTextValue("tModLoader.ModPackViewModsInModBrowser"), PointType.ActionButton, ref nextId);

        AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.UpdateListWithEnabledButton,
            Language.GetTextValue("tModLoader.ModPackUpdateListWithEnabled"), PointType.ActionButton, ref nextId);

        // Delete button (or fake delete if active pack)
        var deleteButton = ReflectionCache.UIModPackItem.DeleteButton?.GetValue(packItem) as UIElement;
        var fakeDeleteButton = ReflectionCache.UIModPackItem.FakeDeleteButton?.GetValue(packItem) as UIElement;

        if (deleteButton is not null && packItem.HasChild(deleteButton))
        {
            AddButtonBinding(bindings, deleteButton,
                Language.GetTextValue("tModLoader.ModPackDelete"), PointType.ActionButton, ref nextId);
        }
        else if (fakeDeleteButton is not null && packItem.HasChild(fakeDeleteButton))
        {
            AddButtonBinding(bindings, fakeDeleteButton,
                Language.GetTextValue("tModLoader.ModPackDisableToDelete"), PointType.DisabledButton, ref nextId);
        }

        // Modern pack buttons (non-legacy)
        if (!bindings.IsLegacy)
        {
            AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.ImportFromPackLocalButton,
                Language.GetTextValue("tModLoader.InstallPackLocal"), PointType.ActionButton, ref nextId);

            AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.RemovePackLocalButton,
                Language.GetTextValue("tModLoader.RemovePackLocal"), PointType.ActionButton, ref nextId);

            AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.ExportPackInstanceButton,
                Language.GetTextValue("tModLoader.ExportPackInstance"), PointType.ActionButton, ref nextId);

            AddButtonIfVisible(bindings, packItem, ReflectionCache.UIModPackItem.RemovePackInstanceButton,
                Language.GetTextValue("tModLoader.DeletePackInstance"), PointType.ActionButton, ref nextId);
        }

        return bindings;
    }

    private void AddButtonIfVisible(ModPackItemBindings bindings, UIElement packItem, FieldInfo? field,
        string label, PointType type, ref int nextId)
    {
        if (field is null) return;

        var button = field.GetValue(packItem) as UIElement;
        if (button is not null && packItem.HasChild(button))
        {
            AddButtonBinding(bindings, button, label, type, ref nextId);
        }
    }

    private void AddButtonBinding(ModPackItemBindings bindings, UIElement button, string label, PointType type, ref int nextId)
    {
        CalculatedStyle dims = button.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        var binding = new PointBinding(nextId++, center, label, string.Empty, button, type);
        bindings.Buttons.Add(binding);
        BindingById[binding.Id] = binding;
    }

    private void AddViewListButton(ModPackItemBindings bindings, UIElement packItem, ref int nextId)
    {
        // The View List button is created as a local variable in the constructor
        // We need to find it by searching children - prioritize text matching over position
        try
        {
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(packItem) is not List<UIElement> elements)
            {
                return;
            }

            string viewListText = Language.GetTextValue("tModLoader.ModPackViewList");

            // First pass: try to find by text content (most reliable)
            foreach (var element in elements)
            {
                if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                string? text = textProperty?.GetValue(element) as string;

                if (text == viewListText)
                {
                    AddButtonBinding(bindings, element, viewListText, PointType.ActionButton, ref nextId);
                    return;
                }
            }

            // Fallback: find by position (less reliable but sometimes needed)
            // Use wider tolerance (±15f instead of ±5f) for better resilience
            foreach (var element in elements)
            {
                if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                // Check position: Top = 40f, Left ≈ 407f (View List button)
                if (Math.Abs(element.Top.Pixels - 40f) < 15f && Math.Abs(element.Left.Pixels - 407f) < 30f)
                {
                    Mod.Logger.Debug($"[ModPacks] Found View List button by position fallback at ({element.Left.Pixels}, {element.Top.Pixels})");
                    AddButtonBinding(bindings, element, viewListText, PointType.ActionButton, ref nextId);
                    return;
                }
            }

            Mod.Logger.Debug("[ModPacks] View List button not found for pack item");
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ModPacks] Error finding View List button: {ex.Message}");
        }
    }

    private int GetMissingCount(UIElement packItem)
    {
        try
        {
            var missing = ReflectionCache.UIModPackItem.Missing?.GetValue(packItem) as System.Collections.IList;
            return missing?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[ModPacks] Error getting missing count: {ex.Message}");
            return 0;
        }
    }

    private UIElement? FindMainContainer(object menuState)
    {
        // The main container is appended directly to the UIState
        try
        {
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(menuState) is List<UIElement> elements && elements.Count > 0)
            {
                return elements[0];
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[ModPacks] Error finding main container: {ex.Message}");
        }
        return null;
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
            string openFolderText = Language.GetTextValue("tModLoader.OpenModPackFolder");
            string saveNewText = Language.GetTextValue("tModLoader.ModPacksSaveEnabledAsNewPack");

            // Track found buttons to avoid duplicates from fallback detection
            var foundButtons = new HashSet<UIElement>();

            // First pass: find buttons by text content (most reliable)
            foreach (var element in elements)
            {
                if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                {
                    continue;
                }

                // Check VAlign for bottom buttons (with tolerance)
                if (element.VAlign < 0.85f)
                {
                    continue;
                }

                var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                string? text = textProperty?.GetValue(element) as string;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                PointType buttonType = PointType.ActionButton;
                string label;

                if (text == backText)
                {
                    buttonType = PointType.BackButton;
                    label = backText;
                }
                else if (text == openFolderText)
                {
                    label = openFolderText;
                }
                else if (text == saveNewText)
                {
                    label = saveNewText;
                }
                else
                {
                    // Unknown text - log and use as-is
                    Mod.Logger.Debug($"[ModPacks] Found bottom button with unknown text: {text}");
                    label = text;
                }

                foundButtons.Add(element);
                AddBottomButtonBinding(element, label, buttonType, ref nextId);
            }

            // Fallback: find by position if text matching missed any
            if (foundButtons.Count == 0)
            {
                Mod.Logger.Debug("[ModPacks] Using position fallback for bottom buttons");
                foreach (var element in elements)
                {
                    if (!element.GetType().Name.Contains("UIAutoScaleTextTextPanel"))
                    {
                        continue;
                    }

                    if (element.VAlign < 0.85f)
                    {
                        continue;
                    }

                    if (foundButtons.Contains(element))
                    {
                        continue;
                    }

                    var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                    string? text = textProperty?.GetValue(element) as string;
                    string label = text ?? "Button";
                    PointType buttonType = element.HAlign < 0.3f ? PointType.BackButton : PointType.ActionButton;

                    AddBottomButtonBinding(element, label, buttonType, ref nextId);
                }
            }

            // Sort by X position
            _bottomButtonBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));

            if (_bottomButtonBindings.Count == 0)
            {
                Mod.Logger.Debug("[ModPacks] No bottom buttons found");
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ModPacks] Error finding bottom buttons: {ex.Message}");
        }
    }

    private void AddBottomButtonBinding(UIElement element, string label, PointType type, ref int nextId)
    {
        CalculatedStyle dims = element.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        var binding = new PointBinding(nextId++, center, label, string.Empty, element, type);
        _bottomButtonBindings.Add(binding);
        BindingById[binding.Id] = binding;
    }

    private static string BuildPackStatus(ModPackItemBindings pack)
    {
        var parts = new List<string>();
        parts.Add($"{pack.NumMods} mod{(pack.NumMods == 1 ? "" : "s")}");

        if (pack.NumEnabled > 0)
        {
            parts.Add($"{pack.NumEnabled} enabled");
        }
        if (pack.NumDisabled > 0)
        {
            parts.Add($"{pack.NumDisabled} disabled");
        }
        if (pack.NumMissing > 0)
        {
            parts.Add($"{pack.NumMissing} missing");
        }

        return string.Join(", ", parts);
    }

    #endregion
}
