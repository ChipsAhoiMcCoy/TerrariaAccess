#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
    private readonly struct NpcGuidanceEntry
    {
        public readonly int NpcIndex;
        public readonly string DisplayName;
        public readonly float DistanceTiles;

        public NpcGuidanceEntry(int npcIndex, string displayName, float distanceTiles)
        {
            NpcIndex = npcIndex;
            DisplayName = displayName;
            DistanceTiles = distanceTiles;
        }
    }

    private static readonly List<NpcGuidanceEntry> NearbyNpcs = new();

    private readonly struct InteractableGuidanceEntry
    {
        public readonly Point Anchor;
        public readonly string DisplayName;
        public readonly Vector2 WorldPosition;
        public readonly float DistanceTiles;

        public InteractableGuidanceEntry(Point anchor, string displayName, Vector2 worldPosition, float distanceTiles)
        {
            Anchor = anchor;
            DisplayName = displayName;
            WorldPosition = worldPosition;
            DistanceTiles = distanceTiles;
        }
    }

    private readonly struct InteractableDefinition
    {
        public readonly int TileType;
        public readonly int WidthTiles;
        public readonly int HeightTiles;
        public readonly int FrameWidth;
        public readonly int FrameHeight;
        public readonly string DisplayName;

        public InteractableDefinition(int tileType, int widthTiles, int heightTiles, string displayName)
        {
            TileType = tileType;
            WidthTiles = Math.Max(1, widthTiles);
            HeightTiles = Math.Max(1, heightTiles);
            FrameWidth = WidthTiles * 18;
            FrameHeight = HeightTiles * 18;
            DisplayName = displayName;
        }
    }

    private static readonly List<InteractableGuidanceEntry> NearbyInteractables = new();
    private static readonly HashSet<Point> InteractableAnchorScratch = new();
    private static readonly InteractableDefinition[] InteractableDefinitions =
    {
        new(TileID.WorkBenches, 2, 1, "Work bench"),
        new(TileID.HeavyWorkBench, 3, 2, "Heavy work bench"),
        new(TileID.Anvils, 2, 1, "Anvil"),
        new(TileID.MythrilAnvil, 3, 2, "Mythril or Orichalcum anvil"),
        new(TileID.Furnaces, 3, 2, "Furnace"),
        new(TileID.Hellforge, 3, 2, "Hellforge"),
        new(TileID.AdamantiteForge, 3, 2, "Adamantite or Titanium forge"),
        new(TileID.TinkerersWorkbench, 3, 2, "Tinkerer's workbench"),
        new(TileID.ImbuingStation, 3, 3, "Imbuing station"),
        new(TileID.AlchemyTable, 3, 2, "Alchemy table"),
        new(TileID.Loom, 2, 3, "Loom"),
        new(TileID.Sawmill, 3, 3, "Sawmill"),
        new(TileID.Bottles, 1, 1, "Placed bottle"),
        new(TileID.Tables, 3, 2, "Table"),
        new(TileID.BewitchingTable, 3, 3, "Bewitching table"),
        new(TileID.CrystalBall, 2, 3, "Crystal ball"),
        new(TileID.DemonAltar, 3, 2, "Demon or crimson altar")
    };

    private static readonly Dictionary<int, List<InteractableDefinition>> InteractableDefinitionsByTileType = BuildInteractableDefinitionMap();

    private readonly struct PlayerGuidanceEntry
    {
        public readonly int PlayerIndex;
        public readonly string DisplayName;
        public readonly float DistanceTiles;

        public PlayerGuidanceEntry(int playerIndex, string displayName, float distanceTiles)
        {
            PlayerIndex = playerIndex;
            DisplayName = displayName;
            DistanceTiles = distanceTiles;
        }
    }

    private static readonly List<PlayerGuidanceEntry> NearbyPlayers = new();
    private static readonly List<ExplorationTargetRegistry.ExplorationTarget> NearbyExplorationTargets = new();

    private static Dictionary<int, List<InteractableDefinition>> BuildInteractableDefinitionMap()
    {
        Dictionary<int, List<InteractableDefinition>> map = new();
        foreach (InteractableDefinition definition in InteractableDefinitions)
        {
            if (!map.TryGetValue(definition.TileType, out List<InteractableDefinition>? list))
            {
                list = new List<InteractableDefinition>();
                map[definition.TileType] = list;
            }

            list.Add(definition);
        }

        return map;
    }

    private static void RefreshNpcEntries(Player player)
    {
        int preservedNpcIndex = -1;
        if (_selectedNpcIndex >= 0 && _selectedNpcIndex < NearbyNpcs.Count)
        {
            preservedNpcIndex = NearbyNpcs[_selectedNpcIndex].NpcIndex;
        }

        NearbyNpcs.Clear();
        if (player is null || !player.active)
        {
            _selectedNpcIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (TryCreateNpcEntry(i, origin, includeOutOfRange: false, out NpcGuidanceEntry entry))
            {
                NearbyNpcs.Add(entry);
            }
        }

        if (preservedNpcIndex >= 0 && !NearbyNpcs.Exists(entry => entry.NpcIndex == preservedNpcIndex))
        {
            if (TryCreateNpcEntry(preservedNpcIndex, origin, includeOutOfRange: true, out NpcGuidanceEntry preservedEntry))
            {
                NearbyNpcs.Add(preservedEntry);
            }
            else
            {
                preservedNpcIndex = -1;
            }
        }

        NearbyNpcs.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (NearbyNpcs.Count == 0)
        {
            _selectedNpcIndex = -1;
            return;
        }

        if (preservedNpcIndex >= 0)
        {
            int restoredIndex = NearbyNpcs.FindIndex(entry => entry.NpcIndex == preservedNpcIndex);
            if (restoredIndex >= 0)
            {
                _selectedNpcIndex = restoredIndex;
                return;
            }
        }

        if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
        {
            _selectedNpcIndex = 0;
        }
    }

    private static void RefreshPlayerEntries(Player player)
    {
        int preservedPlayerIndex = -1;
        if (_selectedPlayerIndex >= 0 && _selectedPlayerIndex < NearbyPlayers.Count)
        {
            preservedPlayerIndex = NearbyPlayers[_selectedPlayerIndex].PlayerIndex;
        }

        NearbyPlayers.Clear();
        if (player is null || !player.active || Main.netMode == NetmodeID.SinglePlayer)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            if (TryCreatePlayerEntry(i, player, out PlayerGuidanceEntry entry))
            {
                NearbyPlayers.Add(entry);
            }
        }

        if (NearbyPlayers.Count == 0)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        NearbyPlayers.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (preservedPlayerIndex >= 0)
        {
            int restoredIndex = NearbyPlayers.FindIndex(entry => entry.PlayerIndex == preservedPlayerIndex);
            if (restoredIndex >= 0)
            {
                _selectedPlayerIndex = restoredIndex;
                return;
            }
        }

        if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
        {
            _selectedPlayerIndex = 0;
        }
    }

    private static void RefreshExplorationEntries()
    {
        IReadOnlyList<ExplorationTargetRegistry.ExplorationTarget> snapshot = ExplorationTargetRegistry.GetSnapshot();
        int preservedIndex = _selectedExplorationIndex;

        NearbyExplorationTargets.Clear();
        if (snapshot.Count > 0)
        {
            NearbyExplorationTargets.AddRange(snapshot);
            NearbyExplorationTargets.Sort(static (left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));
        }

        if (NearbyExplorationTargets.Count == 0)
        {
            _selectedExplorationIndex = -1;
            return;
        }

        if (preservedIndex >= 0 && preservedIndex < NearbyExplorationTargets.Count)
        {
            _selectedExplorationIndex = preservedIndex;
            return;
        }

        _selectedExplorationIndex = Math.Clamp(_selectedExplorationIndex, -1, NearbyExplorationTargets.Count - 1);
    }

    private static void RefreshInteractableEntries(Player player)
    {
        bool hasPreservedAnchor = false;
        Point preservedAnchor = Point.Zero;
        if (_selectedInteractableIndex >= 0 && _selectedInteractableIndex < NearbyInteractables.Count)
        {
            preservedAnchor = NearbyInteractables[_selectedInteractableIndex].Anchor;
            hasPreservedAnchor = true;
        }

        NearbyInteractables.Clear();
        if (player is null || !player.active)
        {
            _selectedInteractableIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        int scanRadius = (int)Math.Clamp(DistanceReferenceTiles + 8f, 4f, 240f);
        int playerTileX = (int)(origin.X / 16f);
        int playerTileY = (int)(origin.Y / 16f);
        int minX = Math.Max(0, playerTileX - scanRadius);
        int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
        int minY = Math.Max(0, playerTileY - scanRadius);
        int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

        InteractableAnchorScratch.Clear();
        bool preservedIncluded = false;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile)
                {
                    continue;
                }

                if (!InteractableDefinitionsByTileType.TryGetValue(tile.TileType, out List<InteractableDefinition>? definitions))
                {
                    continue;
                }

                foreach (InteractableDefinition definition in definitions)
                {
                    if (!IsInteractableAnchor(tile, definition))
                    {
                        continue;
                    }

                    Point anchor = new(x, y);
                    bool isPreservedAnchor = hasPreservedAnchor && anchor == preservedAnchor;
                    if (!InteractableAnchorScratch.Add(anchor) && !isPreservedAnchor)
                    {
                        continue;
                    }

                    if (TryCreateInteractableEntry(definition, anchor, origin, isPreservedAnchor, out InteractableGuidanceEntry entry))
                    {
                        NearbyInteractables.Add(entry);
                        if (isPreservedAnchor)
                        {
                            preservedIncluded = true;
                        }
                    }
                }
            }
        }

        if (hasPreservedAnchor && !preservedIncluded && TryCreateInteractableEntryForAnchor(preservedAnchor, origin, includeOutOfRange: true, out InteractableGuidanceEntry preservedEntry))
        {
            NearbyInteractables.Add(preservedEntry);
        }

        NearbyInteractables.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (NearbyInteractables.Count == 0)
        {
            _selectedInteractableIndex = -1;
            return;
        }

        if (hasPreservedAnchor)
        {
            int restoredIndex = NearbyInteractables.FindIndex(entry => entry.Anchor == preservedAnchor);
            if (restoredIndex >= 0)
            {
                _selectedInteractableIndex = restoredIndex;
                return;
            }
        }

        if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
        {
            _selectedInteractableIndex = 0;
        }
    }

    private static bool TryCreateNpcEntry(int npcIndex, Vector2 origin, bool includeOutOfRange, out NpcGuidanceEntry entry)
    {
        entry = default;
        if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
        {
            return false;
        }

        NPC npc = Main.npc[npcIndex];
        if (!IsTrackableNpc(npc))
        {
            return false;
        }

        float distanceTiles = Vector2.Distance(origin, npc.Center) / 16f;
        if (!includeOutOfRange && distanceTiles > DistanceReferenceTiles)
        {
            return false;
        }

        string displayName = ResolveNpcDisplayName(npc);
        entry = new NpcGuidanceEntry(npcIndex, displayName, distanceTiles);
        return true;
    }

    private static bool TryCreatePlayerEntry(int playerIndex, Player owner, out PlayerGuidanceEntry entry)
    {
        entry = default;
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
        {
            return false;
        }

        Player candidate = Main.player[playerIndex];
        if (!IsTrackablePlayer(candidate, owner))
        {
            return false;
        }

        float distanceTiles = Vector2.Distance(owner.Center, candidate.Center) / 16f;
        string displayName = ResolvePlayerDisplayName(candidate, playerIndex);
        entry = new PlayerGuidanceEntry(playerIndex, displayName, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntry(InteractableDefinition definition, Point anchor, Vector2 origin, bool includeOutOfRange, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (!IsWithinWorld(anchor))
        {
            return false;
        }

        Tile tile = Main.tile[anchor.X, anchor.Y];
        if (!tile.HasTile || tile.TileType != definition.TileType)
        {
            return false;
        }

        Vector2 worldPosition = ResolveInteractableWorldPosition(anchor, definition);
        float distanceTiles = Vector2.Distance(worldPosition, origin) / 16f;
        if (!includeOutOfRange && distanceTiles > DistanceReferenceTiles)
        {
            return false;
        }

        entry = new InteractableGuidanceEntry(anchor, definition.DisplayName, worldPosition, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntryForAnchor(Point anchor, Vector2 origin, bool includeOutOfRange, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (!IsWithinWorld(anchor))
        {
            return false;
        }

        Tile tile = Main.tile[anchor.X, anchor.Y];
        if (!tile.HasTile)
        {
            return false;
        }

        if (!InteractableDefinitionsByTileType.TryGetValue(tile.TileType, out List<InteractableDefinition>? definitions))
        {
            return false;
        }

        foreach (InteractableDefinition definition in definitions)
        {
            if (!IsInteractableAnchor(tile, definition))
            {
                continue;
            }

            if (TryCreateInteractableEntry(definition, anchor, origin, includeOutOfRange, out entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInteractableAnchor(Tile tile, InteractableDefinition definition)
    {
        if (definition.FrameWidth <= 0 || definition.FrameHeight <= 0)
        {
            return true;
        }

        int frameX = tile.TileFrameX;
        int frameY = tile.TileFrameY;
        if (frameX < 0 || frameY < 0)
        {
            return false;
        }

        return frameX % definition.FrameWidth == 0 && frameY % definition.FrameHeight == 0;
    }

    private static Vector2 ResolveInteractableWorldPosition(Point anchor, InteractableDefinition definition)
    {
        float centerX = anchor.X + (definition.WidthTiles * 0.5f);
        float centerY = anchor.Y + (definition.HeightTiles * 0.5f);
        return new Vector2(centerX * 16f, centerY * 16f);
    }

    private static bool IsTrackableNpc(NPC npc)
    {
        if (!npc.active || npc.lifeMax <= 0)
        {
            return false;
        }

        if (npc.townNPC || NPCID.Sets.ActsLikeTownNPC[npc.type] || NPCID.Sets.IsTownPet[npc.type])
        {
            return true;
        }

        return false;
    }

    private static bool IsTrackablePlayer(Player candidate, Player owner)
    {
        if (candidate is null || !candidate.active || candidate.dead || candidate.ghost || candidate == owner)
        {
            return false;
        }

        if (string.Equals(candidate.name, owner.name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (candidate.team != 0 && candidate.team == owner.team)
        {
            return true;
        }

        return true;
    }

    private static string ResolveNpcDisplayName(NPC npc)
    {
        if (!string.IsNullOrWhiteSpace(npc.FullName))
        {
            return npc.FullName;
        }

        if (!string.IsNullOrWhiteSpace(npc.GivenName))
        {
            return npc.GivenName;
        }

        string localized = Lang.GetNPCNameValue(npc.type);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        return "NPC";
    }

    private static string ResolvePlayerDisplayName(Player player, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(player.name))
        {
            return player.name;
        }

        return $"Player {fallbackIndex + 1}";
    }

    private static bool IsWithinWorld(Point point)
    {
        return point.X >= 0 && point.X < Main.maxTilesX && point.Y >= 0 && point.Y < Main.maxTilesY;
    }
}
