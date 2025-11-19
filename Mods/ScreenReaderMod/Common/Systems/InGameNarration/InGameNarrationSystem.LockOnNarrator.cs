#nullable enable
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
            if (npcId == _lastNpcId)
            {
                return;
            }

            _lastNpcId = npcId;

            string name = target.GivenOrTypeName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Lang.GetNPCNameValue(target.type);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Enemy {target.type}";
            }

            ScreenReaderService.Announce($"Locked on to {name}", force: true);
        }

        private void ClearTarget()
        {
            if (_lastNpcId == -1)
            {
                return;
            }

            _lastNpcId = -1;
            ScreenReaderService.Announce("No longer targeting", force: true);
        }
    }
}
