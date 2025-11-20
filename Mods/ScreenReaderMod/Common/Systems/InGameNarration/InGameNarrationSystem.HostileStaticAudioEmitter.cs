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
        private const float SecondaryCueVolumeScale = 0.25f;
        private const float StandardRangeTiles = 52f;
        private const float BossRangeTiles = 160f;
        private const float PanScalePixels = 520f;
        private const float PitchScalePixels = 320f;
        private const int MinIntervalFrames = 7;
        private const int MaxIntervalFrames = 32;
        private const float VolumeMin = 0.18f;
        private const float VolumeScale = 0.85f;
        private const float VolumeDistanceReferenceTiles = 90f;
        private const float BossVolumeBonus = 0.12f;
        private const float HostileToneDurationSeconds = 0.24f;
        private const float HostileToneGain = 0.42f;
        private static readonly float[] HostileTonePartials = { 1.24f, 1.5f };
        private static readonly ToneEnvelope HostileToneEnvelope = new(attackFraction: 0.12f, releaseFraction: 0.55f, applyHannWindow: true);

        private static SoundEffect? s_hostileTone;

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

            int primaryNpcId = -1;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < limit; i++)
            {
                HostileCandidate candidate = _candidates[i];
                if (candidate.DistanceTiles < closestDistance)
                {
                    closestDistance = candidate.DistanceTiles;
                    primaryNpcId = candidate.NpcId;
                }
            }

            for (int i = 0; i < limit; i++)
            {
                HostileCandidate candidate = _candidates[i];
                _visibleNpcIds.Add(candidate.NpcId);
                bool isPrimaryCue = candidate.NpcId == primaryNpcId;
                EmitIfDue(playerCenter, candidate, isPrimaryCue);
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
            s_hostileTone?.Dispose();
            s_hostileTone = null;
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

                if (!IsWorldPositionApproximatelyOnScreen(npc.Center))
                {
                    continue;
                }

                _candidates.Add(new HostileCandidate(
                    npc.whoAmI,
                    npc.Center,
                    distanceTiles,
                    maxDistance,
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

        private void EmitIfDue(Vector2 listenerCenter, HostileCandidate candidate, bool isPrimaryCue)
        {
            long currentFrame = (long)Main.GameUpdateCount;
            long scheduled = _nextPingFrame.TryGetValue(candidate.NpcId, out long nextFrame) ? nextFrame : 0;
            if (currentFrame < scheduled)
            {
                return;
            }

            PlayStaticCue(listenerCenter, candidate, isPrimaryCue);

            int delay = ComputeDelayFrames(candidate);
            _nextPingFrame[candidate.NpcId] = currentFrame + Math.Max(1, delay);
        }

        private void PlayStaticCue(Vector2 listenerCenter, HostileCandidate candidate, bool isPrimaryCue)
        {
            SoundEffect tone = EnsureHostileTone();
            if (tone.IsDisposed)
            {
                return;
            }

            Vector2 offset = candidate.WorldPosition - listenerCenter;
            float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / PitchScalePixels, -0.8f, 0.8f);

            float distanceReference = Math.Max(1f, VolumeDistanceReferenceTiles);
            float distanceFactor = 1f / (1f + (candidate.DistanceTiles / distanceReference));
            float baseVolume = MathHelper.Clamp(VolumeMin + distanceFactor * VolumeScale, 0f, 1f);
            if (candidate.IsBoss)
            {
                baseVolume = MathHelper.Clamp(baseVolume + BossVolumeBonus, 0f, 1f);
            }

            float scaledBase = baseVolume * (isPrimaryCue ? 1f : SecondaryCueVolumeScale);
            float volume = MathHelper.Clamp(scaledBase, 0f, 1f) * Main.soundVolume;
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
