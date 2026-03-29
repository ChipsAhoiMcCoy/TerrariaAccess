#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.FirstLetterNavigation;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Systems.ModBrowser;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Gives keyboard profiles access to controller-only bindings and unlocks the associated gameplay features.
/// Acts as an orchestrator, delegating to specialized services for different responsibilities.
/// </summary>
public sealed class GamepadEmulationSystem : ModSystem
{
    private const int ControllerExtrasGroupIndex = 3;

    // Debug logging for input state - enable via SRM_DEBUG_INPUT environment variable
    private static readonly bool InputDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));
    private static int _lastLoggedLinkPoint = -999;
    private static InputMode _lastLoggedInputMode = (InputMode)(-1);

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
    private static ILHook? _gamepadInputIlHook;
    private static Hook? _shiftInUseHook;

    private static HousingQueryHandler? _housingQueryHandler;
    private static bool _smartCursorBindingWasPressed;
    private static bool _smartCursorDesiredEnabled;
    private static bool _smartCursorDesiredInitialized;


    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        EmulationReflectionCache.LogMissingHandles();
        _housingQueryHandler = new HousingQueryHandler();

        _assembleBindPanelsHook = TryCreateHook(EmulationReflectionCache.AssembleBindPanels, ManageControls_AssembleBindPanels, "controls assembly");
        _radialHotbarHook = TryCreateIlHook(EmulationReflectionCache.DrawRadialCircular, AllowKeyboardRadialHotbar, "radial hotbar fade");
        _radialQuickbarHook = TryCreateIlHook(EmulationReflectionCache.DrawRadialQuicks, AllowKeyboardRadialQuickbar, "radial quickbar fade");
        _usingGamepadHook = TryCreateHook(EmulationReflectionCache.UsingGamepadGetter, OverrideUsingGamepad, "PlayerInput.UsingGamepad");
        _usingGamepadUiHook = TryCreateHook(EmulationReflectionCache.UsingGamepadUiGetter, OverrideUsingGamepadUi, "PlayerInput.UsingGamepadUI");
        _gamepadInputIlHook = TryCreateIlHook(EmulationReflectionCache.GamepadInput, InjectVirtualSticksIntoGamepadInput, "PlayerInput.GamePadInput");
        _shiftInUseHook = TryCreateHook(EmulationReflectionCache.ShiftInUseGetter, OverrideShiftInUse, "ItemSlot.ShiftInUse");

        GamepadEmulationState.StateChanged += OnFeatureToggleStateChanged;
        // Restore saved state silently (no announcement on load)
        bool savedState = TerrariaAccessConfig.Instance?.GamepadEmulationEnabled ?? true;
        GamepadEmulationState.SetEnabledSilent(savedState);
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        GamepadEmulationState.StateChanged -= OnFeatureToggleStateChanged;
        GamepadEmulationState.SetEnabled(false);

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
        _shiftInUseHook?.Dispose();
        _shiftInUseHook = null;

        _housingQueryHandler = null;
        _mainMenuSelectWasPressed = false;
        _smartCursorBindingWasPressed = false;
        _smartCursorDesiredInitialized = false;
        VirtualTriggerService.ResetState();
    }

    #region Hook Creation

    private static Hook? TryCreateHook(MethodInfo? target, Delegate detour, string label)
    {
        if (target is null)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[GamepadEmulation] Cannot hook {label}: missing MethodInfo.");
            return null;
        }

        try
        {
            return new Hook(target, detour);
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Error($"[GamepadEmulation] Failed to hook {label}: {ex}");
            return null;
        }
    }

    private static ILHook? TryCreateIlHook(MethodInfo? target, ILContext.Manipulator manipulator, string label)
    {
        if (target is null)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[GamepadEmulation] Cannot patch {label}: missing MethodInfo.");
            return null;
        }

        try
        {
            return new ILHook(target, manipulator);
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch {label}: {ex}");
            return null;
        }
    }

    #endregion

    #region Hook Targets

    private delegate void AssembleBindPanelsDelegate(UIManageControls self);

    private static void ManageControls_AssembleBindPanels(AssembleBindPanelsDelegate orig, UIManageControls self)
    {
        orig(self);

        TryAppendControllerExtras(self, InputMode.Keyboard, EmulationReflectionCache.BindsKeyboard);
        TryAppendControllerExtras(self, InputMode.KeyboardUI, EmulationReflectionCache.BindsKeyboardUi);
    }

    private static void TryAppendControllerExtras(UIManageControls self, InputMode mode, FieldInfo? targetField)
    {
        if (targetField is null || EmulationReflectionCache.CreateBindingGroup is null)
        {
            return;
        }

        if (targetField.GetValue(self) is not List<UIElement> groups)
        {
            return;
        }

        List<string> payload = new(ControllerExclusiveBindingIds);
        if (EmulationReflectionCache.CreateBindingGroup.Invoke(self, new object[] { ControllerExtrasGroupIndex, payload, mode }) is not UIElement group)
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
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[GamepadEmulation] Unable to locate UsingGamepad check for {label} fade logic.");
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch {label}: {ex}");
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

    private delegate bool UsingGamepadGetter();

    private static bool OverrideUsingGamepad(UsingGamepadGetter orig)
    {
        return orig() || ShouldExposeGamepadFlag(forceUi: false);
    }

    private static bool OverrideUsingGamepadUi(UsingGamepadGetter orig)
    {
        return orig() || ShouldExposeGamepadFlag(forceUi: true);
    }

    private static bool ShouldExposeGamepadFlag(bool forceUi)
    {
        if (!GamepadEmulationState.Enabled || InputStateHelper.IsTextInputActive())
        {
            return false;
        }

        // Only report "using gamepad" in UI contexts where we intentionally emulate
        // controller navigation. Keeping this true in-world causes other systems/mods
        // to continuously force XBoxGamepad mode.
        if (forceUi)
        {
            return InputStateHelper.NeedsGamepadUiMode();
        }

        return InputStateHelper.NeedsGamepadUiMode() || IsLockOnContextActive();
    }

    private static bool IsLockOnContextActive()
    {
        bool lockOnEnabled = LockOnHelper.Enabled;
        bool lockOnKeyPressed = GamepadEmulationKeybinds.LockOn is { } lockOnKeybind &&
            (lockOnKeybind.Current || VirtualTriggerService.IsKeybindPressedRaw(lockOnKeybind));

        return lockOnEnabled || lockOnKeyPressed;
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
                cursor.EmitDelegate<Func<bool, bool>>(connected => connected || InputStateHelper.ShouldEmulateGamepad());
            }
            else
            {
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn("[GamepadEmulation] Unable to force controller connection; GamePadInput may short-circuit.");
            }

            cursor = new ILCursor(il);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchStsfld(typeof(PlayerInput), nameof(PlayerInput.GamepadThumbstickRight))))
            {
                cursor.EmitDelegate(VirtualStickService.InjectFromKeyboard);
            }
            else
            {
                global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn("[GamepadEmulation] Unable to locate GamepadThumbstickRight assignment for virtual stick injection.");
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch GamePadInput for virtual sticks: {ex}");
        }
    }

    private delegate bool ShiftInUseGetter();

    private static bool OverrideShiftInUse(ShiftInUseGetter orig)
    {
        // If gamepad emulation is not enabled, use original behavior
        if (!GamepadEmulationState.Enabled)
        {
            return orig();
        }

        // If text input is active, use original behavior (allow normal Shift for typing)
        if (InputStateHelper.IsTextInputActive())
        {
            return orig();
        }

        // Respect ShiftForcedOn - this is set by gamepad X button
        if (ItemSlot.ShiftForcedOn)
        {
            return true;
        }

        // Suppress vanilla keyboard Shift when gamepad emulation is enabled
        // This only affects keyboard Shift, not gamepad (which uses ShiftForcedOn)
        if (Main.keyState.PressingShift())
        {
            return false;
        }

        // Fall through to original behavior for any edge cases
        return orig();
    }


    #endregion

    #region Shift Key Suppression

    /// <summary>
    /// Suppresses the SmartSelect trigger when it's coming from the keyboard Shift key.
    /// Terraria's default keyboard profile maps LeftShift to SmartSelect, but we want
    /// SmartSelect to only be triggered by our F key keybind when gamepad emulation is active.
    /// </summary>
    private static void SuppressShiftSmartSelect()
    {
        if (!GamepadEmulationState.Enabled)
        {
            return;
        }

        if (InputStateHelper.IsTextInputActive())
        {
            return;
        }

        // Only suppress if Shift is being pressed (the unwanted trigger source)
        if (!Main.keyState.PressingShift())
        {
            return;
        }

        // If our SmartSelect keybind (F key) is being pressed, allow the trigger
        if (GamepadEmulationKeybinds.SmartSelect is { } keybind &&
            (keybind.Current || VirtualTriggerService.IsKeybindPressedRaw(keybind)))
        {
            return;
        }

        // Shift is pressed but F is not - suppress the SmartSelect trigger
        TriggersPack pack = PlayerInput.Triggers;
        pack.Current.KeyStatus[TriggerNames.SmartSelect] = false;
        pack.JustPressed.KeyStatus[TriggerNames.SmartSelect] = false;
    }

    /// <summary>
    /// Aggressively suppresses all Shift-based triggers when gamepad emulation is enabled.
    /// This prevents any Shift key bindings from triggering unexpected behavior.
    /// Only the mod's own SmartSelect keybind (F key) is allowed to trigger SmartSelect.
    /// </summary>
    private static void SuppressAllShiftTriggers()
    {
        if (!GamepadEmulationState.Enabled)
        {
            return;
        }

        if (InputStateHelper.IsTextInputActive())
        {
            return;
        }

        if (!Main.keyState.PressingShift())
        {
            return;
        }

        // Get triggers that may have been set by Shift key bindings
        TriggersPack triggerPack = PlayerInput.Triggers;

        // Suppress SmartSelect (unless our F keybind is pressed)
        bool allowSmartSelect = GamepadEmulationKeybinds.SmartSelect is { } keybind &&
            (keybind.Current || VirtualTriggerService.IsKeybindPressedRaw(keybind));
        if (!allowSmartSelect)
        {
            triggerPack.Current.KeyStatus[TriggerNames.SmartSelect] = false;
            triggerPack.JustPressed.KeyStatus[TriggerNames.SmartSelect] = false;
        }

        // Suppress any other triggers that might be inadvertently bound to Shift
        // Check if Shift alone is what's activating these triggers (no other keys involved)
        // By checking if the trigger's latest input mode is Keyboard, we can be more precise
        if (triggerPack.Current.LatestInputMode.TryGetValue(TriggerNames.Inventory, out InputMode invMode) &&
            (invMode == InputMode.Keyboard || invMode == InputMode.KeyboardUI))
        {
            // Only suppress if Escape (the normal Inventory key) is NOT pressed
            if (!Main.keyState.IsKeyDown(Keys.Escape))
            {
                // Suppress inventory trigger if it was somehow activated by Shift
                // This is a safety net for edge cases
                if (triggerPack.Current.Inventory)
                {
                    triggerPack.Current.KeyStatus[TriggerNames.Inventory] = false;
                    triggerPack.JustPressed.KeyStatus[TriggerNames.Inventory] = false;
                }
            }
        }
    }

    #endregion

    #region Input Update

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        LogInputDebugState("PostUpdateInput");

        // Block navigation triggers when first letter navigation is active.
        // This MUST happen before UILinkPointNavigator.Update() reads them.
        FirstLetterNavigationManager.SuppressNavigationTriggers();

        HandleFeatureToggleHotkey();
        SuppressAllShiftTriggers();

        // Inject housing-relevant triggers early so CheckHousingQueryOnMouseClick can see them.
        // Skip entirely when first letter navigation is active — keys are reserved for searching.
        // Skip when fancy UI is active (mod config, bestiary, etc.) — injecting MouseLeft here
        // causes clicks at the mouse cursor position instead of the focused element.
        if (GamepadEmulationState.Enabled && Main.playerInventory && !Main.inFancyUI
            && !InputStateHelper.IsTextInputActive() && !FirstLetterNavigationManager.IsEnabled)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        }

        _housingQueryHandler?.Update();

        if (!GamepadEmulationState.Enabled)
        {
            _smartCursorBindingWasPressed = false;
            _smartCursorDesiredInitialized = false;
            return;
        }

        HandleSmartCursorBinding();

        bool needsUiMode = InputStateHelper.NeedsGamepadUiMode();
        ForceGamepadUiModeIfNeeded(needsUiMode);
        ApplyGlobalVirtualTriggers();
        ApplyInventoryVirtualTriggers(needsUiMode);
        ApplyMenuNavigationVirtualTriggers(needsUiMode);
        ApplyMainMenuVirtualTriggers();
    }

    private static void ForceGamepadUiModeIfNeeded(bool needsUiMode)
    {
        if (InputStateHelper.IsTextInputActive())
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

        if (GamepadEmulationState.Enabled)
        {
            if (IsLockOnContextActive())
            {
                // LockOn toggle path expects gamepad world mode in vanilla.
                // Keep world gamepad mode only while lock-on context is active so TAB remains
                // toggle-based instead of hold-to-aim.
                PlayerInput.CurrentInputMode = InputMode.XBoxGamepad;
                return;
            }

            // Keep gameplay input mode as keyboard. Forcing XBoxGamepad in-world can cause
            // SmartCursor to follow the GamePad wanted flag path, which some mod stacks
            // may continuously set and effectively lock SmartCursor on.
            PlayerInput.CurrentInputMode = InputMode.Keyboard;
            PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
        }
    }

    private static void ApplyGlobalVirtualTriggers()
    {
        if (!GamepadEmulationState.Enabled || Main.gameMenu || Main.inFancyUI || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active || player.dead || player.ghost)
        {
            return;
        }

        // Skip LockOn (Tab) injection when:
        // 1. In inventory and first-letter navigation is active or Tab is pressed
        //    (Tab toggles first-letter nav, not targeting)
        // 2. In a searchable menu (bestiary, mod browser, etc.) where Tab toggles search mode
        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool skipLockOn = (Main.playerInventory && (FirstLetterNavigationManager.IsEnabled || tabPressed))
            || SearchModeManager.IsRelevantMenu;
        if (!skipLockOn)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.LockOn, TriggerNames.LockOn);
        }

        // SmartSelect: Inject the SmartSelect trigger for in-world auto-tool selection
        // Skip when first letter navigation is active in inventory — keys reserved for searching
        if (!(Main.playerInventory && FirstLetterNavigationManager.IsEnabled))
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);
        }

        if (!Main.playerInventory)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryQuickUse, TriggerNames.QuickMount);
        }
    }

    private static void HandleSmartCursorBinding()
    {
        if (InputStateHelper.IsTextInputActive())
        {
            _smartCursorBindingWasPressed = false;
            return;
        }

        EnsureSmartCursorDesiredStateInitialized();
        bool smartCursorPressed = IsSmartCursorBindingPressedRaw();
        if (!smartCursorPressed && !DpadVirtualizationSystem.IsTemporarilySuppressingSmartCursor())
        {
            _smartCursorDesiredEnabled = GetActualSmartCursorState();
        }

        if (smartCursorPressed)
        {
            VirtualTriggerService.InjectFromState("SmartCursor", isHeld: true);
        }

        ApplySmartCursorStateFromBinding(smartCursorPressed);
    }

    private static void ApplySmartCursorStateFromBinding(bool smartCursorPressed)
    {
        // Toggle mode: one key press flips between enabled/disabled.
        if (Main.cSmartCursorModeIsToggleAndNotHold)
        {
            if (smartCursorPressed && !_smartCursorBindingWasPressed)
            {
                _smartCursorDesiredEnabled = !_smartCursorDesiredEnabled;
            }
        }
        else
        {
            // Hold mode: key down enables smart cursor, key up disables it.
            _smartCursorDesiredEnabled = smartCursorPressed;
        }

        _smartCursorBindingWasPressed = smartCursorPressed;
    }

    private static void EnsureSmartCursorDesiredStateInitialized()
    {
        if (_smartCursorDesiredInitialized)
        {
            return;
        }

        _smartCursorDesiredEnabled = GetActualSmartCursorState();
        _smartCursorDesiredInitialized = true;
    }

    private static bool IsSmartCursorBindingPressedRaw()
    {
        return IsVanillaTriggerPressedRaw("SmartCursor");
    }

    internal static void ApplySmartCursorWantedState(bool enabled)
    {
        Main.SmartCursorWanted_Mouse = enabled;
        Main.SmartCursorWanted_GamePad = enabled;
    }

    internal static bool TryGetForcedSmartCursorState(out bool enabled)
    {
        if (!GamepadEmulationState.Enabled || !_smartCursorDesiredInitialized)
        {
            enabled = false;
            return false;
        }

        enabled = _smartCursorDesiredEnabled;
        return true;
    }

    private static bool GetActualSmartCursorState()
    {
        return Main.SmartCursorIsUsed || Main.SmartCursorWanted;
    }

    private static bool IsVanillaTriggerPressedRaw(string triggerName)
    {
        PlayerInputProfile? profile = PlayerInput.CurrentProfile;
        if (profile is null)
        {
            return false;
        }

        return IsVanillaTriggerPressedRaw(profile, InputMode.Keyboard, triggerName) ||
               IsVanillaTriggerPressedRaw(profile, InputMode.KeyboardUI, triggerName);
    }

    private static bool IsVanillaTriggerPressedRaw(PlayerInputProfile profile, InputMode mode, string triggerName)
    {
        if (!profile.InputModes.TryGetValue(mode, out KeyConfiguration? config))
        {
            return false;
        }

        if (!config.KeyStatus.TryGetValue(triggerName, out List<string>? assignments) || assignments is null)
        {
            return false;
        }

        KeyboardState keyState = Main.keyState;
        foreach (string assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment))
            {
                continue;
            }

            if (TryIsMouseBindingPressed(assignment))
            {
                return true;
            }

            if (Enum.TryParse(assignment, ignoreCase: true, out Keys key) && keyState.IsKeyDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsMouseBindingPressed(string assignment)
    {
        return assignment switch
        {
            "Mouse1" => Main.mouseLeft,
            "Mouse2" => Main.mouseRight,
            _ => false,
        };
    }

    private static void ApplyInventoryVirtualTriggers(bool inventoryUiActive)
    {
        if (!inventoryUiActive || !GamepadEmulationState.Enabled || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        if (!Main.playerInventory)
        {
            return;
        }

        // Skip MouseLeft/MouseRight injection when fancy UI is active (mod config, bestiary, etc.)
        // These UIs process clicks at the mouse cursor position, not the focused element,
        // so injecting MouseLeft causes the wrong element to be activated.
        if (Main.inFancyUI)
        {
            return;
        }

        // When first letter navigation is active, skip ALL inventory trigger injection.
        // Letter keys are exclusively reserved for item searching in this mode.
        // This blanket approach avoids whack-a-mole suppression of individual keybinds.
        if (FirstLetterNavigationManager.IsEnabled)
        {
            VirtualTriggerService.UpdateTrackingOnly();
            return;
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        VirtualTriggerService.ApplyMouseLeftFromTrigger();

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);

        // Only inject MouseRight if no chest/container is open.
        // When a container is open, continued MouseRight injection can cause it to toggle closed.
        Player player = Main.LocalPlayer;
        bool chestOpen = player is not null && (player.chest != -1 || player.tileEntityAnchor.InUse);
        if (!chestOpen)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryInteract, TriggerNames.MouseRight);
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySectionPrevious, TriggerNames.HotbarMinus);
        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySectionNext, TriggerNames.HotbarPlus);

        // Block Grapple trigger when E is used for section cycling to prevent accidental crafting.
        if (GamepadEmulationKeybinds.InventorySectionNext is { } sectionNextKeybind &&
            VirtualTriggerService.IsKeybindPressedRaw(sectionNextKeybind))
        {
            PlayerInput.Triggers.Current.KeyStatus[TriggerNames.Grapple] = false;
            PlayerInput.Triggers.JustPressed.KeyStatus[TriggerNames.Grapple] = false;
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryQuickUse, TriggerNames.QuickMount);

        if (!chestOpen)
        {
            VirtualTriggerService.ApplyMouseRightFromTrigger();
        }
    }

    private static void ApplyMenuNavigationVirtualTriggers(bool uiModeActive)
    {
        if (!GamepadEmulationState.Enabled || !uiModeActive || InputStateHelper.IsTextInputActive())
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

        VirtualTriggerService.InjectFromState(TriggerNames.MenuUp, up || stickUp);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuDown, down || stickDown);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuLeft, left);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuRight, right);
    }

    /// <summary>
    /// When on the main menu (vanilla or tModLoader), injects MouseLeft from the InventorySelect
    /// keybind so the I key can activate focused menu items just like Enter/Space.
    /// The UILinkPointNavigator already positions the virtual cursor over the focused item,
    /// so setting mouseLeft/mouseLeftRelease triggers the standard selection path.
    /// </summary>
    private static void ApplyMainMenuVirtualTriggers()
    {
        if (!GamepadEmulationState.Enabled || !Main.gameMenu || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        // Skip menus where our accessibility systems handle the I key action internally
        // (e.g., mod list, mod browser, bestiary). For vanilla menus like player/world select,
        // allow MouseLeft injection so the I key activates the focused item.
        if (Main.MenuUI?.IsVisible == true && IsMenuUiHandledByAccessibilitySystem())
        {
            return;
        }

        ModKeybind? selectKeybind = GamepadEmulationKeybinds.InventorySelect;
        if (selectKeybind is null)
        {
            return;
        }

        bool pressed = selectKeybind.Current || VirtualTriggerService.IsKeybindPressedRaw(selectKeybind);
        bool justPressed = pressed && !_mainMenuSelectWasPressed;
        _mainMenuSelectWasPressed = pressed;

        if (justPressed)
        {
            Main.mouseLeft = true;
            Main.mouseLeftRelease = true;
        }
    }

    private static bool _mainMenuSelectWasPressed;

    /// <summary>
    /// Menu type names where our accessibility systems handle the I key (InventorySelect) action
    /// internally. For these menus, we must NOT inject Main.mouseLeft to avoid double-click.
    /// All other MenuUI states (UICharacterSelect, UIWorldSelect, UIWorkshopHub, etc.) get
    /// the standard MouseLeft injection so the I key activates the focused UILinkPoint.
    /// </summary>
    private static readonly HashSet<string> MenusWithOwnActionHandling = new(StringComparer.Ordinal)
    {
        "Terraria.ModLoader.UI.UIMods",
        "Terraria.ModLoader.UI.ModBrowser.UIModBrowser",
        "Terraria.ModLoader.UI.UIModInfo",
        "Terraria.ModLoader.UI.UIModPacks",
        "Terraria.ModLoader.UI.UIModSources",
        "Terraria.GameContent.UI.States.UIBestiaryTest",
    };

    /// <summary>
    /// Returns true if the current MenuUI state is handled by one of our accessibility systems
    /// that processes the I key (InventorySelect) internally.
    /// </summary>
    private static bool IsMenuUiHandledByAccessibilitySystem()
    {
        string? typeName = Main.MenuUI?.CurrentState?.GetType().FullName;
        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        return MenusWithOwnActionHandling.Contains(typeName);
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

    #endregion

    #region Feature Toggle

    private static void HandleFeatureToggleHotkey()
    {
        if (Main.keyState.IsKeyDown(Keys.F6) && !Main.oldKeyState.IsKeyDown(Keys.F6))
        {
            GamepadEmulationState.Toggle();
        }
    }

    private static void OnFeatureToggleStateChanged(bool enabled)
    {
        if (!enabled)
        {
            PlayerInput.SettingsForUI.TryRevertingToMouseMode();
            VirtualStickService.ResetState();
            _smartCursorBindingWasPressed = false;
            _smartCursorDesiredInitialized = false;
        }

        // Save the state to config for persistence
        if (TerrariaAccessConfig.Instance is { } config)
        {
            config.GamepadEmulationEnabled = enabled;
            config.SaveChanges(silent: true);
        }

        string key = enabled
            ? "Mods.TerrariaAccess.GamepadEmulation.Enabled"
            : "Mods.TerrariaAccess.GamepadEmulation.Disabled";
        string fallback = enabled ? "Gamepad Emulation Enabled" : "Gamepad Emulation Disabled";
        string announcement = LocalizationHelper.GetTextOrFallback(key, fallback);
        ScreenReaderService.Announce(announcement, force: true);
    }

    #endregion

    #region Debug Logging

    /// <summary>
    /// Logs diagnostic information about input state. Enable with SRM_DEBUG_INPUT env var.
    /// Only logs when state changes to avoid log spam.
    /// </summary>
    private static void LogInputDebugState(string context)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        int currentLinkPoint = UILinkPointNavigator.CurrentPoint;
        InputMode currentInputMode = PlayerInput.CurrentInputMode;

        // Only log on state changes
        bool linkPointChanged = currentLinkPoint != _lastLoggedLinkPoint;
        bool inputModeChanged = currentInputMode != _lastLoggedInputMode;

        if (!linkPointChanged && !inputModeChanged)
        {
            return;
        }

        _lastLoggedLinkPoint = currentLinkPoint;
        _lastLoggedInputMode = currentInputMode;

        Player? player = Main.LocalPlayer;
        bool inventoryOpen = Main.playerInventory;
        bool usingGamepadUi = PlayerInput.UsingGamepadUI;
        bool emulationEnabled = GamepadEmulationState.Enabled;
        bool textInputActive = InputStateHelper.IsTextInputActive();
        int chestIndex = player?.chest ?? -1;
        bool firstLetterNavEnabled = FirstLetterNavigation.FirstLetterNavigationManager.IsEnabled;

        // Get trigger states for key binds
        TriggersPack pack = PlayerInput.Triggers;
        bool mouseLeftActive = pack.Current.MouseLeft;
        bool mouseRightActive = pack.Current.MouseRight;
        bool smartSelectActive = pack.Current.KeyStatus.TryGetValue(TriggerNames.SmartSelect, out bool ss) && ss;

        string message = $"[InputDebug] {context}: " +
            $"linkPoint={currentLinkPoint} " +
            $"inputMode={currentInputMode} " +
            $"inventory={inventoryOpen} " +
            $"usingGamepadUi={usingGamepadUi} " +
            $"emulation={emulationEnabled} " +
            $"textInput={textInputActive} " +
            $"chest={chestIndex} " +
            $"firstLetterNav={firstLetterNavEnabled} " +
            $"mouseL={mouseLeftActive} " +
            $"mouseR={mouseRightActive} " +
            $"smartSelect={smartSelectActive}";

        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    /// <summary>
    /// Logs when virtual triggers are injected. Enable with SRM_DEBUG_INPUT env var.
    /// </summary>
    private static void LogTriggerInjection(string triggerName, string source)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        int linkPoint = UILinkPointNavigator.CurrentPoint;
        InputMode mode = PlayerInput.CurrentInputMode;

        string message = $"[InputDebug] TriggerInjected: trigger={triggerName} source={source} linkPoint={linkPoint} mode={mode}";
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    #endregion
}
