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
using ScreenReaderMod.Common.Systems.GamepadEmulation;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Gives keyboard profiles access to controller-only bindings and unlocks the associated gameplay features.
/// Acts as an orchestrator, delegating to specialized services for different responsibilities.
/// </summary>
public sealed class GamepadEmulationSystem : ModSystem
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
    private static ILHook? _gamepadInputIlHook;
    private static Hook? _shiftInUseHook;

    private static HousingQueryHandler? _housingQueryHandler;


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
        // Force parity on at startup so the game always sees a controller and the virtual sticks/keybinds stay active.
        GamepadEmulationState.SetEnabled(true);
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
        VirtualTriggerService.ResetState();
    }

    #region Hook Creation

    private static Hook? TryCreateHook(MethodInfo? target, Delegate detour, string label)
    {
        if (target is null)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[GamepadEmulation] Cannot hook {label}: missing MethodInfo.");
            return null;
        }

        try
        {
            return new Hook(target, detour);
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[GamepadEmulation] Failed to hook {label}: {ex}");
            return null;
        }
    }

    private static ILHook? TryCreateIlHook(MethodInfo? target, ILContext.Manipulator manipulator, string label)
    {
        if (target is null)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[GamepadEmulation] Cannot patch {label}: missing MethodInfo.");
            return null;
        }

        try
        {
            return new ILHook(target, manipulator);
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch {label}: {ex}");
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
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[GamepadEmulation] Unable to locate UsingGamepad check for {label} fade logic.");
            }
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch {label}: {ex}");
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
        return orig() || InputStateHelper.ShouldEmulateGamepad();
    }

    private static bool OverrideUsingGamepadUi(UsingGamepadGetter orig)
    {
        return orig() || InputStateHelper.ShouldEmulateGamepad();
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
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn("[GamepadEmulation] Unable to force controller connection; GamePadInput may short-circuit.");
            }

            cursor = new ILCursor(il);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchStsfld(typeof(PlayerInput), nameof(PlayerInput.GamepadThumbstickRight))))
            {
                cursor.EmitDelegate(VirtualStickService.InjectFromKeyboard);
            }
            else
            {
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn("[GamepadEmulation] Unable to locate GamepadThumbstickRight assignment for virtual stick injection.");
            }
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Error($"[GamepadEmulation] Failed to patch GamePadInput for virtual sticks: {ex}");
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

    #endregion

    #region Input Update

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        HandleFeatureToggleHotkey();
        SuppressShiftSmartSelect();

        // Inject housing-relevant triggers early so CheckHousingQueryOnMouseClick can see them.
        if (GamepadEmulationState.Enabled && Main.playerInventory && !InputStateHelper.IsTextInputActive())
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        }

        _housingQueryHandler?.Update();

        if (!GamepadEmulationState.Enabled)
        {
            return;
        }

        bool needsUiMode = InputStateHelper.NeedsGamepadUiMode();
        ForceGamepadUiModeIfNeeded(needsUiMode);
        ApplyGlobalVirtualTriggers();
        ApplyInventoryVirtualTriggers(needsUiMode);
        ApplyMenuNavigationVirtualTriggers(needsUiMode);
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
            PlayerInput.CurrentInputMode = InputMode.XBoxGamepad;
        }
    }

    private static void ApplyGlobalVirtualTriggers()
    {
        if (!GamepadEmulationState.Enabled || Main.gameMenu || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active || player.dead || player.ghost)
        {
            return;
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.LockOn, TriggerNames.LockOn);

        // SmartSelect: Inject the SmartSelect trigger for in-world auto-tool selection
        // When pressed, Terraria auto-selects the best tool for the targeted tile
        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);
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

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);

        // SmartSelect: Inject the SmartSelect trigger to mimic gamepad Select button behavior
        // In inventory, this drops held items or performs Shift+Click depending on context
        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);

        // Only inject MouseRight if no chest/container is open.
        // When a container is open, continued MouseRight injection can cause it to toggle closed.
        // The player needs to release the interact key and press again to interact with chest slots.
        Player player = Main.LocalPlayer;
        bool chestOpen = player is not null && (player.chest != -1 || player.tileEntityAnchor.InUse);
        if (!chestOpen)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryInteract, TriggerNames.MouseRight);
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySectionPrevious, TriggerNames.HotbarMinus);
        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySectionNext, TriggerNames.HotbarPlus);

        // Block Grapple trigger when E is used for section cycling to prevent accidental crafting.
        // In vanilla Terraria, E is bound to Grapple, and Grapple triggers crafting in the crafting UI.
        if (GamepadEmulationKeybinds.InventorySectionNext is { } sectionNextKeybind &&
            VirtualTriggerService.IsKeybindPressedRaw(sectionNextKeybind))
        {
            PlayerInput.Triggers.Current.KeyStatus[TriggerNames.Grapple] = false;
            PlayerInput.Triggers.JustPressed.KeyStatus[TriggerNames.Grapple] = false;
        }

        // Quick Use: Simulate right stick click (QuickMount trigger in UI mode)
        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryQuickUse, TriggerNames.QuickMount);

        // Ensure MouseRight trigger from keyboard (Interact key) properly sets Main.mouseRight
        // But skip when a container is open to prevent accidental closure
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
        }

        string key = enabled
            ? "Mods.ScreenReaderMod.GamepadEmulation.Enabled"
            : "Mods.ScreenReaderMod.GamepadEmulation.Disabled";
        string fallback = enabled ? "Gamepad Emulation Enabled" : "Gamepad Emulation Disabled";
        string announcement = LocalizationHelper.GetTextOrFallback(key, fallback);
        ScreenReaderService.Announce(announcement, force: true);
    }

    #endregion
}
