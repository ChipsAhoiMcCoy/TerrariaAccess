#nullable enable
using System;
using System.Collections.Generic;
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

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides gamepad navigation and screen reader announcements for the Mod Info (UIModInfo) screen.
/// This screen is shown when clicking "More Info" on a mod in the Manage Mods menu.
/// Uses reflection since UIModInfo is an internal type.
/// </summary>
public sealed class ModInfoAccessibilitySystem : ModSystem
{
    private const int BaseLinkId = 3300;
    private const int MenuModeModInfo = 10008;

    private static int _lastAnnouncedPointId = -1;
    private static int _lastSeenPointId = -1;
    private static object? _lastModInfo;
    private static int _initialFocusFramesRemaining;

    // Navigation state tracking
    private static int _currentFocusIndex;
    private static bool _leftWasPressed;
    private static bool _rightWasPressed;
    private static bool _upWasPressed;
    private static bool _downWasPressed;
    private static bool _aButtonWasPressed;
    private static bool _bButtonWasPressed;

    // Analog stick state for debouncing
    private static bool _stickLeftWasPressed;
    private static bool _stickRightWasPressed;
    private static bool _stickUpWasPressed;
    private static bool _stickDownWasPressed;

    // Cached bindings for navigation
    private static readonly List<PointBinding> TopRowBindings = new();
    private static readonly List<PointBinding> BottomRowBindings = new();
    private static readonly Dictionary<int, PointBinding> BindingById = new();

    // Whether we're in top row (true) or bottom row (false)
    private static bool _inTopRow;

    // Type references
    private static Type? _uiModInfoType;

    // UIModInfo field references
    private static FieldInfo? _modInfoField; // UIMessageBox with description
    private static FieldInfo? _uITextPanelField; // Header panel
    private static FieldInfo? _modHomepageButtonField;
    private static FieldInfo? _modSteamButtonField;
    private static FieldInfo? _extractLocalizationButtonField;
    private static FieldInfo? _fakeExtractLocalizationButtonField;
    private static FieldInfo? _extractButtonField;
    private static FieldInfo? _deleteButtonField;
    private static FieldInfo? _fakeDeleteButtonField;
    private static FieldInfo? _uIElementField; // Main container
    private static FieldInfo? _modDisplayNameField;
    private static FieldInfo? _infoField; // Mod description text

    // Track if description was announced
    private static bool _descriptionAnnounced;

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

