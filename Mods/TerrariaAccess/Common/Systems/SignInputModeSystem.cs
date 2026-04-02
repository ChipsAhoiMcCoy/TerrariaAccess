#nullable enable
using System;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Lets keyboard users temporarily suspend sign text capture while keeping
/// vanilla sign edit mode active so the Save button remains available.
/// </summary>
public sealed class SignInputModeSystem : ModSystem
{
    private static readonly bool DebugEnabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_DIALOGUE_INPUT")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

    private enum SignButtonSelection
    {
        Save,
        Close
    }

    private static bool _buttonNavigationActive;
    private static bool _tabWasPressed;
    private static bool _wasLeftPressed;
    private static bool _wasRightPressed;
    private static bool _awaitDirectionalNeutral;
    private static bool _wasEnterPressed;
    private static bool _wasSpacePressed;
    private static bool _wasEscapePressed;
    private static bool _wasInventorySelectPressed;
    private static bool _wasInventoryInteractPressed;
    private static int _preDrawMouseX;
    private static int _preDrawMouseY;
    private static int _preDrawPlayerInputMouseX;
    private static int _preDrawPlayerInputMouseY;
    private static SignButtonSelection _selectedButton = SignButtonSelection.Save;

    internal static bool IsSignOpenForLocalPlayer => TryGetLocalActiveSignPlayer(out _);

    internal static bool IsButtonNavigationActive =>
        _buttonNavigationActive &&
        Main.editSign &&
        IsSignOpenForLocalPlayer;

    internal static bool IsTextEntryActive =>
        Main.editSign &&
        IsSignOpenForLocalPlayer &&
        !IsButtonNavigationActive;

    internal static bool IsSaveButtonSelected =>
        IsButtonNavigationActive &&
        _selectedButton == SignButtonSelection.Save;

    internal static bool IsCloseButtonSelected =>
        IsButtonNavigationActive &&
        _selectedButton == SignButtonSelection.Close;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.InputTextSign += HandleInputTextSign;
        On_Main.DrawNPCChatButtons += HandleDrawNpcChatButtons;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.InputTextSign -= HandleInputTextSign;
        On_Main.DrawNPCChatButtons -= HandleDrawNpcChatButtons;
        ResetState();
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        if (!TryGetLocalActiveSignPlayer(out _))
        {
            ResetState();
            return;
        }

        if (!Main.editSign)
        {
            _buttonNavigationActive = false;
        }

        if (IsButtonNavigationActive)
        {
            HandleButtonNavigationInput();
            ClearVisualButtonFocus();
        }
        else
        {
            ClearVisualButtonFocus();
            ResetButtonInputLatch();
        }

        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool tabJustPressed = tabPressed && !_tabWasPressed;
        _tabWasPressed = tabPressed;

        if (!tabJustPressed)
        {
            return;
        }

