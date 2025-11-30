#nullable enable
using ScreenReaderMod.Common.Systems;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Players;

public sealed class NpcDialogueInputPlayer : ModPlayer
{
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        NpcDialogueInputTracker.RecordNavigation(triggersSet);
    }
}
