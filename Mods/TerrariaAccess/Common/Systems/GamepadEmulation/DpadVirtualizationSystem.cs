#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Players;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.GamepadEmulation;

/// <summary>
/// Virtualizes the right stick keys as a D-pad for tile-by-tile cursor movement when Smart Cursor is off.
/// Arrow keys are reserved for virtual analog cursor movement in unlocked cursor mode, and D-pad hotbar
/// selection while Smart Cursor is on.
/// </summary>
public sealed class DpadVirtualizationSystem : ModSystem
{
    private const int DefaultRepeatDelayFrames = 6;
    private static readonly bool InputDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

    private static uint _lastDpadHeldFrame = uint.MaxValue;
    private static bool _temporarilySuppressedSmartCursor;

    private readonly int[] _directionCooldowns = new int[4];

    public override void PostUpdateInput()
    {
        if (!ShouldProcess())
        {
            RestoreSmartCursorWantedStateIfNeeded();
            ResetCooldowns();
            _lastDpadHeldFrame = uint.MaxValue;
            return;
        }

        bool analogMovementApplied = ApplyUnlockedCursorArrowAnalogMovement();
        Vector2 nudges = CollectDpadNudges();
        bool dpadHeld = AreAnyEffectiveDpadDirectionsHeld();
        if (nudges == Vector2.Zero)
        {
            // Keep Smart Cursor suppressed for the full duration of a held D-pad input.
            // Otherwise it flickers back on between repeat-delay frames and the narrator
            // announces repeated mode changes instead of continuous tile movement.
            if (!dpadHeld)
            {
                RestoreSmartCursorWantedStateIfNeeded();
                if (!analogMovementApplied)
                {
                    ClampAnalogStickCursorIfNeeded();
                }
            }

            return;
        }

        ApplyDpadStyleSnap(nudges);
    }

    private static bool ShouldProcess()
    {
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

        return InputContextResolver.Current == GamepadEmulationInputContext.WorldGameplay;
    }

    private Vector2 CollectDpadNudges()
    {
        Vector2 nudges = Vector2.Zero;
        GetEffectiveDpadState(out bool up, out bool right, out bool down, out bool left);

        nudges += EvaluateDirection(up, -Vector2.UnitY, 0);
        nudges += EvaluateDirection(right, Vector2.UnitX, 1);
        nudges += EvaluateDirection(down, Vector2.UnitY, 2);
        nudges += EvaluateDirection(left, -Vector2.UnitX, 3);

        return nudges;
    }

    private static bool AreAnyEffectiveDpadDirectionsHeld()
    {
        GetEffectiveDpadState(out bool up, out bool right, out bool down, out bool left);
        return up || right || down || left;
    }

    private static void GetEffectiveDpadState(out bool up, out bool right, out bool down, out bool left)
    {
        // OKLS is tile snap in unlocked cursor mode. Arrow keys are handled
        // separately as D-pad hotbar input while Smart Cursor is active.
        bool smartCursorActive = GetEffectiveSmartCursorState();

        if (smartCursorActive)
        {
            up = false;
            right = false;
            down = false;
            left = false;
        }
        else
        {
            up = IsPressed(GamepadEmulationKeybinds.RightStickUp);
            right = IsPressed(GamepadEmulationKeybinds.RightStickRight);
            down = IsPressed(GamepadEmulationKeybinds.RightStickDown);
            left = IsPressed(GamepadEmulationKeybinds.RightStickLeft);
        }

        if (!InputStateHelper.ShouldUseNativeGamepadWorldInput())
        {
            AppendPhysicalGamepadDpad(ref up, ref right, ref down, ref left);
        }
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
        return VirtualTriggerService.IsKeybindPressed(keybind);
    }

