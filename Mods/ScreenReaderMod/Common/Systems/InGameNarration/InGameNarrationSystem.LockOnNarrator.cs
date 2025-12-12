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
        private int _lastNpcId = -1;
        private int _lastNpcLife = -1;
        private int _lastAnnouncedLife = -1;

        public void Update()
        {
            if (!LockOnHelper.Enabled)
            {
                ClearTarget();
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

            if (npcId != _lastNpcId)
            {
                _lastNpcId = npcId;
                _lastNpcLife = hp;
                _lastAnnouncedLife = hp;
                AnnounceLockOn(target, hp);
                return;
            }

            if (hp != _lastNpcLife)
            {
                int previousLife = _lastNpcLife;
                _lastNpcLife = hp;
                AnnounceHealthChange(hp, previousLife);
            }
        }

        private void ClearTarget()
        {
            if (_lastNpcId == -1)
            {
                return;
            }

            _lastNpcId = -1;
            _lastNpcLife = -1;
            _lastAnnouncedLife = -1;
            ScreenReaderService.Announce("No longer targeting", force: true);
        }

        private static void AnnounceLockOn(NPC target, int hp)
        {
            string name = GetNpcName(target);
            ScreenReaderService.Announce($"Locked on to {name}, {hp} health", force: true);
        }

        private void AnnounceHealthChange(int hp, int previousLife)
        {
            if (previousLife == -1)
            {
                _lastAnnouncedLife = hp;
                ScreenReaderService.Announce($"{hp} health", force: true);
                return;
            }

            if (hp > previousLife)
            {
                if (hp > _lastAnnouncedLife)
                {
                    _lastAnnouncedLife = hp;
                }

                return;
            }

            int step = GetHealthNarrationStep(hp);
            if (_lastAnnouncedLife - hp >= step)
            {
                _lastAnnouncedLife = hp;
                ScreenReaderService.Announce($"{hp} health", force: true);
            }
        }

        private static int GetHealthNarrationStep(int hp)
        {
            // 4+ digits (1000+): announce every 500 health lost
            // 2-3 digits (10-999): announce every 50 health lost
            // 1 digit (1-9): announce every health point
            if (hp >= 1000)
            {
                return 500;
            }

            if (hp >= 10)
            {
                return 50;
            }

            return 1;
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
