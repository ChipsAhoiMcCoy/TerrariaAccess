#nullable enable
using System;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.GameInput;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class LockOnNarrator
    {
        private const int HealthPercentStep = 10;

        private int _lastNpcId = -1;
        private int _lastAnnouncedPercent = -1;

        public void Update()
        {
            if (!LockOnHelper.Enabled)
            {
                ClearTarget();
                return;
            }

            // Don't announce lock-on while in a fancy UI overlay (bestiary, achievements, etc.)
            if (Main.inFancyUI)
            {
                return;
            }

            NPC? target = LockOnHelper.AimedTarget;
            if (target is null || !target.active)
            {
                ClearTarget();
                return;
            }

            int npcId = target.whoAmI;
            int hp = Math.Max(0, target.life);
            int percent = GetHealthPercent(target);

            if (npcId != _lastNpcId)
            {
                _lastNpcId = npcId;
                _lastAnnouncedPercent = percent;
                AnnounceLockOn(target, hp);
                return;
            }

            if (percent < _lastAnnouncedPercent && _lastAnnouncedPercent - percent >= HealthPercentStep)
            {
                _lastAnnouncedPercent = percent;
                ScreenReaderService.Announce($"{hp} health", force: true);
            }
        }

        private void ClearTarget()
        {
            if (_lastNpcId == -1)
            {
                return;
            }

            _lastNpcId = -1;
            _lastAnnouncedPercent = -1;
            ScreenReaderService.Announce("No longer targeting", force: true);
        }

        private static void AnnounceLockOn(NPC target, int hp)
        {
            string name = GetNpcName(target);
            ScreenReaderService.Announce($"Locked on to {name}, {hp} health", force: true);
        }

        private static int GetHealthPercent(NPC target)
        {
            if (target.lifeMax <= 0)
            {
                return 0;
            }

            int life = Math.Max(0, target.life);
            return (int)Math.Round(100.0 * life / target.lifeMax);
        }

        private static string GetNpcName(NPC target)
        {
            string name = target.GivenOrTypeName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Lang.GetNPCNameValue(target.type);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Enemy {target.type}";
            }

            return name;
        }
    }
}
