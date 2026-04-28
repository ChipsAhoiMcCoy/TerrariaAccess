#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Players;

public sealed class DeathNarrationPlayer : ModPlayer
{
    private const int SupplementDelayFrames = 2;
    private const string RespawnCountdownKey = "Mods.TerrariaAccess.Combat.RespawnIn";

    private bool _pendingDeathSupplement;
    private int _supplementDelayFrames;
    private int _lastRespawnSecondsAnnounced = -1;

    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        _pendingDeathSupplement = true;
        _supplementDelayFrames = SupplementDelayFrames;
        _lastRespawnSecondsAnnounced = -1;
    }

    public override void UpdateDead()
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (_pendingDeathSupplement)
        {
            if (_supplementDelayFrames > 0)
            {
                _supplementDelayFrames--;
                return;
            }

            AnnounceDeathSupplement();
            _pendingDeathSupplement = false;
        }
        else
        {
            AnnounceRespawnCountdown();
        }
    }

    public override void OnRespawn()
    {
        _pendingDeathSupplement = false;
        _supplementDelayFrames = 0;
        _lastRespawnSecondsAnnounced = -1;
    }

    private static string? BuildCoinDetail(Player player)
    {
        long lostCoins = player.lostCoins;
        if (lostCoins <= 0)
        {
            return null;
        }

        string coinString = player.lostCoinString;
        if (string.IsNullOrWhiteSpace(coinString))
        {
            coinString = CoinFormatter.ValueToCoinString(lostCoins);
        }

        if (string.IsNullOrWhiteSpace(coinString))
        {
            return null;
        }

        return Language.GetTextValue("Game.DroppedCoins", coinString);
    }

    private void AnnounceDeathSupplement()
    {
        string? coinDetail = BuildCoinDetail(Player);
        if (!string.IsNullOrWhiteSpace(coinDetail))
        {
            QueueSupplement(coinDetail);
        }

        AnnounceRespawnCountdown(force: true);
    }

    private void AnnounceRespawnCountdown(bool force = false)
    {
        int seconds = GetDisplayedRespawnSeconds(Player);
        if (seconds <= 0)
        {
            return;
        }

        if (!ShouldAnnounceRespawnSeconds(seconds, force))
        {
            return;
        }

        bool includeContext = _lastRespawnSecondsAnnounced < 0;
        _lastRespawnSecondsAnnounced = seconds;
        if (!includeContext)
        {
            QueueSupplement(seconds.ToString());
            return;
        }

        string unit = seconds == 1 ? "second" : "seconds";
        string template = LocalizationHelper.GetTextOrFallback(RespawnCountdownKey, "Respawning in {0} {1}");
        QueueSupplement(string.Format(template, seconds, unit));
    }

    private bool ShouldAnnounceRespawnSeconds(int seconds, bool force)
    {
        if (force)
        {
            return seconds != _lastRespawnSecondsAnnounced;
        }

        if (seconds == _lastRespawnSecondsAnnounced)
        {
            return false;
        }

        return seconds <= 10 || seconds % 5 == 0;
    }

    private static int GetDisplayedRespawnSeconds(Player player)
    {
        if (player.respawnTimer <= 0)
        {
            return 0;
        }

        return (int)(1f + player.respawnTimer / 60f);
    }

    private static void QueueSupplement(string message)
    {
        ScreenReaderService.Announce(message, force: true, requestInterrupt: false);
    }
}
