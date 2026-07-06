#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Emits quiet continuous tones for nearby solid collision on the player's left, right, and above.
/// These are proximity probes, separate from wall-collision taps that only play while pushing into a wall.
/// </summary>
internal sealed class WallToneEmitter : AudioEmitterBase
{
    private const int SideProbeRangeTiles = 3;
    private const int CeilingProbeRangeTiles = 5;
    private const int ProbeIntervalFrames = 3;
    private const float ProbeInsetPixels = 2f;

    private const float LeftFrequency = 260f;
    private const float RightFrequency = 340f;
    private const float CeilingFrequency = 560f;

    private const float LeftNormalizedScreenX = -0.85f;
    private const float RightNormalizedScreenX = 0.85f;
    private const float CeilingNormalizedScreenX = 0f;

    private const float SideBaseVolume = 0.12f;
    private const float CeilingBaseVolume = 0.09f;
    private const float FarDistanceVolumeScale = 0.35f;

    private readonly LoopingToneChannel _leftTone = new(LeftFrequency, LeftNormalizedScreenX);
    private readonly LoopingToneChannel _rightTone = new(RightFrequency, RightNormalizedScreenX);
    private readonly LoopingToneChannel _ceilingTone = new(CeilingFrequency, CeilingNormalizedScreenX);

