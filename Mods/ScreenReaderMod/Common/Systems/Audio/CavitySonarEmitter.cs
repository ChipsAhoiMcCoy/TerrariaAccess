#nullable enable
using System;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.ID;
using static ScreenReaderMod.Common.Systems.InGameNarrationSystem;

namespace ScreenReaderMod.Common.Systems.Audio;

/// <summary>
/// On-demand sonar scanner triggered by keybind. Sweeps columns left-to-right,
/// playing tones for empty spaces to reveal cave shapes and terrain features.
/// </summary>
internal sealed class CavitySonarEmitter : AudioEmitterBase
{
    // Scan region
    private const int ScanWidth = 21;        // columns (±10 from player)
    private const int HeightAbove = 5;       // tiles above player to scan
    private const int HeightBelow = 20;      // tiles below player to scan
    private const int ScanHeight = HeightAbove + HeightBelow + 1; // total rows

    // Sweep timing
    private const int SweepDurationFrames = 90; // ~1.5 seconds at 60 FPS

    // Tone parameters
    private const float PipFreqBase = 440f;          // frequency at player height (A4)
    private const float SemitoneRatio = 1.05946309f;  // 2^(1/12)
    private const float PipDuration = 0.04f;          // 40ms per pip
    private const float MaxPan = 0.8f;
    private const float EmptyTileVolume = 0.3f;
    private const float LiquidTileVolume = 0.15f;

    // Thinning: if more than this many empty tiles in a column, skip every Nth
    private const int MaxPipsPerColumn = 10;

    // State
    private bool _isScanning;
    private int _scanStartFrame;
    private int _lastPlayedColumn = -1;
    private int _scanOriginX;
    private int _scanOriginY;
    private TileState[,]? _scanGrid; // [col, row]

    private enum TileState : byte
    {
        Solid,
        Empty,
        Liquid
    }

    /// <summary>
    /// Whether a sonar scan is currently in progress.
    /// Used by other emitters to suppress during scan.
    /// </summary>
    internal bool IsScanning => _isScanning;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            CancelScan();
            return;
        }

        bool enabled = ScreenReaderModConfig.Instance?.CavitySonarEnabled ?? true;
        if (!enabled)
        {
            CancelScan();
            return;
        }

        // Check keybind trigger
        if (CavitySonarKeybinds.SonarScan?.JustPressed == true)
        {
            if (_isScanning)
            {
                // Cancel current scan
                CancelScan();
                return;
            }

            BeginScan(player);
            return;
        }

        // If scanning, progress the sweep
        if (_isScanning)
        {
            ProgressSweep();
        }
    }

    public override void Reset()
    {
        CancelScan();
    }

    private void BeginScan(Player player)
    {
        Point foot = GetFootTile(player);
        _scanOriginX = foot.X - ScanWidth / 2;
        _scanOriginY = foot.Y - HeightAbove;
        _scanGrid = new TileState[ScanWidth, ScanHeight];

        // Snapshot the tile grid
        for (int col = 0; col < ScanWidth; col++)
        {
            int worldX = _scanOriginX + col;
            for (int row = 0; row < ScanHeight; row++)
            {
                int worldY = _scanOriginY + row;

                if (worldX < 0 || worldX >= Main.maxTilesX || worldY < 0 || worldY >= Main.maxTilesY)
                {
                    _scanGrid[col, row] = TileState.Solid;
                    continue;
                }

                Tile tile = Framing.GetTileSafely(worldX, worldY);

                if (tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType]
                    && !IsVegetation(tile.TileType))
                {
                    _scanGrid[col, row] = TileState.Solid;
                }
                else if (tile.LiquidAmount > 0)
                {
                    _scanGrid[col, row] = TileState.Liquid;
                }
                else
                {
                    _scanGrid[col, row] = TileState.Empty;
                }
            }
        }

        _isScanning = true;
        _scanStartFrame = (int)Main.GameUpdateCount;
        _lastPlayedColumn = -1;
    }

    private void ProgressSweep()
    {
        if (_scanGrid is null)
        {
            CancelScan();
            return;
        }

        int elapsedFrames = (int)Main.GameUpdateCount - _scanStartFrame;

        if (elapsedFrames >= SweepDurationFrames)
        {
            CancelScan();
            return;
        }

        int framesPerColumn = Math.Max(1, SweepDurationFrames / ScanWidth);
        int currentColumn = elapsedFrames / framesPerColumn;

        if (currentColumn >= ScanWidth)
        {
            CancelScan();
            return;
        }

        if (currentColumn != _lastPlayedColumn)
        {
            PlayColumnPips(currentColumn);
            _lastPlayedColumn = currentColumn;
        }
    }

    private void PlayColumnPips(int column)
    {
        if (_scanGrid is null) return;

        float configVolume = ScreenReaderModConfig.Instance?.CavitySonarVolume ?? 1f;
        if (configVolume <= 0f) return;

        // Pan: leftmost = -MaxPan, center = 0, rightmost = +MaxPan
        float pan = ScanWidth > 1
            ? MathHelper.Lerp(-MaxPan, MaxPan, (float)column / (ScanWidth - 1))
            : 0f;

        // Count non-solid tiles in this column for thinning
        int emptyCount = 0;
        for (int row = 0; row < ScanHeight; row++)
        {
            if (_scanGrid[column, row] != TileState.Solid)
            {
                emptyCount++;
            }
        }

        // Determine thinning step: if too many pips, play every Nth
        int step = emptyCount > MaxPipsPerColumn ? Math.Max(1, emptyCount / MaxPipsPerColumn) : 1;
        int skipCounter = 0;

        for (int row = 0; row < ScanHeight; row++)
        {
            TileState state = _scanGrid[column, row];
            if (state == TileState.Solid) continue;

            skipCounter++;
            if (step > 1 && (skipCounter % step) != 0) continue;

            // Row relative to player: row - HeightAbove
            // Positive = below player, negative = above
            int relativeRow = row - HeightAbove;

            // Frequency: player height = base, up = higher, down = lower
            // Each tile offset = 1 semitone
            float frequency = PipFreqBase * MathF.Pow(SemitoneRatio, -relativeRow);

            float volume = state == TileState.Liquid ? LiquidTileVolume : EmptyTileVolume;
            volume *= configVolume;

            FootstepToneProvider.Play(frequency, volume, useTriangleWave: state == TileState.Liquid, pan);
        }
    }

    private void CancelScan()
    {
        _isScanning = false;
        _scanGrid = null;
        _lastPlayedColumn = -1;
    }

    private static Point GetFootTile(Player player)
    {
        return new Point(
            (int)(player.Bottom.X / 16f),
            (int)(player.Bottom.Y / 16f));
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
}
