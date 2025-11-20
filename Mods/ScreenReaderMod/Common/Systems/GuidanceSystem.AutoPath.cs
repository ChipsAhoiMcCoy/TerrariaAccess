#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
    private const float AutoPathHorizontalDeadzone = 6f;
    private const float AutoPathJumpThresholdPixels = 20f;
    private const float AutoPathArrivalPixels = 12f;
    private const int AutoPathMaxDropTiles = 12;
    private const int AutoPathJumpHeightTiles = 4;
    private const int AutoPathSearchRadiusTiles = 80;
    private const int AutoPathRepathCooldownFrames = 18;
    private const int AutoPathStuckFrames = 60;

    internal static void ToggleAutoPath(Player player)
    {
        if (_autoPathActive)
        {
            DisableAutoPath("Auto path disabled.");
            return;
        }

        if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out string label))
        {
            ScreenReaderService.Announce("Auto path requires an active waypoint, NPC, player, or crafting station.");
            return;
        }

        _autoPathActive = true;
        _autoPathLabel = label;
        _autoPathArrivedAnnounced = false;
        _autoPathLastDistanceTiles = 0f;
        _autoPathLastProgressFrame = Main.GameUpdateCount;
        _autoPathNodes.Clear();
        _autoPathNodeIndex = 0;
        _autoPathNextSearchFrame = 0;
        _autoPathPlatformDropHold = 0;
        ScreenReaderService.Announce(string.IsNullOrWhiteSpace(label) ? "Auto path enabled." : $"Auto pathing to {label}.");

        int facing = targetPosition.X >= player.Center.X ? 1 : -1;
        player.direction = facing;
    }

    internal static void DisableAutoPath(string? announcement = null)
    {
        if (!_autoPathActive)
        {
            return;
        }

        _autoPathActive = false;
        _autoPathLabel = string.Empty;
        _autoPathArrivedAnnounced = false;
        _autoPathNodes.Clear();
        _autoPathNodeIndex = 0;
        _autoPathNextSearchFrame = 0;
        _autoPathPlatformDropHold = 0;

        if (!string.IsNullOrWhiteSpace(announcement))
        {
            ScreenReaderService.Announce(announcement);
        }
    }

    internal static void ApplyAutoPath(Player player)
    {
        if (!_autoPathActive || Main.dedServ || Main.gameMenu || Main.gamePaused || _namingActive)
        {
            return;
        }

        if (player is null || !player.active || player.dead || player.ghost || player.whoAmI != Main.myPlayer)
        {
            DisableAutoPath();
            return;
        }

        if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out string label))
        {
            DisableAutoPath("Auto path cancelled; no tracked target.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            _autoPathLabel = label;
        }

        float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;
        if (_autoPathLastDistanceTiles <= 0f || distanceTiles + 0.35f < _autoPathLastDistanceTiles)
        {
            _autoPathLastDistanceTiles = distanceTiles;
            _autoPathLastProgressFrame = Main.GameUpdateCount;
        }

        if (distanceTiles * 16f <= AutoPathArrivalPixels)
        {
            if (!_autoPathArrivedAnnounced && !string.IsNullOrWhiteSpace(_autoPathLabel))
            {
                ScreenReaderService.Announce($"Arrived at {_autoPathLabel}.");
            }

            _autoPathArrivedAnnounced = true;
            DisableAutoPath();
            return;
        }

        if (Main.GameUpdateCount >= _autoPathNextSearchFrame || _autoPathNodes.Count == 0 || _autoPathNodeIndex >= _autoPathNodes.Count)
        {
            RebuildAutoPath(player, targetPosition);
        }

        if (_autoPathNodes.Count == 0)
        {
            // Fallback: move directly toward target if no path was found.
            ApplySimpleDrive(player, targetPosition);
            return;
        }

        Vector2 waypoint = _autoPathNodes[Math.Min(_autoPathNodeIndex, _autoPathNodes.Count - 1)];
        if (Vector2.Distance(player.Center, waypoint) <= AutoPathArrivalPixels)
        {
            _autoPathNodeIndex++;
            if (_autoPathNodeIndex >= _autoPathNodes.Count)
            {
                // Reached final node; rely on arrival threshold above.
                return;
            }

            waypoint = _autoPathNodes[_autoPathNodeIndex];
        }

        ApplySimpleDrive(player, waypoint);

        if (Main.GameUpdateCount - _autoPathLastProgressFrame > AutoPathStuckFrames)
        {
            _autoPathNextSearchFrame = 0;
            _autoPathPlatformDropHold = 0;
        }
    }

    private static void RebuildAutoPath(Player player, Vector2 targetPosition)
    {
        _autoPathNextSearchFrame = Main.GameUpdateCount + (ulong)AutoPathRepathCooldownFrames;
        _autoPathNodes.Clear();
        _autoPathNodeIndex = 0;

        if (!TryBuildTilePath(player, targetPosition, out List<Point> tiles))
        {
            return;
        }

        foreach (Point tile in tiles)
        {
            float worldX = (tile.X + 0.5f) * 16f;
            float worldY = (tile.Y - 1.5f) * 16f; // roughly center of a 3-tall player
            _autoPathNodes.Add(new Vector2(worldX, worldY));
        }
    }

    private static bool TryBuildTilePath(Player player, Vector2 targetPosition, out List<Point> path)
    {
        path = new List<Point>();

        Point start = GetPlayerFootTile(player);
        Point targetTile = new((int)(targetPosition.X / 16f), (int)(targetPosition.Y / 16f));

        if (!IsWithinWorld(start) || !IsWithinWorld(targetTile))
        {
            return false;
        }

        if (!TryFindStandable(targetTile.X, targetTile.Y, out Point standableTarget))
        {
            return false;
        }

        if (!TryFindStandable(start.X, start.Y, out Point standableStart))
        {
            return false;
        }

        int maxRadius = AutoPathSearchRadiusTiles;
        int minX = Math.Max(1, standableStart.X - maxRadius);
        int maxX = Math.Min(Main.maxTilesX - 2, standableStart.X + maxRadius);
        int minY = Math.Max(1, standableStart.Y - maxRadius);
        int maxY = Math.Min(Main.maxTilesY - 2, standableStart.Y + maxRadius);

        var queue = new Queue<Point>();
        var cameFrom = new Dictionary<Point, Point>();
        var visited = new HashSet<Point>();

        queue.Enqueue(standableStart);
        visited.Add(standableStart);

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();
            if (current == standableTarget)
            {
                ReconstructPath(current, standableStart, cameFrom, path);
                path.Reverse();
                return true;
            }

            foreach (Point neighbor in EnumerateNeighbors(current, minX, maxX, minY, maxY))
            {
                if (visited.Contains(neighbor))
                {
                    continue;
                }

                visited.Add(neighbor);
                cameFrom[neighbor] = current;
                queue.Enqueue(neighbor);
            }

            if (visited.Count > 8000)
            {
                break;
            }
        }

        return false;
    }

    private static void ReconstructPath(Point current, Point start, Dictionary<Point, Point> cameFrom, List<Point> path)
    {
        Point node = current;
        path.Add(node);
        while (cameFrom.TryGetValue(node, out Point parent))
        {
            node = parent;
            path.Add(node);
            if (node == start)
            {
                break;
            }
        }
    }

    private static IEnumerable<Point> EnumerateNeighbors(Point origin, int minX, int maxX, int minY, int maxY)
    {
        int originX = origin.X;
        int originY = origin.Y;

        foreach (int dir in new[] { -1, 1 })
        {
            int nextX = originX + dir;
            if (nextX < minX || nextX > maxX)
            {
                continue;
            }

            if (TryFindStandable(nextX, originY, AutoPathJumpHeightTiles, AutoPathMaxDropTiles, minY, maxY, out Point standable))
            {
                yield return standable;
            }
        }

        // Direct vertical drop if possible.
        if (TryFindStandable(originX, originY + 1, 0, AutoPathMaxDropTiles, minY, maxY, out Point down))
        {
            yield return down;
        }
    }

    private static bool TryFindStandable(int footX, int footY, out Point standable)
    {
        return TryFindStandable(footX, footY, AutoPathJumpHeightTiles, AutoPathMaxDropTiles, 1, Main.maxTilesY - 2, out standable);
    }

    private static bool TryFindStandable(int footX, int footY, int jumpHeight, int maxDrop, int minY, int maxY, out Point standable)
    {
        for (int offset = -jumpHeight; offset <= maxDrop; offset++)
        {
            int y = footY + offset;
            if (y < minY || y > maxY)
            {
                continue;
            }

            if (IsStandable(footX, y))
            {
                standable = new Point(footX, y);
                return true;
            }
        }

        standable = default;
        return false;
    }

    private static bool IsStandable(int footX, int footY)
    {
        int left = footX - 1;
        int right = footX;
        int head = footY - 2;

        if (head < 1 || footY + 1 >= Main.maxTilesY)
        {
            return false;
        }

        for (int x = left; x <= right; x++)
        {
            if (x < 1 || x >= Main.maxTilesX - 1)
            {
                return false;
            }
        }

        for (int x = left; x <= right; x++)
        {
            for (int y = head; y <= footY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType] && !IsDoor(tile))
                {
                    return false;
                }
            }
        }

        bool hasSupport = false;
        for (int x = left; x <= right; x++)
        {
            Tile below = Main.tile[x, footY + 1];
            if (below.HasTile && (Main.tileSolid[below.TileType] || Main.tileSolidTop[below.TileType] || IsDoor(below)))
            {
                hasSupport = true;
                break;
            }
        }

        return hasSupport;
    }

    private static Point GetPlayerFootTile(Player player)
    {
        int centerX = (int)((player.position.X + (player.width * 0.5f)) / 16f);
        int footY = (int)((player.position.Y + player.height) / 16f);
        return new Point(centerX, footY);
    }

    private static void ApplySimpleDrive(Player player, Vector2 targetPosition)
    {
        Vector2 offset = targetPosition - player.Center;
        bool moveRight = offset.X > AutoPathHorizontalDeadzone;
        bool moveLeft = offset.X < -AutoPathHorizontalDeadzone;

        int footX = (int)(player.Center.X / 16f);
        int footY = (int)(player.Bottom.Y / 16f);
        Tile below = Main.tile[footX, footY];
        bool onPlatform = below.HasTile && Main.tileSolidTop[below.TileType] && !Main.tileSolid[below.TileType];

        TriggersSet triggers = PlayerInput.Triggers.Current;
        triggers.Right = moveRight;
        triggers.Left = moveLeft;
        triggers.Jump = false;
        triggers.Down = false;

        player.controlRight = moveRight;
        player.controlLeft = moveLeft;

        int intendedDirection = moveRight ? 1 : moveLeft ? -1 : player.direction;
        player.direction = intendedDirection;

        bool targetAbove = offset.Y < -AutoPathJumpThresholdPixels;
        bool targetBelow = offset.Y > AutoPathJumpThresholdPixels;

        if (targetBelow && onPlatform)
        {
            _autoPathPlatformDropHold = Math.Max(_autoPathPlatformDropHold, 10);
        }

        bool shouldDrop = targetBelow || _autoPathPlatformDropHold > 0;
        bool shouldJump = targetAbove || (shouldDrop && onPlatform);
        triggers.Jump = shouldJump;
        triggers.Down = shouldDrop;
        player.controlJump = shouldJump;
        player.controlDown = shouldDrop;

        if (_autoPathPlatformDropHold > 0)
        {
            _autoPathPlatformDropHold--;
        }

        if (shouldJump && player.velocity.Y == 0f)
        {
            player.velocity.Y = Player.jumpSpeed;
        }

        float maxSpeed = Math.Max(4.5f, Math.Min(player.maxRunSpeed * 1.2f, 9.5f));
        float targetSpeed = intendedDirection * maxSpeed;
        float accel = player.runAcceleration * 1.8f;
        if (intendedDirection == 0)
        {
            targetSpeed = 0f;
            accel = player.runSlowdown * 1.2f;
        }

        player.velocity.X = MathHelper.Lerp(player.velocity.X, targetSpeed, MathHelper.Clamp(accel * 0.1f, 0.14f, 0.35f));
        player.runSlowdown = Math.Max(player.runSlowdown, 0.18f);
        PlayerInput.Triggers.Current = triggers;

        TryToggleDoor(player, intendedDirection);
    }

    private static bool IsDoor(Tile tile)
    {
        if (!tile.HasTile)
        {
            return false;
        }

        return tile.TileType == TileID.ClosedDoor || tile.TileType == TileID.OpenDoor;
    }

    private static void TryToggleDoor(Player player, int direction)
    {
        if (direction == 0)
        {
            return;
        }

        int footX = (int)(player.Center.X / 16f) + direction;
        int footY = (int)(player.Bottom.Y / 16f);

        for (int dy = -2; dy <= 0; dy++)
        {
            int checkY = footY + dy;
            if (checkY < 1 || checkY >= Main.maxTilesY - 1)
            {
                continue;
            }

            Tile tile = Main.tile[footX, checkY];
            if (tile.TileType != TileID.ClosedDoor)
            {
                continue;
            }

            int anchorY = checkY - (tile.TileFrameY / 54);
            anchorY = Math.Clamp(anchorY, 1, Main.maxTilesY - 2);
            if (WorldGen.OpenDoor(footX, anchorY, direction))
            {
                return;
            }

            WorldGen.OpenDoor(footX, anchorY, -direction);
            return;
        }
    }
}
