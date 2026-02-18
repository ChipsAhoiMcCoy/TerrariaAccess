#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.ModMenuAccessibility;

/// <summary>
/// Base class for mod menu accessibility systems that provides common navigation infrastructure.
/// Subclasses handle menu-specific logic while inheriting shared input handling and announcement patterns.
/// </summary>
public abstract class ModMenuAccessibilityBase : ModSystem
{
    #region Abstract Members (must be implemented by subclasses)

    /// <summary>
    /// The base link ID for this menu's UILinkPointNavigator points.
    /// Use values from <see cref="LinkIdRegistry"/>.
    /// </summary>
    protected abstract int BaseLinkId { get; }

    /// <summary>
    /// The full type name of the UI state this system handles (e.g., "Terraria.ModLoader.UI.UIMods").
    /// </summary>
    protected abstract string MenuTypeName { get; }

    /// <summary>
    /// A short name for logging purposes (e.g., "ManageMods").
    /// </summary>
    protected abstract string SystemLogName { get; }

    /// <summary>
    /// Called when the menu state changes to this menu.
    /// Initialize any menu-specific state here.
    /// </summary>
    protected abstract void OnMenuEntered(object menuState);

    /// <summary>
    /// Called when leaving this menu.
    /// Clean up any menu-specific state here.
    /// </summary>
    protected abstract void OnMenuExited();

    /// <summary>
    /// Configure gamepad navigation points for the current frame.
    /// Populate binding collections and set up UILinkPointNavigator points.
    /// </summary>
    protected abstract void ConfigureGamepadPoints(object menuState);

    /// <summary>
    /// Handle navigation input (D-pad, analog stick) for this frame.
    /// Use the input state from <see cref="CurrentInput"/> and helpers.
    /// </summary>
    protected abstract void HandleNavigation(object menuState);

    /// <summary>
    /// Handle action button input (A button, Enter, Space) for this frame.
    /// </summary>
    protected abstract void HandleAction(object menuState);

    /// <summary>
    /// Get the current point ID based on navigation state.
    /// Return null if no valid focus point.
    /// </summary>
    protected abstract int? GetCurrentPointId();

    /// <summary>
    /// Build an announcement string for the given binding.
    /// </summary>
    protected abstract string BuildAnnouncement(PointBinding binding, object menuState);

    /// <summary>
    /// Check if this system should process input (beyond standard gamepad checks).
    /// Return true to process, false to skip.
    /// </summary>
    protected virtual bool ShouldProcessInput(object menuState) => true;

    #endregion

    #region Common State

    /// <summary>
    /// The last menu state object reference for detecting menu changes.
    /// </summary>
    protected object? LastMenuState { get; private set; }

    /// <summary>
    /// The last point ID that was announced.
    /// </summary>
    protected int LastAnnouncedPointId { get; set; } = -1;

    /// <summary>
    /// The last point ID that was seen (for stability detection).
    /// </summary>
    protected int LastSeenPointId { get; set; } = -1;

    /// <summary>
    /// Frames remaining for initial focus setting.
    /// </summary>
    protected int InitialFocusFramesRemaining { get; set; }

    /// <summary>
    /// The current focus index within the current region/list.
    /// </summary>
    protected int CurrentFocusIndex { get; set; }

    /// <summary>
    /// Lookup dictionary for bindings by ID.
    /// </summary>
    protected Dictionary<int, PointBinding> BindingById { get; } = new();

    #endregion

    #region Input State

    /// <summary>
    /// Current frame's input state, populated each frame.
    /// </summary>
    protected NavigationInput CurrentInput { get; private set; }

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
    private bool _keyInventorySelectWasPressed;

    #endregion

    #region Lifecycle

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        Mod.Logger.Info($"[{SystemLogName}] Loading accessibility system");
        On_Main.DrawMenu += HandleDrawMenu;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.DrawMenu -= HandleDrawMenu;

        // Clear common state
        BindingById.Clear();
        LastMenuState = null;
        LastAnnouncedPointId = -1;
        LastSeenPointId = -1;
        InitialFocusFramesRemaining = 0;
        CurrentFocusIndex = 0;

