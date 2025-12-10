#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WorldPositionalAudioService
    {
        private readonly TreasureBagBeaconEmitter _treasureBagBeaconEmitter;
        private readonly HostileStaticAudioEmitter _hostileStaticAudioEmitter;
        private readonly FootstepAudioEmitter _footstepAudioEmitter;
        private readonly ClimbAudioEmitter _climbAudioEmitter;
        private readonly BiomeAnnouncementEmitter _biomeAnnouncementEmitter;
        private readonly CadenceGate _cadenceGate = new();

        public WorldPositionalAudioService(
            TreasureBagBeaconEmitter treasureBagBeaconEmitter,
            HostileStaticAudioEmitter hostileStaticAudioEmitter,
            FootstepAudioEmitter footstepAudioEmitter,
            ClimbAudioEmitter climbAudioEmitter,
            BiomeAnnouncementEmitter biomeAnnouncementEmitter)
        {
            _treasureBagBeaconEmitter = treasureBagBeaconEmitter;
            _hostileStaticAudioEmitter = hostileStaticAudioEmitter;
            _footstepAudioEmitter = footstepAudioEmitter;
            _climbAudioEmitter = climbAudioEmitter;
            _biomeAnnouncementEmitter = biomeAnnouncementEmitter;
        }

        public void Update(NarrationServiceContext context)
        {
            Player player = context.Player;
            if (player is null || !player.active)
            {
                Reset();
                return;
            }

            Run("hostile-static", 1, () => _hostileStaticAudioEmitter.Update(player));
            Run("treasure-bag", 2, () => _treasureBagBeaconEmitter.Update(player));
            Run("footstep", 1, () => _footstepAudioEmitter.Update(player));
            Run("climb", 1, () => _climbAudioEmitter.Update(player));
            Run("biome", 12, () => _biomeAnnouncementEmitter.Update(player));
        }

        public void Reset()
        {
            _treasureBagBeaconEmitter.Reset();
            _hostileStaticAudioEmitter.Reset();
            _footstepAudioEmitter.Reset();
            _climbAudioEmitter.Reset();
            _biomeAnnouncementEmitter.Reset();
            _cadenceGate.Reset();
        }

        public void ResetStaticResources()
        {
            _cadenceGate.Reset();
            TreasureBagBeaconEmitter.DisposeStaticResources();
            HostileStaticAudioEmitter.DisposeStaticResources();
            FootstepToneProvider.DisposeStaticResources();
        }

        private void Run(string key, uint intervalFrames, Action action)
        {
            if (!_cadenceGate.ShouldRun(key, intervalFrames))
            {
                return;
            }

            action();
            NarrationInstrumentationContext.RecordKey($"world-audio:{key}");
        }

        private sealed class CadenceGate
        {
            private readonly Dictionary<string, uint> _nextFrame = new(StringComparer.OrdinalIgnoreCase);

            public bool ShouldRun(string key, uint minIntervalFrames)
            {
                uint now = Main.GameUpdateCount;
                if (_nextFrame.TryGetValue(key, out uint scheduled) && now < scheduled)
                {
                    return false;
                }

                _nextFrame[key] = now + minIntervalFrames;
                return true;
            }

            public void Reset()
            {
                _nextFrame.Clear();
            }
        }
    }
}