    private int _probeTimer;
    private WallProbeState _lastProbeState;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player) || ShouldSuppress(player))
        {
            Reset();
            return;
        }

        TerrariaAccessConfig? config = TerrariaAccessConfig.Instance;
        bool enabled = config?.WallTonesEnabled ?? true;
        float configVolume = config?.WallToneVolume ?? 1f;
        if (!enabled || configVolume <= 0f)
        {
            Reset();
            return;
        }

        if (_probeTimer <= 0)
        {
            _lastProbeState = Probe(player);
            _probeTimer = ProbeIntervalFrames;
        }
        else
        {
            _probeTimer--;
        }

        ApplyProbeState(configVolume);
    }

    public override void Reset()
    {
        _probeTimer = 0;
        _lastProbeState = default;
        _leftTone.Stop();
        _rightTone.Stop();
        _ceilingTone.Stop();
    }

    public void DisposeStaticResources()
    {
        Reset();
        LoopingToneChannel.DisposeStaticResources();
    }

    private static bool ShouldSuppress(Player player)
    {
        if (Main.playerInventory ||
            Main.ingameOptionsWindow ||
            Main.inFancyUI ||
            Main.gameMenu ||
            Main.InGuideCraftMenu ||
            Main.InReforgeMenu ||
            Main.CreativeMenu.Enabled ||
            Main.hairWindow ||
            Main.clothesWindow ||
            Main.drawingPlayerChat ||
            Main.editChest ||
            Main.editSign ||
            player.talkNPC != -1 ||
            player.sign != -1 ||
            player.chest != -1 ||
            Main.npcShop != 0 ||
            player.tileEntityAnchor.InUse)
        {
            return true;
        }

        return Main.InGameUI?.CurrentState is not null;
    }

    private void ApplyProbeState(float configVolume)
    {
        _leftTone.SetVolume(ComputeVolume(_lastProbeState.LeftDistanceTiles, SideProbeRangeTiles, SideBaseVolume, configVolume));
        _rightTone.SetVolume(ComputeVolume(_lastProbeState.RightDistanceTiles, SideProbeRangeTiles, SideBaseVolume, configVolume));
        _ceilingTone.SetVolume(ComputeVolume(_lastProbeState.CeilingDistanceTiles, CeilingProbeRangeTiles, CeilingBaseVolume, configVolume));
    }

    private static float ComputeVolume(int distanceTiles, int maxDistanceTiles, float baseVolume, float configVolume)
    {
        if (distanceTiles <= 0 || maxDistanceTiles <= 0)
        {
            return 0f;
        }

        float normalizedDistance = MathHelper.Clamp((distanceTiles - 1f) / Math.Max(1f, maxDistanceTiles - 1f), 0f, 1f);
        float closeness = 1f - normalizedDistance;
        float distanceScale = MathHelper.Lerp(FarDistanceVolumeScale, 1f, closeness * closeness);
        return MathHelper.Clamp(baseVolume * distanceScale * configVolume, 0f, 1f);
    }

    private static WallProbeState Probe(Player player)
    {
        return new WallProbeState(
            LeftDistanceTiles: MeasureHorizontalCollision(player, -1),
            RightDistanceTiles: MeasureHorizontalCollision(player, 1),
            CeilingDistanceTiles: MeasureCeilingCollision(player));
    }

    private static int MeasureHorizontalCollision(Player player, int direction)
    {
        Rectangle hitbox = player.Hitbox;
        Vector2 basePosition = new(hitbox.X, hitbox.Y + ProbeInsetPixels);
        int probeHeight = Math.Max(1, (int)(hitbox.Height - ProbeInsetPixels * 2f));

        for (int distance = 1; distance <= SideProbeRangeTiles; distance++)
        {
            Vector2 probePosition = basePosition + new Vector2(direction * distance * 16f, 0f);
            if (Collision.SolidCollision(probePosition, hitbox.Width, probeHeight))
            {
                return distance;
            }
        }

        return 0;
    }

    private static int MeasureCeilingCollision(Player player)
    {
        Rectangle hitbox = player.Hitbox;
        Vector2 basePosition = new(hitbox.X + ProbeInsetPixels, hitbox.Y);
        int probeWidth = Math.Max(1, (int)(hitbox.Width - ProbeInsetPixels * 2f));

        for (int distance = 1; distance <= CeilingProbeRangeTiles; distance++)
        {
            Vector2 probePosition = basePosition + new Vector2(0f, -distance * 16f);
            if (Collision.SolidCollision(probePosition, probeWidth, hitbox.Height))
            {
                return distance;
            }
        }

        return 0;
    }

    private readonly record struct WallProbeState(
        int LeftDistanceTiles,
        int RightDistanceTiles,
        int CeilingDistanceTiles);

    private sealed class LoopingToneChannel
    {
        private const int SampleRate = 44100;
        private const float LoopDurationSeconds = 0.2f;
        private const float OutputGain = 0.65f;

        private static readonly SpatializedSoundCache<int> ToneCache = new();

        private readonly float _frequency;
        private readonly float _normalizedScreenX;
        private SoundEffectInstance? _instance;

        public LoopingToneChannel(float frequency, float normalizedScreenX)
        {
            _frequency = frequency;
            _normalizedScreenX = normalizedScreenX;
        }

        public void SetVolume(float volume)
        {
            float safeVolume = SpatializedSoundEngine.NormalizeVolume(volume);
            if (safeVolume <= 0f)
            {
                Stop();
                return;
            }

            if (!IsInstanceUsable(_instance))
            {
                SoundEffect tone = EnsureTone(_frequency, _normalizedScreenX);
                _instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                    tone,
                    safeVolume,
                    looped: true);
                return;
            }

            SpatializedSoundEngine.SetWorldCueVolume(_instance, safeVolume);
        }

        public void Stop()
        {
            if (_instance is null)
            {
                return;
            }

            SpatializedSoundEngine.StopAndDispose(_instance);
            _instance = null;
        }

        public static void DisposeStaticResources()
        {
            ToneCache.Dispose();
        }

        private static SoundEffect EnsureTone(float frequency, float normalizedScreenX)
        {
            int cacheFrequency = Math.Clamp((int)MathF.Round(frequency), 40, 12000);
            return ToneCache.GetOrCreate(
                cacheFrequency,
                normalizedScreenX,
                quantizedNormalizedScreenX => CreateLoopingTone(cacheFrequency, quantizedNormalizedScreenX));
        }

        private static SoundEffect CreateLoopingTone(float frequency, float normalizedScreenX)
        {
            int sampleCount = Math.Max(1, (int)(SampleRate * LoopDurationSeconds));
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float phase = MathHelper.TwoPi * frequency * time;
                float fundamental = MathF.Sin(phase);
                float secondPartial = MathF.Sin(phase * 2f) * 0.18f;
                samples[i] = (fundamental + secondPartial) * OutputGain;
            }

            return SpatializedSoundEngine.CreateSpatialFromSamples(
                samples,
                SampleRate,
                normalizedScreenX,
                wrapDelay: true);
        }

        private static bool IsInstanceUsable(SoundEffectInstance? instance)
        {
            if (instance is null)
            {
                return false;
            }

            try
            {
                return !instance.IsDisposed && instance.State != SoundState.Stopped;
            }
            catch
            {
                return false;
            }
        }
    }
}
