#nullable enable
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Recovers the vanilla Enter-to-chat path if a text input owner survives after
/// returning to ordinary gameplay.
/// </summary>
public sealed class ChatOpenRecoverySystem : ModSystem
{
    private bool _enterWasPressed;

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        bool enterPressed = Main.keyState.IsKeyDown(Keys.Enter);
        bool enterJustPressed = enterPressed && !_enterWasPressed;
        _enterWasPressed = enterPressed;

        ClearStaleGameplayTextInputOwner();
        RestoreChatOpenReleaseGate(enterJustPressed);
    }

    private static void ClearStaleGameplayTextInputOwner()
    {
        if (Main.CurrentInputTextTakerOverride is null && !PlayerInput.WritingText)
        {
            return;
        }

        if (!IsPlainGameplayContext())
        {
            return;
        }

        Main.CurrentInputTextTakerOverride = null;
        PlayerInput.WritingText = false;
        TerrariaAccess.Instance?.Logger.Info("[ChatOpenRecovery] Cleared stale text input owner during gameplay.");
    }

    private static void RestoreChatOpenReleaseGate(bool enterJustPressed)
    {
        if (!enterJustPressed || Main.chatRelease)
        {
            return;
        }

        if (!CanVanillaOpenGameplayChat())
        {
            return;
        }

        Main.chatRelease = true;
        TerrariaAccess.Instance?.Logger.Info("[ChatOpenRecovery] Restored chatRelease for Enter chat open.");
    }

    private static bool CanVanillaOpenGameplayChat()
    {
        if (!IsPlainGameplayContext())
        {
            return false;
        }

        if (Main.CurrentInputTextTakerOverride is not null)
        {
            return false;
        }

        if (!Main.hasFocus)
        {
            return false;
        }

        if (Main.keyState.IsKeyDown(Keys.LeftAlt) ||
            Main.keyState.IsKeyDown(Keys.RightAlt) ||
            Main.keyState.IsKeyDown(Keys.Escape))
        {
            return false;
        }

        return true;
    }

    private static bool IsPlainGameplayContext()
    {
        if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
        {
            return false;
        }

        if (Main.gameMenu ||
            Main.playerInventory ||
            Main.inFancyUI ||
            Main.ingameOptionsWindow ||
            Main.blockInput)
        {
            return false;
        }

        if (Main.InGameUI?.IsVisible ?? false)
        {
            return false;
        }

        return true;
    }
}
