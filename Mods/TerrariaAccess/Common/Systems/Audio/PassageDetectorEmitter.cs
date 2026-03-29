#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Detects horizontal passages (side tunnels) while the player is falling through a vertical shaft.
/// Plays a spatialized ascending chirp from the center of each detected opening so the player knows
/// there is a tunnel to their left or right that they can enter.
/// </summary>
internal sealed class PassageDetectorEmitter : AudioEmitterBase
{
    /// <summary>
    /// Minimum vertical gap height (in tiles) to count as a passable tunnel.
    /// The player is roughly 3 tiles tall, so 3 is the minimum they can fit through.
    /// </summary>
    private const int MinPassageHeight = 3;

    /// <summary>
    /// Minimum vertical speed (pixels/frame) to consider the player as moving vertically.
    /// Checked as absolute value so it triggers for both upward and downward movement.
    /// </summary>
    private const float MinVerticalSpeed = 1.5f;

    /// <summary>
    /// Maximum distance (in tiles) to scan outward from the player's hitbox edge
    /// when looking for passage openings. 12 tiles covers wide natural caverns.
    /// </summary>
    private const int MaxWallScanDistance = 12;

    /// <summary>
    /// Vertical scan range (tiles above and below the player center) when checking a wall column for gaps.
    /// </summary>
    private const int VerticalScanRange = 6;

    /// <summary>
    /// Number of consecutive frames the player must be moving vertically before scanning activates.
    /// Filters out brief velocity spikes from spawning, walking over bumps, etc.
    /// </summary>
    private const int MinSustainedFrames = 3;

    /// <summary>
    /// Base volume for the passage chirp before config scaling.
    /// Higher than footsteps (0.45) to compensate for the chirp's shorter duration
    /// and faster decay envelope, which make it perceptually quieter at equal peak levels.
    /// </summary>
    private const float ChirpVolume = 0.65f;

    // Chirp synthesis parameters
    private const int ChirpSampleRate = 44100;
    private const float ChirpDurationSeconds = 0.055f;
    private const float ChirpStartFrequency = 1100f;
    private const float ChirpEndFrequency = 2200f;

    /// <summary>
    /// Cached chirp SoundEffect - a short ascending frequency sweep that sounds like a bird chirp.
    /// </summary>
    private static SoundEffect? s_chirpTone;

    /// <summary>
    /// Live sound instances for cleanup tracking.
    /// </summary>
    private readonly List<SoundEffectInstance> _liveInstances = new();

    /// <summary>
    /// Tracks which passages have already been announced during the current fall,
    /// keyed by (side direction, passage top tile Y).
    /// </summary>
    private readonly HashSet<(int side, int gapStartY)> _announcedPassages = new();

    /// <summary>
    /// Whether the player was moving vertically on the previous frame, used to reset state on stopping.
    /// </summary>
    private bool _wasMovingVertically;

