#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.KeyboardParity;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Gives keyboard profiles access to controller-only bindings and unlocks the associated gameplay features.
/// </summary>
public sealed class KeyboardInputParitySystem : ModSystem
{
  private const int ControllerExtrasGroupIndex = 3;

  private static readonly string[] ControllerExclusiveBindingIds = {
        TriggerNames.LockOn,
        TriggerNames.RadialHotbar,
        TriggerNames.RadialQuickbar,
        TriggerNames.DpadRadial1,
        TriggerNames.DpadRadial2,
        TriggerNames.DpadRadial3,
        TriggerNames.DpadRadial4
    };

  private static Hook? _assembleBindPanelsHook;
  private static ILHook? _radialHotbarHook;
  private static ILHook? _radialQuickbarHook;
  private static Hook? _usingGamepadHook;
  private static Hook? _usingGamepadUiHook;
  private static FieldInfo? _bindsKeyboardField;
  private static FieldInfo? _bindsKeyboardUiField;
  private static MethodInfo? _createBindingGroupMethod;
  private static MethodInfo? _assembleBindPanelsMethod;
  private static MethodInfo? _drawRadialCircularMethod;
  private static MethodInfo? _drawRadialQuicksMethod;
  private static MethodInfo? _usingGamepadGetter;
  private static MethodInfo? _usingGamepadUiGetter;
  private static MethodInfo? _gamepadInputMethod;
  private static MethodInfo? _leftClickSellOrTrashMethod;
  private static ILHook? _gamepadInputIlHook;
  private static Hook? _leftClickSellOrTrashHook;
  private static int _lastMouseNpcType = -1;
  private static int _lastHousingQueryPoint = -1;
  private static bool _wasEnterOrSpaceDown = false;
  private static bool _wasMouseLeftDown = false;
  private static bool _wasIKeyDown = false;

  public override void Load()
  {
    if (Main.dedServ)
    {
      return;
    }

    CacheReflectionHandles();

    _assembleBindPanelsHook = TryCreateHook(_assembleBindPanelsMethod, ManageControls_AssembleBindPanels, "controls assembly");
    _radialHotbarHook = TryCreateIlHook(_drawRadialCircularMethod, AllowKeyboardRadialHotbar, "radial hotbar fade");
    _radialQuickbarHook = TryCreateIlHook(_drawRadialQuicksMethod, AllowKeyboardRadialQuickbar, "radial quickbar fade");
    _usingGamepadHook = TryCreateHook(_usingGamepadGetter, OverrideUsingGamepad, "PlayerInput.UsingGamepad");
    _usingGamepadUiHook = TryCreateHook(_usingGamepadUiGetter, OverrideUsingGamepadUi, "PlayerInput.UsingGamepadUI");
    _gamepadInputIlHook = TryCreateIlHook(_gamepadInputMethod, InjectVirtualSticksIntoGamepadInput, "PlayerInput.GamePadInput");
    _leftClickSellOrTrashHook = TryCreateHook(_leftClickSellOrTrashMethod, SuppressShiftTrashWhenDisabled, "ItemSlot.LeftClick_SellOrTrash");

    KeyboardParityFeatureState.StateChanged += OnFeatureToggleStateChanged;
    // Force parity on at startup so the game always sees a controller and the virtual sticks/keybinds stay active.
    KeyboardParityFeatureState.SetEnabled(true);
  }

  public override void Unload()
  {
    if (Main.dedServ)
    {
      return;
    }

    KeyboardParityFeatureState.StateChanged -= OnFeatureToggleStateChanged;
    KeyboardParityFeatureState.SetEnabled(false);

    _assembleBindPanelsHook?.Dispose();
    _assembleBindPanelsHook = null;
    _radialHotbarHook?.Dispose();
    _radialHotbarHook = null;
    _radialQuickbarHook?.Dispose();
    _radialQuickbarHook = null;
    _gamepadInputIlHook?.Dispose();
    _gamepadInputIlHook = null;
    _usingGamepadHook?.Dispose();
    _usingGamepadHook = null;
    _usingGamepadUiHook?.Dispose();
    _usingGamepadUiHook = null;
    _leftClickSellOrTrashHook?.Dispose();
    _leftClickSellOrTrashHook = null;

    _bindsKeyboardField = null;
    _bindsKeyboardUiField = null;
    _createBindingGroupMethod = null;
    _assembleBindPanelsMethod = null;
    _drawRadialCircularMethod = null;
    _drawRadialQuicksMethod = null;
    _leftClickSellOrTrashMethod = null;
    _usingGamepadGetter = null;
    _usingGamepadUiGetter = null;
  }

