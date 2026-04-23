#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Systems.Guidance;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem
{
    private static readonly List<GuidanceEntry> NearbyNpcs = new();

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

    private static readonly List<GuidanceEntry> NearbyInteractables = new();
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

    private static readonly List<GuidanceEntry> NearbyPlayers = new();
    private static readonly List<ExplorationTargetRegistry.ExplorationTarget> NearbyExplorationTargets = new();

    private static readonly List<GuidanceEntry> NearbyDroppedItems = new();

    private static readonly List<GuidanceEntry> NearbyCritters = new();

    private static readonly List<GuidanceEntry> NearbyPlantlife = new();
    private static readonly HashSet<Point> PlantlifeAnchorScratch = new();

    private static readonly List<GuidanceEntry> NearbyHostileMobs = new();
    private static readonly HashSet<int> CustomEntryScratch = new();

    // Tile 703 is JunglePlantsEcho (Don't Dig Up seed); not named in tModLoader's TileID reference.
    private const int JunglePlantsEchoTileId = 703;

    private static readonly int[] PlantlifeTileTypes =
    {
        TileID.MatureHerbs,
        TileID.BloomingHerbs,
        TileID.MushroomPlants,
        TileID.DyePlants,
        TileID.JunglePlants,
        JunglePlantsEchoTileId,
        TileID.AbigailsFlower
    };

    // Jungle Plants tiles share one TileID across many frames (tall grass, vines,
    // spore plants, etc.). Only frameX == 144 is the Jungle Spores variant — see
    // Terraria WorldGen.KillTile drop logic for tile types 61 and 703.
    private const int JungleSporeFrameX = 144;

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

    private static int CompareGuidanceEntries(GuidanceEntry left, GuidanceEntry right)
    {
        int cmp = left.DistanceTiles.CompareTo(right.DistanceTiles);
        if (cmp != 0) return cmp;
        cmp = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        if (cmp != 0) return cmp;
        cmp = left.WorldPosition.X.CompareTo(right.WorldPosition.X);
        if (cmp != 0) return cmp;
        return left.WorldPosition.Y.CompareTo(right.WorldPosition.Y);
    }

    private static void RefreshNpcEntries(Player player)
    {
        int preservedNpcIndex = -1;
        if (_selectedNpcIndex >= 0 && _selectedNpcIndex < NearbyNpcs.Count)
        {
            preservedNpcIndex = NearbyNpcs[_selectedNpcIndex].Index;
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
            if (TryCreateNpcEntry(i, origin, includeOutOfRange: false, out GuidanceEntry entry))
            {
                NearbyNpcs.Add(entry);
            }
        }

        if (preservedNpcIndex >= 0 && !NearbyNpcs.Exists(entry => entry.Index == preservedNpcIndex))
        {
            preservedNpcIndex = -1;
        }

        NearbyNpcs.Sort(CompareGuidanceEntries);

        if (NearbyNpcs.Count == 0)
        {
            _selectedNpcIndex = -1;
            return;
        }

        if (preservedNpcIndex >= 0)
        {
            int restoredIndex = NearbyNpcs.FindIndex(entry => entry.Index == preservedNpcIndex);
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
            preservedPlayerIndex = NearbyPlayers[_selectedPlayerIndex].Index;
        }

        NearbyPlayers.Clear();
        if (player is null || !player.active || Main.netMode == NetmodeID.SinglePlayer)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            if (TryCreatePlayerEntry(i, player, out GuidanceEntry entry))
            {
                NearbyPlayers.Add(entry);
            }
        }

        if (NearbyPlayers.Count == 0)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        NearbyPlayers.Sort(CompareGuidanceEntries);

        if (preservedPlayerIndex >= 0)
        {
            int restoredIndex = NearbyPlayers.FindIndex(entry => entry.Index == preservedPlayerIndex);
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
        ExplorationTargetRegistry.ExplorationTarget? retainedSelection = _lastExplorationSelection;

        NearbyExplorationTargets.Clear();
        if (snapshot.Count > 0)
        {
            NearbyExplorationTargets.AddRange(snapshot);
            NearbyExplorationTargets.Sort(static (left, right) =>
            {
                int compareDistance = left.DistanceTiles.CompareTo(right.DistanceTiles);
                if (compareDistance != 0)
                {
                    return compareDistance;
                }

                int compareLabel = string.Compare(
                    SanitizeLabel(left.Label),
                    SanitizeLabel(right.Label),
                    StringComparison.OrdinalIgnoreCase);
                if (compareLabel != 0)
                {
                    return compareLabel;
                }

                int compareX = left.WorldPosition.X.CompareTo(right.WorldPosition.X);
                if (compareX != 0)
                {
                    return compareX;
                }

                return left.WorldPosition.Y.CompareTo(right.WorldPosition.Y);
            });
        }

        if (NearbyExplorationTargets.Count == 0)
        {
            _selectedExplorationIndex = -1;
            _lastExplorationSelection = null;
            ExplorationTargetRegistry.SetSelectedTarget(null);
            return;
        }

        if (retainedSelection.HasValue)
        {
            ExplorationTargetRegistry.ExplorationTarget target = retainedSelection.Value;
            int bestMatchIndex = NearbyExplorationTargets.FindIndex(candidate => candidate.Key.Equals(target.Key));
            float bestMatchDistance = float.MaxValue;
            if (bestMatchIndex < 0)
            {
                // Prefer the closest matching entry so nearby identical labels don't collapse into the first match.
                for (int i = 0; i < NearbyExplorationTargets.Count; i++)
                {
                    ExplorationTargetRegistry.ExplorationTarget candidate = NearbyExplorationTargets[i];
                    if (!IsExplorationTargetMatch(candidate, target))
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(candidate.WorldPosition, target.WorldPosition);
                    if (distance < bestMatchDistance)
                    {
                        bestMatchDistance = distance;
                        bestMatchIndex = i;
                    }
                }
            }

            if (bestMatchIndex >= 0)
            {
                _selectedExplorationIndex = bestMatchIndex;
                _lastExplorationSelection = NearbyExplorationTargets[_selectedExplorationIndex];
                ExplorationTargetRegistry.SetSelectedTarget(_lastExplorationSelection);
                return;
            }

            _selectedExplorationIndex = -1;
            _lastExplorationSelection = null;
            ExplorationTargetRegistry.SetSelectedTarget(null);
            return;
        }

        if (preservedIndex >= 0 && preservedIndex < NearbyExplorationTargets.Count)
        {
            _selectedExplorationIndex = preservedIndex;
            _lastExplorationSelection = NearbyExplorationTargets[_selectedExplorationIndex];
            ExplorationTargetRegistry.SetSelectedTarget(_lastExplorationSelection);
            return;
        }

        _selectedExplorationIndex = Math.Clamp(_selectedExplorationIndex, -1, NearbyExplorationTargets.Count - 1);
        if (_selectedExplorationIndex >= 0)
        {
            _lastExplorationSelection = NearbyExplorationTargets[_selectedExplorationIndex];
            ExplorationTargetRegistry.SetSelectedTarget(_lastExplorationSelection);
        }
        else
        {
            _lastExplorationSelection = null;
            ExplorationTargetRegistry.SetSelectedTarget(null);
        }
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
        int scanRadius = (int)Math.Clamp(ScanRangeTiles + 8f, 4f, 240f);
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

                    if (TryCreateInteractableEntry(definition, anchor, origin, includeOutOfRange: false, out GuidanceEntry entry))
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

        if (hasPreservedAnchor && !preservedIncluded)
        {
            hasPreservedAnchor = false;
        }

        NearbyInteractables.Sort(CompareGuidanceEntries);

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

    private static bool TryCreateNpcEntry(int npcIndex, Vector2 origin, bool includeOutOfRange, out GuidanceEntry entry)
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
        if (!includeOutOfRange && distanceTiles > ScanRangeTiles)
        {
            return false;
        }

        string displayName = ResolveNpcDisplayName(npc);
        entry = GuidanceEntry.CreateNpc(npcIndex, displayName, npc.Center, distanceTiles);
        return true;
    }

    private static bool TryCreatePlayerEntry(int playerIndex, Player owner, out GuidanceEntry entry)
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
        entry = GuidanceEntry.CreatePlayer(playerIndex, displayName, candidate.Center, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntry(InteractableDefinition definition, Point anchor, Vector2 origin, bool includeOutOfRange, out GuidanceEntry entry)
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
        if (!includeOutOfRange && distanceTiles > ScanRangeTiles)
        {
            return false;
        }

        entry = GuidanceEntry.CreateInteractable(anchor, definition.DisplayName, worldPosition, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntryForAnchor(Point anchor, Vector2 origin, bool includeOutOfRange, out GuidanceEntry entry)
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

    private const float ScreenEdgePaddingPixels = 48f;

    private static bool IsWorldPositionApproximatelyOnScreen(Vector2 worldPosition, float paddingPixels = ScreenEdgePaddingPixels)
    {
        float zoomX = Math.Abs(Main.GameViewMatrix.Zoom.X) < 0.001f ? 1f : Main.GameViewMatrix.Zoom.X;
        float zoomY = Math.Abs(Main.GameViewMatrix.Zoom.Y) < 0.001f ? zoomX : Main.GameViewMatrix.Zoom.Y;
        float zoom = Math.Max(0.001f, Math.Min(zoomX, zoomY));

        float viewWidth = Main.screenWidth / zoom;
        float viewHeight = Main.screenHeight / zoom;
        Vector2 topLeft = Main.screenPosition;

        float left = topLeft.X - paddingPixels;
        float top = topLeft.Y - paddingPixels;
        float right = left + viewWidth + paddingPixels * 2f;
        float bottom = top + viewHeight + paddingPixels * 2f;

        return worldPosition.X >= left && worldPosition.X <= right &&
               worldPosition.Y >= top && worldPosition.Y <= bottom;
    }

    private static void RefreshDroppedItemEntries(Player player)
    {
        int preservedItemIndex = -1;
        if (_selectedDroppedItemIndex >= 0 && _selectedDroppedItemIndex < NearbyDroppedItems.Count)
        {
            preservedItemIndex = NearbyDroppedItems[_selectedDroppedItemIndex].Index;
        }

        NearbyDroppedItems.Clear();
        if (player is null || !player.active)
        {
            _selectedDroppedItemIndex = -1;
            return;
        }

        Vector2 origin = player.Center;

        for (int i = 0; i < Main.maxItems; i++)
        {
            Item item = Main.item[i];
            if (!item.active || item.stack <= 0)
            {
                continue;
            }

            Vector2 itemCenter = item.Center;
            if (!IsWorldPositionApproximatelyOnScreen(itemCenter))
            {
                continue;
            }

            float distanceTiles = Vector2.Distance(origin, itemCenter) / 16f;
            string displayName = ResolveDroppedItemDisplayName(item);
            NearbyDroppedItems.Add(GuidanceEntry.CreateDroppedItem(i, displayName, itemCenter, distanceTiles));
        }

        NearbyDroppedItems.Sort(CompareGuidanceEntries);

        if (NearbyDroppedItems.Count == 0)
        {
            _selectedDroppedItemIndex = -1;
            return;
        }

        if (preservedItemIndex >= 0)
        {
            int restoredIndex = NearbyDroppedItems.FindIndex(entry => entry.Index == preservedItemIndex);
            if (restoredIndex >= 0)
            {
                _selectedDroppedItemIndex = restoredIndex;
                return;
            }
        }

        // Preserve "All" mode (-1) for categories that support it; only clamp out-of-bounds positive indices
        if (_selectedDroppedItemIndex >= NearbyDroppedItems.Count)
        {
            _selectedDroppedItemIndex = NearbyDroppedItems.Count - 1;
        }
    }

    private static string ResolveDroppedItemDisplayName(Item item)
    {
        string name = ResolveDroppedItemBaseName(item);
        return item.stack > 1 ? $"{item.stack} {name}" : name;
    }

    private static string ResolveDroppedItemBaseName(Item item)
    {
        string name = Lang.GetItemNameValue(item.type);
        return !string.IsNullOrWhiteSpace(name) ? name : "Item";
    }

    private static void RefreshCritterEntries(Player player)
    {
        int preservedNpcIndex = -1;
        if (_selectedCritterIndex >= 0 && _selectedCritterIndex < NearbyCritters.Count)
        {
            preservedNpcIndex = NearbyCritters[_selectedCritterIndex].Index;
        }

        NearbyCritters.Clear();
        if (player is null || !player.active)
        {
            _selectedCritterIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || npc.lifeMax <= 0)
            {
                continue;
            }

            if (!NPCID.Sets.CountsAsCritter[npc.type])
            {
                continue;
            }

            float distanceTiles = Vector2.Distance(origin, npc.Center) / 16f;
            if (distanceTiles > ScanRangeTiles)
            {
                continue;
            }

            string displayName = ResolveCritterDisplayName(npc);
            NearbyCritters.Add(GuidanceEntry.CreateCritter(i, displayName, npc.Center, distanceTiles));
        }

        NearbyCritters.Sort(CompareGuidanceEntries);

        if (NearbyCritters.Count == 0)
        {
            _selectedCritterIndex = -1;
            return;
        }

        if (preservedNpcIndex >= 0)
        {
            int restoredIndex = NearbyCritters.FindIndex(entry => entry.Index == preservedNpcIndex);
            if (restoredIndex >= 0)
            {
                _selectedCritterIndex = restoredIndex;
                return;
            }
        }

        // Preserve "All" mode (-1) for categories that support it; only clamp out-of-bounds positive indices
        if (_selectedCritterIndex >= NearbyCritters.Count)
        {
            _selectedCritterIndex = NearbyCritters.Count - 1;
        }
    }

    private static string ResolveCritterDisplayName(NPC npc)
    {
        string localized = Lang.GetNPCNameValue(npc.type);
        return !string.IsNullOrWhiteSpace(localized) ? localized : "Critter";
    }

    private static void RefreshPlantlifeEntries(Player player)
    {
        bool hasPreservedAnchor = false;
        Point preservedAnchor = Point.Zero;
        if (_selectedPlantlifeIndex >= 0 && _selectedPlantlifeIndex < NearbyPlantlife.Count)
        {
            preservedAnchor = NearbyPlantlife[_selectedPlantlifeIndex].Anchor;
            hasPreservedAnchor = true;
        }

        NearbyPlantlife.Clear();
        PlantlifeAnchorScratch.Clear();

        if (player is null || !player.active)
        {
            _selectedPlantlifeIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        int scanRadius = (int)Math.Clamp(ScanRangeTiles + 8f, 4f, 120f);
        int playerTileX = (int)(origin.X / 16f);
        int playerTileY = (int)(origin.Y / 16f);
        int minX = Math.Max(0, playerTileX - scanRadius);
        int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
        int minY = Math.Max(0, playerTileY - scanRadius);
        int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile)
                {
                    continue;
                }

                if (Array.IndexOf(PlantlifeTileTypes, tile.TileType) < 0)
                {
                    continue;
                }

                if (!IsTrackablePlantlifeFrame(tile))
                {
                    continue;
                }

                Point anchor = new(x, y);
                if (!PlantlifeAnchorScratch.Add(anchor))
                {
                    continue;
                }

                Vector2 worldPosition = new((x + 0.5f) * 16f, (y + 0.5f) * 16f);
                float distanceTiles = Vector2.Distance(worldPosition, origin) / 16f;

                if (distanceTiles > ScanRangeTiles)
                {
                    continue;
                }

                string displayName = ResolvePlantDisplayName(tile);
                NearbyPlantlife.Add(GuidanceEntry.CreatePlantlife(anchor, displayName, worldPosition, distanceTiles));
            }
        }

        NearbyPlantlife.Sort(CompareGuidanceEntries);

        if (NearbyPlantlife.Count == 0)
        {
            _selectedPlantlifeIndex = -1;
            return;
        }

        if (hasPreservedAnchor)
        {
            int restoredIndex = NearbyPlantlife.FindIndex(entry => entry.Anchor == preservedAnchor);
            if (restoredIndex >= 0)
            {
                _selectedPlantlifeIndex = restoredIndex;
                return;
            }
        }

        // Preserve "All" mode (-1) for categories that support it; only clamp out-of-bounds positive indices
        if (_selectedPlantlifeIndex >= NearbyPlantlife.Count)
        {
            _selectedPlantlifeIndex = NearbyPlantlife.Count - 1;
        }
    }

    private static bool IsTrackablePlantlifeFrame(Tile tile)
    {
        if (tile.TileType == TileID.JunglePlants || tile.TileType == JunglePlantsEchoTileId)
        {
            return tile.TileFrameX == JungleSporeFrameX;
        }

        return true;
    }

    private static string ResolvePlantDisplayName(Tile tile)
    {
        if (tile.TileType == TileID.JunglePlants || tile.TileType == JunglePlantsEchoTileId)
        {
            return "Jungle Spores";
        }

        return tile.TileType switch
        {
            TileID.MushroomPlants => "Glowing Mushroom",
            TileID.MatureHerbs => ResolveHerbName(tile, "Mature"),
            TileID.BloomingHerbs => ResolveHerbName(tile, "Blooming"),
            TileID.DyePlants => ResolveDyePlantName(tile),
            TileID.AbigailsFlower => "Abigail's Flower",
            _ => "Plant"
        };
    }

    private static string ResolveDyePlantName(Tile tile)
    {
        int style = tile.TileFrameX / 34;
        return style switch
        {
            0 => "Teal Mushroom",
            1 => "Green Mushroom",
            2 => "Sky Blue Flower",
            3 => "Yellow Marigold",
            4 => "Blue Berries",
            5 => "Lime Kelp",
            6 => "Pink Prickly Pear",
            7 => "Orange Bloodroot",
            8 => "Strange Plant",
            9 => "Strange Plant",
            10 => "Strange Plant",
            11 => "Strange Plant",
            _ => "Dye Plant"
        };
    }

    private static string ResolveHerbName(Tile tile, string prefix)
    {
        int frameX = tile.TileFrameX / 18;
        string herbName = frameX switch
        {
            0 => "Daybloom",
            1 => "Moonglow",
            2 => "Blinkroot",
            3 => "Deathweed",
            4 => "Waterleaf",
            5 => "Fireblossom",
            6 => "Shiverthorn",
            _ => "Herb"
        };
        return $"{prefix} {herbName}";
    }

    private static void RefreshHostileMobEntries(Player player)
    {
        int preservedNpcIndex = -1;
        if (_selectedHostileMobIndex >= 0 && _selectedHostileMobIndex < NearbyHostileMobs.Count)
        {
            preservedNpcIndex = NearbyHostileMobs[_selectedHostileMobIndex].Index;
        }

        NearbyHostileMobs.Clear();
        if (player is null || !player.active)
        {
            _selectedHostileMobIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!IsEligibleHostileMob(npc, player))
            {
                continue;
            }

            if (!IsWorldPositionApproximatelyOnScreen(npc.Center))
            {
                continue;
            }

            float distanceTiles = Vector2.Distance(origin, npc.Center) / 16f;
            if (distanceTiles > ScanRangeTiles)
            {
                continue;
            }

            string displayName = ResolveHostileMobDisplayName(npc);
            NearbyHostileMobs.Add(GuidanceEntry.CreateHostileMob(i, displayName, npc.Center, distanceTiles));
        }

        NearbyHostileMobs.Sort(CompareGuidanceEntries);

        if (NearbyHostileMobs.Count == 0)
        {
            _selectedHostileMobIndex = -1;
            return;
        }

        if (preservedNpcIndex >= 0)
        {
            int restoredIndex = NearbyHostileMobs.FindIndex(entry => entry.Index == preservedNpcIndex);
            if (restoredIndex >= 0)
            {
                _selectedHostileMobIndex = restoredIndex;
                return;
            }
        }

        // Preserve "All" mode (-1) for categories that support it; only clamp out-of-bounds positive indices
        if (_selectedHostileMobIndex >= NearbyHostileMobs.Count)
        {
            _selectedHostileMobIndex = NearbyHostileMobs.Count - 1;
        }
    }

    private static bool IsEligibleHostileMob(NPC npc, Player player)
    {
        if (!npc.active || npc.lifeMax <= 5 || npc.damage <= 0)
        {
            return false;
        }

        if (!npc.CanBeChasedBy(player, ignoreDontTakeDamage: false))
        {
            return false;
        }

        if (npc.townNPC || npc.friendly)
        {
            return false;
        }

        return true;
    }

    private static string ResolveHostileMobDisplayName(NPC npc)
    {
        string name = Lang.GetNPCNameValue(npc.type);
        return !string.IsNullOrWhiteSpace(name) ? name : "Enemy";
    }

    private static void RefreshCustomEntries(Player player)
    {
        NearbyCustomMatches.Clear();
        CustomEntryScratch.Clear();

        if (player is null || !player.active || CustomTargets.Count == 0)
        {
            _selectedCustomIndex = -1;
            return;
        }

        Vector2 origin = player.Center;

        for (int filterIndex = 0; filterIndex < CustomTargets.Count; filterIndex++)
        {
            CustomGuidanceFilter filter = CustomTargets[filterIndex];
            switch (filter.Kind)
            {
                case CustomFilterKind.Tile:
                    AppendCustomTileMatches(filterIndex, filter, origin);
                    break;
                case CustomFilterKind.Npc:
                    foreach (GuidanceEntry entry in NearbyNpcs)
                    {
                        if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
                        {
                            continue;
                        }

                        NPC npc = Main.npc[entry.Index];
                        if (npc.active && npc.type == filter.TypeId)
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
                case CustomFilterKind.Player:
                    foreach (GuidanceEntry entry in NearbyPlayers)
                    {
                        if (entry.Index < 0 || entry.Index >= Main.maxPlayers)
                        {
                            continue;
                        }

                        Player candidate = Main.player[entry.Index];
                        if (candidate.active && string.Equals(SanitizeLabel(candidate.name), SanitizeLabel(filter.Label), StringComparison.OrdinalIgnoreCase))
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
                case CustomFilterKind.Projectile:
                    AppendCustomProjectileMatches(filterIndex, filter, origin);
                    break;
                case CustomFilterKind.DroppedItem:
                    foreach (GuidanceEntry entry in NearbyDroppedItems)
                    {
                        if (entry.Index < 0 || entry.Index >= Main.maxItems)
                        {
                            continue;
                        }

                        Item item = Main.item[entry.Index];
                        if (item.active && item.type == filter.TypeId)
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
                case CustomFilterKind.Critter:
                    foreach (GuidanceEntry entry in NearbyCritters)
                    {
                        if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
                        {
                            continue;
                        }

                        NPC npc = Main.npc[entry.Index];
                        if (npc.active && npc.type == filter.TypeId)
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
                case CustomFilterKind.Plantlife:
                    foreach (GuidanceEntry entry in NearbyPlantlife)
                    {
                        if (LabelsMatch(entry.DisplayName, filter.Label))
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
                case CustomFilterKind.HostileMob:
                    foreach (GuidanceEntry entry in NearbyHostileMobs)
                    {
                        if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
                        {
                            continue;
                        }

                        NPC npc = Main.npc[entry.Index];
                        if (npc.active && npc.type == filter.TypeId)
                        {
                            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
                        }
                    }
                    break;
            }
        }

        NearbyCustomMatches.Sort(static (left, right) =>
        {
            int compareFilter = left.FilterIndex.CompareTo(right.FilterIndex);
            if (compareFilter != 0)
            {
                return compareFilter;
            }

            return CompareGuidanceEntries(left.Entry, right.Entry);
        });

        if (_selectedCustomIndex >= CustomTargets.Count)
        {
            _selectedCustomIndex = CustomTargets.Count - 1;
        }
    }

    private static void AppendCustomTileMatches(int filterIndex, CustomGuidanceFilter filter, Vector2 origin)
    {
        int scanRadius = (int)Math.Clamp(ScanRangeTiles + 8f, 4f, 240f);
        int playerTileX = (int)(origin.X / 16f);
        int playerTileY = (int)(origin.Y / 16f);
        int minX = Math.Max(0, playerTileX - scanRadius);
        int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
        int minY = Math.Max(0, playerTileY - scanRadius);
        int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile && tile.WallType <= 0 && tile.LiquidAmount <= 0)
                {
                    continue;
                }

                if (!InGameNarrationSystem.CursorDescriptors.TryDescribe(x, y, out CursorDescriptorService.CursorDescriptor descriptor) ||
                    descriptor.IsAir ||
                    descriptor.TileType != filter.TypeId ||
                    !LabelsMatch(descriptor.Name, filter.Label))
                {
                    continue;
                }

                Vector2 worldPosition = ResolveCustomTileWorldPosition(x, y);
                float distanceTiles = Vector2.Distance(worldPosition, origin) / 16f;
                if (distanceTiles > ScanRangeTiles)
                {
                    continue;
                }

                int positionKey = HashCode.Combine(
                    filterIndex,
                    (int)MathF.Round(worldPosition.X),
                    (int)MathF.Round(worldPosition.Y),
                    descriptor.TileType);
                if (!CustomEntryScratch.Add(positionKey))
                {
                    continue;
                }

                Point anchor = new((int)MathF.Floor(worldPosition.X / 16f), (int)MathF.Floor(worldPosition.Y / 16f));
                GuidanceEntry entry = GuidanceEntry.CreateInteractable(anchor, filter.Label, worldPosition, distanceTiles);
                NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
            }
        }
    }

    private static void AppendCustomProjectileMatches(int filterIndex, CustomGuidanceFilter filter, Vector2 origin)
    {
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (!projectile.active || projectile.type != filter.TypeId || !IsWorldPositionApproximatelyOnScreen(projectile.Center))
            {
                continue;
            }

            float distanceTiles = Vector2.Distance(origin, projectile.Center) / 16f;
            if (distanceTiles > ScanRangeTiles)
            {
                continue;
            }

            GuidanceEntry entry = GuidanceEntry.CreateInteractable(Point.Zero, filter.Label, projectile.Center, distanceTiles);
            NearbyCustomMatches.Add(new CustomGuidanceMatch(filterIndex, entry));
        }
    }

    private static bool LabelsMatch(string left, string right)
    {
        return string.Equals(SanitizeLabel(left), SanitizeLabel(right), StringComparison.OrdinalIgnoreCase);
    }
}