    /// <summary>
    /// Counts consecutive frames of vertical movement. Scanning only activates after
    /// <see cref="MinSustainedFrames"/> to filter brief velocity spikes.
    /// </summary>
    private int _verticalFrameCount;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            Reset();
            return;
        }

        bool enabled = TerrariaAccessConfig.Instance?.PassageDetectionEnabled ?? true;
        if (!enabled)
        {
            Reset();
            return;
        }

        // Active while moving vertically in either direction (falling or rising)
        bool isMovingVertically = Math.Abs(player.velocity.Y) >= MinVerticalSpeed;

        if (!isMovingVertically)
        {
            if (_wasMovingVertically)
            {
                _announcedPassages.Clear();
                _wasMovingVertically = false;
            }

            _verticalFrameCount = 0;
            CleanupFinishedInstances();
            return;
        }

        _verticalFrameCount++;
        _wasMovingVertically = true;

        // Require sustained vertical movement to filter brief velocity spikes (spawn drops, bumps)
        if (_verticalFrameCount < MinSustainedFrames)
        {
            CleanupFinishedInstances();
            return;
        }

        Rectangle hitbox = player.Hitbox;
        int playerCenterTileY = hitbox.Center.Y / 16;

        // Determine tile columns at the player's hitbox edges
        int playerLeftTileX = hitbox.Left / 16;
        int playerRightTileX = (hitbox.Right - 1) / 16;

        // Player's vertical tile span for overlap checking
        int playerTopTileY = hitbox.Top / 16;
        int playerBottomTileY = (hitbox.Bottom - 1) / 16;

        // Scan left and right for passages
        ScanForPassages(player, playerLeftTileX, playerCenterTileY, playerTopTileY, playerBottomTileY, -1);
        ScanForPassages(player, playerRightTileX, playerCenterTileY, playerTopTileY, playerBottomTileY, 1);

        // Prune old announcements that are far above the player
        CleanupOldAnnouncements(playerCenterTileY);
        CleanupFinishedInstances();
    }

    public override void Reset()
    {
        _announcedPassages.Clear();
        _wasMovingVertically = false;
        _verticalFrameCount = 0;
        StopAllInstances();
    }

    /// <summary>
    /// Disposes of the static chirp tone. Called on mod unload.
    /// </summary>
    public static void DisposeStaticResources()
    {
        s_chirpTone?.Dispose();
        s_chirpTone = null;
    }

    /// <summary>
    /// Scans outward from the player's hitbox edge in the given direction,
    /// checking each column that contains solid tiles for vertical gaps representing side passages.
    /// Stops once a fully solid column (no gaps) is found, since passages beyond it are unreachable.
    /// </summary>
    private void ScanForPassages(Player player, int playerEdgeTileX, int centerTileY,
        int playerTopTileY, int playerBottomTileY, int direction)
    {
        int scanTop = Math.Clamp(centerTileY - VerticalScanRange, 0, Main.maxTilesY - 1);
        int scanBottom = Math.Clamp(centerTileY + VerticalScanRange, 0, Main.maxTilesY - 1);

        for (int dist = 1; dist <= MaxWallScanDistance; dist++)
        {
            int columnX = playerEdgeTileX + direction * dist;
            if (columnX < 0 || columnX >= Main.maxTilesX)
            {
                return;
            }

            // Count solid and empty tiles in the scan window
            bool hasAnySolid = false;
            bool hasAnyEmpty = false;
            for (int y = scanTop; y <= scanBottom; y++)
            {
                if (IsSolidAt(columnX, y))
                {
                    hasAnySolid = true;
                }
                else
                {
                    hasAnyEmpty = true;
                }

                if (hasAnySolid && hasAnyEmpty)
                {
                    break; // Mixed column - has potential gaps
                }
            }

            if (!hasAnySolid)
            {
                // All empty - open air, keep scanning outward
                continue;
            }

            if (!hasAnyEmpty)
            {
                // Fully solid column - wall with no gaps; passages beyond are unreachable
                return;
            }

            // Mixed column: has both solid and empty tiles. Check for passage gaps.
            FindGapsInWall(player, columnX, scanTop, scanBottom, direction, playerTopTileY, playerBottomTileY);
        }
    }

    /// <summary>
    /// Scans a wall column for contiguous vertical gaps of <see cref="MinPassageHeight"/> or more tiles.
    /// Each qualifying gap is checked for body overlap and announced if the player is aligned with it.
    /// </summary>
    private void FindGapsInWall(Player player, int wallX, int scanTop, int scanBottom, int side,
        int playerTopTileY, int playerBottomTileY)
    {
        int gapStart = -1;

        for (int y = scanTop; y <= scanBottom + 1; y++)
        {
            bool solid = y > scanBottom || IsSolidAt(wallX, y);

            if (!solid && gapStart < 0)
            {
                gapStart = y;
            }
            else if (solid && gapStart >= 0)
            {
                int gapHeight = y - gapStart;
                if (gapHeight >= MinPassageHeight)
                {
                    ValidateAndAnnounce(player, wallX, gapStart, gapHeight, side, playerTopTileY, playerBottomTileY);
                }

                gapStart = -1;
            }
        }
    }

    /// <summary>
    /// Validates that a gap is a real passage (bounded by solid tiles and extends horizontally)
    /// and announces it the instant the player's body overlaps with the opening.
    /// </summary>
    private void ValidateAndAnnounce(Player player, int wallX, int gapStart, int gapHeight, int side,
        int playerTopTileY, int playerBottomTileY)
    {
        // Must have a solid ceiling above the gap
        if (gapStart <= 0 || !IsSolidAt(wallX, gapStart - 1))
        {
            return;
        }

        // Floor is implicitly solid (the loop found a solid tile to end the gap)

        int midY = gapStart + gapHeight / 2;

        // Skip passages inside player-built structures. Player bases have background walls
        // behind their rooms; natural cave shafts and passages typically do not.
        if (GapHasBackgroundWalls(wallX, gapStart, gapHeight))
        {
            return;
        }

        // Verify the passage extends at least 3 tiles deep (filters shallow doorway-sized notches
        // and 1-tile wall irregularities that aren't real explorable passages).
        const int minPassageDepth = 3;
        for (int d = 1; d <= minPassageDepth; d++)
        {
            int checkX = wallX + side * d;
            if (checkX < 0 || checkX >= Main.maxTilesX || IsSolidAt(checkX, midY))
            {
                return;
            }
        }

        // Fire immediately when the player's body overlaps with the gap
        int gapBottom = gapStart + gapHeight - 1;
        bool overlaps = playerTopTileY <= gapBottom && playerBottomTileY >= gapStart;
        if (!overlaps)
        {
            return;
        }

        // Deduplicate: don't re-announce the same passage during this fall
        var key = (side, gapStart);
        if (!_announcedPassages.Add(key))
        {
            return;
        }

        // Calculate world position of the gap center (middle tile of the opening)
        Vector2 gapCenter = new(wallX * 16f + 8f, midY * 16f + 8f);

        // Use spatial audio to position the chirp relative to the player
        SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
            player.Center,
            gapCenter,
            ChirpVolume);

        float configVolume = TerrariaAccessConfig.Instance?.FootstepVolume ?? 1f;
        float volume = MathHelper.Clamp(sample.Volume * configVolume * AudioVolumeDefaults.WorldCueVolumeScale, 0f, 1f);
        if (volume <= 0f)
        {
            return;
        }

        PlayChirp(volume, sample.Pan);
    }

    /// <summary>
    /// Returns true if every empty tile in the gap has a background wall.
    /// Background walls indicate the gap is inside a player-built structure (rooms, doorways)
    /// rather than a natural cave opening, which should not trigger passage chirps.
    /// </summary>
    private static bool GapHasBackgroundWalls(int columnX, int gapStart, int gapHeight)
    {
        for (int y = gapStart; y < gapStart + gapHeight; y++)
        {
            Tile tile = Framing.GetTileSafely(columnX, y);
            if (tile.WallType == WallID.None)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Plays the ascending frequency-sweep chirp at the given volume and pan.
    /// </summary>
    private void PlayChirp(float volume, float pan)
    {
        SoundEffect tone = EnsureChirpTone();
        if (tone.IsDisposed)
        {
            return;
        }

        SoundEffectInstance instance = tone.CreateInstance();
        instance.IsLooped = false;
        instance.Volume = MathHelper.Clamp(volume, 0f, 1f) * Main.soundVolume;
        instance.Pan = MathHelper.Clamp(pan, -1f, 1f);
        instance.Play();
        _liveInstances.Add(instance);
    }

    /// <summary>
    /// Returns true if the tile at the given coordinates blocks passage detection.
    /// This includes solid tiles AND door/gate tiles (even when open), since doorways
    /// are player-placed structures and should not trigger passage chirps.
    /// </summary>
    private static bool IsSolidAt(int x, int y)
    {
        Tile tile = Framing.GetTileSafely(x, y);
        if (!tile.HasTile || tile.IsActuated)
        {
            return false;
        }

        // Treat open doors and tall gates as solid obstructions for passage detection.
        // Closed doors/gates are already in tileSolid, but open variants are not,
        // yet they represent player-placed doorways that shouldn't trigger chirps.
        ushort type = tile.TileType;
        if (type == TileID.OpenDoor || type == TileID.TallGateOpen || type == TileID.TrapdoorOpen)
        {
            return true;
        }

        return Main.tileSolid[type] && !Main.tileSolidTop[type];
    }

    /// <summary>
    /// Removes announced passage entries that are far above the player's current position
    /// to prevent the hash set from growing unbounded during long falls.
    /// </summary>
    private void CleanupOldAnnouncements(int currentTileY)
    {
        _announcedPassages.RemoveWhere(key => key.gapStartY < currentTileY - VerticalScanRange * 3);
    }

    private void CleanupFinishedInstances()
    {
        for (int i = _liveInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance inst = _liveInstances[i];
            if (inst.State == SoundState.Stopped)
            {
                inst.Dispose();
                _liveInstances.RemoveAt(i);
            }
        }
    }

    private void StopAllInstances()
    {
        foreach (SoundEffectInstance inst in _liveInstances)
        {
            try { inst.Stop(); } catch { /* ignore audio backend failures */ }
            inst.Dispose();
        }

        _liveInstances.Clear();
    }

    /// <summary>
    /// Lazily creates and caches the chirp SoundEffect - a short ascending frequency sweep
    /// from 1100 Hz to 2200 Hz over 55ms that sounds like a quick bird chirp.
    /// </summary>
    private static SoundEffect EnsureChirpTone()
    {
        if (s_chirpTone is { IsDisposed: false })
        {
            return s_chirpTone;
        }

        s_chirpTone?.Dispose();
        s_chirpTone = CreateChirpTone();
        return s_chirpTone;
    }

    /// <summary>
    /// Synthesizes a frequency-swept chirp: a sine wave that sweeps from
    /// <see cref="ChirpStartFrequency"/> to <see cref="ChirpEndFrequency"/>
    /// with a fast attack and exponential decay envelope.
    /// </summary>
    private static SoundEffect CreateChirpTone()
    {
        int sampleCount = Math.Max(1, (int)(ChirpSampleRate * ChirpDurationSeconds));
        byte[] buffer = new byte[sampleCount * sizeof(short)];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float progress = i / (float)sampleCount;

            // Ascending frequency sweep
            float freq = MathHelper.Lerp(ChirpStartFrequency, ChirpEndFrequency, progress);

            // Accumulate phase for smooth frequency transitions
            phase += MathHelper.TwoPi * freq / ChirpSampleRate;
            if (phase > MathHelper.TwoPi)
            {
                phase -= MathHelper.TwoPi;
            }

            // Envelope: very fast attack (5%), smooth exponential decay
            float envelope;
            const float attackEnd = 0.05f;
            if (progress < attackEnd)
            {
                envelope = progress / attackEnd;
            }
            else
            {
                envelope = MathF.Exp(-6f * (progress - attackEnd));
            }

            float sample = MathF.Sin(phase) * envelope;
            short quantized = (short)MathHelper.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

            int index = i * 2;
            buffer[index] = (byte)(quantized & 0xFF);
            buffer[index + 1] = (byte)((quantized >> 8) & 0xFF);
        }

        return new SoundEffect(buffer, ChirpSampleRate, AudioChannels.Mono);
    }
}
