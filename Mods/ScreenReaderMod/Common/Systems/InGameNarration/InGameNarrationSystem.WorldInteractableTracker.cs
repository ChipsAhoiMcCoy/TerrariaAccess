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
    private sealed class WorldInteractableTracker
    {
        private const int ScanIntervalTicks = 18;
        private const int MaxConcurrentCues = 6;
        private const float SecondaryCueVolumeScale = 0.25f;

        private static readonly Dictionary<string, SoundEffect> ToneCache = new();

        private readonly List<WorldInteractableSource> _sources = new();
        private readonly List<Candidate> _candidateBuffer = new();
        private readonly List<Candidate> _trackedCandidates = new();
        private readonly List<CandidateDistance> _distanceScratch = new();
        private readonly Dictionary<TrackedInteractableKey, int> _nextPingFrameByKey = new();
        private readonly HashSet<TrackedInteractableKey> _visibleThisFrame = new();
        private readonly List<TrackedInteractableKey> _staleKeys = new();
        private readonly HashSet<TrackedInteractableKey> _arrivedKeys = new();
        private readonly List<SoundEffectInstance> _liveInstances = new();

        private int _ticksUntilNextScan;
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
                _nextPingFrameByKey.Clear();
                _candidateBuffer.Clear();
                _trackedCandidates.Clear();
                _visibleThisFrame.Clear();
                _distanceScratch.Clear();
                _staleKeys.Clear();
                _arrivedKeys.Clear();
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
                    _nextPingFrameByKey.Clear();
                    _candidateBuffer.Clear();
                    _trackedCandidates.Clear();
                    _distanceScratch.Clear();
                    _visibleThisFrame.Clear();
                    _staleKeys.Clear();
                    _arrivedKeys.Clear();
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
                TrimInactiveKeys();
                CleanupFinishedInstances();
                return;
            }

            Vector2 playerCenter = player.Center;

            _distanceScratch.Clear();
            foreach (Candidate candidate in _trackedCandidates)
            {
                float distanceTiles = Vector2.Distance(candidate.WorldPosition, playerCenter) / 16f;
                if (distanceTiles > candidate.Profile.MaxAudibleDistanceTiles)
                {
                    continue;
                }

                _distanceScratch.Add(new CandidateDistance(candidate, distanceTiles));
            }

            if (_distanceScratch.Count == 0)
            {
                TrimInactiveKeys();
                CleanupFinishedInstances();
                return;
            }

            _distanceScratch.Sort(static (left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

            _visibleThisFrame.Clear();

            int limit = Math.Min(MaxConcurrentCues, _distanceScratch.Count);
            TrackedInteractableKey primaryKey = default;
            bool hasPrimary = false;
            if (limit > 0)
            {
                primaryKey = _distanceScratch[0].Candidate.Key;
                hasPrimary = true;
            }

            for (int i = 0; i < limit; i++)
            {
                CandidateDistance entry = _distanceScratch[i];
                _visibleThisFrame.Add(entry.Candidate.Key);
                UpdateArrivalState(entry);
                bool isPrimaryCue = hasPrimary && entry.Candidate.Key.Equals(primaryKey);
                EmitIfDue(playerCenter, entry, isPrimaryCue);
            }

            TrimInactiveKeys();
            CleanupFinishedInstances();
        }

        public void Reset()
        {
            _ticksUntilNextScan = 0;
            _candidateBuffer.Clear();
            _trackedCandidates.Clear();
            _distanceScratch.Clear();
            _nextPingFrameByKey.Clear();
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

        private void PrepareSources(Player player)
        {
            foreach (WorldInteractableSource source in _sources)
            {
                source.PrepareFrame(player);
            }
        }

        private void EmitIfDue(Vector2 playerCenter, CandidateDistance entry, bool isPrimaryCue)
        {
            TrackedInteractableKey key = entry.Candidate.Key;

            if (!_nextPingFrameByKey.TryGetValue(key, out int scheduledFrame) ||
                Main.GameUpdateCount >= (uint)Math.Max(0, scheduledFrame))
            {
                PlayCue(playerCenter, entry, isPrimaryCue);

                int delay = entry.Candidate.Profile.ComputeDelayFrames(entry.DistanceTiles);
                int nextFrame = delay <= 0 ? 0 : ScheduleNextFrame(delay);
                _nextPingFrameByKey[key] = nextFrame;
            }
        }

        private void TrimInactiveKeys()
        {
            _staleKeys.Clear();
            foreach (TrackedInteractableKey key in _nextPingFrameByKey.Keys)
            {
                if (!_visibleThisFrame.Contains(key))
                {
                    _staleKeys.Add(key);
                }
            }

            foreach (TrackedInteractableKey key in _staleKeys)
            {
                _nextPingFrameByKey.Remove(key);
                _arrivedKeys.Remove(key);
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
                string label = entry.Candidate.Profile.ArrivalLabel;
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

        private void PlayCue(Vector2 playerCenter, CandidateDistance entry, bool isPrimaryCue)
        {
            if (Main.soundVolume <= 0f)
            {
                return;
            }

            Vector2 offset = entry.Candidate.WorldPosition - playerCenter;
            InteractableCueProfile profile = entry.Candidate.Profile;

            float pitch = MathHelper.Clamp(-offset.Y / profile.PitchScalePixels, -0.8f, 0.8f);
            float pan = MathHelper.Clamp(offset.X / profile.PanScalePixels, -1f, 1f);
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
                volume * (isPrimaryCue ? 1f : SecondaryCueVolumeScale),
                0f,
                1f);
            if (scaledVolume <= 0f)
            {
                return;
            }

            SoundEffect tone = EnsureTone(profile);
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pitch = pitch;
            instance.Pan = pan;
            instance.Volume = scaledVolume;

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
                        buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, localId), worldPosition, definition.Profile));
                    }
                }
            }
        }

        protected virtual bool ShouldIncludeAnchor(TileInteractableDefinition definition, Player player, Point anchor) => true;

        private static bool IsAnchorTile(Tile tile, TileInteractableDefinition definition)
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
    }

    private sealed class StaticTileInteractableSource : TileInteractableSource
    {
        public StaticTileInteractableSource(float scanRadiusTiles, params TileInteractableDefinition[] definitions)
            : base(scanRadiusTiles, definitions)
        {
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

                buffer.Add(new Candidate(new TrackedInteractableKey(SourceId, i), center, _profile));
            }
        }
    }

    private readonly record struct Candidate(TrackedInteractableKey Key, Vector2 WorldPosition, InteractableCueProfile Profile);

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
            string arrivalLabel = "")
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
            if (MaxAudibleDistanceTiles <= 0f)
            {
                return MinIntervalFrames;
            }

            float normalized = Math.Clamp(distanceTiles / MaxAudibleDistanceTiles, 0f, 1f);
            if (DelayResponseExponent != 1f)
            {
                float closeness = 1f - normalized;
                float shapedCloseness = MathF.Pow(Math.Clamp(closeness, 0f, 1f), DelayResponseExponent);
                normalized = 1f - shapedCloseness;
            }
            float frames = MathHelper.Lerp(MinIntervalFrames, MaxIntervalFrames, normalized);
            return Math.Max(1, (int)MathF.Round(frames));
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
            minIntervalFrames: 10,
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
            minIntervalFrames: 8,
            maxIntervalFrames: 52,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a heart crystal");

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
            minIntervalFrames: 12,
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
            minIntervalFrames: 12,
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
            minIntervalFrames: 10,
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
            minIntervalFrames: 10,
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
            minIntervalFrames: 8,
            maxIntervalFrames: 50,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a bee larva");

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
            minIntervalFrames: 8,
            maxIntervalFrames: 46,
            delayResponseExponent: 0.7f,
            arrivalLabel: "a fallen star");
    }
}
