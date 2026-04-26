#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Automatically mines entire veins of ores and gems when breaking a single tile.
/// Uses flood-fill to find connected tiles of the same type.
/// Queues tiles for processing to ensure proper item drops.
/// </summary>
public class ChunkMinerSystem : ModSystem
{
    // Queue of pending chunk mine operations
    private static readonly Queue<ChunkMineRequest> _pendingRequests = new();

    // Track which tiles are already queued to avoid duplicates
    private static readonly HashSet<(int x, int y)> _queuedTiles = new();
    private static bool _processingQueuedTiles;

    // Gems tiles (63-68) and ExposedGems (178)
    private static readonly HashSet<int> GemTileTypes = new()
    {
        TileID.Sapphire,    // 63
        TileID.Ruby,        // 64
        TileID.Emerald,     // 65
        TileID.Topaz,       // 66
        TileID.Amethyst,    // 67
        TileID.Diamond,     // 68
        TileID.ExposedGems, // 178 - embedded gems in stone
    };

    // Additional mineable deposits that benefit from chunk mining
    private static readonly HashSet<int> OtherMineableTileTypes = new()
    {
        TileID.DesertFossil,     // 396 - Sturdy/Desert Fossil
        TileID.FossilOre,        // 408 - Fossil ore from underground desert
        TileID.Silt,             // 123 - Silt blocks
        TileID.Slush,            // 224 - Slush blocks
    };

    /// <summary>
    /// Checks if a tile type should trigger chunk mining.
    /// </summary>
    public static bool IsChunkMineableTile(int tileType)
    {
        // Bounds check for TileID.Sets.Ore array
        if (tileType >= 0 && tileType < TileID.Sets.Ore.Length && TileID.Sets.Ore[tileType])
            return true;

        // Check gems
        if (GemTileTypes.Contains(tileType))
            return true;

        // Check other mineable deposits
        if (OtherMineableTileTypes.Contains(tileType))
            return true;

        return false;
    }

    /// <summary>
    /// Called by the GlobalTile hook when a chunk-mineable tile is broken.
    /// Queues connected tiles for mining on the next frame.
    /// </summary>
    public static void QueueChunkMine(int startX, int startY, int targetType)
    {
        if (!TerrariaAccessConfig.Instance.ChunkMinerEnabled)
            return;

        if (_processingQueuedTiles)
            return;

        // Find all connected tiles using BFS
        var tilesToMine = FindConnectedTiles(startX, startY, targetType);

        // Queue tiles that aren't already queued
        foreach (var pos in tilesToMine)
        {
            if (!_queuedTiles.Contains(pos))
            {
                _queuedTiles.Add(pos);
                _pendingRequests.Enqueue(new ChunkMineRequest(pos.x, pos.y, targetType));
            }
        }
    }

    /// <summary>
    /// Uses BFS flood-fill to find all connected tiles of the same type.
    /// </summary>
    private static List<(int x, int y)> FindConnectedTiles(int startX, int startY, int targetType)
    {
        var result = new List<(int x, int y)>();
        var maxTiles = TerrariaAccessConfig.Instance.ChunkMinerMaxTiles;
        var visited = new HashSet<(int x, int y)>();
        var queue = new Queue<(int x, int y)>();

        // Start with the original tile's neighbors (the original is already being killed)
        visited.Add((startX, startY));

        // Add the 8 neighbors to start
        AddNeighborsToSearch(startX, startY, targetType, queue, visited);

        while (queue.Count > 0 && result.Count < maxTiles)
        {
            var (x, y) = queue.Dequeue();

            // Double-check bounds and tile validity
            if (!WorldGen.InWorld(x, y, 1))
                continue;

            var tile = Main.tile[x, y];
            if (!tile.HasTile || tile.TileType != targetType)
                continue;

            // Add to result
            result.Add((x, y));

            // Add neighbors of this tile to the search queue
            AddNeighborsToSearch(x, y, targetType, queue, visited);
        }

        return result;
    }

