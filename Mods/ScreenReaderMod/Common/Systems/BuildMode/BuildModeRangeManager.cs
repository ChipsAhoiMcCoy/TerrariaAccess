#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace ScreenReaderMod.Common.Systems.BuildMode;

internal sealed class BuildModeRangeManager
{
    private bool _expanded;
    private int _originalTileRangeX;
    private int _originalTileRangeY;
    private int _originalBlockRange;

    public bool IsExpanded => _expanded;

    public void ExpandPlacementRangeToViewport(Player player)
    {
        if (player is null || !player.active)
        {
            return;
        }

        float zoomX = Math.Abs(Main.GameViewMatrix.Zoom.X) < 0.001f ? 1f : Main.GameViewMatrix.Zoom.X;
        float zoomY = Math.Abs(Main.GameViewMatrix.Zoom.Y) < 0.001f ? zoomX : Main.GameViewMatrix.Zoom.Y;
        float zoom = Math.Max(0.001f, Math.Min(zoomX, zoomY));

        float viewWidth = Main.screenWidth / zoom;
        float viewHeight = Main.screenHeight / zoom;

        Vector2 topLeft = Main.screenPosition;
        Vector2 bottomRight = topLeft + new Vector2(viewWidth, viewHeight);

        float leftTiles = MathF.Abs(player.Center.X - topLeft.X) / 16f;
        float rightTiles = MathF.Abs(bottomRight.X - player.Center.X) / 16f;
        float upTiles = MathF.Abs(player.Center.Y - topLeft.Y) / 16f;
        float downTiles = MathF.Abs(bottomRight.Y - player.Center.Y) / 16f;

        int horizontalRange = (int)Math.Ceiling(Math.Max(leftTiles, rightTiles)) + 2;
        int verticalRange = (int)Math.Ceiling(Math.Max(upTiles, downTiles)) + 2;

        if (!_expanded)
        {
            _expanded = true;
            _originalTileRangeX = Player.tileRangeX;
            _originalTileRangeY = Player.tileRangeY;
            _originalBlockRange = player.blockRange;
        }

        Player.tileRangeX = Math.Max(Player.tileRangeX, horizontalRange);
        Player.tileRangeY = Math.Max(Player.tileRangeY, verticalRange);
        player.blockRange = Math.Max(player.blockRange, Math.Max(horizontalRange, verticalRange));
    }

    public void RestorePlacementRange(Player player)
    {
        if (!_expanded)
        {
            return;
        }

        _expanded = false;
        if (player is null || !player.active)
        {
            return;
        }

        Player.tileRangeX = _originalTileRangeX;
        Player.tileRangeY = _originalTileRangeY;
        player.blockRange = _originalBlockRange;
    }
}
