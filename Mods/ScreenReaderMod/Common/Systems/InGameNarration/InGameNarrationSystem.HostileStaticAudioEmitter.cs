#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class HostileStaticAudioEmitter
    {
        private const int ScanIntervalTicks = 4;
        private const int MaxConcurrentCues = 3;
        private const float StandardRangeTiles = 52f;
        private const float BossRangeTiles = 160f;
        private const float BaseVolume = 0.21f;
        private const float BossVolumeBonus = 0.09f;
        private const float PanScalePixels = 520f;
        private const float PitchScalePixels = 320f;
        private const int MinIntervalFrames = 7;
        private const int MaxIntervalFrames = 32;

        private static SoundEffect? s_staticNoise;

        private readonly List<HostileCandidate> _candidates = new();
        private readonly Dictionary<int, long> _nextPingFrame = new();
        private readonly HashSet<int> _visibleNpcIds = new();
        private readonly List<int> _staleNpcIds = new();
        private readonly List<SoundEffectInstance> _liveInstances = new();

        private int _ticksUntilNextScan;

        public void Update(Player listener)
        {
            if (listener is null || Main.dedServ || Main.gameMenu || Main.soundVolume <= 0f)
            {
                Reset();
                return;
            }

            if (_ticksUntilNextScan <= 0)
            {
                RebuildCandidates(listener);
                _ticksUntilNextScan = ScanIntervalTicks;
            }
            else
            {
                _ticksUntilNextScan--;
            }

            if (_candidates.Count == 0)
            {
                TrimInactiveEntries();
                CleanupFinishedInstances();
                return;
            }

            _candidates.Sort(static (left, right) =>
            {
                if (left.IsBoss != right.IsBoss)
                {
                    return left.IsBoss ? -1 : 1;
                }

                return left.DistanceTiles.CompareTo(right.DistanceTiles);
            });

            _visibleNpcIds.Clear();
            Vector2 playerCenter = listener.Center;
            int limit = Math.Min(MaxConcurrentCues, _candidates.Count);
            for (int i = 0; i < limit; i++)
            {
                HostileCandidate candidate = _candidates[i];
                _visibleNpcIds.Add(candidate.NpcId);
                EmitIfDue(playerCenter, candidate);
            }

            TrimInactiveEntries();
            CleanupFinishedInstances();
        }

        public void Reset()
        {
            _ticksUntilNextScan = 0;
            _candidates.Clear();
            _nextPingFrame.Clear();
            _visibleNpcIds.Clear();
            _staleNpcIds.Clear();
            StopAllInstances();
        }

        public static void DisposeStaticResources()
        {
            s_staticNoise?.Dispose();
            s_staticNoise = null;
        }

        private void RebuildCandidates(Player listener)
        {
            _candidates.Clear();
            Vector2 listenerCenter = listener.Center;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!IsEligibleHostile(npc, listener))
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

                float baseVolume = BaseVolume + (isBoss ? BossVolumeBonus : 0f);

                _candidates.Add(new HostileCandidate(
                    npc.whoAmI,
                    npc.Center,
                    distanceTiles,
                    maxDistance,
                    baseVolume,
                    isBoss));
            }
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

        private void EmitIfDue(Vector2 listenerCenter, HostileCandidate candidate)
        {
            long currentFrame = (long)Main.GameUpdateCount;
            long scheduled = _nextPingFrame.TryGetValue(candidate.NpcId, out long nextFrame) ? nextFrame : 0;
            if (currentFrame < scheduled)
            {
                return;
            }

            PlayStaticCue(listenerCenter, candidate);

            int delay = ComputeDelayFrames(candidate);
            _nextPingFrame[candidate.NpcId] = currentFrame + Math.Max(1, delay);
        }

        private void PlayStaticCue(Vector2 listenerCenter, HostileCandidate candidate)
        {
            SoundEffect tone = EnsureStaticTone();
            if (tone.IsDisposed)
            {
                return;
            }

            Vector2 offset = candidate.WorldPosition - listenerCenter;
            float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / PitchScalePixels, -0.8f, 0.8f);

            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                candidate.BaseVolume,
                candidate.DistanceTiles,
                candidate.MaxAudibleDistanceTiles,
                minFactor: 0.22f,
                exponent: 1.35f);

            if (candidate.IsBoss)
            {
                loudness = MathHelper.Clamp(loudness + 0.12f, 0f, 1f);
            }

            float volume = loudness * Main.soundVolume;
            if (volume <= 0f)
            {
                return;
            }

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = pan;
            instance.Pitch = pitch;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f);

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

        private void TrimInactiveEntries()
        {
            if (_nextPingFrame.Count == 0)
            {
                return;
            }

            _staleNpcIds.Clear();
            foreach ((int npcId, _) in _nextPingFrame)
            {
                if (!_visibleNpcIds.Contains(npcId))
                {
                    _staleNpcIds.Add(npcId);
                }
            }

            foreach (int npcId in _staleNpcIds)
            {
                _nextPingFrame.Remove(npcId);
            }

            _visibleNpcIds.Clear();
            _staleNpcIds.Clear();
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

        private static SoundEffect EnsureStaticTone()
        {
            if (s_staticNoise is { IsDisposed: false })
            {
                return s_staticNoise;
            }

            s_staticNoise?.Dispose();
            s_staticNoise = SynthesizedSoundFactory.CreateNoise(
                durationSeconds: 0.28f,
                envelope: new ToneEnvelope(attackFraction: 0.08f, releaseFraction: 0.6f, applyHannWindow: false),
                gain: 0.275f);
            return s_staticNoise;
        }

        private readonly record struct HostileCandidate(
            int NpcId,
            Vector2 WorldPosition,
            float DistanceTiles,
            float MaxAudibleDistanceTiles,
            float BaseVolume,
            bool IsBoss);
    }
}
