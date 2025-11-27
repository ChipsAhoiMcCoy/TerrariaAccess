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
  private static ILHook? _gamepadInputIlHook;

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

    KeyboardParityFeatureState.StateChanged += OnFeatureToggleStateChanged;
    KeyboardParityFeatureState.SetEnabled(false);
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

    _bindsKeyboardField = null;
    _bindsKeyboardUiField = null;
    _createBindingGroupMethod = null;
    _assembleBindPanelsMethod = null;
    _drawRadialCircularMethod = null;
    _drawRadialQuicksMethod = null;
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

    if (!KeyboardParityFeatureState.Enabled)
    {
      return;
    }

    bool needsUiMode = NeedsGamepadUiMode();
    ForceGamepadUiModeIfNeeded(needsUiMode);
    ApplyVirtualTriggers(needsUiMode);
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

  private static void ForceGamepadUiModeIfNeeded(bool needsUiMode)
  {
    if (needsUiMode)
    {
      PlayerInput.CurrentInputMode = InputMode.XBoxGamepadUI;
      return;
    }

    if (KeyboardParityFeatureState.Enabled && !IsTextInputActive())
    {
      PlayerInput.CurrentInputMode = InputMode.XBoxGamepad;
    }
  }

  private static void ApplyVirtualTriggers(bool inventoryUiActive)
  {
    if (!inventoryUiActive || !KeyboardParityFeatureState.Enabled)
    {
      return;
    }

    InjectVirtualTrigger(ControllerParityKeybinds.InventorySmartSelect, TriggerNames.SmartSelect);
    InjectVirtualTrigger(ControllerParityKeybinds.InventorySectionPrevious, TriggerNames.HotbarMinus);
    InjectVirtualTrigger(ControllerParityKeybinds.InventorySectionNext, TriggerNames.HotbarPlus);
  }

  private static void InjectVirtualTrigger(ModKeybind? keybind, string triggerName)
  {
    if (keybind is null || !keybind.Current)
    {
      return;
    }

    TriggersPack pack = PlayerInput.Triggers;
    if (pack.Current.KeyStatus.TryGetValue(triggerName, out bool alreadyActive) && alreadyActive)
    {
      return;
    }

    bool wasHeldLastFrame = pack.Old.KeyStatus.TryGetValue(triggerName, out bool wasHeld) && wasHeld;
    SetTriggerState(pack, triggerName, InputMode.Keyboard);
    if (!wasHeldLastFrame)
    {
      pack.JustPressed.KeyStatus[triggerName] = true;
      pack.JustPressed.LatestInputMode[triggerName] = InputMode.Keyboard;
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

  private static void InjectVirtualSticksIntoGamepadInput(ILContext il)
  {
    try
    {
      var cursor = new ILCursor(il);
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
    bool aimOverride = TryReadStick(state, Keys.O, Keys.L, Keys.K, Keys.OemSemicolon, out Vector2 aim);

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
