#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.ModBrowser;
using ScreenReaderMod.Common.Systems.ModMenuAccessibility;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Provides full keyboard/gamepad navigation and screen reader announcements for the Achievements menu.
/// Works from both the title menu and in-game pause menu by hooking UIAchievementsMenu.Draw directly.
/// </summary>
public sealed class AchievementsAccessibilitySystem : ModSystem
{
    #region Constants

    private const int BaseLinkId = LinkIdRegistry.Achievements;
    private const string SystemLogName = "Achievements";
    private const string AchievementsMenuTypeName = "Terraria.GameContent.UI.States.UIAchievementsMenu";
    private const string ContextKeyScreen = "achievements:screen";

    // Category names for announcements
    private static readonly string[] CategoryNames = { "Slayer", "Collector", "Explorer", "Challenger", "Modded" };

    #endregion

    #region Hook Delegates & Fields

    private delegate void DrawDelegate(UIState self, SpriteBatch spriteBatch);
    private delegate void OnActivateDelegate(UIState self);

    private static Hook? _drawHook;
    private static Hook? _onActivateHook;

    private static AchievementsAccessibilitySystem? _instance;

    #endregion

    #region Navigation State

    private enum NavigationRegion
    {
        CategoryButtons,
        AchievementList,
        BottomButtons,
        ConfirmDialog
    }

    private NavigationRegion _currentRegion = NavigationRegion.AchievementList;
    private int _currentAchievementIndex;
    private int _currentCategoryIndex;
    private int _currentBottomIndex;
    private int _dialogFocusIndex; // 0 = Yes, 1 = No

    // Bindings
    private readonly List<PointBinding> _categoryBindings = new();
    private readonly List<AchievementBinding> _achievementBindings = new();
    private readonly List<PointBinding> _bottomBindings = new();
    private PointBinding? _dialogYesBinding;
    private PointBinding? _dialogNoBinding;
    private readonly Dictionary<int, PointBinding> _bindingById = new();

    // Stability tracking
    private int _lastAnnouncedPointId = -1;
    private int _lastSeenPointId = -1;
    private object? _lastMenuState;
    private int _initialFocusFramesRemaining;

    #endregion

    #region Input State

    // D-pad previous state
    private bool _leftWasPressed;
    private bool _rightWasPressed;
    private bool _upWasPressed;
    private bool _downWasPressed;

    // Analog stick previous state
    private bool _stickLeftWasPressed;
    private bool _stickRightWasPressed;
    private bool _stickUpWasPressed;
    private bool _stickDownWasPressed;

    // Button previous state
    private bool _aButtonWasPressed;
    private bool _bButtonWasPressed;

    // Keyboard previous state
    private bool _keyLeftWasPressed;
    private bool _keyRightWasPressed;
    private bool _keyUpWasPressed;
    private bool _keyDownWasPressed;
    private bool _keyEnterWasPressed;
    private bool _keySpaceWasPressed;

    private NavigationInput _currentInput;

    #endregion

    #region Public Properties