        ToggleMode();
    }

    private static void HandleInputTextSign(On_Main.orig_InputTextSign orig)
    {
        if (!IsButtonNavigationActive)
        {
            orig();
            return;
        }

        Main.clrInput();
        PlayerInput.WritingText = false;
        Main.inputTextEnter = false;
        Main.inputTextEscape = false;
    }

    private static void HandleDrawNpcChatButtons(On_Main.orig_DrawNPCChatButtons orig, int superColor, Microsoft.Xna.Framework.Color chatColor, int numLines, string focusText, string focusText3)
    {
        if (!IsButtonNavigationActive)
        {
            orig(superColor, chatColor, numLines, focusText, focusText3);
            return;
        }

        ClearVisualButtonFocus();
        ParkMouseAwayFromDialogueButtons();

        try
        {
            orig(superColor, chatColor, numLines, focusText, focusText3);
        }
        finally
        {
            RestoreMouseAfterDialogueDraw();
            ClearVisualButtonFocus();
        }
    }

    private static void ToggleMode()
    {
        if (!TryGetLocalActiveSignPlayer(out _))
        {
            ResetState();
            return;
        }

        if (!Main.editSign)
        {
            EnterTextEntryMode(fromViewingState: true);
            return;
        }

        if (IsButtonNavigationActive)
        {
            EnterTextEntryMode(fromViewingState: false);
            return;
        }

        EnterButtonNavigationMode();
    }

    private static void EnterButtonNavigationMode()
    {
        _buttonNavigationActive = true;
        BlockTabForCurrentFrame();
        Main.inputTextEnter = false;
        Main.inputTextEscape = false;
        PlayerInput.WritingText = false;
        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
        _selectedButton = SignButtonSelection.Save;
        ResetButtonInputLatch();
        _awaitDirectionalNeutral = true;
        ClearVisualButtonFocus();

        LogState("Entered button navigation");

        SoundEngine.PlaySound(SoundID.MenuClose);
        AnnounceSelectedButton(includeModeHint: true);
    }

    private static void EnterTextEntryMode(bool fromViewingState)
    {
        _buttonNavigationActive = false;
        Main.editSign = true;
        BlockTabForCurrentFrame();
        Main.inputTextEnter = false;
        Main.inputTextEscape = false;
        PlayerInput.WritingText = false;
        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
        ClearVisualButtonFocus();
        ResetButtonInputLatch();
        _awaitDirectionalNeutral = false;

        LogState(fromViewingState ? "Entered text entry from view mode" : "Returned to text entry");

        SoundEngine.PlaySound(SoundID.MenuOpen);
        ScreenReaderService.Announce(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.SignInput.TextEditingEnabled",
                fromViewingState
                    ? "Sign text editing. Type the sign text. Press Tab to switch to the buttons."
                    : "Sign text editing resumed. Type the sign text. Press Tab to switch to the buttons."),
            force: true);
    }

    private static void BlockTabForCurrentFrame()
    {
        Main.clrInput();
        Main.blockKey = Keys.Tab.ToString();
    }

    private static void ResetState()
    {
        _buttonNavigationActive = false;
        _tabWasPressed = false;
        _selectedButton = SignButtonSelection.Save;
        _awaitDirectionalNeutral = false;
        ClearVisualButtonFocus();
        ResetButtonInputLatch();
    }

    private static void LogState(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        Player? player = Main.LocalPlayer;
        TerrariaAccess.Instance?.Logger.Info(
            $"[SignInputMode] {message}: frame={Main.GameUpdateCount} " +
            $"editSign={Main.editSign} " +
            $"buttonNavigation={_buttonNavigationActive} " +
            $"inputMode={PlayerInput.CurrentInputMode} " +
            $"linkPoint={UILinkPointNavigator.CurrentPoint} " +
            $"sign={(player?.sign ?? -1)}");
    }

    private static void HandleButtonNavigationInput()
    {
        KeyboardState keyState = Main.keyState;

        bool leftPressed = keyState.IsKeyDown(Keys.Left) ||
                           keyState.IsKeyDown(Keys.A) ||
                           (GamepadEmulationKeybinds.ArrowLeft is { } leftKeybind &&
                            VirtualTriggerService.IsKeybindPressed(leftKeybind));
        bool rightPressed = keyState.IsKeyDown(Keys.Right) ||
                            keyState.IsKeyDown(Keys.D) ||
                            (GamepadEmulationKeybinds.ArrowRight is { } rightKeybind &&
                             VirtualTriggerService.IsKeybindPressed(rightKeybind));

        if (_awaitDirectionalNeutral)
        {
            if (!leftPressed && !rightPressed)
            {
                _awaitDirectionalNeutral = false;
                _wasLeftPressed = false;
                _wasRightPressed = false;
            }
            else
            {
                _wasLeftPressed = leftPressed;
                _wasRightPressed = rightPressed;
            }
        }

        bool enterPressed = keyState.IsKeyDown(Keys.Enter);
        bool enterJustPressed = enterPressed && !_wasEnterPressed;
        _wasEnterPressed = enterPressed;

        bool spacePressed = keyState.IsKeyDown(Keys.Space);
        bool spaceJustPressed = spacePressed && !_wasSpacePressed;
        _wasSpacePressed = spacePressed;

        bool escapePressed = keyState.IsKeyDown(Keys.Escape);
        bool escapeJustPressed = escapePressed && !_wasEscapePressed;
        _wasEscapePressed = escapePressed;

        bool inventorySelectPressed = GamepadEmulationKeybinds.InventorySelect is { } selectKeybind &&
                                      VirtualTriggerService.IsKeybindPressed(selectKeybind);
        bool inventorySelectJustPressed = inventorySelectPressed && !_wasInventorySelectPressed;
        _wasInventorySelectPressed = inventorySelectPressed;

        bool inventoryInteractPressed = GamepadEmulationKeybinds.InventoryInteract is { } interactKeybind &&
                                        VirtualTriggerService.IsKeybindPressed(interactKeybind);
        bool inventoryInteractJustPressed = inventoryInteractPressed && !_wasInventoryInteractPressed;
        _wasInventoryInteractPressed = inventoryInteractPressed;

        bool leftJustPressed = !_awaitDirectionalNeutral && leftPressed && !_wasLeftPressed;
        bool rightJustPressed = !_awaitDirectionalNeutral && rightPressed && !_wasRightPressed;
        _wasLeftPressed = leftPressed;
        _wasRightPressed = rightPressed;

        if (leftJustPressed ^ rightJustPressed)
        {
            bool selectionChanged = leftJustPressed
                ? SetSelectedButton(SignButtonSelection.Save)
                : SetSelectedButton(SignButtonSelection.Close);

            _awaitDirectionalNeutral = true;
            if (selectionChanged)
            {
                AnnounceSelectedButton(includeModeHint: false);
            }
        }

        if (enterJustPressed || spaceJustPressed || inventorySelectJustPressed)
        {
            ActivateSelectedButton();
            return;
        }

        if (escapeJustPressed || inventoryInteractJustPressed)
        {
            _selectedButton = SignButtonSelection.Close;
            ActivateSelectedButton();
        }
    }

    private static bool SetSelectedButton(SignButtonSelection selection)
    {
        if (_selectedButton == selection)
        {
            return false;
        }

        _selectedButton = selection;
        // Sign button navigation bypasses vanilla hover focus to avoid the
        // rapid refocus loop, so we emit the same tick sound here when the
        // logical selection changes.
        SoundEngine.PlaySound(SoundID.MenuTick);
        LogState($"Selected {_selectedButton}");
        return true;
    }

    private static void ActivateSelectedButton()
    {
        LogState($"Activating {_selectedButton}");

        if (_selectedButton == SignButtonSelection.Save)
        {
            Main.SubmitSignText();
        }
        else
        {
            Main.CloseNPCChatOrSign();
        }
    }

    private static void ClearVisualButtonFocus()
    {
        Main.npcChatFocus1 = false;
        Main.npcChatFocus2 = false;
        Main.npcChatFocus3 = false;
        Main.npcChatFocus4 = false;
    }

    private static void AnnounceSelectedButton(bool includeModeHint)
    {
        string label = _selectedButton == SignButtonSelection.Save
            ? Lang.inter[47].Value
            : Lang.inter[52].Value;

        string announcement = _selectedButton == SignButtonSelection.Save
            ? $"{label} button, 1 of 2"
            : $"{label} button, 2 of 2";

        if (includeModeHint)
        {
            string hint = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.SignInput.ButtonNavigationEnabled",
                "Sign button navigation. Save selected. Use left and right or A and D to move between buttons. Press Tab to return to editing.");
            announcement = $"{hint} {announcement}";
        }

        ScreenReaderService.Announce(announcement, force: true);
    }

    private static void ResetButtonInputLatch()
    {
        _wasLeftPressed = false;
        _wasRightPressed = false;
        _awaitDirectionalNeutral = false;
        _wasEnterPressed = false;
        _wasSpacePressed = false;
        _wasEscapePressed = false;
        _wasInventorySelectPressed = false;
        _wasInventoryInteractPressed = false;
    }

    private static void ParkMouseAwayFromDialogueButtons()
    {
        _preDrawMouseX = Main.mouseX;
        _preDrawMouseY = Main.mouseY;
        _preDrawPlayerInputMouseX = PlayerInput.MouseX;
        _preDrawPlayerInputMouseY = PlayerInput.MouseY;

        const int parkedX = -4096;
        const int parkedY = -4096;
        Main.mouseX = parkedX;
        Main.mouseY = parkedY;
        PlayerInput.MouseX = parkedX;
        PlayerInput.MouseY = parkedY;
    }

    private static void RestoreMouseAfterDialogueDraw()
    {
        Main.mouseX = _preDrawMouseX;
        Main.mouseY = _preDrawMouseY;
        PlayerInput.MouseX = _preDrawPlayerInputMouseX;
        PlayerInput.MouseY = _preDrawPlayerInputMouseY;
    }

    private static bool TryGetLocalActiveSignPlayer(out Player? player)
    {
        player = Main.LocalPlayer;
        return player is not null &&
               player.active &&
               player.whoAmI == Main.myPlayer &&
               player.sign >= 0;
    }
}