            // Check the UI state directly, not relying on _lastModInfo being set
            // This prevents MenuNarration from announcing before we're ready
            object? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null)
            {
                return false;
            }

            // Check by type name since we might not have the type reference yet
            string? typeName = currentState.GetType().FullName;
            if (typeName == "Terraria.ModLoader.UI.UIModInfo")
            {
                return true;
            }

            return false;
        }
    }

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        // Get type reference via reflection
        _uiModInfoType = Type.GetType("Terraria.ModLoader.UI.UIModInfo, tModLoader");

        Mod.Logger.Info($"[ModInfo] Load: UIModInfo type found: {_uiModInfoType is not null}");

        if (_uiModInfoType is null)
        {
            Mod.Logger.Warn("[ModInfo] Could not find UIModInfo type");
            return;
        }

        // Hook into DrawMenu to process during menu rendering
        On_Main.DrawMenu += HandleDrawMenu;

        // Get UIModInfo fields
        _modInfoField = _uiModInfoType.GetField("_modInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        _uITextPanelField = _uiModInfoType.GetField("_uITextPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        _modHomepageButtonField = _uiModInfoType.GetField("_modHomepageButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _modSteamButtonField = _uiModInfoType.GetField("_modSteamButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _extractLocalizationButtonField = _uiModInfoType.GetField("extractLocalizationButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _fakeExtractLocalizationButtonField = _uiModInfoType.GetField("fakeExtractLocalizationButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _extractButtonField = _uiModInfoType.GetField("_extractButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _deleteButtonField = _uiModInfoType.GetField("_deleteButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _fakeDeleteButtonField = _uiModInfoType.GetField("_fakeDeleteButton", BindingFlags.NonPublic | BindingFlags.Instance);
        _uIElementField = _uiModInfoType.GetField("_uIElement", BindingFlags.NonPublic | BindingFlags.Instance);
        _modDisplayNameField = _uiModInfoType.GetField("_modDisplayName", BindingFlags.NonPublic | BindingFlags.Instance);
        _infoField = _uiModInfoType.GetField("_info", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        if (_uiModInfoType is not null)
        {
            On_Main.DrawMenu -= HandleDrawMenu;
        }

        BindingById.Clear();
        TopRowBindings.Clear();
        BottomRowBindings.Clear();
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _lastModInfo = null;
        _initialFocusFramesRemaining = 0;
        _currentFocusIndex = 0;
        _inTopRow = false;
        _descriptionAnnounced = false;
    }

    private void HandleDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        TryProcessModInfoMenu();
    }

    private void TryProcessModInfoMenu()
    {
        if (_uiModInfoType is null)
        {
            return;
        }

        object? currentState = Main.MenuUI?.CurrentState;

        // Check if we're in the UIModInfo state
        bool isUIModInfo = currentState is not null &&
                           currentState.GetType().FullName == "Terraria.ModLoader.UI.UIModInfo";

        if (!isUIModInfo)
        {
            // Clear menu reference if we've left UIModInfo
            if (_lastModInfo is not null)
            {
                Mod.Logger.Info("[ModInfo] Left Mod Info menu");
                _lastModInfo = null;
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
                TopRowBindings.Clear();
                BottomRowBindings.Clear();
                BindingById.Clear();
                _descriptionAnnounced = false;
            }
            return;
        }

        // Track menu state
        bool menuChanged = !ReferenceEquals(currentState, _lastModInfo);
        if (menuChanged)
        {
            _lastModInfo = currentState;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            _initialFocusFramesRemaining = 10;
            _currentFocusIndex = 0;
            _inTopRow = false; // Start on bottom row (Back button)
            _descriptionAnnounced = false;
            _aButtonWasPressed = true; // Prevent A button from immediately triggering on menu entry
            _bButtonWasPressed = true; // Prevent B button from immediately triggering on menu entry
            Mod.Logger.Info("[ModInfo] Entered Mod Info menu");
        }

        // Process gamepad navigation
        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        if (!hasGamepadInput)
        {
            return;
        }

        try
        {
            ConfigureGamepadPoints(currentState!);
            HandleManualNavigation(currentState!);
            HandleActionButton(currentState!);
            AnnounceCurrentFocus(currentState!);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[ModInfo] Failed to configure gamepad points: {ex}");
        }
    }

    private static void ConfigureGamepadPoints(object modInfo)
    {
        BindingById.Clear();
        TopRowBindings.Clear();
        BottomRowBindings.Clear();

        int nextId = BaseLinkId;

        // Get the main container element to check which buttons are attached
        UIElement? mainContainer = _uIElementField?.GetValue(modInfo) as UIElement;

        // Get button references
        UIElement? homepageButton = _modHomepageButtonField?.GetValue(modInfo) as UIElement;
        UIElement? steamButton = _modSteamButtonField?.GetValue(modInfo) as UIElement;
        UIElement? extractLocButton = _extractLocalizationButtonField?.GetValue(modInfo) as UIElement;
        UIElement? fakeExtractLocButton = _fakeExtractLocalizationButtonField?.GetValue(modInfo) as UIElement;
        UIElement? extractButton = _extractButtonField?.GetValue(modInfo) as UIElement;
        UIElement? deleteButton = _deleteButtonField?.GetValue(modInfo) as UIElement;
        UIElement? fakeDeleteButton = _fakeDeleteButtonField?.GetValue(modInfo) as UIElement;

        // Top row buttons (at Top = -65f): Homepage (center), Steam (left), Extract Localization (right)
        // These are conditionally shown based on mod properties

        // Check if homepage button is visible (has a parent = is attached to UI)
        if (homepageButton is not null && IsElementVisible(mainContainer, homepageButton))
        {
            var binding = CreateButtonBinding(ref nextId, homepageButton,
                Language.GetTextValue("tModLoader.ModInfoVisitHomepage"), PointType.ActionButton);
            TopRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Steam button
        if (steamButton is not null && IsElementVisible(mainContainer, steamButton))
        {
            var binding = CreateButtonBinding(ref nextId, steamButton,
                Language.GetTextValue("tModLoader.ModInfoVisitSteampage"), PointType.ActionButton);
            TopRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Extract Localization button (either active or fake/disabled version)
        if (extractLocButton is not null && IsElementVisible(mainContainer, extractLocButton))
        {
            var binding = CreateButtonBinding(ref nextId, extractLocButton,
                Language.GetTextValue("tModLoader.ModInfoExtractLocalization"), PointType.ActionButton);
            TopRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }
        else if (fakeExtractLocButton is not null && IsElementVisible(mainContainer, fakeExtractLocButton))
        {
            var binding = CreateButtonBinding(ref nextId, fakeExtractLocButton,
                Language.GetTextValue("tModLoader.ModInfoExtractLocalization") + " (disabled)", PointType.DisabledButton);
            TopRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Bottom row buttons (at Top = -20f): Back (left), Extract (center), Delete (right)
        // Back button is a local variable in OnInitialize, we need to find it by searching children
        UIElement? backButton = FindBackButton(mainContainer);
        if (backButton is not null)
        {
            var binding = CreateButtonBinding(ref nextId, backButton,
                Language.GetTextValue("UI.Back"), PointType.BackButton);
            BottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Extract button
        if (extractButton is not null && IsElementVisible(mainContainer, extractButton))
        {
            var binding = CreateButtonBinding(ref nextId, extractButton,
                Language.GetTextValue("tModLoader.ModInfoExtract"), PointType.ActionButton);
            BottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Delete button (either active or fake/disabled version)
        if (deleteButton is not null && IsElementVisible(mainContainer, deleteButton))
        {
            var binding = CreateButtonBinding(ref nextId, deleteButton,
                Language.GetTextValue("UI.Delete"), PointType.ActionButton);
            BottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }
        else if (fakeDeleteButton is not null && IsElementVisible(mainContainer, fakeDeleteButton))
        {
            var binding = CreateButtonBinding(ref nextId, fakeDeleteButton,
                Language.GetTextValue("UI.Delete") + " (disabled)", PointType.DisabledButton);
            BottomRowBindings.Add(binding);
            BindingById[binding.Id] = binding;
        }

        // Sort bindings by X position for proper left/right navigation
        TopRowBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
        BottomRowBindings.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));

        if (BindingById.Count == 0)
        {
            return;
        }

        // Create all link points
        var allBindings = new List<PointBinding>();
        allBindings.AddRange(TopRowBindings);
        allBindings.AddRange(BottomRowBindings);

        foreach (PointBinding binding in allBindings)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
            linkPoint.Unlink();
        }

        // Connect top row horizontally
        for (int i = 0; i < TopRowBindings.Count - 1; i++)
        {
            ConnectHorizontal(TopRowBindings[i], TopRowBindings[i + 1]);
        }

        // Connect bottom row horizontally
        for (int i = 0; i < BottomRowBindings.Count - 1; i++)
        {
            ConnectHorizontal(BottomRowBindings[i], BottomRowBindings[i + 1]);
        }

        // Connect rows vertically
        if (TopRowBindings.Count > 0 && BottomRowBindings.Count > 0)
        {
            // Connect each top row button to nearest bottom row button
            foreach (var topBinding in TopRowBindings)
            {
                var nearest = FindNearestHorizontal(topBinding, BottomRowBindings);
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
        if (PlayerInput.UsingGamepadUI && _initialFocusFramesRemaining > 0)
        {
            int defaultPointId = BottomRowBindings.Count > 0 ? BottomRowBindings[0].Id :
                                 TopRowBindings.Count > 0 ? TopRowBindings[0].Id :
                                 BaseLinkId;

            UILinkPointNavigator.ChangePoint(defaultPointId);
            _initialFocusFramesRemaining--;
        }
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

        // The Back button is at VAlign = 1f, Top = -20f, HAlign is not set (defaults to 0)
        // It's a UIAutoScaleTextTextPanel<string> with "Back" text
        // We need to iterate through children to find it

        try
        {
            // Try Elements field
            var elementsField = typeof(UIElement).GetField("Elements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementsField?.GetValue(container) is List<UIElement> elements)
            {
                string backText = Language.GetTextValue("UI.Back");

                foreach (var element in elements)
                {
                    // Check if it's a UIAutoScaleTextTextPanel - look at the type name
                    string typeName = element.GetType().Name;
                    if (!typeName.Contains("UIAutoScaleTextTextPanel"))
                    {
                        continue;
                    }

                    // Check position: VAlign = 1f, HAlign < 0.1f (left side), Top.Pixels = -20f
                    if (element.VAlign < 0.9f)
                    {
                        continue;
                    }

                    // Left-aligned buttons have HAlign of 0
                    if (element.HAlign > 0.1f)
                    {
                        continue;
                    }

                    // Check Top position (should be around -20f for bottom row)
                    if (element.Top.Pixels < -30f || element.Top.Pixels > -10f)
                    {
                        continue;
                    }

                    // This is likely the Back button - verify by checking for the text
                    // Try to get the Text property
                    var textProperty = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                    if (textProperty is not null)
                    {
                        string? text = textProperty.GetValue(element) as string;
                        if (text == backText)
                        {
                            return element;
                        }
                    }

                    // If we can't verify by text, use position as fallback
                    return element;
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[ModInfo] Error finding Back button: {ex.Message}");
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

    private static PointBinding CreateButtonBinding(ref int nextId, UIElement element, string label, PointType type)
    {
        CalculatedStyle dims = element.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        return new PointBinding(nextId++, center, label, element, type);
    }

    private static void HandleManualNavigation(object modInfo)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        // Detect new presses for D-pad
        bool leftPressed = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
        bool rightPressed = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
        bool upPressed = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
        bool downPressed = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

        // Update D-pad previous state
        _leftWasPressed = gpState.DPad.Left == ButtonState.Pressed;
        _rightWasPressed = gpState.DPad.Right == ButtonState.Pressed;
        _upWasPressed = gpState.DPad.Up == ButtonState.Pressed;
        _downWasPressed = gpState.DPad.Down == ButtonState.Pressed;

        // Check analog stick with deadzone and debouncing
        Vector2 stick = gpState.ThumbSticks.Left;
        const float threshold = 0.5f;

        bool stickLeftNow = stick.X < -threshold;
        bool stickRightNow = stick.X > threshold;
        bool stickUpNow = stick.Y > threshold;
        bool stickDownNow = stick.Y < -threshold;

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

        _stickLeftWasPressed = stickLeftNow;
        _stickRightWasPressed = stickRightNow;
        _stickUpWasPressed = stickUpNow;
        _stickDownWasPressed = stickDownNow;

        if (!leftPressed && !rightPressed && !upPressed && !downPressed)
        {
            return;
        }

        var currentList = _inTopRow ? TopRowBindings : BottomRowBindings;

        bool navigated = false;

        if (leftPressed && _currentFocusIndex > 0)
        {
            _currentFocusIndex--;
            navigated = true;
        }
        else if (rightPressed && _currentFocusIndex < currentList.Count - 1)
        {
            _currentFocusIndex++;
            navigated = true;
        }
        else if (upPressed && !_inTopRow && TopRowBindings.Count > 0)
        {
            _inTopRow = true;
            _currentFocusIndex = Math.Min(_currentFocusIndex, TopRowBindings.Count - 1);
            navigated = true;
        }
        else if (downPressed && _inTopRow && BottomRowBindings.Count > 0)
        {
            _inTopRow = false;
            _currentFocusIndex = Math.Min(_currentFocusIndex, BottomRowBindings.Count - 1);
            navigated = true;
        }

        if (navigated)
        {
            int? newPointId = GetCurrentPointId();
            if (newPointId.HasValue)
            {
                UILinkPointNavigator.ChangePoint(newPointId.Value);
                ScreenReaderMod.Instance?.Logger.Info($"[ModInfo] Navigated to row {(_inTopRow ? "top" : "bottom")}, index {_currentFocusIndex}, point {newPointId.Value}");
            }
        }
    }

    private static void HandleActionButton(object modInfo)
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);

        if (!gpState.IsConnected)
        {
            return;
        }

        bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
        bool aJustPressed = aPressed && !_aButtonWasPressed;
        _aButtonWasPressed = aPressed;

        bool bPressed = gpState.Buttons.B == ButtonState.Pressed;
        bool bJustPressed = bPressed && !_bButtonWasPressed;
        _bButtonWasPressed = bPressed;

        if (aJustPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && BindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                // Don't click disabled buttons
                if (binding.Type == PointType.DisabledButton)
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[ModInfo] Button is disabled: {binding.Label}");
                    ScreenReaderService.Announce("Button disabled", force: true);
                    return;
                }

                if (binding.Element is UIElement buttonElement)
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[ModInfo] Clicking button: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(buttonElement, Main.MouseScreen);
                        buttonElement.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        ScreenReaderMod.Instance?.Logger.Warn($"[ModInfo] Button click failed: {ex.Message}");
                    }
                }
            }
        }

        // B button goes back
        if (bJustPressed)
        {
            // Find and click the Back button
            foreach (var binding in BottomRowBindings)
            {
                if (binding.Type == PointType.BackButton && binding.Element is UIElement backButton)
                {
                    ScreenReaderMod.Instance?.Logger.Info("[ModInfo] B button pressed, clicking Back");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(backButton, Main.MouseScreen);
                        backButton.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        ScreenReaderMod.Instance?.Logger.Warn($"[ModInfo] Back button click failed: {ex.Message}");
                    }
                    return;
                }
            }
        }
    }

    private static int? GetCurrentPointId()
    {
        var currentList = _inTopRow ? TopRowBindings : BottomRowBindings;
        if (currentList.Count == 0 || _currentFocusIndex < 0 || _currentFocusIndex >= currentList.Count)
        {
            return null;
        }

        return currentList[_currentFocusIndex].Id;
    }

    private static void AnnounceCurrentFocus(object modInfo)
    {
        int? expectedPoint = GetCurrentPointId();
        int currentPoint = expectedPoint ?? UILinkPointNavigator.CurrentPoint;

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

        if (!BindingById.TryGetValue(currentPoint, out PointBinding binding))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[ModInfo] No binding found for point {currentPoint}");
            return;
        }

        string announcement = BuildAnnouncement(binding, modInfo);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);

        ScreenReaderMod.Instance?.Logger.Info($"[ModInfo] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private static string BuildAnnouncement(PointBinding binding, object modInfo)
    {
        string label = TextSanitizer.Clean(binding.Label);
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        // On first focus, announce the mod name and description
        if (!_descriptionAnnounced)
        {
            _descriptionAnnounced = true;

            string? modDisplayName = _modDisplayNameField?.GetValue(modInfo) as string;
            string? description = _infoField?.GetValue(modInfo) as string;

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
                return $"{header}. {label}";
            }
        }

        return label;
    }

    private static void ConnectHorizontal(PointBinding left, PointBinding right)
    {
        UILinkPoint leftPoint = EnsureLinkPoint(left.Id);
        UILinkPoint rightPoint = EnsureLinkPoint(right.Id);

        leftPoint.Right = right.Id;
        rightPoint.Left = left.Id;
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

    private enum PointType
    {
        ActionButton,
        BackButton,
        DisabledButton
    }

    private readonly record struct PointBinding(int Id, Vector2 Position, string Label, UIElement? Element, PointType Type);
}
