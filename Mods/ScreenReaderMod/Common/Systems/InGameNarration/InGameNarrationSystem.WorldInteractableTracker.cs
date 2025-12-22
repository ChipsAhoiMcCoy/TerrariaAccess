#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems;
using ScreenReaderMod.Common.Utilities;
using ExplorationTargetKey = ScreenReaderMod.Common.Systems.ExplorationTargetRegistry.ExplorationTargetKey;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WorldInteractableTracker
    {
        private const int ScanIntervalTicks = 18;
        private const float SecondaryCueVolumeScale = 0.25f;
        private const float SelectedTargetMatchToleranceTiles = 6f;
        private const float MinimumVisibilityBrightness = 0.02f;

        private static readonly Dictionary<string, SoundEffect> ToneCache = new();
        private static readonly HashSet<int> RescuableNpcTypes = new()
        {
            NPCID.OldMan,
            NPCID.BoundGoblin,
            NPCID.BoundWizard,
            NPCID.BoundMechanic,
            NPCID.WebbedStylist,
            NPCID.SleepingAngler,
            NPCID.DemonTaxCollector,
            NPCID.GolferRescue,
            NPCID.BartenderUnconscious,
            NPCID.TravellingMerchant,
            NPCID.SkeletonMerchant,
            NPCID.BoundTownSlimeOld,
            NPCID.BoundTownSlimePurple,
            NPCID.BoundTownSlimeYellow
        };

        private readonly List<WorldInteractableSource> _sources = new();
        private readonly List<Candidate> _candidateBuffer = new();
        private readonly List<Candidate> _trackedCandidates = new();
        private readonly List<CandidateDistance> _distanceScratch = new();
        private readonly List<CandidateDistance> _sweepOrder = new();
        private readonly List<TrackedInteractableKey> _currentSweepKeys = new();
        private readonly HashSet<TrackedInteractableKey> _visibleThisFrame = new();
        private readonly List<TrackedInteractableKey> _staleKeys = new();
        private readonly HashSet<TrackedInteractableKey> _arrivedKeys = new();
        private readonly HashSet<TrackedInteractableKey> _emittedThisSweep = new();
        private readonly Dictionary<TrackedInteractableKey, int> _nextCueFrame = new();
        private readonly List<SoundEffectInstance> _liveInstances = new();

        private int _ticksUntilNextScan;
        private int _nextSweepFrame;
        private int _sweepCursor;
        private bool _isEnabled;

        public WorldInteractableTracker()
        {
            RegisterSource(new ChestInteractableSource(
                scanRadiusTiles: 80f,
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.Containers, (int)TileID.Containers2 },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.Chest)));

            RegisterSource(new StaticTileInteractableSource(
                scanRadiusTiles: 75f,
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.Heart },
                    frameWidth: 36,
                    frameHeight: 54,
                    widthTiles: 2,
                    heightTiles: 3,
                    profile: InteractableCueProfile.HeartCrystal),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.LifeFruit },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.LifeFruit),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.PlanteraBulb },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.PlanteraBulb),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.DemonAltar },
                    frameWidth: 54,
                    frameHeight: 36,
                    widthTiles: 3,
                    heightTiles: 2,
                    profile: InteractableCueProfile.DemonAltar,
                    tilePredicate: static _ => !WorldGen.crimson),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.DemonAltar },
                    frameWidth: 54,
                    frameHeight: 36,
                    widthTiles: 3,
                    heightTiles: 2,
                    profile: InteractableCueProfile.CrimsonAltar,
                    tilePredicate: static _ => WorldGen.crimson),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.ShadowOrbs },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.ShadowOrb,
                    tilePredicate: static _ => !WorldGen.crimson),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.ShadowOrbs },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.CrimsonHeart,
                    tilePredicate: static _ => WorldGen.crimson),
                new TileInteractableDefinition(
                    tileTypes: new[] { (int)TileID.Larva },
                    frameWidth: 36,
                    frameHeight: 36,
                    widthTiles: 2,
                    heightTiles: 2,
                    profile: InteractableCueProfile.BeeLarva)));

            RegisterSource(new OreInteractableSource(
                scanRadiusTiles: 90f));

            RegisterSource(new NpcInteractableSource(
                scanRadiusTiles: 80f,
                InteractableCueProfile.RescueNpc,
                RescuableNpcTypes));

            RegisterSource(new ItemInteractableSource(
                scanRadiusTiles: 75f,
                InteractableCueProfile.FallenStar,
                ItemID.FallenStar));
        }

        public void Update(Player player, bool isEnabled)
        {
            if (Main.dedServ || Main.soundVolume <= 0f)
            {
                StopAllInstances();
                ClearAllCueSchedules();
                _candidateBuffer.Clear();
                _trackedCandidates.Clear();
                _visibleThisFrame.Clear();
                _distanceScratch.Clear();
                _staleKeys.Clear();
                _arrivedKeys.Clear();
                ExplorationTargetRegistry.UpdateTargets(Array.Empty<ExplorationTargetRegistry.ExplorationTarget>());
                _ticksUntilNextScan = 0;
                _isEnabled = false;
                return;
            }

            PrepareSources(player);

            if (!isEnabled)
            {
                if (_isEnabled)
                {
                    StopAllInstances();
                    ClearAllCueSchedules();
                    _candidateBuffer.Clear();
                    _trackedCandidates.Clear();
                    _distanceScratch.Clear();
                    _visibleThisFrame.Clear();
                    _staleKeys.Clear();
                    _arrivedKeys.Clear();
                    ExplorationTargetRegistry.UpdateTargets(Array.Empty<ExplorationTargetRegistry.ExplorationTarget>());
                }

                _isEnabled = false;
                _ticksUntilNextScan = 0;
                return;
            }

            _isEnabled = true;

            if (_ticksUntilNextScan <= 0)
            {
                RebuildCandidateList(player);
                _ticksUntilNextScan = ScanIntervalTicks;
            }
            else
            {
                _ticksUntilNextScan--;
            }

            if (_trackedCandidates.Count == 0)
            {
                ResetSweepSchedule();
                ExplorationTargetRegistry.UpdateTargets(Array.Empty<ExplorationTargetRegistry.ExplorationTarget>());
                TrimInactiveKeys();
                CleanupFinishedInstances();
                return;
            }

            Vector2 playerCenter = player.Center;

            _distanceScratch.Clear();
            foreach (Candidate candidate in _trackedCandidates)
            {
                if (!IsWorldPositionApproximatelyOnScreen(candidate.WorldPosition))
                {
                    continue;
                }

                if (!IsTileLit(candidate.WorldPosition, MinimumVisibilityBrightness))
                {
                    continue;
                }

                float distanceTiles = Vector2.Distance(candidate.WorldPosition, playerCenter) / 16f;
                if (distanceTiles > candidate.Profile.MaxAudibleDistanceTiles)
                {
                    continue;
                }

                _distanceScratch.Add(new CandidateDistance(candidate, distanceTiles));
            }

            if (_distanceScratch.Count == 0)
            {
                ResetSweepSchedule();
                TrimInactiveKeys();
                CleanupFinishedInstances();
                ExplorationTargetRegistry.UpdateTargets(Array.Empty<ExplorationTargetRegistry.ExplorationTarget>());
                return;
            }

            _distanceScratch.Sort(static (left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

            // Keep an unfiltered snapshot for the exploration registry so UI cycling still has entries
            // even when we temporarily focus a single target or lose the focused target.
            List<ExplorationTargetRegistry.ExplorationTarget> snapshot = _distanceScratch.Select(d =>
            {
                string label = d.Candidate.ArrivalLabelOverride ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = d.Candidate.Profile.ArrivalLabel;
                }

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = d.Candidate.Profile.Id;
                }

                ExplorationTargetKey key = new(d.Candidate.Key.SourceId, d.Candidate.Key.LocalId);
                return new ExplorationTargetRegistry.ExplorationTarget(key, label, d.Candidate.WorldPosition, d.DistanceTiles);
            }).ToList();
            ExplorationTargetRegistry.UpdateTargets(snapshot);

            List<CandidateDistance> unfiltered = new(_distanceScratch);
            bool focused = ApplySelectedTargetFilter();
            if (!focused)
            {
                _distanceScratch.Clear();
                _distanceScratch.AddRange(unfiltered);
                ExplorationTargetRegistry.SetSelectedTarget(null);
            }

            _visibleThisFrame.Clear();

            int limit = _distanceScratch.Count;
            if (limit <= 0)
            {
                ResetSweepSchedule();
                TrimInactiveKeys();
                CleanupFinishedInstances();
                return;
            }

            _sweepOrder.Clear();
            _sweepOrder.AddRange(_distanceScratch);
            _sweepOrder.Sort(static (left, right) =>
            {
                int compareX = left.Candidate.WorldPosition.X.CompareTo(right.Candidate.WorldPosition.X);
                if (compareX != 0)
                {
                    return compareX;
                }

                int compareY = left.Candidate.WorldPosition.Y.CompareTo(right.Candidate.WorldPosition.Y);
                if (compareY != 0)
                {
                    return compareY;
                }

                return left.DistanceTiles.CompareTo(right.DistanceTiles);
            });

            if (_sweepOrder.Count > limit)
            {
                _sweepOrder.RemoveRange(limit, _sweepOrder.Count - limit);
            }

            bool orderChanged = SyncSweepOrder();
            foreach (CandidateDistance entry in _sweepOrder)
            {
                _visibleThisFrame.Add(entry.Candidate.Key);
                UpdateArrivalState(entry);
            }

            if (orderChanged)
            {
                StartNewSweepWindow();
            }

            EmitNextSweepCue(playerCenter);

            TrimInactiveKeys();
            CleanupFinishedInstances();
        }

        public void Reset()
        {
            _ticksUntilNextScan = 0;
            _candidateBuffer.Clear();
            _trackedCandidates.Clear();
            _distanceScratch.Clear();
            ClearAllCueSchedules();
            _visibleThisFrame.Clear();
            _staleKeys.Clear();
            _arrivedKeys.Clear();
            StopAllInstances();
            _isEnabled = false;

            foreach (WorldInteractableSource source in _sources)
            {
                source.Reset();
            }
        }

        public static void DisposeStaticResources()
        {
            foreach ((string key, SoundEffect tone) in ToneCache)
            {
                tone.Dispose();
            }

            ToneCache.Clear();
        }

        private void RebuildCandidateList(Player player)
        {
            _candidateBuffer.Clear();
            foreach (WorldInteractableSource source in _sources)
            {
                source.Collect(player, _candidateBuffer);
            }

            _trackedCandidates.Clear();
            _trackedCandidates.AddRange(_candidateBuffer);
        }

        private static bool IsTileLit(Vector2 worldPosition, float minimumBrightness)
        {
            int tileX = (int)(worldPosition.X / 16f);
            int tileY = (int)(worldPosition.Y / 16f);
            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY)
            {
                return false;
            }

            float brightness = Lighting.Brightness(tileX, tileY);
            return brightness >= minimumBrightness;
        }

        private void PrepareSources(Player player)
        {
            foreach (WorldInteractableSource source in _sources)
            {
                source.PrepareFrame(player);
            }
        }

        private void EmitNextSweepCue(Vector2 playerCenter)
        {
            if (_sweepOrder.Count == 0)
            {
                _emittedThisSweep.Clear();
                return;
            }

            if (_sweepCursor >= _sweepOrder.Count)
            {
                _sweepCursor = 0;
            }

            if (Main.GameUpdateCount < (uint)Math.Max(0, _nextSweepFrame))
            {
                return;
            }

            if (_sweepCursor == 0 && _emittedThisSweep.Count > 0)
            {
                _emittedThisSweep.Clear();
            }

            CandidateDistance entry = _sweepOrder[_sweepCursor];
            if (_emittedThisSweep.Contains(entry.Candidate.Key))
            {
                _sweepCursor = (_sweepCursor + 1) % _sweepOrder.Count;
                return;
            }

            PlayCue(playerCenter, entry, isPrimaryCue: true);

            _emittedThisSweep.Add(entry.Candidate.Key);

            int delay = ComputeSweepStepDelay(entry, _sweepOrder.Count);
            _nextSweepFrame = delay <= 0 ? 0 : ScheduleNextFrame(delay);
            _sweepCursor = (_sweepCursor + 1) % _sweepOrder.Count;
            if (_sweepCursor == 0)
            {
                _emittedThisSweep.Clear();
            }
        }

        private bool SyncSweepOrder()
        {
            bool changed = _sweepOrder.Count != _currentSweepKeys.Count;
            if (!changed)
            {
                for (int i = 0; i < _sweepOrder.Count; i++)
                {
                    if (!_sweepOrder[i].Candidate.Key.Equals(_currentSweepKeys[i]))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                _currentSweepKeys.Clear();
                foreach (CandidateDistance entry in _sweepOrder)
                {
                    _currentSweepKeys.Add(entry.Candidate.Key);
                }

                _sweepCursor = 0;
            }
            else if (_sweepCursor >= _sweepOrder.Count)
            {
                _sweepCursor = 0;
            }

            return changed;
        }

        private static int ComputeSweepStepDelay(CandidateDistance entry, int sweepCount)
        {
            InteractableCueProfile profile = entry.Candidate.Profile;
            int targetSweepFrames = Math.Max(profile.MinIntervalFrames, profile.MaxIntervalFrames);
            int interval = targetSweepFrames / Math.Max(1, sweepCount);
            interval = Math.Clamp(interval, profile.MinIntervalFrames, profile.MaxIntervalFrames);
            return interval;
        }

        private void TrimInactiveKeys()
        {
            _staleKeys.Clear();
            foreach (TrackedInteractableKey key in _arrivedKeys)
            {
                if (!_visibleThisFrame.Contains(key))
                {
                    _staleKeys.Add(key);
                }
            }

            foreach (TrackedInteractableKey key in _staleKeys)
            {
                _arrivedKeys.Remove(key);
                _nextCueFrame.Remove(key);
            }

            if (_visibleThisFrame.Count == 0)
            {
                ResetSweepSchedule();
            }

            _visibleThisFrame.Clear();
            _staleKeys.Clear();
        }

        private void UpdateArrivalState(CandidateDistance entry)
        {
            TrackedInteractableKey key = entry.Candidate.Key;
            if (entry.DistanceTiles <= GuidanceSystem.ArrivalTileThreshold)
            {
                if (_arrivedKeys.Contains(key))
                {
                    return;
                }

                _arrivedKeys.Add(key);
                string label = entry.Candidate.ArrivalLabelOverride ?? entry.Candidate.Profile.ArrivalLabel;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    ScreenReaderService.Announce($"Arrived at {label}");
                }
            }
            else
            {
                _arrivedKeys.Remove(key);
            }
        }

        private bool ApplySelectedTargetFilter()
        {
            if (!ExplorationTargetRegistry.TryGetSelectedTarget(out ExplorationTargetRegistry.ExplorationTarget target))
            {
                return false;
            }

            for (int i = 0; i < _distanceScratch.Count; i++)
            {
                CandidateDistance entry = _distanceScratch[i];
                if (entry.Candidate.Key.SourceId == target.Key.SourceId &&
                    entry.Candidate.Key.LocalId == target.Key.LocalId)
                {
                    _distanceScratch.Clear();
                    _distanceScratch.Add(entry);
                    return true;
                }
            }

            float bestDistance = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < _distanceScratch.Count; i++)
            {
                CandidateDistance entry = _distanceScratch[i];
                float delta = Vector2.Distance(entry.Candidate.WorldPosition, target.WorldPosition) / 16f;
                if (delta < bestDistance)
                {
                    bestDistance = delta;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestDistance > SelectedTargetMatchToleranceTiles)
            {
                return false;
            }

            CandidateDistance match = _distanceScratch[bestIndex];
            _distanceScratch.Clear();
            _distanceScratch.Add(match);
            return true;
        }

        private void PlayCue(Vector2 playerCenter, CandidateDistance entry, bool isPrimaryCue)
        {
            if (Main.soundVolume <= 0f)
            {
                return;
            }

            int currentFrame = (int)Main.GameUpdateCount;
            TrackedInteractableKey cueKey = entry.Candidate.Key;
            if (_nextCueFrame.TryGetValue(cueKey, out int readyFrame) && currentFrame < readyFrame)
            {
                return;
            }

            SpatialAudioPanner.SpatialDirection direction = SpatialAudioPanner.ComputeDirection(
                playerCenter,
                entry.Candidate.WorldPosition,
                entry.Candidate.Profile.PitchScalePixels,
                entry.Candidate.Profile.PanScalePixels,
                pitchClamp: 0.8f);
            InteractableCueProfile profile = entry.Candidate.Profile;

            if (profile.SoundStyle.HasValue)
            {
                float soundStyleBaseVolume = profile.ComputeVolume(entry.DistanceTiles);
                float soundStyleLoudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                    soundStyleBaseVolume,
                    entry.DistanceTiles,
                    profile.MaxAudibleDistanceTiles,
                    minFactor: 0.45f);
                float soundStyleScaledVolume = MathHelper.Clamp(
                    soundStyleLoudness * (isPrimaryCue ? 1f : SecondaryCueVolumeScale) * AudioVolumeDefaults.WorldCueVolumeScale,
                    0f,
                    1f);
                if (soundStyleScaledVolume <= 0f)
                {
                    return;
                }

                SoundStyle style = profile.SoundStyle.Value
                    .WithVolumeScale(soundStyleScaledVolume)
                    .WithPitchOffset(direction.Pitch);
                SoundEngine.PlaySound(style, entry.Candidate.WorldPosition);
                _nextCueFrame[cueKey] = currentFrame + Math.Max(1, profile.MinIntervalFrames);
                return;
            }

            float baseVolume = profile.ComputeVolume(entry.DistanceTiles);
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                baseVolume,
                entry.DistanceTiles,
                profile.MaxAudibleDistanceTiles,
                minFactor: 0.45f);
            float volume = loudness * Main.soundVolume;
            if (volume <= 0f)
            {
                return;
            }

            float scaledVolume = MathHelper.Clamp(
                volume * (isPrimaryCue ? 1f : SecondaryCueVolumeScale) * AudioVolumeDefaults.WorldCueVolumeScale,
                0f,
                1f);
            if (scaledVolume <= 0f)
            {
                return;
            }

            SoundEffect tone = EnsureTone(profile);
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pitch = direction.Pitch;
            instance.Pan = direction.Pan;
            instance.Volume = scaledVolume;

            try
            {
                instance.Play();
                _liveInstances.Add(instance);
                _nextCueFrame[cueKey] = currentFrame + Math.Max(1, profile.MinIntervalFrames);
            }
            catch
            {
                instance.Dispose();
            }
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
            foreach (SoundEffectInstance instance in _liveInstances)
            {
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

        private void ResetSweepSchedule()
        {
            _sweepOrder.Clear();
            _currentSweepKeys.Clear();
            _emittedThisSweep.Clear();
            _nextSweepFrame = 0;
            _sweepCursor = 0;
        }

        private void ClearAllCueSchedules()
        {
            ResetSweepSchedule();
            _nextCueFrame.Clear();
        }

        private void StartNewSweepWindow()
        {
            _emittedThisSweep.Clear();
            _sweepCursor = 0;
            _nextSweepFrame = (int)Main.GameUpdateCount;
        }

        private static int ScheduleNextFrame(int delayFrames)
        {
            int safeDelay = Math.Max(1, delayFrames);
            ulong current = Main.GameUpdateCount;
            ulong target = current + (ulong)safeDelay;
            if (target > int.MaxValue)
            {
                target = int.MaxValue;
            }

            return (int)target;
        }

        private static SoundEffect EnsureTone(InteractableCueProfile profile)
        {
            if (ToneCache.TryGetValue(profile.Id, out SoundEffect? cached) && cached is { IsDisposed: false })
            {
                return cached;
            }

            cached?.Dispose();
            SoundEffect created = SynthesizedSoundFactory.CreateAdditiveTone(
                profile.FundamentalFrequency,
                profile.PartialMultipliers,
                profile.Envelope,
                profile.DurationSeconds,
                profile.BaseGain);

            ToneCache[profile.Id] = created;
            return created;
        }

        private void RegisterSource(WorldInteractableSource source)
        {
            source.AssignSourceId(_sources.Count);
            _sources.Add(source);
        }
    }

    private abstract class WorldInteractableSource
    {
        protected WorldInteractableSource(float scanRadiusTiles)
        {
            ScanRadiusTiles = scanRadiusTiles;
        }

        internal int SourceId { get; private set; }
        protected float ScanRadiusTiles { get; }

        internal void AssignSourceId(int id) => SourceId = id;

        public virtual void PrepareFrame(Player player)
        {
        }

        public abstract void Collect(Player player, List<Candidate> buffer);

        public virtual void Reset()
        {
        }
    }

    private abstract class TileInteractableSource : WorldInteractableSource
    {
        private readonly TileInteractableDefinition[] _definitions;

        protected TileInteractableSource(float scanRadiusTiles, params TileInteractableDefinition[] definitions)
            : base(scanRadiusTiles)
        {
            _definitions = definitions ?? Array.Empty<TileInteractableDefinition>();
            for (int i = 0; i < _definitions.Length; i++)
            {
                ref TileInteractableDefinition definition = ref _definitions[i];
                definition.AssignDefinitionId(i);
            }
        }

        protected IReadOnlyList<TileInteractableDefinition> Definitions => _definitions;

        public override void Collect(Player player, List<Candidate> buffer)
        {
            if (_definitions.Length == 0)
            {
                return;
            }

            Vector2 playerCenter = player.Center;
            int playerTileX = (int)(playerCenter.X / 16f);
            int playerTileY = (int)(playerCenter.Y / 16f);
            int scanRadius = (int)Math.Clamp(ScanRadiusTiles, 1f, 200f);

            int minX = Math.Max(0, playerTileX - scanRadius);
            int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
            int minY = Math.Max(0, playerTileY - scanRadius);
            int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile)
                    {
                        continue;
                    }

                    foreach (TileInteractableDefinition definition in _definitions)
                    {
                        if (!definition.MatchesTile(tile))
                        {
                            continue;
                        }

                        if (!IsAnchorTile(tile, definition))
                        {
                            continue;
                        }

                        Point anchor = new(x, y);
                        if (!ShouldIncludeAnchor(definition, player, anchor))
                        {
                            continue;
                        }

                        Vector2 worldPosition = definition.GetWorldCenter(anchor);
                        int localId = HashCode.Combine(definition.DefinitionId, anchor.X, anchor.Y);
                        buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, localId), worldPosition, definition.Profile, null));
                    }
                }
            }
        }

        protected virtual bool ShouldIncludeAnchor(TileInteractableDefinition definition, Player player, Point anchor) => true;

        protected static bool IsAnchorTile(Tile tile, TileInteractableDefinition definition)
        {
            if (definition.FrameWidth <= 0 || definition.FrameHeight <= 0)
            {
                return true;
            }

            int frameX = tile.TileFrameX;
            int frameY = tile.TileFrameY;

            if (frameX < 0 || frameY < 0)
            {
                return false;
            }

            return frameX % definition.FrameWidth == 0 &&
                   frameY % definition.FrameHeight == 0;
        }
    }

    private sealed class ChestInteractableSource : TileInteractableSource
    {
        public ChestInteractableSource(float scanRadiusTiles, params TileInteractableDefinition[] definitions)
            : base(scanRadiusTiles, definitions)
        {
        }

        public override void Collect(Player player, List<Candidate> buffer)
        {
            Vector2 playerCenter = player.Center;
            int playerTileX = (int)(playerCenter.X / 16f);
            int playerTileY = (int)(playerCenter.Y / 16f);

            int scanRadius = (int)Math.Clamp(ScanRadiusTiles, 1f, 200f);
            int minX = Math.Max(0, playerTileX - scanRadius);
            int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
            int minY = Math.Max(0, playerTileY - scanRadius);
            int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

            foreach (TileInteractableDefinition definition in Definitions)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        if (!tile.HasTile)
                        {
                            continue;
                        }

                        if (!definition.MatchesTile(tile))
                        {
                            continue;
                        }

                        if (!IsAnchorTile(tile, definition))
                        {
                            continue;
                        }

                        Point anchor = new(x, y);
                        if (!ShouldIncludeAnchor(definition, player, anchor))
                        {
                            continue;
                        }

                        Vector2 worldPosition = definition.GetWorldCenter(anchor);
                        int localId = HashCode.Combine(definition.DefinitionId, anchor.X, anchor.Y);
                        string label = ResolveChestLabel(anchor, tile);
                        buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, localId), worldPosition, definition.Profile, label));
                    }
                }
            }
        }

        private static string ResolveChestLabel(Point anchor, Tile tile)
        {
            string name = string.Empty;
            int chestIndex = Chest.FindChestByGuessing(anchor.X, anchor.Y);
            if (chestIndex >= 0 && chestIndex < Main.chest.Length)
            {
                Chest? chest = Main.chest[chestIndex];
                if (chest is not null)
                {
                    name = TextSanitizer.Clean(chest.name);
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                if (CursorDescriptors.TryDescribe(anchor.X, anchor.Y, out CursorDescriptorService.CursorDescriptor descriptor) && !string.IsNullOrWhiteSpace(descriptor.Name))
                {
                    name = descriptor.Name;
                }
            }

            return string.IsNullOrWhiteSpace(name) ? "Chest" : name;
        }
    }

    private sealed class StaticTileInteractableSource : TileInteractableSource
    {
        public StaticTileInteractableSource(float scanRadiusTiles, params TileInteractableDefinition[] definitions)
            : base(scanRadiusTiles, definitions)
        {
        }
    }

    private sealed class OreInteractableSource : TileInteractableSource
    {
        private const int OreFrameSizePixels = 18;
        private static readonly int[] GemTileTypes =
        {
            TileID.Amethyst,
            TileID.Topaz,
            TileID.Sapphire,
            TileID.Emerald,
            TileID.Ruby,
            TileID.Diamond,
            TileID.AmberStoneBlock
        };

        public OreInteractableSource(float scanRadiusTiles)
            : base(scanRadiusTiles, CreateDefinition())
        {
        }

        public override void Collect(Player player, List<Candidate> buffer)
        {
            Vector2 playerCenter = player.Center;
            int playerTileX = (int)(playerCenter.X / 16f);
            int playerTileY = (int)(playerCenter.Y / 16f);
            int scanRadius = (int)Math.Clamp(ScanRadiusTiles, 1f, 200f);

            int minX = Math.Max(0, playerTileX - scanRadius);
            int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
            int minY = Math.Max(0, playerTileY - scanRadius);
            int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

            HashSet<Point> visited = new();
            Stack<Point> stack = new();

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Point start = new(x, y);
                    if (visited.Contains(start))
                    {
                        continue;
                    }

                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !IsOreOrGem(tile.TileType))
                    {
                        continue;
                    }

                    int oreType = tile.TileType;
                    float bestDistanceSq = float.MaxValue;
                    Point bestAnchor = start;

                    visited.Add(start);
                    stack.Push(start);

                    while (stack.Count > 0)
                    {
                        Point current = stack.Pop();
                        Vector2 center = new((current.X + 0.5f) * 16f, (current.Y + 0.5f) * 16f);
                        float distanceSq = Vector2.DistanceSquared(center, playerCenter);
                        if (distanceSq < bestDistanceSq)
                        {
                            bestDistanceSq = distanceSq;
                            bestAnchor = current;
                        }

                        foreach (Point neighbor in GetNeighbors(current, minX, maxX, minY, maxY))
                        {
                            if (visited.Contains(neighbor))
                            {
                                continue;
                            }

                            Tile neighborTile = Main.tile[neighbor.X, neighbor.Y];
                            if (!neighborTile.HasTile || neighborTile.TileType != oreType)
                            {
                                continue;
                            }

                            visited.Add(neighbor);
                            stack.Push(neighbor);
                        }
                    }

                    Vector2 worldPosition = new((bestAnchor.X + 0.5f) * 16f, (bestAnchor.Y + 0.5f) * 16f);
                    int localId = HashCode.Combine(oreType, bestAnchor.X, bestAnchor.Y);
                    string oreLabel = ResolveOreLabel(bestAnchor.X, bestAnchor.Y, oreType);
                    InteractableCueProfile profile = IsGem(oreType) ? InteractableCueProfile.Gem : InteractableCueProfile.Ore;
                    buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, localId), worldPosition, profile, oreLabel));
                }
            }
        }

        private static TileInteractableDefinition CreateDefinition()
        {
            int[] oreTiles = Enumerable
                .Range(0, TileID.Sets.Ore.Length)
                .Where(id => TileID.Sets.Ore[id])
                .Concat(GemTileTypes)
                .Distinct()
                .ToArray();

            return new TileInteractableDefinition(
                tileTypes: oreTiles,
                frameWidth: OreFrameSizePixels,
                frameHeight: OreFrameSizePixels,
                widthTiles: 1,
                heightTiles: 1,
                profile: InteractableCueProfile.Ore);
        }

        private static bool IsOreOrGem(int tileType)
        {
            if (tileType >= 0 && tileType < TileID.Sets.Ore.Length && TileID.Sets.Ore[tileType])
            {
                return true;
            }

            return Array.IndexOf(GemTileTypes, tileType) >= 0;
        }

        private static bool IsGem(int tileType) => Array.IndexOf(GemTileTypes, tileType) >= 0;

        private string ResolveOreLabel(int tileX, int tileY, int tileType)
        {
            if (CursorDescriptors.TryDescribe(tileX, tileY, out CursorDescriptorService.CursorDescriptor descriptor) && !string.IsNullOrWhiteSpace(descriptor.Name))
            {
                return descriptor.Name;
            }

            return IsGem(tileType) ? "Gem" : "Ore";
        }

        private static IEnumerable<Point> GetNeighbors(Point point, int minX, int maxX, int minY, int maxY)
        {
            if (point.X > minX)
            {
                yield return new Point(point.X - 1, point.Y);
            }
            if (point.X < maxX)
            {
                yield return new Point(point.X + 1, point.Y);
            }
            if (point.Y > minY)
            {
                yield return new Point(point.X, point.Y - 1);
            }
            if (point.Y < maxY)
            {
                yield return new Point(point.X, point.Y + 1);
            }
        }
    }

    private sealed class ItemInteractableSource : WorldInteractableSource
    {
        private readonly int[] _itemTypes;
        private readonly InteractableCueProfile _profile;

        public ItemInteractableSource(float scanRadiusTiles, InteractableCueProfile profile, params int[] itemTypes)
            : base(scanRadiusTiles)
        {
            _profile = profile;
            _itemTypes = itemTypes ?? Array.Empty<int>();
        }

        public override void Collect(Player player, List<Candidate> buffer)
        {
            if (_itemTypes.Length == 0)
            {
                return;
            }

            float maxDistancePixels = ScanRadiusTiles * 16f;
            float maxDistanceSq = maxDistancePixels * maxDistancePixels;

            for (int i = 0; i < Main.maxItems; i++)
            {
                Item item = Main.item[i];
                if (!item.active || item.stack <= 0)
                {
                    continue;
                }

                if (Array.IndexOf(_itemTypes, item.type) < 0)
                {
                    continue;
                }

                Vector2 center = item.Center;
                float distanceSq = Vector2.DistanceSquared(center, player.Center);
                if (distanceSq > maxDistanceSq)
                {
                    continue;
                }

                buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, i), center, _profile, null));
            }
        }
    }

    private sealed class NpcInteractableSource : WorldInteractableSource
    {
        private readonly InteractableCueProfile _profile;
        private readonly HashSet<int> _npcTypes = new();

        public NpcInteractableSource(float scanRadiusTiles, InteractableCueProfile profile, IEnumerable<int> npcTypes)
            : base(scanRadiusTiles)
        {
            _profile = profile;
            if (npcTypes is null)
            {
                return;
            }

            foreach (int npcType in npcTypes)
            {
                _npcTypes.Add(npcType);
            }
        }

        public override void Collect(Player player, List<Candidate> buffer)
        {
            if (_npcTypes.Count == 0)
            {
                return;
            }

            Vector2 playerCenter = player.Center;
            float scanRadius = Math.Max(1f, ScanRadiusTiles);

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.lifeMax <= 0)
                {
                    continue;
                }

                if (!_npcTypes.Contains(npc.type))
                {
                    continue;
                }

                float distanceTiles = Vector2.Distance(playerCenter, npc.Center) / 16f;
                if (distanceTiles > scanRadius)
                {
                    continue;
                }

                string label = ResolveNpcLabel(npc);
                buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, i), npc.Center, _profile, label));
            }
        }

        private static string ResolveNpcLabel(NPC npc)
        {
            if (!string.IsNullOrWhiteSpace(npc.FullName))
            {
                return npc.FullName;
            }

            if (!string.IsNullOrWhiteSpace(npc.GivenName))
            {
                return npc.GivenName;
            }

            if (!string.IsNullOrWhiteSpace(npc.TypeName))
            {
                return npc.TypeName;
            }

            return "Rescue NPC";
        }
    }

    private readonly record struct Candidate(TrackedInteractableKey Key, Vector2 WorldPosition, InteractableCueProfile Profile, string? ArrivalLabelOverride);

    private readonly record struct CandidateDistance(Candidate Candidate, float DistanceTiles);

    private readonly struct TrackedInteractableKey : IEquatable<TrackedInteractableKey>
    {
        public TrackedInteractableKey(int sourceId, int localId)
        {
            SourceId = sourceId;
            LocalId = localId;
        }

        public int SourceId { get; }
        public int LocalId { get; }

        public bool Equals(TrackedInteractableKey other) => SourceId == other.SourceId && LocalId == other.LocalId;

        public override bool Equals(object? obj) => obj is TrackedInteractableKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceId, LocalId);
    }

    private struct TileInteractableDefinition
    {
        public TileInteractableDefinition(
            int[] tileTypes,
            int frameWidth,
            int frameHeight,
            int widthTiles,
            int heightTiles,
            InteractableCueProfile profile,
            Func<Tile, bool>? tilePredicate = null)
        {
            TileTypes = tileTypes ?? Array.Empty<int>();
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            WidthTiles = widthTiles;
            HeightTiles = heightTiles;
            Profile = profile;
            TilePredicate = tilePredicate;
            DefinitionId = -1;
        }

        public int[] TileTypes { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int WidthTiles { get; }
        public int HeightTiles { get; }
        public InteractableCueProfile Profile { get; }
        public Func<Tile, bool>? TilePredicate { get; }
        public int DefinitionId { get; private set; }

        public void AssignDefinitionId(int id) => DefinitionId = id;

        public bool MatchesTile(Tile tile)
        {
            foreach (int type in TileTypes)
            {
                if (tile.TileType != type)
                {
                    continue;
                }

                return TilePredicate?.Invoke(tile) ?? true;
            }

            return false;
        }

        public Vector2 GetWorldCenter(Point anchor)
        {
            float centerX = (anchor.X + WidthTiles * 0.5f) * 16f;
            float centerY = (anchor.Y + HeightTiles * 0.5f) * 16f;
            return new Vector2(centerX, centerY);
        }
    }

    private readonly struct InteractableCueProfile
    {
        private const float DefaultPanScale = 520f;
        private const float DefaultPitchScale = 320f;
        private const float MinDelayResponseExponent = 0.05f;
        private const int SweepIntervalFrames = 10;

        private InteractableCueProfile(
            string id,
            float fundamentalFrequency,
            float[] partialMultipliers,
            ToneEnvelope envelope,
            float durationSeconds,
            float baseGain,
            float minVolume,
            float maxVolume,
            float maxAudibleDistanceTiles,
            int minIntervalFrames,
            int maxIntervalFrames,
            float panScalePixels = DefaultPanScale,
            float pitchScalePixels = DefaultPitchScale,
            float delayResponseExponent = 1f,
            string arrivalLabel = "",
            SoundStyle? soundStyle = null)
        {
            Id = id;
            FundamentalFrequency = fundamentalFrequency;
            PartialMultipliers = partialMultipliers ?? Array.Empty<float>();
            Envelope = envelope;
            DurationSeconds = durationSeconds;
            BaseGain = baseGain;
            MinVolume = minVolume;
            MaxVolume = maxVolume;
            MaxAudibleDistanceTiles = maxAudibleDistanceTiles;
            MinIntervalFrames = minIntervalFrames;
            MaxIntervalFrames = maxIntervalFrames;
            PanScalePixels = panScalePixels;
            PitchScalePixels = pitchScalePixels;
            DelayResponseExponent = Math.Max(MinDelayResponseExponent, delayResponseExponent);
            ArrivalLabel = arrivalLabel ?? string.Empty;
            SoundStyle = soundStyle;
        }

        public string Id { get; }
        public float FundamentalFrequency { get; }
        public float[] PartialMultipliers { get; }
        public ToneEnvelope Envelope { get; }
        public float DurationSeconds { get; }
        public float BaseGain { get; }
        public float MinVolume { get; }
        public float MaxVolume { get; }
        public float MaxAudibleDistanceTiles { get; }
        public int MinIntervalFrames { get; }
        public int MaxIntervalFrames { get; }
        public float PanScalePixels { get; }
        public float PitchScalePixels { get; }
        public float DelayResponseExponent { get; }
        public string ArrivalLabel { get; }
        public SoundStyle? SoundStyle { get; }

        public float ComputeVolume(float distanceTiles)
        {
            if (MaxAudibleDistanceTiles <= 0f)
            {
                return MaxVolume;
            }

            float normalized = Math.Clamp(distanceTiles / MaxAudibleDistanceTiles, 0f, 1f);
            float closeness = 1f - normalized;
            return MathHelper.Lerp(MinVolume, MaxVolume, closeness);
        }

        public int ComputeDelayFrames(float distanceTiles)
        {
            _ = distanceTiles;
            return Math.Max(1, MinIntervalFrames);
        }

        public static InteractableCueProfile Chest { get; } = new(
            id: "chest",
            fundamentalFrequency: 620f,
            partialMultipliers: new[] { 1.5f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.18f,
            baseGain: 0.4f,
            minVolume: 0.2f,
            maxVolume: 0.8f,
            maxAudibleDistanceTiles: 85f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 52,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a chest");

        public static InteractableCueProfile HeartCrystal { get; } = new(
            id: "heart-crystal",
            fundamentalFrequency: 880f,
            partialMultipliers: new[] { 2f, 2.5f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.22f,
            baseGain: 0.4f,
            minVolume: 0.22f,
            maxVolume: 0.94f,
            maxAudibleDistanceTiles: 90f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 52,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a heart crystal");

        public static InteractableCueProfile LifeFruit { get; } = new(
            id: "life-fruit",
            fundamentalFrequency: 940f,
            partialMultipliers: new[] { 1.6f, 2.15f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.22f,
            baseGain: 0.4f,
            minVolume: 0.22f,
            maxVolume: 0.92f,
            maxAudibleDistanceTiles: 90f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 52,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a life fruit");

        public static InteractableCueProfile PlanteraBulb { get; } = new(
            id: "plantera-bulb",
            fundamentalFrequency: 520f,
            partialMultipliers: new[] { 1.35f, 1.75f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.22f,
            baseGain: 0.4f,
            minVolume: 0.24f,
            maxVolume: 0.86f,
            maxAudibleDistanceTiles: 90f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 52,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a Plantera bulb");

        public static InteractableCueProfile DemonAltar { get; } = new(
            id: "demon-altar",
            fundamentalFrequency: 480f,
            partialMultipliers: new[] { 1.25f, 1.5f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.38f,
            minVolume: 0.2f,
            maxVolume: 0.7f,
            maxAudibleDistanceTiles: 85f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 56,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a demon altar");

        public static InteractableCueProfile CrimsonAltar { get; } = new(
            id: "crimson-altar",
            fundamentalFrequency: 540f,
            partialMultipliers: new[] { 1.15f, 1.45f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.38f,
            minVolume: 0.24f,
            maxVolume: 0.74f,
            maxAudibleDistanceTiles: 85f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 56,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a crimson altar");

        public static InteractableCueProfile ShadowOrb { get; } = new(
            id: "shadow-orb",
            fundamentalFrequency: 360f,
            partialMultipliers: new[] { 1.33f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.38f,
            minVolume: 0.2f,
            maxVolume: 0.7f,
            maxAudibleDistanceTiles: 80f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 54,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a shadow orb");

        public static InteractableCueProfile CrimsonHeart { get; } = new(
            id: "crimson-heart",
            fundamentalFrequency: 430f,
            partialMultipliers: new[] { 1.25f, 1.6f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.38f,
            minVolume: 0.22f,
            maxVolume: 0.72f,
            maxAudibleDistanceTiles: 80f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 54,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a crimson heart");

        public static InteractableCueProfile BeeLarva { get; } = new(
            id: "bee-larva",
            fundamentalFrequency: 720f,
            partialMultipliers: new[] { 1.25f, 1.75f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.42f,
            minVolume: 0.25f,
            maxVolume: 0.82f,
            maxAudibleDistanceTiles: 70f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 50,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a bee larva");

        public static InteractableCueProfile RescueNpc { get; } = new(
            id: "rescue-npc",
            fundamentalFrequency: 760f,
            partialMultipliers: new[] { 1.25f, 1.55f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.36f,
            minVolume: 0.22f,
            maxVolume: 0.8f,
            maxAudibleDistanceTiles: 80f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 48,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a rescue NPC");

        public static InteractableCueProfile FallenStar { get; } = new(
            id: "fallen-star",
            fundamentalFrequency: 1180f,
            partialMultipliers: new[] { 1.4f, 2f, 2.8f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.38f,
            minVolume: 0.24f,
            maxVolume: 0.78f,
            maxAudibleDistanceTiles: 95f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 46,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a fallen star");

        public static InteractableCueProfile Ore { get; } = new(
            id: "ore",
            fundamentalFrequency: 460f,
            partialMultipliers: new[] { 1.5f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.2f,
            baseGain: 0.36f,
            minVolume: 0.24f,
            maxVolume: 0.82f,
            maxAudibleDistanceTiles: 92f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 48,
            delayResponseExponent: 0.68f,
            arrivalLabel: string.Empty,
            soundStyle: SoundID.Tink);

        public static InteractableCueProfile Gem { get; } = new(
            id: "gem",
            fundamentalFrequency: 660f,
            partialMultipliers: new[] { 1.3f, 1.6f },
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WorldCue,
            durationSeconds: 0.22f,
            baseGain: 0.38f,
            minVolume: 0.24f,
            maxVolume: 0.86f,
            maxAudibleDistanceTiles: 92f,
            minIntervalFrames: SweepIntervalFrames,
            maxIntervalFrames: 48,
            delayResponseExponent: 0.68f,
            arrivalLabel: string.Empty,
            soundStyle: SoundID.Shatter);
    }
}
