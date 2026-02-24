#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.FirstLetterNavigation;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI.Gamepad;
using Terraria.WorldBuilding;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Handles housing query detection and NPC housing assignment for gamepad emulation mode.
/// Replicates the gamepad behavior where pressing X on the housing query button
/// checks housing viability, or on an NPC icon moves that NPC to the player's location.
/// </summary>
internal sealed class HousingQueryHandler
{
    private int _lastMouseNpcType = -1;
    private int _lastHousingQueryPoint = -1;
    private bool _wasInteractKeyDown;

    /// <summary>
    /// Checks for housing interactions and triggers appropriate actions.
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

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        bool onHousingQueryButton = currentPoint == 600;
        bool onNpcIcon = currentPoint > 600 && currentPoint <= 650;
        bool onNpcHousingTab = Main.EquipPage == 1;

        if (!GamepadEmulationState.Enabled || !onNpcHousingTab)
        {
            ResetKeyTracking();
            _lastHousingQueryPoint = currentPoint;
            return;
        }

        // Check for interact key press (Enter, Space, I, or mod keybind)
        bool interactKeyDown = IsInteractKeyDown();
        bool interactJustPressed = interactKeyDown && !_wasInteractKeyDown;
        _wasInteractKeyDown = interactKeyDown;

        if (onHousingQueryButton)
        {
            // Case 2: On housing query button - check if location is valid for housing
            if (interactJustPressed)
            {
                TriggerHousingCheckAtPlayerPosition();
            }
        }
        else if (onNpcIcon)
        {
            // Case 3: On an NPC icon - move that NPC to player's location
            int hoveredNpcIndex = UILinkPointNavigator.Shortcuts.NPCS_LastHovered;

            if (interactJustPressed && hoveredNpcIndex >= 0)
            {
                TriggerNpcMoveToPlayerPosition(hoveredNpcIndex);
            }
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
        ResetKeyTracking();
    }

    private void ResetKeyTracking()
    {
        _wasInteractKeyDown = false;
    }

    private static bool IsInteractKeyDown()
    {
        // When first letter navigation is active, all keyboard input is reserved for searching.
        if (FirstLetterNavigationManager.IsEnabled)
        {
            return false;
        }

        // Check mod keybind (default: I key)
        if (GamepadEmulationKeybinds.InventorySelect?.Current ?? false)
        {
            return true;
        }

        // Check triggers
        TriggersSet current = PlayerInput.Triggers.Current;
        if (current.MouseLeft || current.Grapple)
        {
            return true;
        }

        // Check keyboard keys (Enter, Space, I)
        KeyboardState kbState = Keyboard.GetState();
        if (kbState.IsKeyDown(Keys.Enter) || kbState.IsKeyDown(Keys.Space) || kbState.IsKeyDown(Keys.I))
        {
            return true;
        }

        // Check actual mouse left button
        if (Main.mouseLeft)
        {
            return true;
        }

        return false;
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
        Point tilePos = player.Center.ToTileCoordinates();
        int tileX = tilePos.X;
        int tileY = tilePos.Y;

        // Validate tile is in world bounds
        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return;
        }

        // Trigger the housing query - MoveTownNPC outputs error messages via Main.NewText,
        // but returns true silently on success. We announce success directly via ScreenReaderService
        // to ensure reliable feedback regardless of how other code paths interact.
        if (WorldGen.MoveTownNPC(tileX, tileY, -1))
        {
            // Housing is valid - announce success directly
            ScreenReaderService.Announce(Lang.inter[39].Value, force: true);
        }

        // Play tick sound to match native gamepad behavior
        SoundEngine.PlaySound(SoundID.MenuTick);
    }

    private static void TriggerNpcMoveToPlayerPosition(int npcIndex)
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        Point tilePos = player.Center.ToTileCoordinates();
        int tileX = tilePos.X;
        int tileY = tilePos.Y;

        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return;
        }

        // First validate the location is suitable for housing with this NPC
        // MoveTownNPC returns true if valid, and outputs error messages via Main.NewText if not
        if (WorldGen.MoveTownNPC(tileX, tileY, npcIndex))
        {
            // Location is valid - actually move the NPC
            WorldGen.moveRoom(tileX, tileY, npcIndex);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
    }
}
