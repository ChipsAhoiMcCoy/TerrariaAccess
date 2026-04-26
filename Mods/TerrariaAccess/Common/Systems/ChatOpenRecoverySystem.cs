#nullable enable
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
    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        ClearStaleGameplayTextInputOwner();
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