  private static void CacheReflectionHandles()
  {
    Type uiType = typeof(UIManageControls);
    _bindsKeyboardField = uiType.GetField("_bindsKeyboard", BindingFlags.NonPublic | BindingFlags.Instance);
    _bindsKeyboardUiField = uiType.GetField("_bindsKeyboardUI", BindingFlags.NonPublic | BindingFlags.Instance);
    _createBindingGroupMethod = uiType.GetMethod("CreateBindingGroup", BindingFlags.NonPublic | BindingFlags.Instance);
    _assembleBindPanelsMethod = uiType.GetMethod("AssembleBindPanels", BindingFlags.NonPublic | BindingFlags.Instance);

    Type itemSlotType = typeof(ItemSlot);
    _drawRadialCircularMethod = itemSlotType.GetMethod("DrawRadialCircular", BindingFlags.Public | BindingFlags.Static);
    _drawRadialQuicksMethod = itemSlotType.GetMethod("DrawRadialQuicks", BindingFlags.Public | BindingFlags.Static);
    _leftClickSellOrTrashMethod = itemSlotType.GetMethod("LeftClick_SellOrTrash", BindingFlags.NonPublic | BindingFlags.Static);

    Type playerInputType = typeof(PlayerInput);
    _usingGamepadGetter = playerInputType.GetMethod("get_UsingGamepad", BindingFlags.Public | BindingFlags.Static);
    _usingGamepadUiGetter = playerInputType.GetMethod("get_UsingGamepadUI", BindingFlags.Public | BindingFlags.Static);
    _gamepadInputMethod = playerInputType.GetMethod("GamePadInput", BindingFlags.NonPublic | BindingFlags.Static);

    if (_bindsKeyboardField is null || _bindsKeyboardUiField is null || _createBindingGroupMethod is null)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn("[KeyboardInputParity] Failed to cache UIManageControls reflection handles; controller extras will be skipped.");
    }
  }

  private static Hook? TryCreateHook(MethodInfo? target, Delegate detour, string label)
  {
    if (target is null)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[KeyboardInputParity] Cannot hook {label}: missing MethodInfo.");
      return null;
    }

    try
    {
      return new Hook(target, detour);
    }
    catch (Exception ex)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[KeyboardInputParity] Failed to hook {label}: {ex}");
      return null;
    }
  }

  private static ILHook? TryCreateIlHook(MethodInfo? target, ILContext.Manipulator manipulator, string label)
  {
    if (target is null)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[KeyboardInputParity] Cannot patch {label}: missing MethodInfo.");
      return null;
    }

    try
    {
      return new ILHook(target, manipulator);
    }
    catch (Exception ex)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[KeyboardInputParity] Failed to patch {label}: {ex}");
      return null;
    }
  }

  private delegate void AssembleBindPanelsDelegate(UIManageControls self);

  private static void ManageControls_AssembleBindPanels(AssembleBindPanelsDelegate orig, UIManageControls self)
  {
    orig(self);

    TryAppendControllerExtras(self, InputMode.Keyboard, _bindsKeyboardField);
    TryAppendControllerExtras(self, InputMode.KeyboardUI, _bindsKeyboardUiField);
  }

  private static void TryAppendControllerExtras(UIManageControls self, InputMode mode, FieldInfo? targetField)
  {
    if (targetField is null || _createBindingGroupMethod is null)
    {
      return;
    }

    if (targetField.GetValue(self) is not List<UIElement> groups)
    {
      return;
    }

    List<string> payload = new(ControllerExclusiveBindingIds);
    if (_createBindingGroupMethod.Invoke(self, new object[] { ControllerExtrasGroupIndex, payload, mode }) is not UIElement group)
    {
      return;
    }

    groups.Add(group);
  }

  private static void AllowKeyboardRadialHotbar(ILContext il)
  {
    InjectKeyboardRadialAllowance(il, TriggerNames.RadialHotbar, "radial hotbar");
  }

  private static void AllowKeyboardRadialQuickbar(ILContext il)
  {
    InjectKeyboardRadialAllowance(il, TriggerNames.RadialQuickbar, "radial quickbar");
  }

  private static void InjectKeyboardRadialAllowance(ILContext il, string triggerName, string label)
  {
    try
    {
      var cursor = new ILCursor(il);
      if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCall(typeof(PlayerInput), "get_UsingGamepad")))
      {
        cursor.EmitDelegate<Func<bool, bool>>(isUsingGamepad => isUsingGamepad || ShouldAllowRadialFromKeyboard(triggerName));
      }
      else
      {
        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[KeyboardInputParity] Unable to locate UsingGamepad check for {label} fade logic.");
      }
    }
    catch (Exception ex)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[KeyboardInputParity] Failed to patch {label}: {ex}");
    }
  }

  private static bool ShouldAllowRadialFromKeyboard(string triggerName)
  {
    return HasBinding(InputMode.Keyboard, triggerName) || HasBinding(InputMode.KeyboardUI, triggerName);
  }

  private static bool HasBinding(InputMode mode, string triggerName)
  {
    PlayerInputProfile? profile = PlayerInput.CurrentProfile;
    if (profile is null)
    {
      return false;
    }

    if (!profile.InputModes.TryGetValue(mode, out KeyConfiguration? configuration))
    {
      return false;
    }

    if (!configuration.KeyStatus.TryGetValue(triggerName, out List<string>? assignments))
    {
      return false;
    }

    return assignments.Count > 0;
  }

  public override void PostUpdateInput()
  {
    if (Main.dedServ)
    {
      return;
    }

    HandleFeatureToggleHotkey();

    // Inject housing-relevant triggers early so CheckHousingQueryOnMouseClick can see them.
    // Without this, the trigger check happens before ApplyInventoryVirtualTriggers() runs.
    if (KeyboardParityFeatureState.Enabled && Main.playerInventory && !IsTextInputActive())
    {
      InjectVirtualTrigger(ControllerParityKeybinds.InventorySelect, TriggerNames.MouseLeft);
    }

    CheckHousingQueryOnMouseClick();

    if (!KeyboardParityFeatureState.Enabled)
    {
      return;
    }

    bool needsUiMode = NeedsGamepadUiMode();
    ForceGamepadUiModeIfNeeded(needsUiMode);
    ApplyGlobalVirtualTriggers();
    ApplyInventoryVirtualTriggers(needsUiMode);
    ApplyMenuNavigationVirtualTriggers(needsUiMode);
  }

  private static bool ShouldEmulateGamepad()
  {
    if (!KeyboardParityFeatureState.Enabled)
    {
      return false;
    }

    if (IsTextInputActive())
    {
      return false;
    }

    return true;
  }

  private delegate bool UsingGamepadGetter();

  private static bool OverrideUsingGamepad(UsingGamepadGetter orig)
  {
    return orig() || ShouldEmulateGamepad();
  }

  private static bool OverrideUsingGamepadUi(UsingGamepadGetter orig)
  {
    return orig() || ShouldEmulateGamepad();
  }

  private delegate bool LeftClickSellOrTrashDelegate(Item[] inv, int context, int slot);

  /// <summary>
  /// Suppresses shift-based quick actions (sell/trash) when keyboard parity is enabled.
  ///
  /// The issue: Terraria's default keyboard profiles map LeftShift to SmartSelect. When we
  /// enable gamepad UI mode for keyboard parity, pressing Shift triggers SmartSelect, which
  /// sets ShiftForcedOn=true and calls LeftClick. This causes Shift to act like the quick
  /// action key instead of being ignored.
  ///
  /// The fix: Check if our explicit SmartSelect keybind (F by default) is pressed. If Shift
  /// is pressed but our keybind is not, block the action. This ensures users must use F for
  /// quick actions, not accidental Shift presses.
  /// </summary>
  private static bool SuppressShiftTrashWhenDisabled(LeftClickSellOrTrashDelegate orig, Item[] inv, int context, int slot)
  {
    // If keyboard parity is not enabled, use original behavior entirely.
    if (!KeyboardParityFeatureState.Enabled)
    {
      return orig(inv, context, slot);
    }

    bool shiftInUse = ItemSlot.ShiftInUse;
    bool physicalShiftPressed = Main.keyState.PressingShift();

    if (!shiftInUse)
    {
      // Shift is not held (neither physical nor forced), let original handle it.
      return orig(inv, context, slot);
    }

    // Check if our explicit SmartSelect keybind is being pressed.
    // This is the only way users should trigger quick actions in keyboard parity mode.
    bool smartSelectKeybindPressed = IsSmartSelectKeybindPressed();

    if (smartSelectKeybindPressed)
    {
      // User is pressing our SmartSelect keybind (F by default). Allow the action.
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[ShiftSuppression] Allowing action: SmartSelect keybind pressed");
      return orig(inv, context, slot);
    }

    // SmartSelect keybind is NOT pressed. Check if physical Shift triggered this.
    // The game's default keyboard profile maps LeftShift to SmartSelect, which causes
    // Shift to trigger SmartSelect in gamepad UI mode. Block this.
    if (physicalShiftPressed)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[ShiftSuppression] BLOCKING: Physical Shift pressed but SmartSelect keybind not pressed");
      return true;
    }

    // ShiftInUse is true but neither our keybind nor physical Shift is pressed.
    // This could be ShiftForcedOn from some other game mechanism. Allow it.
    global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[ShiftSuppression] Allowing action: ShiftForcedOn from game mechanism");
    return orig(inv, context, slot);
  }

  /// <summary>
  /// Checks if our explicit SmartSelect keybind (InventorySmartSelect) is currently pressed.
  /// Uses both the ModKeybind API and raw keyboard state for reliability.
  /// </summary>
  private static bool IsSmartSelectKeybindPressed()
  {
    ModKeybind? keybind = ControllerParityKeybinds.InventorySmartSelect;
    if (keybind is null)
    {
      return false;
    }

    // Check via ModKeybind API
    if (keybind.Current)
    {
      return true;
    }

    // Fallback: check raw keyboard state for the assigned keys
    return IsKeybindPressedRaw(keybind);
  }

  private static void ForceGamepadUiModeIfNeeded(bool needsUiMode)
  {
    if (IsTextInputActive())
    {
      // Drop back to keyboard input while typing so chat/sign text boxes stay usable.
      PlayerInput.CurrentInputMode = InputMode.Keyboard;
      return;
    }

    if (needsUiMode)
    {
      PlayerInput.CurrentInputMode = InputMode.XBoxGamepadUI;
      return;
    }

    if (KeyboardParityFeatureState.Enabled)
    {
      PlayerInput.CurrentInputMode = InputMode.XBoxGamepad;
    }
  }

  private static void ApplyInventoryVirtualTriggers(bool inventoryUiActive)
  {
    if (!inventoryUiActive || !KeyboardParityFeatureState.Enabled || IsTextInputActive())
    {
      return;
    }

    if (!Main.playerInventory)
    {
      return;
    }

    InjectVirtualTrigger(ControllerParityKeybinds.InventorySelect, TriggerNames.MouseLeft);
    InjectVirtualTrigger(ControllerParityKeybinds.InventorySmartSelect, TriggerNames.SmartSelect);

    InjectVirtualTrigger(ControllerParityKeybinds.InventorySectionPrevious, TriggerNames.HotbarMinus);
    InjectVirtualTrigger(ControllerParityKeybinds.InventorySectionNext, TriggerNames.HotbarPlus);
    InjectVirtualTrigger(ControllerParityKeybinds.InventoryQuickUse, TriggerNames.QuickMount);
  }

  private static void ApplyGlobalVirtualTriggers()
  {
    if (!KeyboardParityFeatureState.Enabled || Main.gameMenu || IsTextInputActive())
    {
      return;
    }

    Player player = Main.LocalPlayer;
    if (player is null || !player.active || player.dead || player.ghost)
    {
      return;
    }

    InjectVirtualTrigger(ControllerParityKeybinds.LockOn, TriggerNames.LockOn);
  }

  private static void ApplyMenuNavigationVirtualTriggers(bool uiModeActive)
  {
    if (!KeyboardParityFeatureState.Enabled || !uiModeActive || IsTextInputActive())
    {
      return;
    }

    if (PlayerInput.CurrentInputMode != InputMode.XBoxGamepadUI)
    {
      return;
    }

    if (!IsModConfigUiActive())
    {
      return;
    }

    KeyboardState state = Main.keyState;
    bool up = state.IsKeyDown(Keys.W);
    bool down = state.IsKeyDown(Keys.S);
    bool left = state.IsKeyDown(Keys.A);
    bool right = state.IsKeyDown(Keys.D);

    Vector2 leftStick = PlayerInput.GamepadThumbstickLeft;
    const float stickThreshold = 0.55f;
    bool stickUp = leftStick.Y < -stickThreshold;
    bool stickDown = leftStick.Y > stickThreshold;

    InjectVirtualTrigger(TriggerNames.MenuUp, up || stickUp);
    InjectVirtualTrigger(TriggerNames.MenuDown, down || stickDown);
    InjectVirtualTrigger(TriggerNames.MenuLeft, left);
    InjectVirtualTrigger(TriggerNames.MenuRight, right);
  }

  private static bool IsModConfigUiActive()
  {
    return IsModConfigUiState(Main.MenuUI?.CurrentState) || IsModConfigUiState(Main.InGameUI?.CurrentState);
  }

  private static bool IsModConfigUiState(UIState? state)
  {
    string? fullName = state?.GetType().FullName;
    if (string.IsNullOrWhiteSpace(fullName))
    {
      return false;
    }

    return fullName.Contains("Terraria.ModLoader.Config.UI.UIModConfig", StringComparison.Ordinal) ||
           fullName.Contains("Terraria.ModLoader.Config.UI.UIModConfigList", StringComparison.Ordinal);
  }

  private static void InjectVirtualTrigger(ModKeybind? keybind, string triggerName)
  {
    if (keybind is null)
    {
      return;
    }

    // Check ModKeybind first, then fall back to raw keyboard state detection
    // This ensures detection works even in gamepad UI mode
    bool isPressed = keybind.Current || IsKeybindPressedRaw(keybind);
    if (!isPressed)
    {
      return;
    }

    TriggersPack pack = PlayerInput.Triggers;
    if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
    {
      return;
    }

    // Use gamepad UI mode when in UI context so the game properly processes the trigger
    InputMode sourceMode = PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI
        ? InputMode.XBoxGamepadUI
        : InputMode.Keyboard;

    bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
    SetTriggerState(pack, triggerName, sourceMode);
    if (!wasHeldLastFrame)
    {
      pack.JustPressed.KeyStatus[triggerName] = true;
      pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
    }
  }

  /// <summary>
  /// Checks if a ModKeybind's assigned keys are pressed using raw keyboard state.
  /// This is a fallback for when ModKeybind.Current doesn't work correctly in gamepad modes.
  /// </summary>
  private static bool IsKeybindPressedRaw(ModKeybind keybind)
  {
    try
    {
      List<string> assignedKeys = keybind.GetAssignedKeys();
      if (assignedKeys is null || assignedKeys.Count == 0)
      {
        return false;
      }

      KeyboardState kbState = Main.keyState;
      foreach (string keyName in assignedKeys)
      {
        if (Enum.TryParse<Keys>(keyName, ignoreCase: true, out Keys key))
        {
          if (kbState.IsKeyDown(key))
          {
            return true;
          }
        }
      }
    }
    catch
    {
      // Ignore errors in fallback detection
    }

    return false;
  }

  private static void InjectVirtualTrigger(string triggerName, bool isHeld)
  {
    if (!isHeld)
    {
      return;
    }

    TriggersPack pack = PlayerInput.Triggers;
    if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
    {
      return;
    }

    // Use gamepad UI mode when in UI context so the game properly processes the trigger
    InputMode sourceMode = PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI
        ? InputMode.XBoxGamepadUI
        : InputMode.Keyboard;

    bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
    SetTriggerState(pack, triggerName, sourceMode);
    if (!wasHeldLastFrame)
    {
      pack.JustPressed.KeyStatus[triggerName] = true;
      pack.JustPressed.LatestInputMode[triggerName] = sourceMode;
    }
  }

  private static void SetTriggerState(TriggersPack pack, string triggerName, InputMode sourceMode)
  {
    pack.Current.KeyStatus[triggerName] = true;
    pack.Current.LatestInputMode[triggerName] = sourceMode;
    pack.JustReleased.KeyStatus[triggerName] = false;
  }


  private static void HandleFeatureToggleHotkey()
  {
    if (Main.keyState.IsKeyDown(Keys.F6) && !Main.oldKeyState.IsKeyDown(Keys.F6))
    {
      KeyboardParityFeatureState.Toggle();
    }
  }

  /// <summary>
  /// Detects when the user activates the housing query button and
  /// immediately triggers a housing check at the player's position.
  /// This replicates the gamepad behavior where pressing X on the housing query button
  /// checks housing viability at the player's current tile location.
  /// </summary>
  private static void CheckHousingQueryOnMouseClick()
  {
    Main? mainInstance = Main.instance;
    if (mainInstance is null)
    {
      return;
    }

    int currentMouseNpcType = mainInstance.mouseNPCType;

    if (Main.gameMenu || !Main.playerInventory)
    {
      _lastMouseNpcType = currentMouseNpcType;
      return;
    }

    // Detect transition into housing query mode (mouseNPCType == 0)
    // mouseNPCType == 0 means "housing query" mode; other values represent NPC indices
    bool justEnteredHousingQueryMode = currentMouseNpcType == 0 && _lastMouseNpcType != 0;
    _lastMouseNpcType = currentMouseNpcType;

    // Skip if actual gamepad hardware triggered this - the native UILinksInitializer
    // handler already does the housing check when X button is pressed on gamepad.
    // We only check for actual gamepad hardware, not the emulated state from keyboard parity.
    if (IsActualGamepadGrapplePressed())
    {
      return;
    }

    // Case 1: User just entered housing query mode (clicked the housing query button)
    if (justEnteredHousingQueryMode)
    {
      TriggerHousingCheckAtPlayerPosition();
      return;
    }

    // Case 2: User is on the housing query button (UILinkPoint 600) and presses
    // an interact key. This allows the user to check housing status using
    // their standard interact key while focused on the button.
    int currentPoint = UILinkPointNavigator.CurrentPoint;
    bool onHousingButton = currentPoint == 600;
    bool onNpcHousingTab = Main.EquipPage == 1;

    if (KeyboardParityFeatureState.Enabled && onNpcHousingTab && onHousingButton)
    {
      TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

      // Check triggers that might be used for interaction
      bool triggerPressed = justPressed.MouseLeft || justPressed.Grapple ||
                           justPressed.SmartSelect || justPressed.MouseRight;

      // Check the mod keybinds directly
      bool keybindPressed = (ControllerParityKeybinds.InventorySelect?.JustPressed ?? false) ||
                           (ControllerParityKeybinds.InventorySmartSelect?.JustPressed ?? false);

      // Check for Enter/Space which are common confirm keys on keyboard
      KeyboardState kbState = Keyboard.GetState();
      bool enterOrSpaceDown = kbState.IsKeyDown(Keys.Enter) || kbState.IsKeyDown(Keys.Space);
      bool enterJustPressed = enterOrSpaceDown && !_wasEnterOrSpaceDown;
      _wasEnterOrSpaceDown = enterOrSpaceDown;

      // Check for actual mouse left button
      bool mouseLeftDown = Main.mouseLeft;
      bool mouseLeftJustPressed = mouseLeftDown && !_wasMouseLeftDown;
      _wasMouseLeftDown = mouseLeftDown;

      // Check for I key directly (the default InventorySelect key)
      bool iKeyDown = kbState.IsKeyDown(Keys.I);
      bool iKeyJustPressed = iKeyDown && !_wasIKeyDown;
      _wasIKeyDown = iKeyDown;

      if (triggerPressed || keybindPressed || enterJustPressed || mouseLeftJustPressed || iKeyJustPressed)
      {
        TriggerHousingCheckAtPlayerPosition();
      }
    }
    else
    {
      // Reset tracking when not on point 600
      _wasEnterOrSpaceDown = false;
      _wasMouseLeftDown = false;
      _wasIKeyDown = false;
    }

    _lastHousingQueryPoint = currentPoint;
  }

  private static bool IsActualGamepadGrapplePressed()
  {
    try
    {
      GamePadState state = GamePad.GetState(PlayerIndex.One);
      if (!state.IsConnected)
      {
        return false;
      }

      // X button on Xbox controller (which maps to Grapple trigger)
      return state.Buttons.X == ButtonState.Pressed;
    }
    catch
    {
      return false;
    }
  }

  private static void TriggerHousingCheckAtPlayerPosition()
  {
    Player player = Main.LocalPlayer;
    if (player is null || !player.active)
    {
      return;
    }

    // Use player's center position, matching gamepad behavior
    // (see UILinksInitializer.cs line ~720: Main.player[Main.myPlayer].Center.ToTileCoordinates())
    Point tilePos = player.Center.ToTileCoordinates();
    int tileX = tilePos.X;
    int tileY = tilePos.Y;

    // Validate tile is in world bounds
    if (!WorldGen.InWorld(tileX, tileY, 1))
    {
      return;
    }

    // Trigger the housing query - this will output the result via Main.NewText,
    // which is hooked by TryAnnounceHousingQuery in InGameNarrationSystem
    WorldGen.MoveTownNPC(tileX, tileY, -1);
  }

  private static void InjectVirtualSticksIntoGamepadInput(ILContext il)
  {
    try
    {
      var cursor = new ILCursor(il);
      int connectionFlagIndex = -1;
      if (cursor.TryGotoNext(
            MoveType.After,
            instr => instr.MatchLdsfld(typeof(Main), nameof(Main.SettingBlockGamepadsEntirely)),
            instr => instr.MatchBrfalse(out _),
            instr => instr.MatchLdcI4(0),
            instr => instr.MatchRet(),
            instr => instr.MatchLdloc(out connectionFlagIndex)))
      {
        cursor.EmitDelegate<Func<bool, bool>>(connected => connected || ShouldEmulateGamepad());
      }
      else
      {
        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn("[KeyboardInputParity] Unable to force controller connection; GamePadInput may short-circuit.");
      }

      cursor = new ILCursor(il);
      if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchStsfld(typeof(PlayerInput), nameof(PlayerInput.GamepadThumbstickRight))))
      {
        cursor.EmitDelegate(InjectVirtualSticksFromKeyboard);
      }
      else
      {
        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn("[KeyboardInputParity] Unable to locate GamepadThumbstickRight assignment for virtual stick injection.");
      }
    }
    catch (Exception ex)
    {
      global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[KeyboardInputParity] Failed to patch GamePadInput for virtual sticks: {ex}");
    }
  }

  private static void InjectVirtualSticksFromKeyboard()
  {
    if (!KeyboardParityFeatureState.Enabled || IsTextInputActive())
    {
      return;
    }

    KeyboardState state = Main.keyState;
    bool movementOverride = TryReadStick(state, Keys.W, Keys.S, Keys.A, Keys.D, out Vector2 movement);

    // When Smart Cursor is off, right stick keys are used for cursor nudge instead.
    bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
    bool aimOverride = false;
    Vector2 aim = Vector2.Zero;
    if (smartCursorActive)
    {
      aimOverride = TryReadStick(ControllerParityKeybinds.RightStickUp, ControllerParityKeybinds.RightStickDown, ControllerParityKeybinds.RightStickLeft, ControllerParityKeybinds.RightStickRight, out aim);
    }

    if (movementOverride)
    {
      ApplyStickInversion(ref movement, PlayerInput.CurrentProfile?.LeftThumbstickInvertX == true, PlayerInput.CurrentProfile?.LeftThumbstickInvertY == true);
      PlayerInput.GamepadThumbstickLeft = movement;
    }

    if (aimOverride)
    {
      ApplyStickInversion(ref aim, PlayerInput.CurrentProfile?.RightThumbstickInvertX == true, PlayerInput.CurrentProfile?.RightThumbstickInvertY == true);
      PlayerInput.GamepadThumbstickRight = aim;
    }

    if (movementOverride || aimOverride || state.IsKeyDown(Keys.Space) || Main.mouseLeft || Main.mouseRight)
    {
      PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Gamepad);
    }
  }

  private static bool TryReadStick(KeyboardState state, Keys up, Keys down, Keys left, Keys right, out Vector2 result)
  {
    float x = 0f;
    float y = 0f;

    if (state.IsKeyDown(up))
    {
      y -= 1f;
    }

    if (state.IsKeyDown(down))
    {
      y += 1f;
    }

    if (state.IsKeyDown(left))
    {
      x -= 1f;
    }

    if (state.IsKeyDown(right))
    {
      x += 1f;
    }

    result = new Vector2(x, y);
    if (result == Vector2.Zero)
    {
      return false;
    }

    result.Normalize();
    return true;
  }

  private static bool TryReadStick(ModKeybind? up, ModKeybind? down, ModKeybind? left, ModKeybind? right, out Vector2 result)
  {
    float x = 0f;
    float y = 0f;

    if (up?.Current == true)
    {
      y -= 1f;
    }

    if (down?.Current == true)
    {
      y += 1f;
    }

    if (left?.Current == true)
    {
      x -= 1f;
    }

    if (right?.Current == true)
    {
      x += 1f;
    }

    result = new Vector2(x, y);
    if (result == Vector2.Zero)
    {
      return false;
    }

    result.Normalize();
    return true;
  }

  private static void ApplyStickInversion(ref Vector2 stick, bool invertX, bool invertY)
  {
    if (invertX)
    {
      stick.X *= -1f;
    }

    if (invertY)
    {
      stick.Y *= -1f;
    }
  }

  private static void OnFeatureToggleStateChanged(bool enabled)
  {
    if (!enabled)
    {
      PlayerInput.SettingsForUI.TryRevertingToMouseMode();
      PlayerInput.GamepadThumbstickLeft = Vector2.Zero;
      PlayerInput.GamepadThumbstickRight = Vector2.Zero;
    }

    string announcement = enabled ? "Controller parity enabled." : "Controller parity disabled.";
    ScreenReaderService.Announce(announcement, force: true);
  }

  private static bool IsTextInputActive()
  {
    if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
    {
      return true;
    }

    return Main.CurrentInputTextTakerOverride is not null;
  }

  private static bool NeedsGamepadUiMode()
  {
    if (!KeyboardParityFeatureState.Enabled && !IsKeyboardInputMode())
    {
      return false;
    }

    if (Main.gameMenu)
    {
      return true;
    }

    Player? player = Main.myPlayer >= 0 ? Main.player[Main.myPlayer] : null;
    if (player is null)
    {
      return false;
    }

    if (Main.playerInventory
        || Main.ingameOptionsWindow
        || IsFancyUiActive()
        || Main.InGuideCraftMenu
        || Main.InReforgeMenu
        || Main.CreativeMenu.Enabled
        || Main.hairWindow
        || Main.clothesWindow)
    {
      return true;
    }

    if (player.talkNPC != -1 || player.sign != -1)
    {
      return true;
    }

    if (player.chest != -1 || Main.npcShop != 0)
    {
      return true;
    }

    if (player.tileEntityAnchor.InUse)
    {
      return true;
    }

    return false;
  }

  private static bool IsFancyUiActive()
  {
    if (Main.MenuUI?.IsVisible ?? false)
    {
      return true;
    }

    return Main.InGameUI?.IsVisible ?? false;
  }

  private static bool IsKeyboardInputMode()
  {
    InputMode mode = PlayerInput.CurrentInputMode;
    return mode == InputMode.Keyboard || mode == InputMode.KeyboardUI;
  }
}