    private static void ApplyDpadStyleSnap(Vector2 nudges)
    {
        if (nudges == Vector2.Zero)
        {
            return;
        }

        bool smartCursorActive = GetEffectiveSmartCursorState();

        // Keep both cursor wanted flags disabled while D-pad tile nudging is active.
        if (smartCursorActive)
        {
            _temporarilySuppressedSmartCursor = true;
        }

        Main.SmartCursorWanted_Mouse = false;
        Main.SmartCursorWanted_GamePad = false;
        Matrix zoomMatrix = Main.GameViewMatrix.ZoomMatrix;

        Point originTile = ResolveDpadNudgeOriginTile(smartCursorActive);
        Point targetTile = ClampToPlacementReach(new Point(
            originTile.X + Math.Sign(nudges.X),
            originTile.Y + Math.Sign(nudges.Y)));
        Vector2 snappedPixels = Vector2.Transform(targetTile.ToWorldCoordinates() - Main.screenPosition, zoomMatrix);

        int newX = (int)snappedPixels.X;
        int newY = (int)snappedPixels.Y;

        // Only register D-pad input and apply position if cursor actually moved
        if (newX == Main.mouseX && newY == Main.mouseY)
        {
            return;
        }

        LogDpadNudge(nudges, smartCursorActive, originTile, targetTile, newX, newY);
        RegisterDpadHeldFrame();
        ApplyCursorPosition(newX, newY);
    }

    private static Point ResolveDpadNudgeOriginTile(bool smartCursorActive)
    {
        if (smartCursorActive && !_temporarilySuppressedSmartCursor)
        {
            int smartX = Main.SmartCursorX;
            int smartY = Main.SmartCursorY;
            if (smartX >= 0 && smartY >= 0 && WorldGen.InWorld(smartX, smartY, 1))
            {
                return new Point(smartX, smartY);
            }
        }

        Matrix inverseZoom = Matrix.Invert(Main.GameViewMatrix.ZoomMatrix);
        Vector2 cursorWorld = Vector2.Transform(Main.MouseScreen, inverseZoom) + Main.screenPosition;
        return cursorWorld.ToTileCoordinates();
    }

