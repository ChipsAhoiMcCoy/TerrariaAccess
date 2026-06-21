#nullable enable
using TerrariaAccess.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

public sealed class NpcDialogueInputPlayer : ModPlayer
{
    public override void PreUpdate()
    {
        if (Player.whoAmI != Main.myPlayer || !DialogueInputGuard.IsDialogueUiActive(Player))
        {
            return;
        }

        DialogueInputGuard.SuppressGameplayTriggersEarly(
            Player,
            PlayerInput.Triggers.Current,
            "NpcDialogueInputPlayer.PreUpdate");
        DialogueInputGuard.ClaimUiInput(Player, "NpcDialogueInputPlayer.PreUpdate");
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        NpcDialogueInputTracker.RecordNavigation(triggersSet);

        if (!DialogueInputGuard.IsDialogueUiActive(Player))
        {
            return;
        }

        DialogueInputGuard.SuppressGameplayTriggersEarly(
            Player,
            triggersSet,
            "NpcDialogueInputPlayer.ProcessTriggers");
    }
}
