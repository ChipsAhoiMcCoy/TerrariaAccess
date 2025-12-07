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
    private sealed class TreasureBagBeaconEmitter
    {
        private const float PitchScale = 320f;
        private const float PanScalePixels = 480f;
        private const float DistanceReferenceTiles = 110f;
        private const float MinVolume = 0.24f;
        private const float VolumeRange = 0.72f;
        private const int SampleRate = 44100;
        private const int BaseFrequencyHz = 490;
        private const int CyclesPerBuffer = 64;

        private readonly Dictionary<int, SoundEffectInstance> _activeInstances = new();
        private readonly HashSet<int> _currentFrameIndices = new();
        private readonly List<int> _staleIndices = new();
        private static SoundEffect? _treasureBagTone;

        public void Update(Player player)
        {
            if (Main.dedServ)
            {
                return;
            }

            if (Main.soundVolume <= 0f)
            {
                StopAllInstances();
                return;
            }

            _currentFrameIndices.Clear();
            Vector2 playerCenter = player.Center;

            for (int i = 0; i < Main.maxItems; i++)
            {
                Item item = Main.item[i];
                if (!item.active || item.stack <= 0)
                {
                    continue;
                }

                if (!ItemID.Sets.BossBag[item.type])
                {
                    continue;
                }

                if (!IsWorldPositionApproximatelyOnScreen(item.Center))
                {
                    continue;
                }

                _currentFrameIndices.Add(i);
                EmitOrUpdateInstance(i, item.Center, playerCenter);
            }

            if (_activeInstances.Count == 0)
            {
                return;
            }

            _staleIndices.Clear();

            foreach (int index in _activeInstances.Keys)
            {
                if (!_currentFrameIndices.Contains(index))
                {
                    _staleIndices.Add(index);
                }
            }

            foreach (int index in _staleIndices)
            {
                if (_activeInstances.TryGetValue(index, out SoundEffectInstance? instance))
                {
                    StopInstance(index, instance);
                }
            }

            _staleIndices.Clear();
        }

        public void Reset()
        {
            StopAllInstances();
        }

        public static void DisposeStaticResources()
        {
            _treasureBagTone?.Dispose();
            _treasureBagTone = null;
        }

        private void EmitOrUpdateInstance(int itemIndex, Vector2 bagCenter, Vector2 playerCenter)
        {
            try
            {
                SoundEffectInstance instance = EnsureInstance(itemIndex);

                Vector2 offset = bagCenter - playerCenter;
                float pitch = MathHelper.Clamp(-offset.Y / PitchScale, -0.6f, 0.6f);
                float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);
                float distancePixels = offset.Length();
                float distanceTiles = distancePixels / 16f;
                float distanceFactor = 1f / (1f + (distanceTiles / Math.Max(1f, DistanceReferenceTiles)));
                float baseVolume = MathHelper.Clamp(MinVolume + distanceFactor * VolumeRange, 0f, 1f);
                float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                    baseVolume,
                    distanceTiles,
                    DistanceReferenceTiles,
                    minFactor: 0.4f);
                float volume = loudness * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;

                instance.Pitch = pitch;
                instance.Pan = pan;
                instance.Volume = volume;

                if (instance.State != SoundState.Playing)
                {
                    try
                    {
                        instance.Play();
                    }
                    catch (Exception inner)
                    {
                        instance.Dispose();
                        _activeInstances.Remove(itemIndex);
                        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Debug($"[TreasureBagTone] Play failed: {inner.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[TreasureBagTone] Update failed: {ex.Message}");
                if (_activeInstances.TryGetValue(itemIndex, out SoundEffectInstance? existing))
                {
                    StopInstance(itemIndex, existing);
                }
            }
        }

        private SoundEffectInstance EnsureInstance(int itemIndex)
        {
            if (_activeInstances.TryGetValue(itemIndex, out SoundEffectInstance? existing))
            {
                if (!existing.IsDisposed)
                {
                    return existing;
                }

                _activeInstances.Remove(itemIndex);
            }

            SoundEffect tone = EnsureTreasureBagTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = true;
            _activeInstances[itemIndex] = instance;
            return instance;
        }

        private static SoundEffect EnsureTreasureBagTone()
        {
            if (_treasureBagTone is { IsDisposed: false })
            {
                return _treasureBagTone;
            }

            _treasureBagTone?.Dispose();
            _treasureBagTone = CreateTreasureBagTone();
            return _treasureBagTone;
        }

        private static SoundEffect CreateTreasureBagTone()
        {
            const int samplesPerCycle = SampleRate / BaseFrequencyHz;
            int sampleCount = samplesPerCycle * CyclesPerBuffer;
            byte[] buffer = new byte[sampleCount * sizeof(short)];

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = MathF.Sin(MathHelper.TwoPi * i / samplesPerCycle) * 0.32f;
                short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(value & 0xFF);
                buffer[index + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
        }

        private void StopInstance(int itemIndex, SoundEffectInstance instance)
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
            _activeInstances.Remove(itemIndex);
        }

        private void StopAllInstances()
        {
            if (_activeInstances.Count > 0)
            {
                _staleIndices.Clear();
                foreach (int index in _activeInstances.Keys)
                {
                    _staleIndices.Add(index);
                }

                foreach (int index in _staleIndices)
                {
                    if (_activeInstances.TryGetValue(index, out SoundEffectInstance? instance))
                    {
                        StopInstance(index, instance);
                    }
                }

                _staleIndices.Clear();
                _activeInstances.Clear();
            }

            _currentFrameIndices.Clear();
        }
    }
}
