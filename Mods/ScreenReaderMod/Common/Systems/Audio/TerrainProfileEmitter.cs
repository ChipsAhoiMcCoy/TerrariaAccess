#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;
using static ScreenReaderMod.Common.Systems.InGameNarrationSystem;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// Emits a continuous tone whose pitch tracks the ground depth ahead of the player.
/// Lower pitch = deeper drop ahead. Fades to silence when stationary.
/// </summary>
internal sealed class TerrainProfileEmitter : AudioEmitterBase
{
    private readonly CavitySonarEmitter _sonarEmitter;

    internal TerrainProfileEmitter(CavitySonarEmitter sonarEmitter)
    {
        _sonarEmitter = sonarEmitter;
    }

    // Scan parameters
    private const int MaxScanRangeTiles = 32;
    private const int DefaultScanRange = 16;
    private const int MaxDepthScan = 30;
    private const int NearRangeEnd = 5;

    // Frequency mapping
    private const float BaseFrequency = 400f;   // floor at feet
    private const float MinFrequency = 80f;      // bottomless pit
    private const float WallFrequency = 500f;    // wall ahead (higher than base)

    // Smoothing
    private const float LerpSpeed = 0.15f;
    private const int FadeFrames = 10;
    private const float MinSpeedThreshold = 0.5f;

    // Cliff alert
    private const int CliffDepthChange = 5;
    private const float CliffChirpFrequency = 200f;
    private const float CliffChirpVolume = 0.3f;
    private const int CliffChirpCooldownFrames = 30;

    // Tone generation — use a longer looping buffer for smooth pitch control
    private const int SampleRate = 44100;
    private const float LoopDurationSeconds = 0.15f;

    private SoundEffectInstance? _toneInstance;
    private SoundEffect? _toneEffect;
    private float _currentPitch;
    private float _targetPitch;
    private float _currentVolume;
    private int _fadeCounter;
    private int _lastDirection;
    private int _lastCliffChirpFrame;
    private readonly float[] _depthBuffer = new float[MaxScanRangeTiles];

    private static int ScanRange => Math.Clamp(
        ScreenReaderModConfig.Instance?.TerrainProfileScanRange ?? DefaultScanRange,
        4, MaxScanRangeTiles);

    public override void Update(Player player)
    {
        if (!CanProcessMovementAudio(player))
        {
            StopTone();
            return;
        }

        bool enabled = ScreenReaderModConfig.Instance?.TerrainProfileEnabled ?? true;
        if (!enabled)
        {
            StopTone();
            return;
        }

        // Suppress during sonar scan to avoid audio conflict
        if (_sonarEmitter.IsScanning)
        {
            FadeOut();
            return;
        }

        // Don't play while falling
        if (!IsGrounded(player))
        {
            FadeOut();
            return;
        }

        float horizontalSpeed = Math.Abs(player.velocity.X);
        if (horizontalSpeed < MinSpeedThreshold)
        {
            FadeOut();
            return;
        }

        int direction = player.velocity.X > 0 ? 1 : -1;
        _lastDirection = direction;
        _fadeCounter = 0;

        // Scan terrain ahead
        Point footTile = GetFootTile(player);
        ScanTerrain(footTile, direction);

        // Compute target frequency from depth data
        float targetFreq = ComputeTargetFrequency(footTile, direction);

        // Check for cliff alert
        CheckCliffAlert(player);

        // Map frequency to pitch shift relative to BaseFrequency
        _targetPitch = FrequencyToPitch(targetFreq);

        float configVolume = ScreenReaderModConfig.Instance?.TerrainProfileVolume ?? 1f;
        float targetVolume = 0.45f * configVolume;

        EnsureTonePlaying(targetVolume);
        SmoothUpdate(targetVolume);
    }

    public override void Reset()
    {
        StopTone();
        _currentPitch = 0f;
        _targetPitch = 0f;
        _currentVolume = 0f;
        _fadeCounter = 0;
        _lastDirection = 0;
        _lastCliffChirpFrame = 0;
        Array.Clear(_depthBuffer);
    }

    private static bool IsGrounded(Player player)
    {
        return Math.Abs(player.velocity.Y) < 0.02f;
    }

    private static Point GetFootTile(Player player)
    {
        return new Point(
            (int)(player.Bottom.X / 16f),
            (int)(player.Bottom.Y / 16f));
    }

