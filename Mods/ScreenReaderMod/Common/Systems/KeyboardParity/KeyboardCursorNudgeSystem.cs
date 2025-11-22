#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Lets keyboard arrow keys nudge the cursor in-game, mirroring the controller D-Pad feedback loop.
/// </summary>
public sealed class KeyboardCursorNudgeSystem : ModSystem
{
    private const float BasePixelsPerFrame = 12f;
    private const float AccelerationPerFrame = 0.8f;
    private const float MaxSpeed = 28f;

    private int _heldFrames;

    public override void PostUpdateInput()
    {
        if (!ShouldProcess())
        {
            _heldFrames = 0;
            return;
        }

        Vector2 direction = ReadArrowDirection(Main.keyState);
        if (direction == Vector2.Zero)
        {
            _heldFrames = 0;
            return;
        }

        _heldFrames++;
        float speed = BasePixelsPerFrame + MathHelper.Min(_heldFrames * AccelerationPerFrame, MaxSpeed - BasePixelsPerFrame);
        direction = Vector2.Normalize(direction);
        if (float.IsNaN(direction.X) || float.IsNaN(direction.Y))
        {
            direction = Vector2.Zero;
        }

        if (direction == Vector2.Zero)
        {
            _heldFrames = 0;
            return;
        }

        Vector2 delta = direction * speed;
        ApplyCursorDelta(delta);
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

        return true;
    }

    private static Vector2 ReadArrowDirection(KeyboardState state)
    {
        Vector2 direction = Vector2.Zero;

        if (state.IsKeyDown(Keys.Up))
        {
            direction.Y -= 1f;
        }

        if (state.IsKeyDown(Keys.Down))
        {
            direction.Y += 1f;
        }

        if (state.IsKeyDown(Keys.Left))
        {
            direction.X -= 1f;
        }

        if (state.IsKeyDown(Keys.Right))
        {
            direction.X += 1f;
        }

        return direction;
    }

    private static void ApplyCursorDelta(Vector2 delta)
    {
        if (delta == Vector2.Zero)
        {
            return;
        }

        int newX = (int)MathHelper.Clamp(PlayerInput.MouseX + delta.X, 0f, Main.screenWidth - 1f);
        int newY = (int)MathHelper.Clamp(PlayerInput.MouseY + delta.Y, 0f, Main.screenHeight - 1f);

        PlayerInput.MouseX = newX;
        PlayerInput.MouseY = newY;
        Main.mouseX = newX;
        Main.mouseY = newY;
        PlayerInput.SettingsForUI.SetCursorMode(CursorMode.Gamepad);
    }
}
