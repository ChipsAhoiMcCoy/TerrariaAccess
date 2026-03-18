#nullable enable
using TerrariaAccess.Common.Systems;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

public sealed class NpcDialogueInputPlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        NpcDialogueInputTracker.RecordNavigation(triggersSet);
    }
}
