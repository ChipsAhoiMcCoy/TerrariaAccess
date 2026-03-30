#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Systems;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

/// <summary>
/// ModPlayer that handles navigation input for the accessible wire color menu.
/// Menu opening is handled by WireColorMenuSystem hook during Draw cycle.
/// </summary>
public sealed class WireColorMenuPlayer : ModPlayer
{
    private bool _wasUpPressed;
    private bool _wasDownPressed;
    private bool _wasEnterPressed;
    private bool _wasEscapePressed;
    private bool _wasSpacePressed;
    private bool _wasConfirmPressed;
    private bool _wasCancelPressed;
    private bool _wasInventorySelectPressed;
    private bool _wasInventoryInteractPressed;

    // Track if we need to suppress interactions after menu closes
    private bool _suppressUntilButtonRelease;

    public override void PreUpdate()
    {
        if (Main.gameMenu || Main.dedServ)
        {
            return;
        }

        AccessibleWireColorMenu menu = AccessibleWireColorMenu.Instance;

        if (menu.IsOpen)
        {
            // Handle navigation input directly from keyboard state
            HandleNavigationInput(menu);

            // Block all player controls while menu is open
            SuppressAllPlayerControls();

            // Check close conditions
            CheckCloseConditions(menu);
        }
        else
        {
            // After menu closes, continue suppressing until close buttons are released
            // This prevents B button from triggering NPC interaction immediately
            if (_suppressUntilButtonRelease)
            {
                bool stillHoldingCloseButton = IsCloseButtonHeld();
                if (stillHoldingCloseButton)
                {
                    // Keep suppressing interactions while close button is held
                    SuppressInteractionControls();
                }
                else
                {
                    // Close button released, stop suppressing
                    _suppressUntilButtonRelease = false;
                    ResetInputTracking();
                }
            }
            else
            {
                // Reset input tracking when menu is closed
                ResetInputTracking();
            }
        }
    }

    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        AccessibleWireColorMenu.Instance.ForceClose();
        ResetInputTracking();
    }

    public override void OnEnterWorld()
    {
        AccessibleWireColorMenu.Instance.ForceClose();
        ResetInputTracking();
    }

    private void ResetInputTracking()
    {
        _wasUpPressed = false;
        _wasDownPressed = false;
        _wasEnterPressed = false;
        _wasEscapePressed = false;
        _wasSpacePressed = false;
        _wasConfirmPressed = false;
        _wasCancelPressed = false;
        _wasInventorySelectPressed = false;
        _wasInventoryInteractPressed = false;
        _suppressUntilButtonRelease = false;
    }

    private bool IsCloseButtonHeld()
    {
        // Check keyboard escape
        if (Main.keyState.IsKeyDown(Keys.Escape))
        {
            return true;
        }

        // Check InventoryInteract keybind (P key by default)
        if (GamepadEmulationKeybinds.InventoryInteract is { } interactKeybind)
        {
            if (VirtualTriggerService.IsKeybindPressed(interactKeybind))
            {
                return true;
            }
        }

        // Check gamepad B button
        try
        {
            GamePadState gamepadState = GamePad.GetState(PlayerIndex.One);
            if (gamepadState.IsConnected && gamepadState.Buttons.B == ButtonState.Pressed)
            {
                return true;
            }
        }
        catch
        {
            // Ignore gamepad access failures
        }

        return false;
    }

    private void SuppressInteractionControls()
    {
        // Only suppress interaction-related controls, not movement
        Player.controlUseTile = false;
        Player.controlUseItem = false;
        Player.controlInv = false;
    }

    private void CheckCloseConditions(AccessibleWireColorMenu menu)
    {
        // Close menu if player swaps away from wire tool
        if (!WiresUI.Settings.DrawToolModeUI)
        {
            menu.Close();
            return;
        }

        // Close menu if inventory opens
        if (Main.playerInventory)
        {
            menu.Close();
            return;
        }
    }

    private void SuppressAllPlayerControls()
    {
        // Suppress movement
        Player.controlLeft = false;
        Player.controlRight = false;
        Player.controlUp = false;
        Player.controlDown = false;
        Player.controlJump = false;

        // Suppress actions
        Player.controlUseItem = false;
        Player.controlUseTile = false;
        Player.controlThrow = false;
        Player.controlHook = false;
        Player.controlMount = false;
        Player.controlTorch = false;
        Player.controlSmart = false;
        Player.controlQuickHeal = false;
        Player.controlQuickMana = false;

        // Suppress inventory/UI controls
        Player.controlInv = false;
        Player.controlMap = false;

        // Block at the input level too
        Main.blockInput = true;
    }

    private void HandleNavigationInput(AccessibleWireColorMenu menu)
    {
        // Read directly from keyboard state - this works even when blockInput is true
        KeyboardState keyState = Main.keyState;

        // Up navigation (W or Up Arrow)
        bool upPressed = keyState.IsKeyDown(Keys.Up) || keyState.IsKeyDown(Keys.W);

        // Down navigation (S or Down Arrow)
        bool downPressed = keyState.IsKeyDown(Keys.Down) || keyState.IsKeyDown(Keys.S);

        // Enter to toggle
        bool enterPressed = keyState.IsKeyDown(Keys.Enter);
        bool enterJustPressed = enterPressed && !_wasEnterPressed;
        _wasEnterPressed = enterPressed;

        // Space to toggle (alternative)
        bool spacePressed = keyState.IsKeyDown(Keys.Space);
        bool spaceJustPressed = spacePressed && !_wasSpacePressed;
        _wasSpacePressed = spacePressed;

        // InventorySelect keybind (I key by default) for gamepad emulation
        bool inventorySelectPressed = false;
        if (GamepadEmulationKeybinds.InventorySelect is { } selectKeybind)
        {
            inventorySelectPressed = VirtualTriggerService.IsKeybindPressed(selectKeybind);
        }
        bool inventorySelectJustPressed = inventorySelectPressed && !_wasInventorySelectPressed;
        _wasInventorySelectPressed = inventorySelectPressed;

        // Escape to close
        bool escapePressed = keyState.IsKeyDown(Keys.Escape);
        bool escapeJustPressed = escapePressed && !_wasEscapePressed;
        _wasEscapePressed = escapePressed;

        // InventoryInteract keybind (P key by default) to close menu
        bool inventoryInteractPressed = false;
        if (GamepadEmulationKeybinds.InventoryInteract is { } interactKeybind)
        {
            inventoryInteractPressed = VirtualTriggerService.IsKeybindPressed(interactKeybind);
        }
        bool inventoryInteractJustPressed = inventoryInteractPressed && !_wasInventoryInteractPressed;
        _wasInventoryInteractPressed = inventoryInteractPressed;

        // Read gamepad input directly from hardware state - bypasses blockInput
        bool gamepadUp = false;
        bool gamepadDown = false;
        bool gamepadConfirm = false;
        bool gamepadCancel = false;

        try
        {
            GamePadState gamepadState = GamePad.GetState(PlayerIndex.One);
            if (gamepadState.IsConnected)
            {
                // D-pad navigation
                gamepadUp = gamepadState.DPad.Up == ButtonState.Pressed;
                gamepadDown = gamepadState.DPad.Down == ButtonState.Pressed;

                // Also check left thumbstick for navigation
                float leftY = gamepadState.ThumbSticks.Left.Y;
                if (leftY > 0.5f)
                {
                    gamepadUp = true;
                }
                else if (leftY < -0.5f)
                {
                    gamepadDown = true;
                }

                // A button to confirm, B button to cancel
                gamepadConfirm = gamepadState.Buttons.A == ButtonState.Pressed;
                gamepadCancel = gamepadState.Buttons.B == ButtonState.Pressed;
            }
        }
        catch
        {
            // Ignore gamepad access failures
        }

        // Combine keyboard and gamepad for up/down
        bool combinedUp = upPressed || gamepadUp;
        bool combinedDown = downPressed || gamepadDown;

        bool upJustPressed = combinedUp && !_wasUpPressed;
        bool downJustPressed = combinedDown && !_wasDownPressed;

        // Handle confirm (gamepad A)
        bool confirmJustPressed = gamepadConfirm && !_wasConfirmPressed;
        _wasConfirmPressed = gamepadConfirm;

        // Handle cancel (gamepad B)
        bool cancelJustPressed = gamepadCancel && !_wasCancelPressed;
        _wasCancelPressed = gamepadCancel;

        if (upJustPressed)
        {
            menu.NavigateUp();
        }
        else if (downJustPressed)
        {
            menu.NavigateDown();
        }

        if (enterJustPressed || spaceJustPressed || confirmJustPressed || inventorySelectJustPressed)
        {
            menu.ToggleSelected();
        }

        if (escapeJustPressed || cancelJustPressed || inventoryInteractJustPressed)
        {
            // Set flag to suppress interactions until button is released
            // This prevents B button from triggering NPC interaction immediately after close
            _suppressUntilButtonRelease = true;
            menu.Close();
        }

        // Update tracking for next frame
        _wasUpPressed = combinedUp;
        _wasDownPressed = combinedDown;
    }
}