    private void ScanTerrain(Point footTile, int direction)
    {
        int scanRange = ScanRange;
        for (int i = 0; i < scanRange; i++)
        {
            int tileX = footTile.X + direction * (i + 1);
            if (tileX < 0 || tileX >= Main.maxTilesX)
            {
                _depthBuffer[i] = 0;
                continue;
            }

            // Check for wall at body height — require BOTH tiles solid (a 1-tile rise
            // is an auto-step, not a wall; only 2+ tile obstructions block the player)
            if (IsSolidWall(tileX, footTile.Y - 1) && IsSolidWall(tileX, footTile.Y - 2))
            {
                // Wall blocking — encode as negative depth (special)
                _depthBuffer[i] = -1;
                // Fill remaining columns as wall too
                for (int j = i + 1; j < scanRange; j++)
                {
                    _depthBuffer[j] = -1;
                }

                break;
            }

            // Scan downward for floor
            float depth = MaxDepthScan;
            for (int y = footTile.Y; y < footTile.Y + MaxDepthScan && y < Main.maxTilesY; y++)
            {
                if (HasSupportingTile(tileX, y))
                {
                    depth = y - footTile.Y;
                    // Platforms count as slightly less solid (add 0.5 to depth)
                    if (IsPlatform(tileX, y))
                    {
                        depth += 0.5f;
                    }

                    break;
                }
            }

            _depthBuffer[i] = depth;
        }
    }

    private float ComputeTargetFrequency(Point footTile, int direction)
    {
        int scanRange = ScanRange;

        // Check if there's a wall in near range
        bool wallAhead = false;
        for (int i = 0; i < NearRangeEnd && i < scanRange; i++)
        {
            if (_depthBuffer[i] < 0)
            {
                wallAhead = true;
                break;
            }
        }

        if (wallAhead)
        {
            return WallFrequency;
        }

        // Weighted average: near columns weighted 2x, far columns 1x
        float weightedSum = 0f;
        float weightTotal = 0f;

        for (int i = 0; i < scanRange; i++)
        {
            float depth = _depthBuffer[i];
            if (depth < 0) depth = 0; // wall = treat as floor-level for averaging

            float weight = i < NearRangeEnd ? 2f : 1f;
            weightedSum += depth * weight;
            weightTotal += weight;
        }

        float avgDepth = weightTotal > 0 ? weightedSum / weightTotal : 0;

        // Map depth to frequency: BaseFrequency at depth 0, MinFrequency at MaxDepthScan
        float depthNormalized = MathHelper.Clamp(avgDepth / MaxDepthScan, 0f, 1f);
        return MathHelper.Lerp(BaseFrequency, MinFrequency, depthNormalized);
    }

    private void CheckCliffAlert(Player player)
    {
        int now = (int)Main.GameUpdateCount;
        if (now - _lastCliffChirpFrame < CliffChirpCooldownFrames)
        {
            return;
        }

        // Look for sudden depth changes between adjacent near columns
        for (int i = 0; i < NearRangeEnd - 1 && i < ScanRange - 1; i++)
        {
            float d1 = _depthBuffer[i];
            float d2 = _depthBuffer[i + 1];
            if (d1 < 0 || d2 < 0) continue;

            if (d2 - d1 >= CliffDepthChange)
            {
                float configVolume = ScreenReaderModConfig.Instance?.TerrainProfileVolume ?? 1f;
                FootstepToneProvider.Play(CliffChirpFrequency, CliffChirpVolume * configVolume);
                _lastCliffChirpFrame = now;
                break;
            }
        }
    }

    private void EnsureTonePlaying(float volume)
    {
        if (_toneInstance is not null && !_toneInstance.IsDisposed && _toneInstance.State != SoundState.Stopped)
        {
            return;
        }

        // Create a warm filtered sine tone (sine + slight 2nd harmonic) for looping
        if (_toneEffect is null || _toneEffect.IsDisposed)
        {
            _toneEffect = CreateProfileTone();
        }

        _toneInstance = _toneEffect.CreateInstance();
        _toneInstance.IsLooped = true;
        _toneInstance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
        _toneInstance.Pitch = 0f;
        _toneInstance.Play();
        _currentPitch = 0f;
        _currentVolume = volume;
    }

