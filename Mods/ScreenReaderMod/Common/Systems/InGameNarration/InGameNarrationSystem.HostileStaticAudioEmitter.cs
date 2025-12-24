#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class HostileStaticAudioEmitter
    {
        private const int ScanIntervalTicks = 4;
        private const float StandardRangeTiles = 52f;
        private const float BossRangeTiles = 160f;
        private const float PanScalePixels = 520f;
        private const float PitchScalePixels = 320f;
        private const int MinIntervalFrames = 7;
        private const int MaxIntervalFrames = 32;
        private const float HostileToneDurationSeconds = 0.13f;
        private const float HostileToneGain = 0.42f;
        private static readonly float[] HostileTonePartials = { 1.24f, 1.5f };
        private static readonly ToneEnvelope HostileToneEnvelope = new(attackFraction: 0.12f, releaseFraction: 0.55f, applyHannWindow: true);

        private static SoundEffect? s_hostileTone;

        private HostileCandidate? _primaryCandidate;
        private int _activeNpcId = -1;
        private long _nextPingFrame;
        private readonly List<SoundEffectInstance> _liveInstances = new();

        private int _ticksUntilNextScan;

        public void Update(Player listener)
        {
            if (listener is null || Main.dedServ || Main.gameMenu || Main.soundVolume <= 0f)
            {
                Reset();
                return;
            }

            if (!LockOnHelper.CanUseLockonSystem())
            {
                Reset();
                return;
            }

            if (_ticksUntilNextScan <= 0)
            {
                _primaryCandidate = FindPrimaryCandidate(listener);
                _ticksUntilNextScan = ScanIntervalTicks;
            }
            else
            {
                _ticksUntilNextScan--;
            }

            if (!_primaryCandidate.HasValue)
            {
                CleanupFinishedInstances();
                return;
            }

            HostileCandidate candidate = _primaryCandidate.Value;
            if (_activeNpcId != candidate.NpcId)
            {
                _activeNpcId = candidate.NpcId;
                _nextPingFrame = 0;
            }

            EmitIfDue(listener.Center, candidate);
            CleanupFinishedInstances();
        }

        public void Reset()
        {
            _ticksUntilNextScan = 0;
            _primaryCandidate = null;
            _activeNpcId = -1;
            _nextPingFrame = 0;
            StopAllInstances();
        }

        public static void DisposeStaticResources()
        {
            s_hostileTone?.Dispose();
            s_hostileTone = null;
        }

        private HostileCandidate? FindPrimaryCandidate(Player listener)
        {
            HostileCandidate? best = null;
            Vector2 listenerCenter = listener.Center;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!IsEligibleHostile(npc, listener))
                {
                    continue;
                }

                if (LockOnHelper.CanUseLockonSystem() && !IsLockOnEligibleForSound(npc, listenerCenter))
                {
                    continue;
                }

                bool isBoss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];
                float maxDistance = isBoss ? BossRangeTiles : StandardRangeTiles;
                float distanceTiles = Vector2.Distance(listenerCenter, npc.Center) / 16f;
                if (distanceTiles > maxDistance)
                {
                    continue;
                }

                if (!IsWorldPositionApproximatelyOnScreen(npc.Center))
                {
                    continue;
                }

                HostileCandidate candidate = new(
                    npc.whoAmI,
                    npc.Center,
                    distanceTiles,
                    maxDistance,
                    isBoss);

                if (best is null || IsBetterCandidate(candidate, best.Value))
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static bool IsEligibleHostile(NPC npc, Player listener)
        {
            if (!npc.active || npc.lifeMax <= 5 || npc.damage <= 0)
            {
                return false;
            }

            if (!npc.CanBeChasedBy(listener, ignoreDontTakeDamage: false))
            {
                return false;
            }

            if (npc.townNPC || npc.friendly)
            {
                return false;
            }

            return true;
        }

        private static bool IsLockOnEligibleForSound(NPC npc, Vector2 listenerCenter)
        {
            // Mirror the vanilla lock-on eligibility checks: target validity, range, on-screen window, and minimal lighting.
            if (npc is null || !npc.active || npc.dontTakeDamage || npc.friendly || npc.isLikeATownNPC || npc.life < 1 || npc.immortal)
            {
                return false;
            }

            if (npc.aiStyle == 25 && npc.ai.Length > 0 && npc.ai[0] == 0f)
            {
                return false;
            }

            const float LockOnRangePixels = 2000f;
            float distance = Vector2.Distance(listenerCenter, npc.Center);
            if (distance > LockOnRangePixels)
            {
                return false;
            }

            Rectangle screenRect = Utils.CenteredRectangle(Main.player[Main.myPlayer].Center, new Vector2(1920f, 1200f));
            if (!screenRect.Intersects(npc.Hitbox))
            {
                return false;
            }

            float lightLevel = Lighting.GetSubLight(npc.Center).Length() / 3f;
            if (lightLevel < 0.03f)
            {
                return false;
            }

            return true;
        }

        private static bool IsBetterCandidate(HostileCandidate candidate, HostileCandidate current)
        {
            if (candidate.IsBoss != current.IsBoss)
            {
                return candidate.IsBoss;
            }

            return candidate.DistanceTiles < current.DistanceTiles;
        }

        private void EmitIfDue(Vector2 listenerCenter, HostileCandidate candidate)
        {
            long currentFrame = (long)Main.GameUpdateCount;
            if (currentFrame < _nextPingFrame)
            {
                return;
            }

            PlayStaticCue(listenerCenter, candidate);

            int delay = ComputeDelayFrames(candidate);
            _nextPingFrame = currentFrame + Math.Max(1, delay);
        }

        private void PlayStaticCue(Vector2 listenerCenter, HostileCandidate candidate)
        {
            float configVolume = (ScreenReaderModConfig.Instance?.EnemySoundVolume ?? 100) / 100f;
            if (configVolume <= 0f)
            {
                return;
            }

            SoundEffect tone = EnsureHostileTone();
            if (tone.IsDisposed)
            {
                return;
            }

            Vector2 offset = candidate.WorldPosition - listenerCenter;
            float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / PitchScalePixels, -0.8f, 0.8f);

            float volume = MathHelper.Clamp(1f, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale * configVolume;
            if (volume <= 0f)
            {
                return;
            }

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = pan;
            instance.Pitch = pitch;
            instance.Volume = volume;

            try
            {
                instance.Play();
                _liveInstances.Add(instance);
            }
            catch
            {
                instance.Dispose();
            }
        }

        private static int ComputeDelayFrames(HostileCandidate candidate)
        {
            float normalized = candidate.MaxAudibleDistanceTiles <= 0f
                ? 0f
                : Math.Clamp(candidate.DistanceTiles / candidate.MaxAudibleDistanceTiles, 0f, 1f);

            float frames = MathHelper.Lerp(MinIntervalFrames, MaxIntervalFrames, normalized);
            if (candidate.IsBoss)
            {
                frames *= 0.65f;
            }

            return Math.Max(1, (int)MathF.Round(frames));
        }

        private void CleanupFinishedInstances()
        {
            for (int i = _liveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = _liveInstances[i];
                if (instance.IsDisposed || instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    _liveInstances.RemoveAt(i);
                }
            }
        }

        private void StopAllInstances()
        {
            for (int i = _liveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = _liveInstances[i];
                try
                {
                    if (!instance.IsDisposed)
                    {
                        instance.Stop();
                    }
                }
                catch
                {
                }

                instance.Dispose();
            }

            _liveInstances.Clear();
        }

        private static SoundEffect EnsureHostileTone()
        {
            if (s_hostileTone is { IsDisposed: false })
            {
                return s_hostileTone;
            }

            s_hostileTone?.Dispose();
            s_hostileTone = SynthesizedSoundFactory.CreateAdditiveTone(
                fundamentalFrequency: 360f,
                partialMultipliers: HostileTonePartials,
                envelope: HostileToneEnvelope,
                durationSeconds: HostileToneDurationSeconds,
                outputGain: HostileToneGain,
                partialFalloff: 0.75f);
            return s_hostileTone;
        }

        private readonly record struct HostileCandidate(
            int NpcId,
            Vector2 WorldPosition,
            float DistanceTiles,
            float MaxAudibleDistanceTiles,
            bool IsBoss);
    }
}
