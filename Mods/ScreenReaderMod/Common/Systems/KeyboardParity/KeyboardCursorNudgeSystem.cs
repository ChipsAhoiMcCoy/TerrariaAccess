#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Lets keyboard arrow keys nudge the cursor in-game, mirroring the controller D-Pad feedback loop.
/// </summary>
public sealed class KeyboardCursorNudgeSystem : ModSystem
{
    private const int DefaultRepeatDelayFrames = 6;
    private const float TileSizePixels = 16f;

    private static uint _lastArrowHeldFrame = uint.MaxValue;

    private readonly int[] _directionCooldowns = new int[4];

    public override void PostUpdateInput()
    {
        if (!ShouldProcess())
        {
            ResetCooldowns();
            _lastArrowHeldFrame = uint.MaxValue;
            return;
        }

        if (AreArrowKeysHeld())
        {
            RegisterArrowHeldFrame();
        }

        Vector2 nudges = CollectArrowNudges();
        if (nudges == Vector2.Zero)
        {
            return;
        }

        ApplyDpadStyleSnap(nudges);
    }

    private static bool ShouldProcess()
    {
        if (!KeyboardParityFeatureState.Enabled)
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

    private Vector2 CollectArrowNudges()
    {
        Vector2 nudges = Vector2.Zero;

        nudges += EvaluateDirection(IsPressed(KeyboardCursorNudgeKeybinds.Up), -Vector2.UnitY, 0);
        nudges += EvaluateDirection(IsPressed(KeyboardCursorNudgeKeybinds.Right), Vector2.UnitX, 1);
        nudges += EvaluateDirection(IsPressed(KeyboardCursorNudgeKeybinds.Down), Vector2.UnitY, 2);
        nudges += EvaluateDirection(IsPressed(KeyboardCursorNudgeKeybinds.Left), -Vector2.UnitX, 3);

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

        ApplyCursorPosition((int)snappedPixels.X, (int)snappedPixels.Y);
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

    internal static bool WasArrowHeldThisFrame()
    {
        return _lastArrowHeldFrame == Main.GameUpdateCount;
    }

    private static bool AreArrowKeysHeld()
    {
        return IsPressed(KeyboardCursorNudgeKeybinds.Up)
            || IsPressed(KeyboardCursorNudgeKeybinds.Down)
            || IsPressed(KeyboardCursorNudgeKeybinds.Left)
            || IsPressed(KeyboardCursorNudgeKeybinds.Right);
    }

    private static void RegisterArrowHeldFrame()
    {
        _lastArrowHeldFrame = Main.GameUpdateCount;
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
