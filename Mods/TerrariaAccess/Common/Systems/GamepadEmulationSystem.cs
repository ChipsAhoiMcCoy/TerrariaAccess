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
using TerrariaAccess.Common.Systems.InGameNarration;
using TerrariaAccess.Common.Systems.ModBrowser;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
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
    private static string? _lastLoggedInputDebugSignature;

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
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

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
        SmartCursorStateController.ClearSessionState();
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
        GamepadEmulationInputContext context = InputContextResolver.Current;
        if (context == GamepadEmulationInputContext.KeyboardTextInput)
        {
            return false;
        }

        if (forceUi)
        {
            return context == GamepadEmulationInputContext.GamepadUi;
        }

        return context is GamepadEmulationInputContext.GamepadUi
            or GamepadEmulationInputContext.WorldGameplay
            or GamepadEmulationInputContext.NativePhysicalGamepad;
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
            VirtualTriggerService.IsKeybindPressed(keybind))
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
            VirtualTriggerService.IsKeybindPressed(keybind);
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

        SuppressAllShiftTriggers();

        Player? localPlayer = Main.LocalPlayer;
        bool dialogueUiActive = DialogueInputGuard.IsDialogueUiActive(localPlayer);
        bool npcInventoryUiActive = IsNpcInventoryUiActive();

        // Inject housing-relevant triggers early so CheckHousingQueryOnMouseClick can see them.
        // Skip entirely when first letter navigation is active — keys are reserved for searching.
        // Skip when fancy UI is active (mod config, bestiary, etc.) — injecting MouseLeft here
        // causes clicks at the mouse cursor position instead of the focused element.
        if (Main.playerInventory && !IsModConfigUiActive() && !InputContextResolver.IsFancyUiActive()
            && !InputStateHelper.IsTextInputActive() && !FirstLetterNavigationManager.IsEnabled
            && (!dialogueUiActive || npcInventoryUiActive))
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        }
        else if (dialogueUiActive && localPlayer is not null && IsPressed(GamepadEmulationKeybinds.InventorySelect))
        {
            DialogueInputGuard.LogStateIfChanged("GamepadEmulationSystem.PostUpdateInput", localPlayer, "skipped early mouse-left injection");
        }

        _housingQueryHandler?.Update();

        GamepadEmulationInputContext inputContext = InputContextResolver.Current;
        SmartCursorStateController.Update(inputContext, IsSmartCursorReservedForUi());
        ForceInputMode(inputContext);
        ApplyGlobalVirtualTriggers();
        ApplyInventoryVirtualTriggers(inputContext == GamepadEmulationInputContext.GamepadUi);
        ApplyDialogueVirtualTriggers(inputContext == GamepadEmulationInputContext.GamepadUi);
        ApplyMenuNavigationVirtualTriggers(inputContext == GamepadEmulationInputContext.GamepadUi);
        ApplyMainMenuVirtualTriggers();
        SuppressModConfigInventorySelectMouseActivation();
    }

    private static void ForceInputMode(GamepadEmulationInputContext context)
    {
        if (SignInputModeSystem.IsButtonNavigationActive)
        {
            PlayerInput.CurrentInputMode = InputMode.Keyboard;
            PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
            return;
        }

        if (context == GamepadEmulationInputContext.KeyboardTextInput)
        {
            PlayerInput.CurrentInputMode = InputMode.Keyboard;
            PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
            return;
        }

        if (context == GamepadEmulationInputContext.GamepadUi)
        {
            PlayerInput.CurrentInputMode = InputMode.XBoxGamepadUI;
            return;
        }

        if (context == GamepadEmulationInputContext.NativePhysicalGamepad)
        {
            return;
        }

        if (context == GamepadEmulationInputContext.WorldGameplay)
        {
            PlayerInput.CurrentInputMode = InputMode.XBoxGamepad;
            return;
        }
    }

    private static void ApplyGlobalVirtualTriggers()
    {
        if (Main.gameMenu || IsModConfigUiActive() || InputContextResolver.IsFancyUiActive() || InputStateHelper.IsTextInputActive() || DialogueInputGuard.IsDialogueUiActive())
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
        // 3. In sign dialogue, where Tab toggles between text entry and button navigation
        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool skipLockOn = (Main.playerInventory && (FirstLetterNavigationManager.IsEnabled || tabPressed))
            || SearchModeManager.IsRelevantMenu
            || SignInputModeSystem.IsSignOpenForLocalPlayer;
        if (!skipLockOn)
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.LockOn, TriggerNames.LockOn);
        }

        if (!Main.playerInventory)
        {
            VirtualTriggerService.InjectFromKeyboardTrigger(TriggerNames.Jump, TriggerNames.Jump);
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventoryInteract, TriggerNames.MouseRight);
        }

        // SmartSelect: Inject the SmartSelect trigger for in-world auto-tool selection
        // Skip when first letter navigation is active in inventory — keys reserved for searching
        if (!(Main.playerInventory && FirstLetterNavigationManager.IsEnabled))
        {
            VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);
        }

        ApplySmartCursorArrowDpadHotbar();
    }

    private static void ApplySmartCursorArrowDpadHotbar()
    {
        if (Main.playerInventory ||
            InputContextResolver.Current != GamepadEmulationInputContext.WorldGameplay ||
            !GetEffectiveSmartCursorState(ignoreTemporarySuppression: true))
        {
            return;
        }

        InjectArrowDpadHotbarTrigger(GamepadEmulationKeybinds.ArrowUp, TriggerNames.DpadRadial1, "up");
        InjectArrowDpadHotbarTrigger(GamepadEmulationKeybinds.ArrowRight, TriggerNames.DpadRadial2, "right");
        InjectArrowDpadHotbarTrigger(GamepadEmulationKeybinds.ArrowDown, TriggerNames.DpadRadial3, "down");
        InjectArrowDpadHotbarTrigger(GamepadEmulationKeybinds.ArrowLeft, TriggerNames.DpadRadial4, "left");
    }

    private static void InjectArrowDpadHotbarTrigger(ModKeybind? keybind, string triggerName, string direction)
    {
        if (keybind is null || !VirtualTriggerService.IsKeybindPressed(keybind))
        {
            return;
        }

        VirtualTriggerService.InjectFromKeybind(keybind, triggerName);
        LogSmartCursorArrowDpadHotbar(direction, triggerName);
    }

    private static void LogSmartCursorArrowDpadHotbar(string direction, string triggerName)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        string message = $"[InputDebug] SmartCursorArrowDpadHotbar: direction={direction} " +
            $"trigger={triggerName} context={InputContextResolver.Current} " +
            $"inputMode={PlayerInput.CurrentInputMode}";
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    private static bool IsSmartCursorReservedForUi()
    {
        return Main.playerInventory ||
               Main.npcShop != 0 ||
               Main.InGuideCraftMenu ||
               Main.InReforgeMenu ||
               Main.CreativeMenu.Enabled ||
               Main.ingameOptionsWindow ||
               Main.inFancyUI;
    }

    internal static void ApplySmartCursorWantedState(bool enabled)
    {
        SmartCursorStateController.ApplyWantedState(enabled);
    }

    internal static bool TryGetForcedSmartCursorState(out bool enabled)
    {
        return SmartCursorStateController.TryGetForcedState(out enabled);
    }

    internal static bool GetEffectiveSmartCursorState(bool ignoreTemporarySuppression = false)
    {
        return SmartCursorStateController.GetEffectiveState();
    }

    private static void ApplyInventoryVirtualTriggers(bool inventoryUiActive)
    {
        if (!inventoryUiActive || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active || !Main.playerInventory)
        {
            return;
        }

        bool shopInventoryActive = Main.npcShop != 0;
        if (DialogueInputGuard.IsDialogueUiActive(player) && !IsNpcInventoryUiActive())
        {
            if (IsPressed(GamepadEmulationKeybinds.InventorySelect) || IsPressed(GamepadEmulationKeybinds.InventoryQuickUse))
            {
                DialogueInputGuard.LogStateIfChanged("GamepadEmulationSystem.ApplyInventoryVirtualTriggers", player, "suppressed inventory trigger injection during dialogue");
            }

            return;
        }

        // Skip MouseLeft/MouseRight injection when fancy UI is active (mod config, bestiary, etc.)
        // These UIs process clicks at the mouse cursor position, not the focused element,
        // so injecting MouseLeft causes the wrong element to be activated.
        if (IsModConfigUiActive() || InputContextResolver.IsFancyUiActive())
        {
            return;
        }

        // When first letter navigation is active, skip ALL inventory trigger injection.
        // Letter keys are exclusively reserved for item searching in this mode.
        // This blanket approach avoids whack-a-mole suppression of individual keybinds.
        if (FirstLetterNavigationManager.IsEnabled)
        {
            if (shopInventoryActive)
            {
                VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);
            }

            VirtualTriggerService.UpdateTrackingOnly();
            return;
        }

        if (TryHandleFocusedInventoryFavorite(player))
        {
            VirtualTriggerService.UpdateTrackingOnly();
            return;
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        VirtualTriggerService.ApplyMouseLeftFromTrigger();

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.SmartSelect, TriggerNames.SmartSelect);
        TryHandleShopQuickSellFallback(player);

        // Only inject MouseRight if no chest/container is open.
        // When a container is open, continued MouseRight injection can cause it to toggle closed.
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

    private static bool TryHandleFocusedInventoryFavorite(Player player)
    {
        if (!PlayerInput.Triggers.JustPressed.SmartCursor ||
            (Main.mouseItem is not null && !Main.mouseItem.IsAir))
        {
            return false;
        }

        int point = UILinkPointNavigator.CurrentPoint;
        if (!SlotNavigationHelper.TryResolveInventorySlot(point, out int slot, out int context) ||
            !CanFavoriteInventoryContext(context) ||
            (uint)slot >= (uint)player.inventory.Length)
        {
            return false;
        }

        Item item = player.inventory[slot];
        if (item.IsAir)
        {
            return false;
        }

        item.favorited = !item.favorited;
        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
        ConsumeSmartCursorFavoriteTrigger();
        return true;
    }

    private static bool CanFavoriteInventoryContext(int context)
    {
        return context == ItemSlot.Context.InventoryItem ||
               context == ItemSlot.Context.InventoryCoin ||
               context == ItemSlot.Context.InventoryAmmo;
    }

    private static void ConsumeSmartCursorFavoriteTrigger()
    {
        PlayerInput.Triggers.Current.KeyStatus[TriggerNames.SmartCursor] = false;
        PlayerInput.Triggers.JustPressed.KeyStatus[TriggerNames.SmartCursor] = false;
        PlayerInput.Triggers.JustReleased.KeyStatus[TriggerNames.SmartCursor] = false;
        PlayerInput.LockGamepadButtons(TriggerNames.SmartCursor);
        PlayerInput.SettingsForUI.TryRevertingToMouseMode();
    }

    private static void TryHandleShopQuickSellFallback(Player player)
    {
        if (Main.npcShop <= 0 ||
            (Main.mouseItem is not null && !Main.mouseItem.IsAir) ||
            !PlayerInput.Triggers.JustPressed.SmartSelect ||
            !PlayerInput.UsingGamepadUI)
        {
            return;
        }

        Chest[]? shops = Main.instance?.shop;
        if (shops is null || Main.npcShop >= shops.Length || shops[Main.npcShop] is null)
        {
            return;
        }

        int point = UILinkPointNavigator.CurrentPoint;
        if (!SlotNavigationHelper.TryResolveInventorySlot(point, out int slot, out int context))
        {
            return;
        }

        if ((uint)slot >= (uint)player.inventory.Length)
        {
            return;
        }

        Item item = player.inventory[slot];
        if (item.IsAir || item.favorited)
        {
            return;
        }

        string soldLabel = NarrationStringCatalog.ItemLabel(
            TextSanitizer.Clean(item.Name),
            item.stack,
            favorited: false);
        int originalType = item.type;
        int originalStack = item.stack;

        global::TerrariaAccess.Common.Services.NativeSoundSuppression.RunSynchronous(() => ItemSlot.SellOrTrash(player.inventory, context, slot));

        Item remainingItem = player.inventory[slot];
        bool sold = remainingItem.IsAir ||
                    remainingItem.type != originalType ||
                    remainingItem.stack < originalStack;
        if (!sold || (Main.mouseItem is not null && !Main.mouseItem.IsAir))
        {
            return;
        }

        PlayerInput.Triggers.Current.KeyStatus[TriggerNames.SmartSelect] = false;
        PlayerInput.Triggers.JustPressed.KeyStatus[TriggerNames.SmartSelect] = false;
        PlayerInput.LockGamepadButtons(TriggerNames.SmartSelect);

        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
        ScreenReaderService.Announce($"Sold {soldLabel}", force: true);
    }

    private static void ApplyDialogueVirtualTriggers(bool uiModeActive)
    {
        if (SignInputModeSystem.IsButtonNavigationActive)
        {
            return;
        }

        if (!uiModeActive)
        {
            return;
        }

        if (PlayerInput.CurrentInputMode != InputMode.XBoxGamepadUI)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        bool dialogueActive = player.talkNPC != -1 || player.sign != -1;
        if (!dialogueActive)
        {
            return;
        }

        if (Main.playerInventory || IsNpcInventoryUiActive())
        {
            return;
        }

        bool textInputActive = InputStateHelper.IsTextInputActive();
        bool preserveUiDuringTextInput = InputStateHelper.ShouldPreserveGamepadUiDuringTextInput();
        if (textInputActive && !preserveUiDuringTextInput)
        {
            return;
        }

        DialogueInputGuard.ClaimUiInput(player, "GamepadEmulationSystem.ApplyDialogueVirtualTriggers");

        GetVirtualMenuNavigationState(out bool up, out bool down, out bool left, out bool right);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuUp, up);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuDown, down);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuLeft, left);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuRight, right);

        // Preserve keyboard text entry while a sign is being edited. The default confirm
        // binding is a letter key, so injecting MouseLeft here would turn typed text into
        // unintended button presses.
        if (textInputActive)
        {
            return;
        }

        VirtualTriggerService.InjectFromKeybind(GamepadEmulationKeybinds.InventorySelect, TriggerNames.MouseLeft);
        VirtualTriggerService.ApplyMouseLeftFromTrigger();
        if (IsPressed(GamepadEmulationKeybinds.InventorySelect))
        {
            DialogueInputGuard.LogStateIfChanged("GamepadEmulationSystem.ApplyDialogueVirtualTriggers", player, "synthetic confirm injected");
        }
    }

    private static bool IsNpcInventoryUiActive()
    {
        return Main.npcShop != 0 ||
               Main.InGuideCraftMenu ||
               Main.InReforgeMenu;
    }

    private static void ApplyMenuNavigationVirtualTriggers(bool uiModeActive)
    {
        bool textInputActive = InputStateHelper.IsTextInputActive();
        bool preserveUiDuringTextInput = InputStateHelper.ShouldPreserveGamepadUiDuringTextInput();
        if (!uiModeActive || (textInputActive && !preserveUiDuringTextInput))
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

        GetVirtualMenuNavigationState(out bool up, out bool down, out bool left, out bool right);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuUp, up);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuDown, down);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuLeft, left);
        VirtualTriggerService.InjectFromState(TriggerNames.MenuRight, right);
    }

    private static void GetVirtualMenuNavigationState(out bool up, out bool down, out bool left, out bool right)
    {
        KeyboardState state = Main.keyState;
        up = state.IsKeyDown(Keys.W) || IsPressed(GamepadEmulationKeybinds.ArrowUp);
        down = state.IsKeyDown(Keys.S) || IsPressed(GamepadEmulationKeybinds.ArrowDown);
        left = state.IsKeyDown(Keys.A) || IsPressed(GamepadEmulationKeybinds.ArrowLeft);
        right = state.IsKeyDown(Keys.D) || IsPressed(GamepadEmulationKeybinds.ArrowRight);

        Vector2 leftStick = PlayerInput.GamepadThumbstickLeft;
        const float stickThreshold = 0.55f;
        up |= leftStick.Y < -stickThreshold;
        down |= leftStick.Y > stickThreshold;
        left |= leftStick.X < -stickThreshold;
        right |= leftStick.X > stickThreshold;
    }

    private static void SuppressModConfigInventorySelectMouseActivation()
    {
        if (!IsModConfigUiActive() || !IsPressed(GamepadEmulationKeybinds.InventorySelect))
        {
            return;
        }

        PlayerInput.Triggers.Current.KeyStatus[TriggerNames.MouseLeft] = false;
        PlayerInput.Triggers.JustPressed.KeyStatus[TriggerNames.MouseLeft] = false;
        PlayerInput.Triggers.JustReleased.KeyStatus[TriggerNames.MouseLeft] = false;
        Main.mouseLeft = false;
        Main.mouseLeftRelease = false;
        VirtualTriggerService.UpdateTrackingOnly();
    }

    private static bool IsPressed(ModKeybind? keybind)
    {
        return VirtualTriggerService.IsKeybindPressed(keybind);
    }

    /// <summary>
    /// When on the main menu (vanilla or tModLoader), injects MouseLeft from the InventorySelect
    /// keybind so the I key can activate focused menu items just like Enter/Space.
    /// The UILinkPointNavigator already positions the virtual cursor over the focused item,
    /// so setting mouseLeft/mouseLeftRelease triggers the standard selection path.
    /// </summary>
    private static void ApplyMainMenuVirtualTriggers()
    {
        if (!Main.gameMenu || InputStateHelper.IsTextInputActive())
        {
            return;
        }

        if (HairStyleNavigationSystem.ShouldHandleHairStyleConfirm)
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

        bool pressed = IsPressed(selectKeybind);
        bool justPressed = pressed && !_mainMenuSelectWasPressed;
        _mainMenuSelectWasPressed = pressed;

        if (justPressed)
        {
            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
            if (!IsAudioSettingsMenuMode())
            {
                global::TerrariaAccess.Common.Services.NativeSoundSuppression.RequestDeferredSuppressionForCurrentFrame();
            }

            Main.mouseLeft = true;
            Main.mouseLeftRelease = true;
        }
    }

    private static bool _mainMenuSelectWasPressed;

    private const int AudioSettingsMenuMode = 26;

    private static bool IsAudioSettingsMenuMode() => Main.menuMode == AudioSettingsMenuMode;

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
        "Terraria.ModLoader.Config.UI.UIModConfigList",
        "Terraria.ModLoader.Config.UI.UIModConfig",
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

        Player? player = Main.LocalPlayer;
        bool inventoryOpen = Main.playerInventory;
        bool usingGamepadUi = PlayerInput.UsingGamepadUI;
        GamepadEmulationInputContext resolvedContext = InputContextResolver.Current;
        bool emulationEnabled = GamepadEmulationState.Enabled;
        bool textInputActive = InputStateHelper.IsTextInputActive();
        bool physicalGamepadConnected = InputStateHelper.IsPhysicalGamepadConnected();
        bool nativeWorldGamepad = InputStateHelper.ShouldUseNativeGamepadWorldInput();
        int chestIndex = player?.chest ?? -1;
        bool firstLetterNavEnabled = FirstLetterNavigation.FirstLetterNavigationManager.IsEnabled;

        // Get trigger states for key binds
        TriggersPack pack = PlayerInput.Triggers;
        bool mouseLeftActive = pack.Current.MouseLeft;
        bool mouseRightActive = pack.Current.MouseRight;
        bool jumpActive = pack.Current.KeyStatus.TryGetValue(TriggerNames.Jump, out bool jump) && jump;
        bool smartSelectActive = pack.Current.KeyStatus.TryGetValue(TriggerNames.SmartSelect, out bool ss) && ss;
        bool smartCursorTriggerActive = pack.Current.KeyStatus.TryGetValue(TriggerNames.SmartCursor, out bool sc) && sc;
        bool smartCursorPressedRaw = SmartCursorStateController.IsSmartCursorBindingPressedRaw();
        bool smartCursorWantedMouse = Main.SmartCursorWanted_Mouse;
        bool smartCursorWantedGamePad = Main.SmartCursorWanted_GamePad;
        bool smartCursorEffective = Main.SmartCursorIsUsed;
        bool smartCursorDesiredInitialized = SmartCursorStateController.DesiredInitialized;
        bool smartCursorDesired = SmartCursorStateController.DesiredEnabled;
        bool smartCursorSyncPending = SmartCursorStateController.DesiredSyncPending;
        uint smartCursorSyncDeadline = SmartCursorStateController.DesiredSyncDeadline;
        bool smartCursorSuppressed = DpadVirtualizationSystem.IsTemporarilySuppressingSmartCursor();

        string stateSignature =
            $"{currentLinkPoint}|{currentInputMode}|{resolvedContext}|{inventoryOpen}|{usingGamepadUi}|{emulationEnabled}|" +
            $"{textInputActive}|{physicalGamepadConnected}|{nativeWorldGamepad}|{chestIndex}|" +
            $"{firstLetterNavEnabled}|{mouseLeftActive}|{mouseRightActive}|{jumpActive}|{smartSelectActive}|" +
            $"{smartCursorTriggerActive}|{smartCursorPressedRaw}|{smartCursorWantedMouse}|" +
            $"{smartCursorWantedGamePad}|{smartCursorEffective}|{smartCursorDesiredInitialized}|" +
            $"{smartCursorDesired}|{smartCursorSyncPending}|{smartCursorSyncDeadline}|" +
            $"{smartCursorSuppressed}";

        bool stateChanged = stateSignature != _lastLoggedInputDebugSignature;

        string message = $"[InputDebug] {context}: " +
            $"linkPoint={currentLinkPoint} " +
            $"inputMode={currentInputMode} " +
            $"context={resolvedContext} " +
            $"inventory={inventoryOpen} " +
            $"usingGamepadUi={usingGamepadUi} " +
            $"emulation={emulationEnabled} " +
            $"textInput={textInputActive} " +
            $"physicalGamepad={physicalGamepadConnected} " +
            $"nativeWorldGamepad={nativeWorldGamepad} " +
            $"chest={chestIndex} " +
            $"firstLetterNav={firstLetterNavEnabled} " +
            $"mouseL={mouseLeftActive} " +
            $"mouseR={mouseRightActive} " +
            $"jump={jumpActive} " +
            $"smartSelect={smartSelectActive} " +
            $"smartCursorTrigger={smartCursorTriggerActive} " +
            $"smartCursorRaw={smartCursorPressedRaw} " +
            $"smartCursorWantedMouse={smartCursorWantedMouse} " +
            $"smartCursorWantedGamePad={smartCursorWantedGamePad} " +
            $"smartCursorEffective={smartCursorEffective} " +
            $"smartCursorDesiredInit={smartCursorDesiredInitialized} " +
            $"smartCursorDesired={smartCursorDesired} " +
            $"smartCursorSyncPending={smartCursorSyncPending} " +
            $"smartCursorSyncDeadline={smartCursorSyncDeadline} " +
            $"smartCursorSuppressed={smartCursorSuppressed}";

        if (stateChanged)
        {
            _lastLoggedInputDebugSignature = stateSignature;
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
        }
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