        // Reset input state
        ResetInputState();
    }

    private void HandleDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        // Call original first
        orig(self, gameTime);

        // Then process menu accessibility
        TryProcessMenu();
    }

    #endregion

    #region Menu Processing

    private void TryProcessMenu()
    {
        object? currentState = Main.MenuUI?.CurrentState;

        // Check if we're in our target menu
        bool isTargetMenu = currentState is not null &&
                            currentState.GetType().FullName == MenuTypeName;

        if (!isTargetMenu)
        {
            // Clear state if we've left our menu
            if (LastMenuState is not null)
            {
                Mod.Logger.Info($"[{SystemLogName}] Left menu");
                OnMenuExited();
                LastMenuState = null;
                LastAnnouncedPointId = -1;
                LastSeenPointId = -1;
                BindingById.Clear();
            }
            return;
        }

        // Track menu state changes
        bool menuChanged = !ReferenceEquals(currentState, LastMenuState);
        if (menuChanged)
        {
            LastMenuState = currentState;
            LastAnnouncedPointId = -1;
            LastSeenPointId = -1;
            InitialFocusFramesRemaining = GetInitialFocusFrameCount();
            CurrentFocusIndex = 0;

            // Prevent immediate button triggers on menu entry
            _aButtonWasPressed = true;
            _bButtonWasPressed = true;

            Mod.Logger.Info($"[{SystemLogName}] Entered menu");
            OnMenuEntered(currentState!);
        }

        // Check if we should process input
        bool hasGamepadInput = PlayerInput.UsingGamepadUI ||
                               GamePad.GetState(PlayerIndex.One).IsConnected;

        if (!hasGamepadInput && !ShouldProcessKeyboardInput())
        {
            return;
        }

        if (!ShouldProcessInput(currentState!))
        {
            return;
        }

        try
        {
            // Update input state for this frame
            UpdateInputState();

            // Process menu-specific logic
            ConfigureGamepadPoints(currentState!);
            HandleNavigation(currentState!);
            HandleAction(currentState!);
            AnnounceCurrentFocus(currentState!);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"[{SystemLogName}] Error processing menu: {ex}");
        }
    }

    /// <summary>
    /// Override to customize the initial focus frame count.
    /// Default is 10 frames.
    /// </summary>
    protected virtual int GetInitialFocusFrameCount() => 10;

    /// <summary>
    /// Override to enable keyboard navigation.
    /// Default returns true (keyboard navigation enabled when not in search mode).
    /// </summary>
    protected virtual bool ShouldProcessKeyboardInput() => true;

    #endregion

    #region Input Handling

    private void UpdateInputState()
    {
        GamePadState gpState = GamePad.GetState(PlayerIndex.One);
        KeyboardState kbState = Main.keyState;

        var input = new NavigationInput();

        // Handle gamepad input
        if (gpState.IsConnected)
        {
            // D-pad (new press detection)
            input.Left = gpState.DPad.Left == ButtonState.Pressed && !_leftWasPressed;
            input.Right = gpState.DPad.Right == ButtonState.Pressed && !_rightWasPressed;
            input.Up = gpState.DPad.Up == ButtonState.Pressed && !_upWasPressed;
            input.Down = gpState.DPad.Down == ButtonState.Pressed && !_downWasPressed;

            _leftWasPressed = gpState.DPad.Left == ButtonState.Pressed;
            _rightWasPressed = gpState.DPad.Right == ButtonState.Pressed;
            _upWasPressed = gpState.DPad.Up == ButtonState.Pressed;
            _downWasPressed = gpState.DPad.Down == ButtonState.Pressed;

            // Analog stick with deadzone and debouncing
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

            // Buttons
            bool aPressed = gpState.Buttons.A == ButtonState.Pressed;
            input.ActionPressed = aPressed && !_aButtonWasPressed;
            _aButtonWasPressed = aPressed;

            bool bPressed = gpState.Buttons.B == ButtonState.Pressed;
            input.BackPressed = bPressed && !_bButtonWasPressed;
            _bButtonWasPressed = bPressed;
        }

        // Handle keyboard input
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

            // Check gamepad emulation InventorySelect keybind (I key by default)
            bool inventorySelectNow = false;
            if (GamepadEmulation.GamepadEmulationKeybinds.InventorySelect is { } selectKeybind)
            {
                inventorySelectNow = selectKeybind.Current
                    || GamepadEmulation.VirtualTriggerService.IsKeybindPressedRaw(selectKeybind);
            }

            if (!input.ActionPressed)
            {
                if ((enterNow && !_keyEnterWasPressed) || (spaceNow && !_keySpaceWasPressed) ||
                    (inventorySelectNow && !_keyInventorySelectWasPressed))
                {
                    input.ActionPressed = true;
                }
            }

            _keyEnterWasPressed = enterNow;
            _keySpaceWasPressed = spaceNow;
            _keyInventorySelectWasPressed = inventorySelectNow;
        }

        input.HasNavigation = input.Left || input.Right || input.Up || input.Down;

        CurrentInput = input;
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
        _keyInventorySelectWasPressed = false;
    }

    #endregion

    #region Announcement

    private void AnnounceCurrentFocus(object menuState)
    {
        int? expectedPoint = GetCurrentPointId();
        int currentPoint = expectedPoint ?? UILinkPointNavigator.CurrentPoint;

        if (currentPoint < BaseLinkId)
        {
            return;
        }

        // Only announce when the point has been stable for at least one frame
        bool isStable = currentPoint == LastSeenPointId;
        bool alreadyAnnounced = currentPoint == LastAnnouncedPointId;

        LastSeenPointId = currentPoint;

        if (!isStable || alreadyAnnounced)
        {
            return;
        }

        if (!BindingById.TryGetValue(currentPoint, out PointBinding binding))
        {
            Mod.Logger.Debug($"[{SystemLogName}] No binding found for point {currentPoint}");
            return;
        }

        string announcement = BuildAnnouncement(binding, menuState);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        LastAnnouncedPointId = currentPoint;

        SoundEngine.PlaySound(SoundID.MenuTick);

        Mod.Logger.Info($"[{SystemLogName}] Announcing: {announcement}");
        ScreenReaderService.Announce(announcement, force: true);
    }

    /// <summary>
    /// Force an announcement, bypassing stability checks.
    /// </summary>
    protected void ForceAnnounce(string announcement)
    {
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        SoundEngine.PlaySound(SoundID.MenuTick);
        ScreenReaderService.Announce(announcement, force: true);
        Mod.Logger.Info($"[{SystemLogName}] Force announced: {announcement}");
    }

    #endregion

    #region UILinkPoint Helpers

    /// <summary>
    /// Gets or creates a UILinkPoint for the given ID.
    /// </summary>
    protected static UILinkPoint EnsureLinkPoint(int id)
    {
        if (!UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? linkPoint))
        {
            linkPoint = new UILinkPoint(id, true, -1, -1, -1, -1);
            UILinkPointNavigator.Points[id] = linkPoint;
        }

        return linkPoint;
    }

    /// <summary>
    /// Connects two points horizontally (left's Right = right, right's Left = left).
    /// </summary>
    protected static void ConnectHorizontal(PointBinding left, PointBinding right)
    {
        UILinkPoint leftPoint = EnsureLinkPoint(left.Id);
        UILinkPoint rightPoint = EnsureLinkPoint(right.Id);

        leftPoint.Right = right.Id;
        rightPoint.Left = left.Id;
    }

    /// <summary>
    /// Connects two points vertically (up's Down = down, down's Up = up).
    /// </summary>
    protected static void ConnectVertical(PointBinding up, PointBinding down)
    {
        UILinkPoint upPoint = EnsureLinkPoint(up.Id);
        UILinkPoint downPoint = EnsureLinkPoint(down.Id);

        upPoint.Down = down.Id;
        downPoint.Up = up.Id;
    }

    /// <summary>
    /// Creates a button binding with centered position.
    /// </summary>
    protected static PointBinding CreateButtonBinding(ref int nextId, UIElement element, string label, PointType type)
    {
        CalculatedStyle dims = element.GetDimensions();
        Vector2 center = new(dims.X + dims.Width / 2f, dims.Y + dims.Height / 2f);
        return new PointBinding(nextId++, center, label, string.Empty, element, type);
    }

    /// <summary>
    /// Sets up a link point in UILinkPointNavigator (position and unlinked state).
    /// </summary>
    protected static void SetupLinkPoint(PointBinding binding)
    {
        UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
        UILinkPointNavigator.SetPosition(binding.Id, binding.Position);
        linkPoint.Unlink(); // Manual navigation handles linking
    }

    #endregion

    #region Common Types

    /// <summary>
    /// Types of navigable UI elements in mod menus.
    /// </summary>
    protected enum PointType
    {
        /// <summary>Filter/sort buttons at the top of the menu.</summary>
        FilterButton,

        /// <summary>Main mod item in the list.</summary>
        ModItem,

        /// <summary>Sub-button within a mod item (More Info, Delete, Config, etc.).</summary>
        ModItemButton,

        /// <summary>Action button (Enable All, Reload, etc.).</summary>
        ActionButton,

        /// <summary>Back/exit button.</summary>
        BackButton,

        /// <summary>Disabled button that cannot be clicked.</summary>
        DisabledButton
    }

    /// <summary>
    /// Represents a navigable UI element with its link point ID and metadata.
    /// </summary>
    protected readonly record struct PointBinding(
        int Id,
        Vector2 Position,
        string Label,
        string Description,
        UIElement? Element,
        PointType Type
    );

    /// <summary>
    /// Current frame's navigation input state.
    /// </summary>
    protected struct NavigationInput
    {
        public bool Left;
        public bool Right;
        public bool Up;
        public bool Down;
        public bool ActionPressed;
        public bool BackPressed;
        public bool HasNavigation;
    }

    #endregion
}
