#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class FootstepAudioEmitter
    {
        private const float MinSpeed = 0.35f;
        private const int IdleResetFrames = 6;

        private long _nextStepFrame;
        private FootstepSide _nextFootSide = FootstepSide.Left;
        private int _lastVariationIndex = -1;
        private readonly UnifiedRandom _variationRandom = new((int)(DateTime.UtcNow.Ticks & 0x3FFFFFFF));

        public void Update(Player player)
        {
            bool grounded = IsGrounded(player);
            if (!ShouldEmit(player, grounded))
            {
                _nextStepFrame = Main.GameUpdateCount + IdleResetFrames;
                return;
            }

            float speed = player.velocity.Length();
            long currentFrame = Main.GameUpdateCount;
            if (currentFrame < _nextStepFrame)
            {
                return;
            }

            float normalized = MathHelper.Clamp(speed / 9f, 0f, 1.2f);
            int baseCadence = Math.Clamp((int)MathF.Round(18f - normalized * 9f), 4, 26);
            int cadenceFrames = Math.Max(4, (int)MathF.Round(baseCadence * 0.9f));

            float pitch = MathHelper.Lerp(-0.22f, 0.18f, normalized);
            float volume = MathHelper.Lerp(0.25f, 0.6f, normalized);
            int variant = PickNextVariant();
            FootstepToneProvider.Play(_nextFootSide, variant, volume, pitch, pan: 0f);
            _nextFootSide = _nextFootSide == FootstepSide.Left ? FootstepSide.Right : FootstepSide.Left;

            _nextStepFrame = currentFrame + cadenceFrames;
        }

        public void Reset()
        {
            _nextStepFrame = 0;
            _nextFootSide = FootstepSide.Left;
            _lastVariationIndex = -1;
        }

        private static bool ShouldEmit(Player player, bool grounded)
        {
            if (!player.active || player.dead || player.ghost)
            {
                return false;
            }

            if (player.mount.Active || player.pulley)
            {
                return false;
            }

            if (player.velocity.LengthSquared() < MinSpeed * MinSpeed)
            {
                return false;
            }

            if (!grounded)
            {
                return false;
            }

            return true;
        }

        private static bool IsGrounded(Player player)
        {
            return Math.Abs(player.velocity.Y) < 0.02f;
        }

        private int PickNextVariant()
        {
            int next = _variationRandom.Next(FootstepVariantCount);
            if (next == _lastVariationIndex)
            {
                next = (next + 1) % FootstepVariantCount;
            }

            _lastVariationIndex = next;
            return next;
        }
    }
}
