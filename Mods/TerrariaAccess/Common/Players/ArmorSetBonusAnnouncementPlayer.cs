#nullable enable
using Terraria;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Players;

public sealed class ArmorSetBonusAnnouncementPlayer : ModPlayer
{
    private string _lastSetBonus = string.Empty;
    private bool _initialized;

    public override void PostUpdateEquips()
    {
        if (Main.dedServ || Main.gameMenu || Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        string current = SetBonusNarrationFormatter.NormalizeDescription(Player.setBonus);
        if (!_initialized)
        {
            _lastSetBonus = current;
            _initialized = true;
            return;
        }

        if (string.Equals(current, _lastSetBonus, System.StringComparison.Ordinal))
        {
            return;
        }

        _lastSetBonus = current;
        string? announcement = SetBonusNarrationFormatter.BuildActivatedAnnouncement(current);
        if (!string.IsNullOrWhiteSpace(announcement))
        {
            ScreenReaderService.Announce(announcement, force: true);
        }
    }
}
