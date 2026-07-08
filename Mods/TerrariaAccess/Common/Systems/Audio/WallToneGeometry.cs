#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace TerrariaAccess.Common.Systems.Audio;

internal static class WallToneGeometry
{
    public const int SideProbeRangeTiles = 6;
    public const int CeilingProbeRangeTiles = 5;

    private const int TileSize = 16;
    private const int BodyVerticalInsetPixels = 4;
    private const int BodyHorizontalInsetPixels = 2;

    public delegate bool BlockingTilePredicate(int tileX, int tileY);

    public static WallToneScanResult Scan(
        Rectangle hitbox,
        float gravityDirection,
        BlockingTilePredicate isBlockingTile,
        int worldWidthTiles = int.MaxValue,
        int worldHeightTiles = int.MaxValue)
    {
        if (isBlockingTile is null || hitbox.Width <= 0 || hitbox.Height <= 0)
        {
            return default;
        }

        WallToneContact? left = MeasureSide(hitbox, -1, isBlockingTile, worldWidthTiles, worldHeightTiles);
        WallToneContact? right = MeasureSide(hitbox, 1, isBlockingTile, worldWidthTiles, worldHeightTiles);
        WallToneContact? ceiling = MeasureCeiling(hitbox, gravityDirection, isBlockingTile, worldWidthTiles, worldHeightTiles);

        return new WallToneScanResult(left, right, ceiling);
    }

    private static WallToneContact? MeasureSide(
        Rectangle hitbox,
        int direction,
        BlockingTilePredicate isBlockingTile,
        int worldWidthTiles,
        int worldHeightTiles)
    {
        int firstTileX = direction < 0
            ? PixelToTile(hitbox.Left - 1)
            : PixelToTile(hitbox.Right);

        int topTileY = PixelToTile(hitbox.Top + BodyVerticalInsetPixels);
        int bottomTileY = PixelToTile(hitbox.Bottom - BodyVerticalInsetPixels - 1);
        if (bottomTileY < topTileY)
        {
            bottomTileY = topTileY;
        }

        for (int distance = 1; distance <= SideProbeRangeTiles; distance++)
        {
            int tileX = firstTileX + direction * (distance - 1);
            if (!IsInsideWorld(tileX, topTileY, worldWidthTiles, worldHeightTiles) &&
                !IsInsideWorld(tileX, bottomTileY, worldWidthTiles, worldHeightTiles))
            {
                continue;
            }

            Point? bestTile = null;
            int bestCenterDelta = int.MaxValue;
            int centerTileY = PixelToTile(hitbox.Center.Y);
            for (int tileY = topTileY; tileY <= bottomTileY; tileY++)
            {
                if (IsInsideWorld(tileX, tileY, worldWidthTiles, worldHeightTiles) &&
                    isBlockingTile(tileX, tileY))
                {
                    int centerDelta = Math.Abs(tileY - centerTileY);
                    if (centerDelta < bestCenterDelta)
                    {
                        bestTile = new Point(tileX, tileY);
                        bestCenterDelta = centerDelta;
                    }
                }
            }

            if (bestTile.HasValue)
            {
                return new WallToneContact(distance, bestTile.Value);
            }
        }

        return null;
    }

    private static WallToneContact? MeasureCeiling(
        Rectangle hitbox,
        float gravityDirection,
        BlockingTilePredicate isBlockingTile,
        int worldWidthTiles,
        int worldHeightTiles)
    {
        int scanDirection = gravityDirection >= 0f ? -1 : 1;
        int firstTileY = scanDirection < 0
            ? PixelToTile(hitbox.Top - 1)
            : PixelToTile(hitbox.Bottom);

        int leftTileX = PixelToTile(hitbox.Left + BodyHorizontalInsetPixels);
        int rightTileX = PixelToTile(hitbox.Right - BodyHorizontalInsetPixels - 1);
        if (rightTileX < leftTileX)
        {
            rightTileX = leftTileX;
        }

        for (int distance = 1; distance <= CeilingProbeRangeTiles; distance++)
        {
            int tileY = firstTileY + scanDirection * (distance - 1);
            if (!IsInsideWorld(leftTileX, tileY, worldWidthTiles, worldHeightTiles) &&
                !IsInsideWorld(rightTileX, tileY, worldWidthTiles, worldHeightTiles))
            {
                continue;
            }

            Point? bestTile = null;
            int bestCenterDelta = int.MaxValue;
            int centerTileX = PixelToTile(hitbox.Center.X);
            for (int tileX = leftTileX; tileX <= rightTileX; tileX++)
            {
                if (IsInsideWorld(tileX, tileY, worldWidthTiles, worldHeightTiles) &&
                    isBlockingTile(tileX, tileY))
                {
                    int centerDelta = Math.Abs(tileX - centerTileX);
                    if (centerDelta < bestCenterDelta)
                    {
                        bestTile = new Point(tileX, tileY);
                        bestCenterDelta = centerDelta;
                    }
                }
            }

            if (bestTile.HasValue)
            {
                return new WallToneContact(distance, bestTile.Value);
            }
        }

        return null;
    }

    private static int PixelToTile(int pixel) => (int)MathF.Floor(pixel / (float)TileSize);

    private static bool IsInsideWorld(int tileX, int tileY, int worldWidthTiles, int worldHeightTiles)
    {
        return tileX >= 0 &&
            tileY >= 0 &&
            tileX < worldWidthTiles &&
            tileY < worldHeightTiles;
    }
}

internal readonly record struct WallToneScanResult(
    WallToneContact? Left,
    WallToneContact? Right,
    WallToneContact? Ceiling)
{
    public int LeftDistanceTiles => Left?.DistanceTiles ?? 0;
    public int RightDistanceTiles => Right?.DistanceTiles ?? 0;
    public int CeilingDistanceTiles => Ceiling?.DistanceTiles ?? 0;
    public bool HasLeftWall => LeftDistanceTiles > 0;
    public bool HasRightWall => RightDistanceTiles > 0;
    public bool HasCeiling => CeilingDistanceTiles > 0;
    public bool HasBothSideWalls => HasLeftWall && HasRightWall;
}

internal readonly record struct WallToneContact(int DistanceTiles, Point Tile);
