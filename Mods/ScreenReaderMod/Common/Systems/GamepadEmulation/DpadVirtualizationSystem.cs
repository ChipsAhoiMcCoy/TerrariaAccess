#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.GamepadEmulation;

/// <summary>
/// Virtualizes the right stick keys as a D-pad for tile-by-tile cursor movement when Smart Cursor is off.
/// When Smart Cursor is on, these keys act as analog stick input instead.
/// </summary>
public sealed class DpadVirtualizationSystem : ModSystem
{
    private const int DefaultRepeatDelayFrames = 6;
    private const float TileSizePixels = 16f;

    private static uint _lastDpadHeldFrame = uint.MaxValue;

    private readonly int[] _directionCooldowns = new int[4];

    public override void PostUpdateInput()
    {
        if (!ShouldProcess())
        {
            ResetCooldowns();
            _lastDpadHeldFrame = uint.MaxValue;
            return;
        }

        Vector2 nudges = CollectDpadNudges();
        if (nudges == Vector2.Zero)
        {
            return;
        }

        ApplyDpadStyleSnap(nudges);
    }

    private static bool ShouldProcess()
    {
        if (!GamepadEmulationState.Enabled)
        {
            return false;
        }

        if (Main.dedServ || Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return false;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return false;
        }

        if (player.dead || player.ghost)
        {
            return false;
        }

        if (Main.playerInventory
            || Main.ingameOptionsWindow
            || (Main.InGameUI?.IsVisible ?? false)
            || (Main.MenuUI?.IsVisible ?? false)
            || player.talkNPC != -1
            || player.sign != -1
            || player.chest != -1
            || Main.npcShop != 0
            || player.tileEntityAnchor.InUse
            || Main.CreativeMenu.Enabled)
        {
            return false;
        }

        return true;
    }

    private Vector2 CollectDpadNudges()
    {
        // D-pad virtualization uses different keys based on Smart Cursor state:
        // - Smart Cursor OFF: OKLS keys act as D-pad (arrow keys act as analog stick)
        // - Smart Cursor ON: Arrow keys act as D-pad (OKLS keys act as analog stick)
        bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;

        Vector2 nudges = Vector2.Zero;
        bool up, right, down, left;

        if (smartCursorActive)
        {
            // Arrow keys act as D-pad when Smart Cursor is ON (inverse of OKLS)
            up = IsPressed(GamepadEmulationKeybinds.ArrowUp);
            right = IsPressed(GamepadEmulationKeybinds.ArrowRight);
            down = IsPressed(GamepadEmulationKeybinds.ArrowDown);
            left = IsPressed(GamepadEmulationKeybinds.ArrowLeft);
        }
        else
        {
            // OKLS keys act as D-pad when Smart Cursor is OFF
            up = IsPressed(GamepadEmulationKeybinds.RightStickUp);
            right = IsPressed(GamepadEmulationKeybinds.RightStickRight);
            down = IsPressed(GamepadEmulationKeybinds.RightStickDown);
            left = IsPressed(GamepadEmulationKeybinds.RightStickLeft);
        }

        nudges += EvaluateDirection(up, -Vector2.UnitY, 0);
        nudges += EvaluateDirection(right, Vector2.UnitX, 1);
        nudges += EvaluateDirection(down, Vector2.UnitY, 2);
        nudges += EvaluateDirection(left, -Vector2.UnitX, 3);

        return nudges;
    }

    private Vector2 EvaluateDirection(bool pressed, Vector2 unit, int index)
    {
        if (_directionCooldowns[index] > 0)
        {
            _directionCooldowns[index]--;
        }

        if (!pressed)
        {
            _directionCooldowns[index] = 0;
            return Vector2.Zero;
        }

        if (_directionCooldowns[index] == 0)
        {
            _directionCooldowns[index] = ResolveRepeatDelay();
            return unit;
        }

        return Vector2.Zero;
    }