    /// <summary>
    /// Returns true if the achievements menu is currently active and this system is handling input.
    /// Used by MenuNarration to suppress hover/focus announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            return IsAchievementsMenuActive();
        }
    }

    private static bool IsAchievementsMenuActive()
    {
        string? menuTypeName = Main.MenuUI?.CurrentState?.GetType().FullName;
        if (menuTypeName == AchievementsMenuTypeName)
        {
            return true;
        }

        string? inGameTypeName = Main.InGameUI?.CurrentState?.GetType().FullName;
        if (inGameTypeName == AchievementsMenuTypeName)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the active UIAchievementsMenu instance, checking both MenuUI and InGameUI.
    /// </summary>
    private static object? GetActiveMenuState()
    {
        object? state = Main.MenuUI?.CurrentState;
        if (state is not null && state.GetType().FullName == AchievementsMenuTypeName)
        {
            return state;
        }

        state = Main.InGameUI?.CurrentState;
        if (state is not null && state.GetType().FullName == AchievementsMenuTypeName)
        {
            return state;
        }

        return null;
    }

    #endregion

    #region Lifecycle

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        _instance = this;

        Type? achievementsMenuType = ReflectionCache.UIAchievementsMenu.Type;
        if (achievementsMenuType is null)
        {
            Mod.Logger.Warn($"[{SystemLogName}] UIAchievementsMenu type not found, system disabled");
            return;
        }

        Mod.Logger.Info($"[{SystemLogName}] Loading accessibility system");

        MethodInfo? drawMethod = achievementsMenuType.GetMethod(
            "Draw",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(SpriteBatch) },
            null);
        if (drawMethod is not null)
        {
            _drawHook = new Hook(drawMethod, OnDraw);
        }

        MethodInfo? onActivateMethod = achievementsMenuType.GetMethod(
            "OnActivate",
            BindingFlags.Instance | BindingFlags.Public);
        if (onActivateMethod is not null)
        {
            _onActivateHook = new Hook(onActivateMethod, OnActivate);
        }
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        _drawHook?.Dispose();
        _drawHook = null;
        _onActivateHook?.Dispose();
        _onActivateHook = null;

        _categoryBindings.Clear();
        _achievementBindings.Clear();
        _bottomBindings.Clear();
        _bindingById.Clear();
        _dialogYesBinding = null;
        _dialogNoBinding = null;
        _lastMenuState = null;
        _lastAnnouncedPointId = -1;
        _lastSeenPointId = -1;
        _initialFocusFramesRemaining = 0;

        ScreenReaderService.ClearContexts("achievements:");

        ResetInputState();
        _instance = null;
    }

    #endregion

    #region Hooks

    private static void OnActivate(OnActivateDelegate orig, UIState self)
    {
        orig(self);

        if (_instance is null)
        {
            return;
        }

        _instance._lastMenuState = null; // Force re-detection in OnDraw
        _instance._lastAnnouncedPointId = -1;
        _instance._lastSeenPointId = -1;
        _instance._initialFocusFramesRemaining = 15;
        _instance._currentRegion = NavigationRegion.AchievementList;
        _instance._currentAchievementIndex = 0;
        _instance._currentCategoryIndex = 0;
        _instance._currentBottomIndex = 0;
        _instance._dialogFocusIndex = 0;

        // Prevent immediate button triggers
        _instance._aButtonWasPressed = true;
        _instance._bButtonWasPressed = true;

        ScreenReaderService.ClearContexts("achievements:");

        _instance.Mod.Logger.Info($"[{SystemLogName}] Menu activated");
    }

    private static void OnDraw(DrawDelegate orig, UIState self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (_instance is null)
        {
            return;
        }

        _instance.ProcessMenu(self);
    }

    #endregion

    #region Menu Processing

    private void ProcessMenu(object menuState)
    {
        bool menuChanged = !ReferenceEquals(menuState, _lastMenuState);
        if (menuChanged)
        {
            _lastMenuState = menuState;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;
            _initialFocusFramesRemaining = 15;
            _currentAchievementIndex = 0;
            _currentCategoryIndex = 0;
            _currentBottomIndex = 0;
            _dialogFocusIndex = 0;
            _currentRegion = NavigationRegion.AchievementList;

            _aButtonWasPressed = true;
            _bButtonWasPressed = true;

            ScreenReaderService.ClearContexts("achievements:");
            Mod.Logger.Info($"[{SystemLogName}] Entered menu");
        }

        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        if (!hasGamepadInput && !ShouldProcessKeyboardInput())
        {
            return;
        }

        try
        {
            // Update search mode state, tracking transitions
            bool wasSearchActive = SearchModeManager.IsSearchModeActive;
            SearchModeManager.Update();
            bool isSearchActive = SearchModeManager.IsSearchModeActive;

            // When search mode just activated, reset announcement tracking
            // so the prefix queued by SearchModeManager gets consumed
            if (isSearchActive && !wasSearchActive)
            {
                _lastAnnouncedPointId = -1;
                _lastSeenPointId = -1;
            }

            UpdateInputState();
            ConfigureGamepadPoints(menuState);

            // Check for confirmation dialog
            if (IsConfirmDialogActive(menuState))
            {
                if (_currentRegion != NavigationRegion.ConfirmDialog)
                {
                    _currentRegion = NavigationRegion.ConfirmDialog;
                    _dialogFocusIndex = 1; // Default to No
                    _lastAnnouncedPointId = -1;
                    _lastSeenPointId = -1;

                    string dialogPrompt = "Reset all achievements? This cannot be undone.";
                    ScreenReaderService.EnqueuePrefix(dialogPrompt);
                    Mod.Logger.Info($"[{SystemLogName}] Confirmation dialog opened");
                }

                HandleDialogNavigation();
                HandleDialogAction(menuState);
            }
            else
            {
                if (_currentRegion == NavigationRegion.ConfirmDialog)
                {
                    _currentRegion = NavigationRegion.BottomButtons;
                    _lastAnnouncedPointId = -1;
                    _lastSeenPointId = -1;
                    ScreenReaderService.ClearAllPrefixes();
                    Mod.Logger.Info($"[{SystemLogName}] Confirmation dialog closed");
                }

                HandleNavigation(menuState);
                HandleAction(menuState);
            }

            AnnounceCurrentFocus(menuState);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error processing menu: {ex}");
        }
    }

    private bool ShouldProcessKeyboardInput()
    {
        return !SearchModeManager.IsSearchModeActive;
    }

    #endregion

    #region Input Handling

    private void UpdateInputState()
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        KeyboardState kbState = Main.keyState;

        var input = new NavigationInput();

        if (gpState.IsConnected)
        {
            input.Left = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
            input.Right = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
            input.Up = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
            input.Down = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

            _leftWasPressed = gpState.DPad.Left == ButtonState.Pressed;
            _rightWasPressed = gpState.DPad.Right == ButtonState.Pressed;
            _upWasPressed = gpState.DPad.Up == ButtonState.Pressed;
            _downWasPressed = gpState.DPad.Down == ButtonState.Pressed;

            Vector2 stick = gpState.ThumbSticks.Left;
            const float threshold = 0.5f;

            bool stickLeftNow = stick.X < -threshold;
            bool stickRightNow = stick.X > threshold;
            bool stickUpNow = stick.Y > threshold;
            bool stickDownNow = stick.Y < -threshold;

            if (!input.Left && stickLeftNow && !_stickLeftWasPressed) input.Left = true;
            if (!input.Right && stickRightNow && !_stickRightWasPressed) input.Right = true;
            if (!input.Up && stickUpNow && !_stickUpWasPressed) input.Up = true;
            if (!input.Down && stickDownNow && !_stickDownWasPressed) input.Down = true;

            _stickLeftWasPressed = stickLeftNow;
            _stickRightWasPressed = stickRightNow;
            _stickUpWasPressed = stickUpNow;
            _stickDownWasPressed = stickDownNow;

            bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
            input.ActionPressed = aPressed && !_aButtonWasPressed;
            _aButtonWasPressed = aPressed;

            bool bPressed = gpState.Buttons.B == ButtonState.Pressed;
            input.BackPressed = bPressed && !_bButtonWasPressed;
            _bButtonWasPressed = bPressed;
        }

        if (ShouldProcessKeyboardInput())
        {
            bool keyLeftNow = kbState.IsKeyDown(Keys.Left) || kbState.IsKeyDown(Keys.A);
            bool keyRightNow = kbState.IsKeyDown(Keys.Right) || kbState.IsKeyDown(Keys.D);
            bool keyUpNow = kbState.IsKeyDown(Keys.Up) || kbState.IsKeyDown(Keys.W);
            bool keyDownNow = kbState.IsKeyDown(Keys.Down) || kbState.IsKeyDown(Keys.S);

            if (!input.Left && keyLeftNow && !_keyLeftWasPressed) input.Left = true;
            if (!input.Right && keyRightNow && !_keyRightWasPressed) input.Right = true;
            if (!input.Up && keyUpNow && !_keyUpWasPressed) input.Up = true;
            if (!input.Down && keyDownNow && !_keyDownWasPressed) input.Down = true;

            _keyLeftWasPressed = keyLeftNow;
            _keyRightWasPressed = keyRightNow;
            _keyUpWasPressed = keyUpNow;
            _keyDownWasPressed = keyDownNow;

            bool enterNow = kbState.IsKeyDown(Keys.Enter);
            bool spaceNow = kbState.IsKeyDown(Keys.Space);

            if (!input.ActionPressed)
            {
                if ((enterNow && !_keyEnterWasPressed) || (spaceNow && !_keySpaceWasPressed))
                {
                    input.ActionPressed = true;
                }
            }

            _keyEnterWasPressed = enterNow;
            _keySpaceWasPressed = spaceNow;
        }

        input.HasNavigation = input.Left || input.Right || input.Up || input.Down;
        _currentInput = input;
    }

    private void ResetInputState()
    {
        _leftWasPressed = false;
        _rightWasPressed = false;
        _upWasPressed = false;
        _downWasPressed = false;
        _stickLeftWasPressed = false;
        _stickRightWasPressed = false;
        _stickUpWasPressed = false;
        _stickDownWasPressed = false;
        _aButtonWasPressed = false;
        _bButtonWasPressed = false;
        _keyLeftWasPressed = false;
        _keyRightWasPressed = false;
        _keyUpWasPressed = false;
        _keyDownWasPressed = false;
        _keyEnterWasPressed = false;
        _keySpaceWasPressed = false;
    }

    #endregion

    #region Gamepad Point Configuration

    private void ConfigureGamepadPoints(object menuState)
    {
        _bindingById.Clear();
        _categoryBindings.Clear();
        _achievementBindings.Clear();
        _bottomBindings.Clear();
        _dialogYesBinding = null;
        _dialogNoBinding = null;

        int nextId = BaseLinkId;

        // Category filter buttons
        ConfigureCategoryButtons(menuState, ref nextId);

        // Achievement list items
        ConfigureAchievementList(menuState, ref nextId);

        // Bottom buttons (Back, Reset)
        ConfigureBottomButtons(menuState, ref nextId);

        // Confirmation dialog buttons
        ConfigureDialogButtons(menuState, ref nextId);

        // Set up link points
        foreach (var binding in _bindingById.Values)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
            linkPoint.Unlink();
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = nextId - 1;

        // Handle initial focus
        if (_initialFocusFramesRemaining > 0)
        {
            int defaultPointId = _achievementBindings.Count > 0
                ? _achievementBindings[0].Binding.Id
                : (_bottomBindings.Count > 0 ? _bottomBindings[0].Id : BaseLinkId);

            UILinkPointNavigator.ChangePoint(defaultPointId);
            _initialFocusFramesRemaining--;
        }

        // Handle search mode -> navigation mode transition
        if (SearchModeManager.ConsumeFocusFirstModRequest() && _achievementBindings.Count > 0)
        {
            _currentRegion = NavigationRegion.AchievementList;
            _currentAchievementIndex = 0;
            _lastAnnouncedPointId = -1;
            _lastSeenPointId = -1;

            int pointId = _achievementBindings[0].Binding.Id;
            UILinkPointNavigator.ChangePoint(pointId);
        }
    }

    private void ConfigureCategoryButtons(object menuState, ref int nextId)
    {
        try
        {
            if (ReflectionCache.UIAchievementsMenu.CategoryButtons?.GetValue(menuState) is not System.Collections.IList categoryButtons)
            {
                return;
            }

            for (int i = 0; i < categoryButtons.Count; i++)
            {
                if (categoryButtons[i] is not UIElement button)
                {
                    continue;
                }

                string label = i < CategoryNames.Length ? CategoryNames[i] : $"Category {i + 1}";

                // Check toggle state via IsOn property
                bool isOn = false;
                try
                {
                    var isOnProp = button.GetType().GetProperty("IsOn", BindingFlags.Public | BindingFlags.Instance);
                    if (isOnProp?.GetValue(button) is bool onVal)
                    {
                        isOn = onVal;
                    }
                }
                catch
                {
                    // Best effort
                }

                string stateLabel = isOn ? $"{label}, On" : $"{label}, Off";

                CalculatedStyle dims = button.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var binding = new PointBinding(nextId++, center, stateLabel, string.Empty, button, PointType.CategoryButton);
                _categoryBindings.Add(binding);
                _bindingById[binding.Id] = binding;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error configuring category buttons: {ex.Message}");
        }
    }

    private void ConfigureAchievementList(object menuState, ref int nextId)
    {
        try
        {
            if (ReflectionCache.UIAchievementsMenu.AchievementsList?.GetValue(menuState) is not UIList achievementsList)
            {
                return;
            }

            foreach (UIElement item in achievementsList)
            {
                if (item.GetType().Name != "UIAchievementListItem")
                {
                    continue;
                }

                var achievementBinding = CreateAchievementBinding(item, ref nextId);
                if (achievementBinding is not null)
                {
                    _achievementBindings.Add(achievementBinding);
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error configuring achievement list: {ex.Message}");
        }
    }

    private AchievementBinding? CreateAchievementBinding(UIElement item, ref int nextId)
    {
        try
        {
            // Call GetAchievement() on the UIAchievementListItem
            var getAchievementMethod = item.GetType().GetMethod("GetAchievement", BindingFlags.Public | BindingFlags.Instance);
            object? achievement = getAchievementMethod?.Invoke(item, null);
            if (achievement is null)
            {
                return null;
            }

            // Extract achievement properties
            var friendlyNameProp = achievement.GetType().GetProperty("FriendlyName", BindingFlags.Public | BindingFlags.Instance);
            var descriptionProp = achievement.GetType().GetProperty("Description", BindingFlags.Public | BindingFlags.Instance);
            var isCompletedProp = achievement.GetType().GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance);
            var categoryProp = achievement.GetType().GetProperty("Category", BindingFlags.Public | BindingFlags.Instance);
            var hiddenField = achievement.GetType().GetField("Hidden", BindingFlags.Public | BindingFlags.Instance);
            var hasTrackerProp = achievement.GetType().GetProperty("HasTracker", BindingFlags.Public | BindingFlags.Instance);

            string name = string.Empty;
            string description = string.Empty;
            bool isCompleted = false;
            int categoryIndex = -1;
            bool isHidden = false;
            string progressText = string.Empty;

            // Get name from FriendlyName (LocalizedText)
            object? friendlyName = friendlyNameProp?.GetValue(achievement);
            if (friendlyName is null)
            {
                var nameField = achievement.GetType().GetField("FriendlyName", BindingFlags.Public | BindingFlags.Instance);
                friendlyName = nameField?.GetValue(achievement);
            }

            if (friendlyName is LocalizedText localizedName)
            {
                name = localizedName.Value;
            }
            else if (friendlyName is not null)
            {
                var valueProp = friendlyName.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                name = valueProp?.GetValue(friendlyName) as string ?? friendlyName.ToString() ?? string.Empty;
            }

            // Get description from Description (LocalizedText)
            // Try property first, then field, with multiple fallback strategies
            object? desc = descriptionProp?.GetValue(achievement);
            if (desc is null)
            {
                // Description might be a field rather than a property
                var descField = achievement.GetType().GetField("Description", BindingFlags.Public | BindingFlags.Instance);
                desc = descField?.GetValue(achievement);
            }

            if (desc is LocalizedText localizedDesc)
            {
                description = localizedDesc.Value;
            }
            else if (desc is not null)
            {
                // Fallback: try getting Value property via reflection, or use ToString
                var valueProp = desc.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                description = valueProp?.GetValue(desc) as string ?? desc.ToString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                _instance?.Mod.Logger.Debug($"[{SystemLogName}] Empty description for achievement: {name}");
            }

            if (isCompletedProp?.GetValue(achievement) is bool completed)
            {
                isCompleted = completed;
            }

            if (categoryProp?.GetValue(achievement) is Enum categoryEnum)
            {
                categoryIndex = Convert.ToInt32(categoryEnum);
            }

            if (hiddenField?.GetValue(achievement) is bool hidden)
            {
                isHidden = hidden;
            }

            // Get progress if available
            if (hasTrackerProp?.GetValue(achievement) is true)
            {
                try
                {
                    var getTrackerMethod = achievement.GetType().GetMethod("GetTracker", BindingFlags.Public | BindingFlags.Instance);
                    object? tracker = getTrackerMethod?.Invoke(achievement, null);
                    if (tracker is not null)
                    {
                        var getValueMethod = tracker.GetType().GetMethod("GetValue", BindingFlags.Public | BindingFlags.Instance);
                        var getMaxValueMethod = tracker.GetType().GetMethod("GetMaxValue", BindingFlags.Public | BindingFlags.Instance);

                        object? currentVal = getValueMethod?.Invoke(tracker, null);
                        object? maxVal = getMaxValueMethod?.Invoke(tracker, null);

                        if (currentVal is not null && maxVal is not null)
                        {
                            progressText = $"{currentVal} of {maxVal}";
                        }
                    }
                }
                catch
                {
                    // Progress tracking is best-effort
                }
            }

            // Check for modded achievement
            string modName = string.Empty;
            try
            {
                var modAchievementProp = achievement.GetType().GetProperty("ModAchievement", BindingFlags.Public | BindingFlags.Instance);
                object? modAchievement = modAchievementProp?.GetValue(achievement);
                if (modAchievement is not null)
                {
                    var modProp = modAchievement.GetType().GetProperty("Mod", BindingFlags.Public | BindingFlags.Instance);
                    object? mod = modProp?.GetValue(modAchievement);
                    if (mod is not null)
                    {
                        var displayNameProp = mod.GetType().GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
                        if (displayNameProp?.GetValue(mod) is string dn)
                        {
                            modName = dn;
                        }
                    }
                }
            }
            catch
            {
                // Modded info is best-effort
            }

            string categoryName = categoryIndex >= 0 && categoryIndex < CategoryNames.Length - 1
                ? CategoryNames[categoryIndex]
                : (string.IsNullOrEmpty(modName) ? "Unknown" : "Modded");

            CalculatedStyle dims = item.GetDimensions();
            Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
            var binding = new PointBinding(nextId++, center, name, description, item, PointType.AchievementItem);
            _bindingById[binding.Id] = binding;

            return new AchievementBinding
            {
                Binding = binding,
                Name = name,
                Description = description,
                IsCompleted = isCompleted,
                IsHidden = isHidden,
                CategoryName = categoryName,
                ModName = modName,
                ProgressText = progressText,
                ListItem = item
            };
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error creating achievement binding: {ex.Message}");
            return null;
        }
    }

    private void ConfigureBottomButtons(object menuState, ref int nextId)
    {
        try
        {
            // Back button
            if (ReflectionCache.UIAchievementsMenu.Backpanel?.GetValue(menuState) is UIElement backPanel)
            {
                CalculatedStyle dims = backPanel.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                string backLabel = Language.GetTextValue("UI.Back");
                if (string.IsNullOrWhiteSpace(backLabel)) backLabel = "Back";
                var binding = new PointBinding(nextId++, center, backLabel, string.Empty, backPanel, PointType.BackButton);
                _bottomBindings.Add(binding);
                _bindingById[binding.Id] = binding;
            }

            // Reset achievements button
            if (ReflectionCache.UIAchievementsMenu.ResetAchievementsButton?.GetValue(menuState) is UIElement resetButton)
            {
                // Check if the button is visible (has a parent in the hierarchy)
                if (menuState is UIElement parentState && parentState.HasChild(resetButton))
                {
                    CalculatedStyle dims = resetButton.GetDimensions();
                    Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                    var binding = new PointBinding(nextId++, center, "Reset Achievements", string.Empty, resetButton, PointType.ActionButton);
                    _bottomBindings.Add(binding);
                    _bindingById[binding.Id] = binding;
                }
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error configuring bottom buttons: {ex.Message}");
        }
    }

    private void ConfigureDialogButtons(object menuState, ref int nextId)
    {
        try
        {
            if (!IsConfirmDialogActive(menuState))
            {
                return;
            }

            if (ReflectionCache.UIAchievementsMenu.YesButton?.GetValue(menuState) is UIElement yesButton)
            {
                CalculatedStyle dims = yesButton.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var binding = new PointBinding(nextId++, center, "Yes", string.Empty, yesButton, PointType.ActionButton);
                _dialogYesBinding = binding;
                _bindingById[binding.Id] = binding;
            }

            if (ReflectionCache.UIAchievementsMenu.NoButton?.GetValue(menuState) is UIElement noButton)
            {
                CalculatedStyle dims = noButton.GetDimensions();
                Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
                var binding = new PointBinding(nextId++, center, "No", string.Empty, noButton, PointType.ActionButton);
                _dialogNoBinding = binding;
                _bindingById[binding.Id] = binding;
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error configuring dialog buttons: {ex.Message}");
        }
    }

    private bool IsConfirmDialogActive(object menuState)
    {
        try
        {
            object? blockInput = ReflectionCache.UIAchievementsMenu.BlockInput?.GetValue(menuState);
            if (blockInput is UIElement blockElement && blockElement.Parent is not null)
            {
                return true;
            }
        }
        catch
        {
            // Best effort
        }
        return false;
    }

    #endregion

    #region Navigation

    private void HandleNavigation(object menuState)
    {
        if (!_currentInput.HasNavigation)
        {
            return;
        }

        bool navigated = false;

        switch (_currentRegion)
        {
            case NavigationRegion.CategoryButtons:
                navigated = HandleCategoryNavigation();
                break;
            case NavigationRegion.AchievementList:
                navigated = HandleAchievementListNavigation();
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

            // Auto-scroll achievement list
            if (_currentRegion == NavigationRegion.AchievementList)
            {
                ScrollToCurrentAchievement(menuState);
            }
        }
    }

    private bool HandleCategoryNavigation()
    {
        if (_categoryBindings.Count == 0)
        {
            return false;
        }

        if (_currentInput.Left && _currentCategoryIndex > 0)
        {
            _currentCategoryIndex--;
            return true;
        }
        if (_currentInput.Right && _currentCategoryIndex < _categoryBindings.Count - 1)
        {
            _currentCategoryIndex++;
            return true;
        }
        if (_currentInput.Down)
        {
            if (_achievementBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.AchievementList;
                _currentAchievementIndex = 0;
                return true;
            }
            if (_bottomBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.BottomButtons;
                _currentBottomIndex = 0;
                return true;
            }
        }

        return false;
    }

    private bool HandleAchievementListNavigation()
    {
        if (_achievementBindings.Count == 0)
        {
            return false;
        }

        if (_currentInput.Up)
        {
            if (_currentAchievementIndex > 0)
            {
                _currentAchievementIndex--;
                return true;
            }
            if (_categoryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.CategoryButtons;
                _currentCategoryIndex = Math.Min(_currentCategoryIndex, _categoryBindings.Count - 1);
                return true;
            }
        }
        if (_currentInput.Down)
        {
            if (_currentAchievementIndex < _achievementBindings.Count - 1)
            {
                _currentAchievementIndex++;
                return true;
            }
            if (_bottomBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.BottomButtons;
                _currentBottomIndex = 0;
                return true;
            }
        }

        return false;
    }

    private bool HandleBottomButtonNavigation()
    {
        if (_bottomBindings.Count == 0)
        {
            return false;
        }

        if (_currentInput.Left && _currentBottomIndex > 0)
        {
            _currentBottomIndex--;
            return true;
        }
        if (_currentInput.Right && _currentBottomIndex < _bottomBindings.Count - 1)
        {
            _currentBottomIndex++;
            return true;
        }
        if (_currentInput.Up)
        {
            if (_achievementBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.AchievementList;
                _currentAchievementIndex = _achievementBindings.Count - 1;
                return true;
            }
            if (_categoryBindings.Count > 0)
            {
                _currentRegion = NavigationRegion.CategoryButtons;
                _currentCategoryIndex = 0;
                return true;
            }
        }

        return false;
    }

    private void HandleDialogNavigation()
    {
        if (_currentInput.Left || _currentInput.Up)
        {
            if (_dialogFocusIndex > 0)
            {
                _dialogFocusIndex = 0;
                int? pointId = GetCurrentPointId();
                if (pointId.HasValue) UILinkPointNavigator.ChangePoint(pointId.Value);
            }
        }
        else if (_currentInput.Right || _currentInput.Down)
        {
            if (_dialogFocusIndex < 1)
            {
                _dialogFocusIndex = 1;
                int? pointId = GetCurrentPointId();
                if (pointId.HasValue) UILinkPointNavigator.ChangePoint(pointId.Value);
            }
        }
    }

    private void ScrollToCurrentAchievement(object menuState)
    {
        try
        {
            if (_currentAchievementIndex < 0 || _currentAchievementIndex >= _achievementBindings.Count)
            {
                return;
            }

            UIElement item = _achievementBindings[_currentAchievementIndex].ListItem;
            if (ReflectionCache.UIAchievementsMenu.AchievementsList?.GetValue(menuState) is UIList list)
            {
                list.Goto((UIElement element) => ReferenceEquals(element, item));
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Debug($"[{SystemLogName}] Error scrolling to achievement: {ex.Message}");
        }
    }

    #endregion

    #region Action Handling

    private void HandleAction(object menuState)
    {
        if (_currentInput.ActionPressed)
        {
            int? currentPointId = GetCurrentPointId();
            if (currentPointId.HasValue && _bindingById.TryGetValue(currentPointId.Value, out var binding))
            {
                if (binding.Element is UIElement buttonElement)
                {
                    Mod.Logger.Info($"[{SystemLogName}] Clicking: {binding.Label}");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(buttonElement, Main.MouseScreen);
                        buttonElement.LeftClick(clickEvent);

                        // Consume input to prevent native UI from also processing it
                        Main.mouseLeft = false;
                        Main.mouseLeftRelease = false;
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[{SystemLogName}] Click failed: {ex.Message}");
                    }

                    // If clicking a category button, force re-announcement after filter updates
                    if (binding.Type == PointType.CategoryButton)
                    {
                        _lastAnnouncedPointId = -1;
                        _lastSeenPointId = -1;
                    }
                }
            }
        }

        if (_currentInput.BackPressed)
        {
            // Click the Back button
            foreach (var binding in _bottomBindings)
            {
                if (binding.Type == PointType.BackButton && binding.Element is UIElement backButton)
                {
                    Mod.Logger.Info($"[{SystemLogName}] B button pressed, clicking Back");
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    try
                    {
                        var clickEvent = new UIMouseEvent(backButton, Main.MouseScreen);
                        backButton.LeftClick(clickEvent);
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Warn($"[{SystemLogName}] Back click failed: {ex.Message}");
                    }
                    return;
                }
            }
        }
    }

    private void HandleDialogAction(object menuState)
    {
        if (_currentInput.ActionPressed)
        {
            PointBinding? targetBinding = _dialogFocusIndex == 0 ? _dialogYesBinding : _dialogNoBinding;
            if (targetBinding?.Element is UIElement button)
            {
                Mod.Logger.Info($"[{SystemLogName}] Dialog action: {targetBinding.Value.Label}");
                SoundEngine.PlaySound(SoundID.MenuTick);

                try
                {
                    var clickEvent = new UIMouseEvent(button, Main.MouseScreen);
                    button.LeftClick(clickEvent);

                    Main.mouseLeft = false;
                    Main.mouseLeftRelease = false;
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warn($"[{SystemLogName}] Dialog click failed: {ex.Message}");
                }
            }
        }

        // B button dismisses dialog (clicks No)
        if (_currentInput.BackPressed && _dialogNoBinding?.Element is UIElement noButton)
        {
            Mod.Logger.Info($"[{SystemLogName}] B button in dialog, clicking No");
            SoundEngine.PlaySound(SoundID.MenuTick);

            try
            {
                var clickEvent = new UIMouseEvent(noButton, Main.MouseScreen);
                noButton.LeftClick(clickEvent);
            }
            catch (Exception ex)
            {
                Mod.Logger.Warn($"[{SystemLogName}] Dialog No click failed: {ex.Message}");
            }
        }
    }

    #endregion

    #region Announcement

    private int? GetCurrentPointId()
    {
        switch (_currentRegion)
        {
            case NavigationRegion.CategoryButtons:
                if (_categoryBindings.Count == 0 || _currentCategoryIndex < 0 || _currentCategoryIndex >= _categoryBindings.Count)
                    return null;
                return _categoryBindings[_currentCategoryIndex].Id;

            case NavigationRegion.AchievementList:
                if (_achievementBindings.Count == 0 || _currentAchievementIndex < 0 || _currentAchievementIndex >= _achievementBindings.Count)
                    return null;
                return _achievementBindings[_currentAchievementIndex].Binding.Id;

            case NavigationRegion.BottomButtons:
                if (_bottomBindings.Count == 0 || _currentBottomIndex < 0 || _currentBottomIndex >= _bottomBindings.Count)
                    return null;
                return _bottomBindings[_currentBottomIndex].Id;

            case NavigationRegion.ConfirmDialog:
                if (_dialogFocusIndex == 0 && _dialogYesBinding.HasValue)
                    return _dialogYesBinding.Value.Id;
                if (_dialogFocusIndex == 1 && _dialogNoBinding.HasValue)
                    return _dialogNoBinding.Value.Id;
                return null;

            default:
                return null;
        }
    }

    private void AnnounceCurrentFocus(object menuState)
    {
        int? expectedPoint = GetCurrentPointId();
        if (!expectedPoint.HasValue)
        {
            return;
        }

        int currentPoint = expectedPoint.Value;

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

        string announcement = BuildAnnouncement(menuState);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);
        Mod.Logger.Info($"[{SystemLogName}] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    private string BuildAnnouncement(object menuState)
    {
        // On first focus, announce screen header
        bool isFirstAnnouncement = !ScreenReaderService.WasContextAnnounced(ContextKeyScreen);
        string header = string.Empty;

        if (isFirstAnnouncement)
        {
            ScreenReaderService.MarkContextAnnounced(ContextKeyScreen);

            // Count completed achievements
            int completed = 0;
            int total = 0;
            try
            {
                if (ReflectionCache.UIAchievementsMenu.AchievementElements?.GetValue(menuState) is System.Collections.IList allElements)
                {
                    total = allElements.Count;
                    foreach (object? item in allElements)
                    {
                        if (item is null) continue;
                        var getAchievement = item.GetType().GetMethod("GetAchievement", BindingFlags.Public | BindingFlags.Instance);
                        object? achievement = getAchievement?.Invoke(item, null);
                        if (achievement is null) continue;
                        var isCompletedProp = achievement.GetType().GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance);
                        if (isCompletedProp?.GetValue(achievement) is true)
                        {
                            completed++;
                        }
                    }
                }
            }
            catch
            {
                // Best effort
            }

            header = total > 0
                ? $"Achievements, {completed} of {total} unlocked. Press Tab to search. "
                : "Achievements. Press Tab to search. ";
        }

        switch (_currentRegion)
        {
            case NavigationRegion.CategoryButtons:
                return BuildCategoryAnnouncement(header);
            case NavigationRegion.AchievementList:
                return BuildAchievementAnnouncement(header);
            case NavigationRegion.BottomButtons:
                return BuildBottomButtonAnnouncement(header);
            case NavigationRegion.ConfirmDialog:
                return BuildDialogAnnouncement();
            default:
                return string.Empty;
        }
    }

    private string BuildCategoryAnnouncement(string header)
    {
        if (_currentCategoryIndex < 0 || _currentCategoryIndex >= _categoryBindings.Count)
        {
            return string.Empty;
        }

        var binding = _categoryBindings[_currentCategoryIndex];
        string label = TextSanitizer.Clean(binding.Label);
        int position = _currentCategoryIndex + 1;
        int total = _categoryBindings.Count;

        return $"{header}{label}, filter {position} of {total}";
    }

    private string BuildAchievementAnnouncement(string header)
    {
        if (_currentAchievementIndex < 0 || _currentAchievementIndex >= _achievementBindings.Count)
        {
            return string.Empty;
        }

        var achievement = _achievementBindings[_currentAchievementIndex];
        int position = _currentAchievementIndex + 1;
        int total = _achievementBindings.Count;

        // Handle hidden locked achievements
        if (achievement.IsHidden && !achievement.IsCompleted)
        {
            return $"{header}Hidden achievement, Locked, {achievement.CategoryName}, achievement {position} of {total}";
        }

        string status = achievement.IsCompleted ? "Completed" : "Locked";
        string name = TextSanitizer.Clean(achievement.Name);

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name);
        }

        parts.Add(status);
        parts.Add(achievement.CategoryName);

        if (!string.IsNullOrEmpty(achievement.ProgressText) && !achievement.IsCompleted)
        {
            parts.Add(achievement.ProgressText);
        }

        if (!string.IsNullOrEmpty(achievement.ModName))
        {
            parts.Add(achievement.ModName);
        }

        string description = TextSanitizer.Clean(achievement.Description).TrimEnd('.');
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description);
        }

        parts.Add($"achievement {position} of {total}");

        return $"{header}{string.Join(", ", parts)}";
    }

    private string BuildBottomButtonAnnouncement(string header)
    {
        if (_currentBottomIndex < 0 || _currentBottomIndex >= _bottomBindings.Count)
        {
            return string.Empty;
        }

        var binding = _bottomBindings[_currentBottomIndex];
        string label = TextSanitizer.Clean(binding.Label);

        if (_bottomBindings.Count > 1)
        {
            int position = _currentBottomIndex + 1;
            return $"{header}{label}, {position} of {_bottomBindings.Count}";
        }

        return $"{header}{label}";
    }

    private string BuildDialogAnnouncement()
    {
        string label = _dialogFocusIndex == 0 ? "Yes" : "No";
        return label;
    }

    #endregion

    #region Helper Types

    private enum PointType
    {
        CategoryButton,
        AchievementItem,
        ActionButton,
        BackButton
    }

    private readonly record struct PointBinding(
        int Id,
        Vector2 Position,
        string Label,
        string Description,
        UIElement? Element,
        PointType Type
    );

    private sealed class AchievementBinding
    {
        public PointBinding Binding { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public bool IsHidden { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public string ModName { get; init; } = string.Empty;
        public string ProgressText { get; init; } = string.Empty;
        public UIElement ListItem { get; init; } = null!;
    }

    private struct NavigationInput
    {
        public bool Left;
        public bool Right;
        public bool Up;
        public bool Down;
        public bool ActionPressed;
        public bool BackPressed;
        public bool HasNavigation;
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

    #endregion
}