    private static void ApplyCursorPosition(int x, int y)
    {
        int clampedX = (int)MathHelper.Clamp(x, 0f, Main.screenWidth - 1f);
        int clampedY = (int)MathHelper.Clamp(y, 0f, Main.screenHeight - 1f);

        PlayerInput.MouseX = clampedX;
        PlayerInput.MouseY = clampedY;
        Main.mouseX = clampedX;
        Main.mouseY = clampedY;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Mouse);
    }

    private static bool ApplyUnlockedCursorArrowAnalogMovement()
    {
        if (GetEffectiveSmartCursorState())
        {
            return false;
        }

        if (!VirtualStickService.TryReadUnlockedCursorArrowStick(out Vector2 stick))
        {
            return false;
        }

        VirtualStickService.ApplyStickInversion(ref stick,
            PlayerInput.CurrentProfile?.RightThumbstickInvertX == true,
            PlayerInput.CurrentProfile?.RightThumbstickInvertY == true);

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return false;
        }

        Main.SmartCursorWanted_Mouse = false;
        Main.SmartCursorWanted_GamePad = false;

        Vector2 cursorDelta = stick * ResolveUnlockedCursorSpeed(player);
        int newX = PlayerInput.MouseX + (int)cursorDelta.X;
        int newY = PlayerInput.MouseY + (int)cursorDelta.Y;

        if (!IsBuildModeActive())
        {
            ClampUnlockedCursorToGamepadReach(player, ref newX, ref newY);
        }

        LogUnlockedArrowAnalog(stick, cursorDelta, newX, newY);
        ApplyAnalogCursorPosition(newX, newY);
        VirtualStickService.MarkAnalogStickActiveThisFrame();
        return true;
    }

    private static void LogDpadNudge(
        Vector2 nudges,
        bool smartCursorActive,
        Point originTile,
        Point targetTile,
        int newX,
        int newY)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        GetEffectiveDpadState(out bool up, out bool right, out bool down, out bool left);
        string source = smartCursorActive ? "PhysicalDpad" : "OKLS";
        string message = $"[InputDebug] DpadNudge: source={source} context={InputContextResolver.Current} " +
            $"inputMode={PlayerInput.CurrentInputMode} smartCursor={smartCursorActive} " +
            $"keys=up:{up},right:{right},down:{down},left:{left} " +
            $"nudge=({nudges.X:0.##},{nudges.Y:0.##}) " +
            $"originTile=({originTile.X},{originTile.Y}) targetTile=({targetTile.X},{targetTile.Y}) " +
            $"cursor=({Main.mouseX},{Main.mouseY})->({newX},{newY})";
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    private static void LogUnlockedArrowAnalog(Vector2 stick, Vector2 cursorDelta, int newX, int newY)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        KeyboardState keyboard = Main.keyState;
        bool up = IsPressed(GamepadEmulationKeybinds.ArrowUp) || keyboard.IsKeyDown(Keys.Up);
        bool right = IsPressed(GamepadEmulationKeybinds.ArrowRight) || keyboard.IsKeyDown(Keys.Right);
        bool down = IsPressed(GamepadEmulationKeybinds.ArrowDown) || keyboard.IsKeyDown(Keys.Down);
        bool left = IsPressed(GamepadEmulationKeybinds.ArrowLeft) || keyboard.IsKeyDown(Keys.Left);
        string message = $"[InputDebug] UnlockedArrowAnalog: source=ArrowKeys context={InputContextResolver.Current} " +
            $"inputMode={PlayerInput.CurrentInputMode} smartCursor={GetEffectiveSmartCursorState()} " +
            $"keys=up:{up},right:{right},down:{down},left:{left} " +
            $"stick=({stick.X:0.##},{stick.Y:0.##}) delta=({cursorDelta.X:0.##},{cursorDelta.Y:0.##}) " +
            $"cursor=({Main.mouseX},{Main.mouseY})->({newX},{newY})";
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(message);
    }

    private static Vector2 ResolveUnlockedCursorSpeed(Player player)
    {
        float zoom = Main.GameViewMatrix.ZoomMatrix.M11;
        Vector2 speed = new(8f);

        Item heldItem = player.inventory[player.selectedItem];
        if (!heldItem.mech)
        {
            speed += new Vector2(ResolveGamepadItemRange(player, heldItem)) / 4f;
        }

        return speed * zoom;
    }

    private static int ResolveGamepadItemRange(Player player, Item heldItem)
    {
        if (heldItem is null || heldItem.IsAir)
        {
            return 0;
        }

        int range = heldItem.tileBoost;
        if ((uint)heldItem.type < (uint)ItemID.Sets.GamepadExtraRange.Length)
        {
            range += ItemID.Sets.GamepadExtraRange[heldItem.type];
        }

        if (player.yoyoString && (uint)heldItem.type < (uint)ItemID.Sets.Yoyo.Length && ItemID.Sets.Yoyo[heldItem.type])
        {
            range += 5;
        }
        else if (heldItem.createTile < 0 && heldItem.createWall <= 0 && heldItem.shoot > 0)
        {
            range += 10;
        }
        else if (player.controlTorch)
        {
            range++;
        }

        if (heldItem.createWall > 0 || heldItem.createTile > 0 || heldItem.tileWand > 0)
        {
            range += player.blockRange;
        }

        return range;
    }

    private static void ClampUnlockedCursorToGamepadReach(Player player, ref int x, ref int y)
    {
        int range = ResolveGamepadItemRange(player, player.inventory[player.selectedItem]);
        float zoom = Main.GameViewMatrix.ZoomMatrix.M11;
        Point center = Main.ReverseGravitySupport(player.Center - Main.screenPosition).ToPoint();

        int offsetX = x - center.X;
        int offsetY = y - center.Y;

        float heldItemPlacementOffset = 0f;
        Item heldItem = player.HeldItem;
        if (heldItem.createTile >= 0 || heldItem.createWall > 0 || heldItem.tileWand >= 0)
        {
            heldItemPlacementOffset = 0.5f;
        }

        float maxLeft = -((Player.tileRangeX + range) - heldItemPlacementOffset) * 16f * zoom;
        float maxRight = ((Player.tileRangeX + range) - heldItemPlacementOffset) * 16f * zoom;
        float maxUp = -((Player.tileRangeY + range) - heldItemPlacementOffset) * 16f * zoom;
        float maxDown = ((Player.tileRangeY + range) - heldItemPlacementOffset) * 16f * zoom;
        maxUp -= player.height / 16 / 2 * 16;

        offsetX = (int)MathHelper.Clamp(offsetX, maxLeft, maxRight);
        offsetY = (int)MathHelper.Clamp(offsetY, maxUp, maxDown);

        x = offsetX + center.X;
        y = offsetY + center.Y;
    }

    private static void ApplyAnalogCursorPosition(int x, int y)
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
        bool smartCursorActive = GetEffectiveSmartCursorState();

        bool physicalDpadHeld = !InputStateHelper.ShouldUseNativeGamepadWorldInput() && IsPhysicalGamepadDpadHeld();

        if (smartCursorActive)
        {
            return physicalDpadHeld;
        }

        return IsPressed(GamepadEmulationKeybinds.RightStickUp)
            || IsPressed(GamepadEmulationKeybinds.RightStickDown)
            || IsPressed(GamepadEmulationKeybinds.RightStickLeft)
            || IsPressed(GamepadEmulationKeybinds.RightStickRight)
            || physicalDpadHeld;
    }

    internal static bool IsTemporarilySuppressingSmartCursor()
    {
        return _temporarilySuppressedSmartCursor;
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

    /// <summary>
    /// Clamps cursor position to placement reach when using analog stick virtualization
    /// and Build Mode is not active. Prevents extended reach without Build Mode.
    /// </summary>
    private static void ClampAnalogStickCursorIfNeeded()
    {
        if (!VirtualStickService.WasAnalogStickActiveThisFrame())
        {
            return;
        }

        if (IsBuildModeActive())
        {
            return;
        }

        Matrix zoomMatrix = Main.GameViewMatrix.ZoomMatrix;
        Matrix inverseZoom = Matrix.Invert(zoomMatrix);
        Vector2 cursorWorld = Vector2.Transform(Main.MouseScreen, inverseZoom) + Main.screenPosition;
        Point cursorTile = cursorWorld.ToTileCoordinates();

        Point clampedTile = ClampToReach(cursorTile);
        if (clampedTile == cursorTile)
        {
            return;
        }

        Vector2 snappedPixels = Vector2.Transform(
            clampedTile.ToWorldCoordinates() - Main.screenPosition, zoomMatrix);

        int newX = (int)snappedPixels.X;
        int newY = (int)snappedPixels.Y;

        // Skip if cursor position unchanged (prevents oscillation at boundaries)
        if (newX == Main.mouseX && newY == Main.mouseY)
        {
            return;
        }

        ApplyCursorPosition(newX, newY);
    }

    /// <summary>
    /// Clamps a tile coordinate to the player's current placement reach.
    /// Does not include tileBoost since we want to clamp regardless of held item.
    /// </summary>
    private static Point ClampToReach(Point tileTarget)
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return tileTarget;
        }

        int blockRange = player.blockRange;
        float left = player.position.X / 16f - Player.tileRangeX - blockRange;
        float right = (player.position.X + player.width) / 16f + Player.tileRangeX - 1f + blockRange;
        float top = player.position.Y / 16f - Player.tileRangeY - blockRange;
        float bottom = (player.position.Y + player.height) / 16f + Player.tileRangeY - 2f + blockRange;

        int clampedX = (int)MathHelper.Clamp(tileTarget.X, left, right);
        int clampedY = (int)MathHelper.Clamp(tileTarget.Y, top, bottom);
        return new Point(clampedX, clampedY);
    }

    private static bool IsBuildModeActive()
    {
        var buildModePlayer = Main.LocalPlayer?.GetModPlayer<BuildModePlayer>();
        return buildModePlayer?.IsBuildModeActive ?? false;
    }

    private static void RestoreSmartCursorWantedStateIfNeeded()
    {
        if (!_temporarilySuppressedSmartCursor)
        {
            return;
        }

        GamepadEmulationSystem.ApplySmartCursorWantedState(GetEffectiveSmartCursorState());
        _temporarilySuppressedSmartCursor = false;
    }

    private static void AppendPhysicalGamepadDpad(ref bool up, ref bool right, ref bool down, ref bool left)
    {
        try
        {
            GamePadState state = GamePad.GetState(PlayerIndex.One);
            if (!state.IsConnected)
            {
                return;
            }

            up |= state.DPad.Up == ButtonState.Pressed;
            right |= state.DPad.Right == ButtonState.Pressed;
            down |= state.DPad.Down == ButtonState.Pressed;
            left |= state.DPad.Left == ButtonState.Pressed;
        }
        catch
        {
            // Ignore transient controller read failures
        }
    }

    private static bool IsPhysicalGamepadDpadHeld()
    {
        try
        {
            GamePadState state = GamePad.GetState(PlayerIndex.One);
            if (!state.IsConnected)
            {
                return false;
            }

            return state.DPad.Up == ButtonState.Pressed ||
                state.DPad.Right == ButtonState.Pressed ||
                state.DPad.Down == ButtonState.Pressed ||
                state.DPad.Left == ButtonState.Pressed;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetEffectiveSmartCursorState()
    {
        return GamepadEmulationSystem.GetEffectiveSmartCursorState(ignoreTemporarySuppression: true);
    }
}