    private static int ResolveRepeatDelay()
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return DefaultRepeatDelayFrames;
        }

        Item heldItem = player.inventory[player.selectedItem];
        if (!ItemSlot.IsABuildingItem(heldItem))
        {
            return DefaultRepeatDelayFrames;
        }

        int useTime = CombinedHooks.TotalUseTime(heldItem.useTime, player, heldItem);
        return Math.Max(1, useTime);
    }

    private static bool IsPressed(ModKeybind? keybind)
    {
        return keybind?.Current ?? false;
    }

    private static void ApplyDpadStyleSnap(Vector2 nudges)
    {
        if (nudges == Vector2.Zero)
        {
            return;
        }

        Main.SmartCursorWanted_GamePad = false;
        Matrix zoomMatrix = Main.GameViewMatrix.ZoomMatrix;
        Matrix inverseZoom = Matrix.Invert(zoomMatrix);
        Vector2 tileTarget = Vector2.Transform(Main.MouseScreen, inverseZoom) + nudges * new Vector2(TileSizePixels) + Main.screenPosition;
        Point targetTile = ClampToPlacementReach(tileTarget.ToTileCoordinates());
        Vector2 snappedPixels = Vector2.Transform(targetTile.ToWorldCoordinates() - Main.screenPosition, zoomMatrix);

        int newX = (int)snappedPixels.X;
        int newY = (int)snappedPixels.Y;

        // Only register D-pad input and apply position if cursor actually moved
        if (newX == Main.mouseX && newY == Main.mouseY)
        {
            return;
        }

        RegisterDpadHeldFrame();
        ApplyCursorPosition(newX, newY);
    }

    private static void ApplyCursorPosition(int x, int y)
    {
        int clampedX = (int)MathHelper.Clamp(x, 0f, Main.screenWidth - 1f);
        int clampedY = (int)MathHelper.Clamp(y, 0f, Main.screenHeight - 1f);

        PlayerInput.MouseX = clampedX;
        PlayerInput.MouseY = clampedY;
        Main.mouseX = clampedX;
        Main.mouseY = clampedY;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Gamepad);
    }

    /// <summary>
    /// Returns true if any D-pad virtualization key was held this frame.
    /// Used by narration systems to detect cursor movement input.
    /// </summary>
    internal static bool WasDpadHeldThisFrame()
    {
        return _lastDpadHeldFrame == Main.GameUpdateCount;
    }

    /// <summary>
    /// Returns true if any virtual D-pad key is currently held.
    /// Used by narration systems to detect D-pad input mode (vs analog stick).
    /// </summary>
    internal static bool AreDpadKeysHeld()
    {
        // D-pad keys vary based on Smart Cursor state:
        // - Smart Cursor OFF: OKLS keys act as D-pad
        // - Smart Cursor ON: Arrow keys act as D-pad
        bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;

        if (smartCursorActive)
        {
            // Arrow keys act as D-pad when Smart Cursor is ON
            return IsPressed(GamepadEmulationKeybinds.ArrowUp)
                || IsPressed(GamepadEmulationKeybinds.ArrowDown)
                || IsPressed(GamepadEmulationKeybinds.ArrowLeft)
                || IsPressed(GamepadEmulationKeybinds.ArrowRight);
        }

        // OKLS keys act as D-pad when Smart Cursor is OFF
        return IsPressed(GamepadEmulationKeybinds.RightStickUp)
            || IsPressed(GamepadEmulationKeybinds.RightStickDown)
            || IsPressed(GamepadEmulationKeybinds.RightStickLeft)
            || IsPressed(GamepadEmulationKeybinds.RightStickRight);
    }

    private static void RegisterDpadHeldFrame()
    {
        _lastDpadHeldFrame = Main.GameUpdateCount;
    }

    private static Point ClampToPlacementReach(Point tileTarget)
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return tileTarget;
        }

        Item heldItem = player.inventory[player.selectedItem];
        if (!ItemSlot.IsABuildingItem(heldItem))
        {
            return tileTarget;
        }

        int tileBoost = heldItem.tileBoost;
        int blockRange = player.blockRange;

        float left = player.position.X / 16f - Player.tileRangeX - tileBoost - blockRange;
        float right = (player.position.X + player.width) / 16f + Player.tileRangeX + tileBoost - 1f + blockRange;
        float top = player.position.Y / 16f - Player.tileRangeY - tileBoost - blockRange;
        float bottom = (player.position.Y + player.height) / 16f + Player.tileRangeY + tileBoost - 2f + blockRange;

        int clampedX = (int)MathHelper.Clamp(tileTarget.X, left, right);
        int clampedY = (int)MathHelper.Clamp(tileTarget.Y, top, bottom);
        return new Point(clampedX, clampedY);
    }

    private void ResetCooldowns()
    {
        for (int i = 0; i < _directionCooldowns.Length; i++)
        {
            _directionCooldowns[i] = 0;
        }
    }
}
