#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.UI.Gamepad;
using Terraria.WorldBuilding;

namespace ScreenReaderMod.Common.Systems.GamepadEmulation;

/// <summary>
/// Handles housing query detection and triggering for gamepad emulation mode.
/// Replicates the gamepad behavior where pressing X on the housing query button
/// checks housing viability at the player's current tile location.
/// </summary>
internal sealed class HousingQueryHandler
{
    private int _lastMouseNpcType = -1;
    private int _lastHousingQueryPoint = -1;
    private bool _wasEnterOrSpaceDown;
    private bool _wasMouseLeftDown;
    private bool _wasIKeyDown;

    /// <summary>
    /// Checks for housing query button activation and triggers housing check when appropriate.
    /// Should be called during PostUpdateInput.
    /// </summary>
    internal void Update()
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

        if (GamepadEmulationState.Enabled && onNpcHousingTab && onHousingButton)
        {
            TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

            // Check triggers that might be used for interaction
            bool triggerPressed = justPressed.MouseLeft || justPressed.Grapple ||
                                 justPressed.SmartSelect || justPressed.MouseRight;

            // Check the mod keybinds directly
            bool keybindPressed = GamepadEmulationKeybinds.InventorySelect?.JustPressed ?? false;

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

    /// <summary>
    /// Resets all tracking state.
    /// Call this when the feature is disabled or during cleanup.
    /// </summary>
    internal void ResetState()
    {
        _lastMouseNpcType = -1;
        _lastHousingQueryPoint = -1;
        _wasEnterOrSpaceDown = false;
        _wasMouseLeftDown = false;
        _wasIKeyDown = false;
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
}
