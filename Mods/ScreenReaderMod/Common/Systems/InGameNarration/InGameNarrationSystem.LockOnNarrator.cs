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
                AnnounceLockOn(target, hp);
                return;
            }

            if (hp != _lastNpcLife)
            {
                _lastNpcLife = hp;
                AnnounceHealthChange(target, hp);
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
            ScreenReaderService.Announce("No longer targeting", force: true);
        }

        private static void AnnounceLockOn(NPC target, int hp)
        {
            string name = GetNpcName(target);
            ScreenReaderService.Announce($"Locked on to {name}, {hp} health", force: true);
        }

        private static void AnnounceHealthChange(NPC target, int hp)
        {
            ScreenReaderService.Announce($"{hp} health", force: true);
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
