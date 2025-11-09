#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class NpcFootstepAudioEmitter
    {
        private const float MinSpeed = 0.35f;
        private const float MaxDistance = 960f;
        private const float MaxDistanceTiles = MaxDistance / 16f;

        private readonly Dictionary<int, long> _nextStepFrame = new();
        private readonly Dictionary<int, FootstepSide> _nextFootSide = new();
        private readonly Dictionary<int, int> _lastVariationIndex = new();
        private readonly UnifiedRandom _variationRandom = new((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

        public void Update(Player listener)
        {
            if (listener is null || Main.gameMenu)
            {
                return;
            }

            long currentFrame = Main.GameUpdateCount;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!ShouldEmit(npc))
                {
                    continue;
                }

                float distance = Vector2.Distance(listener.Center, npc.Center);
                if (distance > MaxDistance)
                {
                    continue;
                }

                long nextFrame = _nextStepFrame.TryGetValue(npc.whoAmI, out long scheduled) ? scheduled : 0;
                if (currentFrame < nextFrame)
                {
                    continue;
                }

                float speed = npc.velocity.Length();
                float normalized = MathHelper.Clamp(speed / 8f, 0f, 1.2f);
                int baseCadence = Math.Clamp((int)MathF.Round(22f - normalized * 11f), 5, 28);
                int cadenceFrames = Math.Max(5, (int)MathF.Round(baseCadence * 0.9f));

                float pan = MathHelper.Clamp((npc.Center.X - listener.Center.X) / 520f, -1f, 1f);
                float baseVolume = MathHelper.Lerp(0.18f, 0.48f, normalized);
                float distanceTiles = distance / 16f;
                float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                    baseVolume,
                    distanceTiles,
                    MaxDistanceTiles,
                    minFactor: 0.35f);
                float pitch = MathHelper.Lerp(-0.28f, 0.15f, normalized);

                FootstepSide footSide = _nextFootSide.TryGetValue(npc.whoAmI, out FootstepSide storedSide)
                    ? storedSide
                    : FootstepSide.Left;
                int variation = GetNextVariant(npc.whoAmI);
                FootstepToneProvider.Play(footSide, variation, loudness, pitch, pan);
                _nextStepFrame[npc.whoAmI] = currentFrame + cadenceFrames;
                _nextFootSide[npc.whoAmI] = footSide == FootstepSide.Left ? FootstepSide.Right : FootstepSide.Left;
            }

            PruneInactiveNpcEntries();
        }

        public void Reset()
        {
            _nextStepFrame.Clear();
            _nextFootSide.Clear();
            _lastVariationIndex.Clear();
        }

        private void PruneInactiveNpcEntries()
        {
            if (_nextStepFrame.Count == 0)
            {
                return;
            }

            List<int>? toRemove = null;
            foreach (KeyValuePair<int, long> entry in _nextStepFrame)
            {
                int npcIndex = entry.Key;
                if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
                {
                    (toRemove ??= new()).Add(npcIndex);
                    continue;
                }

                NPC npc = Main.npc[npcIndex];
                if (!npc.active || npc.life <= 0)
                {
                    (toRemove ??= new()).Add(npcIndex);
                }
            }

            if (toRemove is null)
            {
                return;
            }

            foreach (int npcIndex in toRemove)
            {
                _nextStepFrame.Remove(npcIndex);
                _nextFootSide.Remove(npcIndex);
                _lastVariationIndex.Remove(npcIndex);
            }
        }

        private static bool ShouldEmit(NPC npc)
        {
            if (!npc.active || npc.life <= 0 || npc.hide)
            {
                return false;
            }

            if (npc.noTileCollide || npc.noGravity || npc.boss || npc.dontTakeDamage)
            {
                return false;
            }

            if (npc.velocity.LengthSquared() < MinSpeed * MinSpeed)
            {
                return false;
            }

            if (!IsGrounded(npc))
            {
                return false;
            }

            return true;
        }

        private static bool IsGrounded(NPC npc)
        {
            if (npc.collideY)
            {
                return true;
            }

            return Math.Abs(npc.velocity.Y) < 0.01f;
        }

        private int GetNextVariant(int npcId)
        {
            int next = _variationRandom.Next(FootstepVariantCount);
            if (_lastVariationIndex.TryGetValue(npcId, out int last) && next == last)
            {
                next = (next + 1) % FootstepVariantCount;
            }

            _lastVariationIndex[npcId] = next;
            return next;
        }
    }
}
