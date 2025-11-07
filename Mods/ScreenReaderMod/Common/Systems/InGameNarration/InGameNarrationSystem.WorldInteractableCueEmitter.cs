#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WorldInteractableCueEmitter
    {
        private const int ScanRadiusTiles = 70;
        private const int UpdateIntervalTicks = 18;
        private const int MaxConcurrentPlayingCues = 8;

        private int _ticksUntilNextScan;
        private int _lastRecordedChestIndex = -1;
        private readonly HashSet<Point> _openedChestAnchors = new();

        private static readonly Dictionary<InteractableKind, SoundEffectInstance> InstanceCache = new();
        private static readonly Dictionary<InteractableKind, SoundEffect> ToneCache = new();

        private static readonly InteractableDefinition[] Definitions =
        {
            new(
                InteractableKind.Chest,
                new[] { (int)TileID.Containers, (int)TileID.Containers2 },
                frameWidth: 36,
                frameHeight: 36,
                widthTiles: 2,
                heightTiles: 2,
                baseFrequency: 620f,
                baseVolume: 0.52f,
                partialMultipliers: new[] { 1.5f }),
            new(
                InteractableKind.HeartCrystal,
                new[] { (int)TileID.Heart },
                frameWidth: 36,
                frameHeight: 54,
                widthTiles: 2,
                heightTiles: 3,
                baseFrequency: 880f,
                baseVolume: 0.5f,
                partialMultipliers: new[] { 2f, 2.5f }),
            new(
                InteractableKind.DemonAltar,
                new[] { (int)TileID.DemonAltar },
                frameWidth: 54,
                frameHeight: 36,
                widthTiles: 3,
                heightTiles: 2,
                baseFrequency: 480f,
                baseVolume: 0.48f,
                partialMultipliers: new[] { 1.25f, 1.5f })
        };

        public void Update(Player player)
        {
            CleanupFinishedInstances();
            TrackOpenedChest(player);

            if (_ticksUntilNextScan > 0)
            {
                _ticksUntilNextScan--;
                return;
            }

            _ticksUntilNextScan = UpdateIntervalTicks;

            Vector2 playerCenter = player.Center;

            foreach (InteractableDefinition definition in Definitions)
            {
                if (!TryFindNearest(playerCenter, definition, out Vector2 worldPosition))
                {
                    continue;
                }

                PlayCue(playerCenter, worldPosition, definition);
            }
        }

        public void Reset()
        {
            _openedChestAnchors.Clear();
            _lastRecordedChestIndex = -1;
            _ticksUntilNextScan = 0;
        }

        public void SetOpenedChestAnchors(ReadOnlySpan<Point> anchors)
        {
            _openedChestAnchors.Clear();
            foreach (Point anchor in anchors)
            {
                _openedChestAnchors.Add(anchor);
            }
        }

        public IReadOnlyCollection<Point> GetOpenedChestAnchors() => _openedChestAnchors;

        private void TrackOpenedChest(Player player)
        {
            int chestIndex = player.chest;
            if (chestIndex == _lastRecordedChestIndex)
            {
                return;
            }

            _lastRecordedChestIndex = chestIndex;

            if (chestIndex < 0 || chestIndex >= Main.chest.Length)
            {
                return;
            }

            Chest? chest = Main.chest[chestIndex];
            if (chest is null)
            {
                return;
            }

            if (chest.x < 0 || chest.y < 0)
            {
                return;
            }

            _openedChestAnchors.Add(new Point(chest.x, chest.y));
        }

        private bool TryFindNearest(Vector2 playerCenter, InteractableDefinition definition, out Vector2 worldPosition)
        {
            int playerTileX = (int)(playerCenter.X / 16f);
            int playerTileY = (int)(playerCenter.Y / 16f);

            int minX = Math.Max(0, playerTileX - ScanRadiusTiles);
            int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + ScanRadiusTiles);
            int minY = Math.Max(0, playerTileY - ScanRadiusTiles);
            int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + ScanRadiusTiles);

            float bestDistanceSq = float.MaxValue;
            Vector2 bestWorld = default;
            bool found = false;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile)
                    {
                        continue;
                    }

                    if (!definition.MatchesTile(tile.TileType))
                    {
                        continue;
                    }

                    if (!IsAnchorTile(tile, definition))
                    {
                        continue;
                    }

                    if (definition.Kind == InteractableKind.Chest && _openedChestAnchors.Contains(new Point(x, y)))
                    {
                        continue;
                    }

                    Vector2 definitionCenter = GetObjectCenterWorld(x, y, definition);
                    float distanceSq = Vector2.DistanceSquared(definitionCenter, playerCenter);
                    if (distanceSq >= bestDistanceSq)
                    {
                        continue;
                    }

                    bestDistanceSq = distanceSq;
                    bestWorld = definitionCenter;
                    found = true;
                }
            }

            worldPosition = bestWorld;
            return found;
        }

        private static Vector2 GetObjectCenterWorld(int tileX, int tileY, InteractableDefinition definition)
        {
            float centerX = (tileX + definition.WidthTiles * 0.5f) * 16f;
            float centerY = (tileY + definition.HeightTiles * 0.5f) * 16f;
            return new Vector2(centerX, centerY);
        }

        private static bool IsAnchorTile(Tile tile, InteractableDefinition definition)
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

            return frameX % definition.FrameWidth == 0 && frameY % definition.FrameHeight == 0;
        }

        private static void PlayCue(Vector2 playerCenter, Vector2 worldPosition, InteractableDefinition definition)
        {
            if (Main.soundVolume <= 0f)
            {
                return;
            }

            if (CountPlayingInstances() >= MaxConcurrentPlayingCues)
            {
                return;
            }

            Vector2 offset = worldPosition - playerCenter;
            float distance = offset.Length();
            float normalizedDistance = MathHelper.Clamp(1f - distance / (ScanRadiusTiles * 16f), 0.1f, 1f);
            float pan = MathHelper.Clamp(offset.X / (ScanRadiusTiles * 16f * 0.75f), -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.75f, 0.75f);
            float baseVolume = definition.BaseVolume * normalizedDistance;
            float finalVolume = MathHelper.Clamp(baseVolume * Main.soundVolume, 0f, 1f);
            if (finalVolume <= 0f)
            {
                return;
            }

            SoundEffectInstance instance = RentInstance(definition);
            instance.IsLooped = false;
            instance.Pan = pan;
            instance.Pitch = pitch;
            instance.Volume = finalVolume;
            instance.Play();
        }

        private static int CountPlayingInstances()
        {
            int playing = 0;
            foreach (SoundEffectInstance instance in InstanceCache.Values)
            {
                if (instance.IsDisposed)
                {
                    continue;
                }

                if (instance.State == SoundState.Playing)
                {
                    playing++;
                }
            }

            return playing;
        }

        private static SoundEffectInstance RentInstance(InteractableDefinition definition)
        {
            if (InstanceCache.TryGetValue(definition.Kind, out SoundEffectInstance? cached))
            {
                if (cached.IsDisposed)
                {
                    InstanceCache.Remove(definition.Kind);
                }
                else
                {
                    try
                    {
                        if (cached.State == SoundState.Playing)
                        {
                            cached.Stop();
                        }
                    }
                    catch
                    {
                        // ignore stop exceptions and recreate below
                    }

                    return cached;
                }
            }

            SoundEffect tone = EnsureTone(definition);
            SoundEffectInstance created = tone.CreateInstance();
            created.IsLooped = false;
            InstanceCache[definition.Kind] = created;
            return created;
        }

        private static SoundEffect EnsureTone(InteractableDefinition definition)
        {
            if (ToneCache.TryGetValue(definition.Kind, out SoundEffect? tone) && tone is not null && !tone.IsDisposed)
            {
                return tone;
            }

            tone?.Dispose();
            SoundEffect created = CreateTone(definition.BaseFrequency, definition.PartialMultipliers);
            ToneCache[definition.Kind] = created;
            return created;
        }

        private static SoundEffect CreateTone(float fundamental, float[] partialMultipliers)
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.18f;
            int sampleCount = Math.Max(1, (int)(sampleRate * durationSeconds));
            byte[] buffer = new byte[sampleCount * sizeof(short)];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float window = 0.5f - 0.5f * MathF.Cos((2f * MathF.PI * i) / Math.Max(1, sampleCount - 1));
                float sample = MathF.Sin(MathHelper.TwoPi * fundamental * t);

                for (int p = 0; p < partialMultipliers.Length; p++)
                {
                    float multiplier = partialMultipliers[p];
                    float partialAmplitude = 0.6f / (p + 1f);
                    sample += MathF.Sin(MathHelper.TwoPi * fundamental * multiplier * t) * partialAmplitude;
                }

                sample *= window * 0.35f;
                short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(value & 0xFF);
                buffer[index + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
        }

        private static void CleanupFinishedInstances()
        {
            foreach ((InteractableKind kind, SoundEffectInstance instance) in InstanceCache.ToArray())
            {
                if (instance.IsDisposed)
                {
                    InstanceCache.Remove(kind);
                    continue;
                }

                if (instance.State != SoundState.Stopped)
                {
                    continue;
                }

                if (Main.soundVolume <= 0f)
                {
                    try
                    {
                        instance.Stop();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static void DisposeStaticResources()
        {
            foreach ((InteractableKind kind, SoundEffectInstance instance) in InstanceCache.ToArray())
            {
                try
                {
                    instance.Stop();
                }
                catch
                {
                }

                instance.Dispose();
                InstanceCache.Remove(kind);
            }

            foreach ((InteractableKind kind, SoundEffect tone) in ToneCache.ToArray())
            {
                tone.Dispose();
                ToneCache.Remove(kind);
            }
        }

        private enum InteractableKind
        {
            Chest,
            HeartCrystal,
            DemonAltar
        }

        private readonly struct InteractableDefinition
        {
            public InteractableDefinition(
                InteractableKind kind,
                int[] tileTypes,
                int frameWidth,
                int frameHeight,
                int widthTiles,
                int heightTiles,
                float baseFrequency,
                float baseVolume,
                float[] partialMultipliers)
            {
                Kind = kind;
                TileTypes = tileTypes;
                FrameWidth = frameWidth;
                FrameHeight = frameHeight;
                WidthTiles = widthTiles;
                HeightTiles = heightTiles;
                BaseFrequency = baseFrequency;
                BaseVolume = baseVolume;
                PartialMultipliers = partialMultipliers;
            }

            public InteractableKind Kind { get; }
            public int[] TileTypes { get; }
            public int FrameWidth { get; }
            public int FrameHeight { get; }
            public int WidthTiles { get; }
            public int HeightTiles { get; }
            public float BaseFrequency { get; }
            public float BaseVolume { get; }
            public float[] PartialMultipliers { get; }

            public bool MatchesTile(int tileType)
            {
                foreach (int type in TileTypes)
                {
                    if (type == tileType)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