    private void SmoothUpdate(float targetVolume)
    {
        if (_toneInstance is null || _toneInstance.IsDisposed || _toneInstance.State == SoundState.Stopped)
        {
            return;
        }

        // Smooth pitch interpolation
        _currentPitch = MathHelper.Lerp(_currentPitch, _targetPitch, LerpSpeed);
        _toneInstance.Pitch = MathHelper.Clamp(_currentPitch, -1f, 1f);

        // Smooth volume
        _currentVolume = MathHelper.Lerp(_currentVolume, targetVolume, LerpSpeed);
        _toneInstance.Volume = MathHelper.Clamp(_currentVolume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
    }

    private void FadeOut()
    {
        if (_toneInstance is null || _toneInstance.IsDisposed || _toneInstance.State == SoundState.Stopped)
        {
            return;
        }

        _fadeCounter++;
        float fadeProgress = MathHelper.Clamp((float)_fadeCounter / FadeFrames, 0f, 1f);
        float fadedVolume = _currentVolume * (1f - fadeProgress);
        _toneInstance.Volume = MathHelper.Clamp(fadedVolume, 0f, 1f) * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;

        if (_fadeCounter >= FadeFrames)
        {
            StopTone();
        }
    }

    private void StopTone()
    {
        if (_toneInstance is not null)
        {
            FootstepToneProvider.StopInstance(_toneInstance);
            _toneInstance = null;
        }

        _fadeCounter = 0;
    }

    internal static void DisposeStaticResources()
    {
        // Static resources are per-instance, nothing global to dispose
    }

    /// <summary>
    /// Converts a target frequency to a pitch shift relative to BaseFrequency.
    /// XNA Pitch: -1 = half freq, 0 = same, +1 = double freq.
    /// </summary>
    private static float FrequencyToPitch(float targetFrequency)
    {
        if (targetFrequency <= 0) return 0f;
        float pitchShift = MathF.Log2(targetFrequency / BaseFrequency);
        return MathHelper.Clamp(pitchShift, -1f, 1f);
    }

    /// <summary>
    /// Creates a warm sine tone with a subtle 2nd harmonic for the profile sound.
    /// </summary>
    private static SoundEffect CreateProfileTone()
    {
        int sampleCount = (int)(SampleRate * LoopDurationSeconds);
        byte[] buffer = new byte[sampleCount * sizeof(short)];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)SampleRate;
            float phase = MathHelper.TwoPi * BaseFrequency * t;

            // Sine + subtle 2nd harmonic for warmth
            float sample = MathF.Sin(phase) + 0.15f * MathF.Sin(phase * 2f);

            // Normalize to avoid clipping (max amplitude = 1.15)
            sample /= 1.15f;

            short quantized = (short)MathHelper.Clamp(sample * short.MaxValue * 0.5f, short.MinValue, short.MaxValue);
            int index = i * 2;
            buffer[index] = (byte)(quantized & 0xFF);
            buffer[index + 1] = (byte)((quantized >> 8) & 0xFF);
        }

        return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
    }

    private static bool HasSupportingTile(int tileX, int tileY)
    {
        Tile tile = Framing.GetTileSafely(tileX, tileY);
        if (!tile.HasTile || tile.IsActuated) return false;
        if (IsVegetation(tile.TileType)) return false;
        return Main.tileSolid[tile.TileType] || TileID.Sets.Platforms[tile.TileType];
    }

    private static bool IsSolidWall(int tileX, int tileY)
    {
        Tile tile = Framing.GetTileSafely(tileX, tileY);
        if (!tile.HasTile || tile.IsActuated) return false;
        if (IsVegetation(tile.TileType)) return false;
        // Slopes and half-blocks don't fully block passage
        if (tile.IsHalfBlock || tile.Slope != SlopeType.Solid) return false;
        return Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
    }

    /// <summary>
    /// Returns true for tree trunks and other tall vegetation that register as solid
    /// but shouldn't be treated as terrain for navigation scanning.
    /// </summary>
    private static bool IsVegetation(ushort tileType)
    {
        return TileID.Sets.IsATreeTrunk[tileType]
            || tileType == TileID.Cactus
            || tileType == TileID.MushroomTrees
            || tileType == TileID.PalmTree
            || tileType == TileID.Bamboo;
    }

    private static bool IsPlatform(int tileX, int tileY)
    {
        Tile tile = Framing.GetTileSafely(tileX, tileY);
        return tile.HasTile && TileID.Sets.Platforms[tile.TileType];
    }
}
