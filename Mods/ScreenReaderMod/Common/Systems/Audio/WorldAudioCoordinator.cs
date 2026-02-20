#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Services;
using Terraria;
using static ScreenReaderMod.Common.Systems.InGameNarrationSystem;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// Coordinates all world audio emitters, managing their update cadence and lifecycle.
/// </summary>
internal sealed class WorldAudioCoordinator
{
    private readonly HostileStaticEmitter _hostileStaticEmitter;
    private readonly FootstepEmitter _footstepEmitter;
    private readonly ClimbEmitter _climbEmitter;
    private readonly BiomeEmitter _biomeEmitter;
    private readonly MultiplayerFootstepEmitter _multiplayerFootstepEmitter;
    private readonly CavitySonarEmitter _cavitySonarEmitter;
    private readonly PassageDetectorEmitter _passageDetectorEmitter;
    private readonly BreathEmitter _breathEmitter;
    private readonly CadenceGate _cadenceGate = new();

    public WorldAudioCoordinator()
    {
        _hostileStaticEmitter = new HostileStaticEmitter();
        _footstepEmitter = new FootstepEmitter();
        _climbEmitter = new ClimbEmitter();
        _biomeEmitter = new BiomeEmitter();
        _multiplayerFootstepEmitter = new MultiplayerFootstepEmitter();
        _cavitySonarEmitter = new CavitySonarEmitter();
        _passageDetectorEmitter = new PassageDetectorEmitter();
        _breathEmitter = new BreathEmitter();
    }

    public WorldAudioCoordinator(
        HostileStaticEmitter hostileStaticEmitter,
        FootstepEmitter footstepEmitter,
        ClimbEmitter climbEmitter,
        BiomeEmitter biomeEmitter,
        MultiplayerFootstepEmitter multiplayerFootstepEmitter,
        CavitySonarEmitter cavitySonarEmitter)
    {
        _hostileStaticEmitter = hostileStaticEmitter;
        _footstepEmitter = footstepEmitter;
        _climbEmitter = climbEmitter;
        _biomeEmitter = biomeEmitter;
        _multiplayerFootstepEmitter = multiplayerFootstepEmitter;
        _cavitySonarEmitter = cavitySonarEmitter;
        _passageDetectorEmitter = new PassageDetectorEmitter();
        _breathEmitter = new BreathEmitter();
    }

    /// <summary>
    /// Updates all audio emitters with appropriate cadence.
    /// </summary>
    /// <param name="context">The narration service context containing the player.</param>
    public void Update(NarrationServiceContext context)
    {
        Player player = context.Player;
        if (player is null || !player.active)
        {
            Reset();
            return;
        }

        Run("hostile-static", 1, () => _hostileStaticEmitter.Update(player));
        Run("footstep", 1, () => _footstepEmitter.Update(player));
        Run("climb", 1, () => _climbEmitter.Update(player));
        Run("biome", 12, () => _biomeEmitter.Update(player));
        Run("multiplayer-footstep", 1, () => _multiplayerFootstepEmitter.Update(player));
        Run("cavity-sonar", 1, () => _cavitySonarEmitter.Update(player));
        Run("passage-detector", 1, () => _passageDetectorEmitter.Update(player));
        Run("breath", 1, () => _breathEmitter.Update(player));
    }

    /// <summary>
    /// Resets all emitters to their initial state.
    /// </summary>
    public void Reset()
    {
        _hostileStaticEmitter.Reset();
        _footstepEmitter.Reset();
        _climbEmitter.Reset();
        _biomeEmitter.Reset();
        _multiplayerFootstepEmitter.Reset();
        _cavitySonarEmitter.Reset();
        _passageDetectorEmitter.Reset();
        _breathEmitter.Reset();
        _cadenceGate.Reset();
    }

    /// <summary>
    /// Disposes of any static audio resources held by emitters.
    /// </summary>
    public void ResetStaticResources()
    {
        _cadenceGate.Reset();
        HostileStaticEmitter.DisposeStaticResources();
        FootstepToneProvider.DisposeStaticResources();
        PassageDetectorEmitter.DisposeStaticResources();
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