    /// <summary>
    /// Adds valid neighboring tiles to the search queue.
    /// </summary>
    private static void AddNeighborsToSearch(
        int x, int y,
        int targetType,
        Queue<(int x, int y)> queue,
        HashSet<(int x, int y)> visited)
    {
        // Check all 8 directions (cardinal + diagonals) for better vein coverage
        int[] dx = { -1, 1, 0, 0, -1, -1, 1, 1 };
        int[] dy = { 0, 0, -1, 1, -1, 1, -1, 1 };

        for (int dir = 0; dir < 8; dir++)
        {
            int nx = x + dx[dir];
            int ny = y + dy[dir];

            // Skip if already visited
            if (visited.Contains((nx, ny)))
                continue;

            // Mark as visited
            visited.Add((nx, ny));

            // Check bounds
            if (!WorldGen.InWorld(nx, ny, 1))
                continue;

            // Check if tile matches target type
            var tile = Main.tile[nx, ny];
            if (!tile.HasTile || tile.TileType != targetType)
                continue;

            // Add to queue for searching
            queue.Enqueue((nx, ny));
        }
    }

    /// <summary>
    /// Process queued chunk mine requests each frame.
    /// </summary>
    public override void PostUpdateWorld()
    {
        if (_pendingRequests.Count == 0)
            return;

        // Process all pending requests this frame
        // (they were found in the same frame, so process them together)
        _processingQueuedTiles = true;
        try
        {
            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();
                _queuedTiles.Remove((request.X, request.Y));

                // Verify tile still exists and matches expected type
                if (!WorldGen.InWorld(request.X, request.Y, 1))
                    continue;

                var tile = Main.tile[request.X, request.Y];
                if (!tile.HasTile || tile.TileType != request.TileType)
                    continue;

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, request.X, request.Y);
                    continue;
                }

                // Let Terraria handle whether the tile can actually be removed.
                WorldGen.KillTile(request.X, request.Y, fail: false, effectOnly: false, noItem: false);

                bool removed = !Main.tile[request.X, request.Y].HasTile;
                if (removed && Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendTileSquare(-1, request.X, request.Y, 1);
                }
            }
        }
        finally
        {
            _processingQueuedTiles = false;
        }
    }

    public override void Unload()
    {
        _pendingRequests.Clear();
        _queuedTiles.Clear();
        _processingQueuedTiles = false;
    }

    /// <summary>
    /// Multiplayer clients also receive tile-kill broadcasts for other players and server changes.
    /// Only the local player's active mining target should start a client-side chunk mine.
    /// </summary>
    public static bool CanStartClientChunkMine(int x, int y)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return true;

        if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers)
            return false;

        Player player = Main.LocalPlayer;
        if (!player.active || player.dead)
            return false;

        Item heldItem = player.HeldItem;
        if (heldItem.pick <= 0)
            return false;

        if (!player.controlUseItem || player.itemAnimation <= 0)
            return false;

        return Player.tileTargetX == x && Player.tileTargetY == y;
    }

    private readonly struct ChunkMineRequest
    {
        public readonly int X;
        public readonly int Y;
        public readonly int TileType;

        public ChunkMineRequest(int x, int y, int tileType)
        {
            X = x;
            Y = y;
            TileType = tileType;
        }
    }
}

/// <summary>
/// GlobalTile hook that triggers chunk mining when an ore or gem is broken.
/// </summary>
public class ChunkMinerGlobalTile : GlobalTile
{
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        // Don't do anything if:
        // - Feature is disabled
        // - Tile mining failed
        // - Effect only (just dust, no actual break)
        // - Not a chunk-mineable tile type
        if (!TerrariaAccessConfig.Instance.ChunkMinerEnabled)
            return;

        if (fail || effectOnly)
            return;

        if (!ChunkMinerSystem.IsChunkMineableTile(type))
            return;

        if (!ChunkMinerSystem.CanStartClientChunkMine(i, j))
            return;

        // Queue connected tiles for mining (will be processed next frame)
        ChunkMinerSystem.QueueChunkMine(i, j, type);
    }
}
