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

    // Smoothing
    private const float LerpSpeed = 0.15f;
    private const int FadeFrames = 10;
    private const float MinSpeedThreshold = 0.5f;

    // Cliff alert
    private const int CliffDepthChange = 5;
    private const float CliffChirpFrequency = 200f;
    private const float CliffChirpVolume = 0.3f;
    private const int CliffChirpCooldownFrames = 30;

    // Flat-ground suppression — only play when terrain ahead has meaningful variation
    private const float MinDepthVariation = 1.5f;          // tiles of depth range to trigger tone

    // Wall thud — distinct impact sound when approaching a wall
    private const float WallThudVolume = 0.4f;
    private const int WallThudCooldownFrames = 30;         // game frames between thuds

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

    // Wall thud state
    private SoundEffect? _wallThudEffect;
    private SoundEffectInstance? _wallThudInstance;
    private int _lastWallThudFrame;

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

        // Wall detection — play a distinct thud instead of the profile tone
        if (IsWallInNearRange())
        {
            FadeOut();
            PlayWallThud();
            return;
        }

        // Flat-ground suppression: only play when terrain ahead has dips or rises
        if (!HasMeaningfulTerrainVariation())
        {
            FadeOut();
            return;
        }

        // Compute target frequency from depth data (terrain only)
        float targetFreq = ComputeTerrainFrequency();

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
        CleanupWallThud();
        _currentPitch = 0f;
        _targetPitch = 0f;
        _currentVolume = 0f;
        _fadeCounter = 0;
        _lastDirection = 0;
        _lastCliffChirpFrame = 0;
        _lastWallThudFrame = 0;
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

    private bool IsWallInNearRange()
    {
        int scanRange = ScanRange;
        for (int i = 0; i < NearRangeEnd && i < scanRange; i++)
        {
            if (_depthBuffer[i] < 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMeaningfulTerrainVariation()
    {
        int scanRange = ScanRange;
        float minDepth = float.MaxValue;
        float maxDepth = float.MinValue;

        for (int i = 0; i < scanRange; i++)
        {
            float d = _depthBuffer[i];
            if (d < 0) continue; // walls handled separately
            if (d < minDepth) minDepth = d;
            if (d > maxDepth) maxDepth = d;
        }

        return (maxDepth - minDepth) >= MinDepthVariation;
    }

    private float ComputeTerrainFrequency()
    {
        int scanRange = ScanRange;

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
        float initialPitch = MathHelper.Clamp(_targetPitch, -1f, 1f);
        _toneInstance.Pitch = initialPitch;
        _toneInstance.Play();
        _currentPitch = initialPitch;
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

    private void PlayWallThud()
    {
        int now = (int)Main.GameUpdateCount;
        if (now - _lastWallThudFrame < WallThudCooldownFrames)
        {
            return;
        }

        // Cleanup finished previous instance
        if (_wallThudInstance is not null && !_wallThudInstance.IsDisposed
            && _wallThudInstance.State == SoundState.Stopped)
        {
            _wallThudInstance.Dispose();
            _wallThudInstance = null;
        }

        if (_wallThudEffect is null || _wallThudEffect.IsDisposed)
        {
            _wallThudEffect = CreateWallThud();
        }

        float configVolume = ScreenReaderModConfig.Instance?.TerrainProfileVolume ?? 1f;
        _wallThudInstance = _wallThudEffect.CreateInstance();
        _wallThudInstance.IsLooped = false;
        _wallThudInstance.Volume = MathHelper.Clamp(WallThudVolume * configVolume, 0f, 1f)
            * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;
        _wallThudInstance.Play();
        _lastWallThudFrame = now;
    }

    private void CleanupWallThud()
    {
        if (_wallThudInstance is not null && !_wallThudInstance.IsDisposed)
        {
            try { _wallThudInstance.Stop(); }
            catch { /* ignore audio backend failures */ }
            _wallThudInstance.Dispose();
        }

        _wallThudInstance = null;
    }

    /// <summary>
    /// Creates a short, punchy "thud" sound for wall detection —
    /// low tone with noise texture, rapid decay.
    /// </summary>
    private static SoundEffect CreateWallThud()
    {
        const float duration = 0.1f;
        int sampleCount = (int)(SampleRate * duration);
        byte[] buffer = new byte[sampleCount * sizeof(short)];
        var rand = new Random(42); // deterministic seed for consistency

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)SampleRate;

            // Fast exponential decay for a punchy impact feel
            float envelope = MathF.Exp(-30f * t);

            // Low 80Hz tone for the bass thud
            float tone = MathF.Sin(MathHelper.TwoPi * 80f * t);

            // Noise component for impact texture
            float noise = (float)(rand.NextDouble() * 2.0 - 1.0);

            // Mix: mostly tone with noise texture
            float sample = (tone * 0.7f + noise * 0.3f) * envelope;

            short quantized = (short)MathHelper.Clamp(
                sample * short.MaxValue * 0.5f, short.MinValue, short.MaxValue);
            int index = i * 2;
            buffer[index] = (byte)(quantized & 0xFF);
            buffer[index + 1] = (byte)((quantized >> 8) & 0xFF);
        }

        return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
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
