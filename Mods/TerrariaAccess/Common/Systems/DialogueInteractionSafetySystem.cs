#nullable enable
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Prevents sign-like interactions from leaking into grapple usage during the same update tick.
/// </summary>
public sealed class DialogueInteractionSafetySystem : ModSystem
{
    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Player.QuickGrapple += HandleQuickGrapple;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Player.QuickGrapple -= HandleQuickGrapple;
    }

    private static void HandleQuickGrapple(On_Player.orig_QuickGrapple orig, Player self)
    {
        if (self.whoAmI == Main.myPlayer && ShouldSuppressQuickGrapple(self))
        {
            DialogueInputGuard.SuppressGameplayTriggersEarly(
                self,
                PlayerInput.Triggers.Current,
                "DialogueInteractionSafetySystem.HandleQuickGrapple");
            DialogueInputGuard.ClaimUiInput(self, "DialogueInteractionSafetySystem.HandleQuickGrapple");
            self.GamepadEnableGrappleCooldown();
            return;
        }

        orig(self);
    }

    private static bool ShouldSuppressQuickGrapple(Player player)
    {
        return DialogueInputGuard.IsDialogueUiActive(player) || IsTargetingSignLikeTile(player);
    }

    private static bool IsTargetingSignLikeTile(Player player)
    {
        if (Main.signHover != -1)
        {
            return true;
        }

        int tileX = Player.tileTargetX;
        int tileY = Player.tileTargetY;
        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return false;
        }

        Tile tile = Main.tile[tileX, tileY];
        return tile.HasTile && Main.tileSign[tile.TileType];
    }
}
