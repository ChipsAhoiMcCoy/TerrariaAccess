#nullable enable
using System;
using System.Collections.Generic;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TerrariaAccess.Common.Systems;

internal sealed class CursorDescriptorService
{
    private const int LiquidDescriptorBaseTileType = -1000;
    private const int WallDescriptorBaseTileType = -2000;

    private static readonly int[] StyledTileTypes =
    {
        TileID.Bottles,
        TileID.Tables,
        TileID.Chairs,
        TileID.WorkBenches,
        TileID.Candles,
        TileID.Chandeliers,
        TileID.HangingLanterns,
        TileID.Banners,
        TileID.Beds,
        TileID.Pianos,
        TileID.Benches,
        TileID.Bathtubs,
        TileID.Lampposts,
        TileID.Lamps,
        TileID.Kegs,
        TileID.ChineseLanterns,
        TileID.CookingPots,
        TileID.SkullLanterns,
        TileID.Candelabras,
        TileID.Bookcases,
        TileID.Thrones,
        TileID.Bowls,
        TileID.GrandfatherClocks,
        TileID.Statues,
        TileID.AlphabetStatues,
        TileID.MushroomStatue,
        TileID.BoulderStatue,
        TileID.Sinks,
        TileID.Painting2X3,
        TileID.Painting3X2,
        TileID.Painting3X3,
        TileID.Painting4X3,
        TileID.Painting6X4,
        TileID.Traps,
        TileID.PressurePlates,
        TileID.WeightedPressurePlate,
        TileID.Timers,
        TileID.LogicSensor,
        TileID.ClosedDoor,
        TileID.OpenDoor,
        TileID.TrapdoorClosed,
        TileID.TrapdoorOpen,
        TileID.TallGateClosed,
        TileID.TallGateOpen,
        TileID.MusicBoxes,
        TileID.Containers,
        TileID.Containers2,
        TileID.Dressers,
        TileID.AmmoBox,
        TileID.BewitchingTable,
        TileID.AlchemyTable,
        TileID.Sundial,
        TileID.SharpeningStation,
        TileID.Fireplace,
        TileID.Tables2,
        TileID.PicnicTable,
        TileID.Toilets,
        TileID.FoodPlatter,
        TileID.TeaKettle,
    };

    /// <summary>
    /// Tile types that use frameX / 18 to determine their style variant.
    /// </summary>
    private static readonly HashSet<int> FrameXBasedStyleTileTypes = new()
    {
        TileID.Timers,
    };

    /// <summary>
    /// Tile types that use frameY / 18 to determine their style variant.
    /// </summary>
    private static readonly HashSet<int> FrameYBasedStyleTileTypes = new()
    {
        TileID.Traps,
        TileID.PressurePlates,
        TileID.WeightedPressurePlate,
        TileID.LogicSensor,
    };

    private static readonly Dictionary<int, Dictionary<int, int>> TileStyleToItemType = BuildTileStyleMap();
    private static readonly Dictionary<int, int> WallTypeToItemType = BuildWallItemMap();
    private static readonly Lazy<HashSet<string>> HousingQueryPhrases = new(BuildHousingQueryPhraseSet);

    internal readonly record struct CursorDescriptor(
        int TileType,
        string Name,
        ScreenReaderService.AnnouncementCategory Category,
        bool IsWall,
        bool IsAir);

    public ScreenReaderService.AnnouncementCategory GetAnnouncementCategory(int tileType)
    {
        if (tileType <= WallDescriptorBaseTileType)
        {
            return ScreenReaderService.AnnouncementCategory.Wall;
        }

        return ScreenReaderService.AnnouncementCategory.Tile;
    }

    public bool TryDescribe(int tileX, int tileY, out CursorDescriptor descriptor)
    {
        descriptor = default;
        if (!WorldGen.InWorld(tileX, tileY))
        {
            return false;
        }

        Tile tile = Main.tile[tileX, tileY];
        int tileType;
        string? name;

        if (!tile.HasTile)
        {
            if (tile.LiquidAmount > 0 && TryDescribeLiquid(tile, out tileType, out name))
            {
                descriptor = BuildDescriptor(tileType, name);
                return true;
            }

            if (TryDescribeWall(tile, out tileType, out name))
            {
                descriptor = BuildDescriptor(tileType, name);
                return true;
            }

            descriptor = BuildDescriptor(-1, "Empty");
            return true;
        }

        tileType = tile.TileType;

        if (tileType == TileID.Banners && TryDescribeBanner(tile, out name))
        {
            name = AppendTileStateLabels(tile, name);
            descriptor = BuildDescriptor(tileType, name);
            return true;
        }

        // Skip TryDescribeTileFromItemPlacement for chests - item names like "Chest" are too generic
        // Instead, let chests fall through to the MapHelper lookup which gives proper names like "Wooden Chest"
        if (tileType != TileID.Banners && !IsChestTile(tileType) && TryDescribeTileFromItemPlacement(tile, tileType, out name))
        {
            name = AppendTileStateLabels(tile, name);
            descriptor = BuildDescriptor(tileType, name);
            return true;
        }

        // Handle chest tiles using map name lookup for proper type names
        if (IsChestTile(tileType))
        {
            name = ResolveChestTypeName(tile, tileType);
            if (IsChestLocked(tileX, tileY) && (name == null || !name.Contains("Locked", StringComparison.OrdinalIgnoreCase)))
            {
                name = $"Locked {name ?? "Chest"}";
            }
            OverrideChestName(tileX, tileY, tileType, ref name);
            name = AppendTileStateLabels(tile, name);
            descriptor = BuildDescriptor(tileType, name);
            return true;
        }

        // Try to get biome-specific tree names
        if (IsTreeTile(tileType))
        {
            string? treeName = TryGetTreeName(tileX, tileY, tile);
            if (!string.IsNullOrWhiteSpace(treeName))
            {
                name = AppendTileStateLabels(tile, treeName);
                descriptor = BuildDescriptor(tileType, name);
                return true;
            }
        }

        try
        {
            int baseOption = GetMapLookupOption(tileType, tile);
            int lookup = MapHelper.TileToLookup(tileType, baseOption);
            name = Lang.GetMapObjectName(lookup);

            // Append growth stage for herbs
            string? growthStage = GetHerbGrowthStageLabel(tileType, baseOption);
            if (!string.IsNullOrWhiteSpace(name) && growthStage != null)
            {
                name = $"{name}, {growthStage}";
            }
        }
        catch
        {
            name = null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = TileID.Search.GetName(tileType);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"tile {tileType}";
        }

        OverrideChestName(tileX, tileY, tileType, ref name);
        name = AppendTileStateLabels(tile, name);

        descriptor = BuildDescriptor(tileType, name);
        return true;
    }

    /// <summary>
    /// Appends shape, actuator, and interactive state labels to a tile name.
    /// </summary>
    private static string? AppendTileStateLabels(Tile tile, string? name)
    {
        string? shapeDescriptor = GetTileShapeDescriptor(tile);
        if (!string.IsNullOrEmpty(shapeDescriptor))
        {
            name = $"{name}, {shapeDescriptor}";
        }

        // Add lever/switch on/off state
        string? toggleStateLabel = GetToggleStateLabel(tile);
        if (!string.IsNullOrEmpty(toggleStateLabel))
        {
            name = $"{name}, {toggleStateLabel}";
        }

        string? doorStateLabel = GetDoorStateLabel(tile);
        if (!string.IsNullOrEmpty(doorStateLabel))
        {
            name = $"{name}, {doorStateLabel}";
        }

        // Add junction box mode
        string? junctionBoxLabel = GetJunctionBoxModeLabel(tile);
        if (!string.IsNullOrEmpty(junctionBoxLabel))
        {
            name = $"{name}, {junctionBoxLabel}";
        }

        if (tile.HasActuator)
        {
            string actuatorLabel = GetLocalizedWithFallback("Mods.TerrariaAccess.TileStates.HasActuator", "has actuator");
            name = $"{name}, {actuatorLabel}";
        }

        return name;
    }

    /// <summary>
    /// Gets the on/off state label for toggleable tiles like levers, switches, and timers.
    /// </summary>
    private static string? GetToggleStateLabel(Tile tile)
    {
        if (!tile.HasTile)
        {
            return null;
        }

        int tileType = tile.TileType;

        // Lever (TileID 132): 2x2 tile, frameX toggles by ±36
        // frameX 0-35 = OFF state, frameX 36-71 = ON state
        if (tileType == TileID.Lever)
        {
            bool isOn = tile.TileFrameX >= 36;
            return GetLocalizedWithFallback(
                isOn ? "Mods.TerrariaAccess.TileStates.LeverOn" : "Mods.TerrariaAccess.TileStates.LeverOff",
                isOn ? "on" : "off");
        }

        // Timer (TileID 144): 1x1 tile, frameY toggles between 0 (OFF) and 18 (ON/ticking)
        if (tileType == TileID.Timers)
        {
            bool isOn = tile.TileFrameY != 0;
            return GetLocalizedWithFallback(
                isOn ? "Mods.TerrariaAccess.TileStates.TimerOn" : "Mods.TerrariaAccess.TileStates.TimerOff",
                isOn ? "on" : "off");
        }

        // Logic Sensor (TileID 423): 1x1 tile, frameX toggles between 0 (OFF) and 18 (ON/activated)
        if (tileType == TileID.LogicSensor)
        {
            bool isOn = tile.TileFrameX >= 18;
            return GetLocalizedWithFallback(
                isOn ? "Mods.TerrariaAccess.TileStates.SensorOn" : "Mods.TerrariaAccess.TileStates.SensorOff",
                isOn ? "active" : "inactive");
        }

        return null;
    }

    /// <summary>
    /// Gets the open/closed state label for doors, trapdoors, and tall gates.
    /// </summary>
    private static string? GetDoorStateLabel(Tile tile)
    {
        if (!tile.HasTile || !IsDoorTile(tile.TileType))
        {
            return null;
        }

        bool isOpen = IsDoorOpen(tile.TileType);
        return GetLocalizedWithFallback(
            isOpen ? "Mods.TerrariaAccess.TileStates.DoorOpen" : "Mods.TerrariaAccess.TileStates.DoorClosed",
            isOpen ? "open" : "closed");
    }

    /// <summary>
    /// Checks if a tile type is any kind of door (regular, trapdoor, or tall gate).
    /// </summary>
    internal static bool IsDoorTile(int tileType)
    {
        return tileType == TileID.ClosedDoor ||
               tileType == TileID.OpenDoor ||
               tileType == TileID.TrapdoorClosed ||
               tileType == TileID.TrapdoorOpen ||
               tileType == TileID.TallGateClosed ||
               tileType == TileID.TallGateOpen;
    }

    /// <summary>
    /// Checks if a door tile type represents an open door.
    /// </summary>
    internal static bool IsDoorOpen(int tileType)
    {
        return tileType == TileID.OpenDoor ||
               tileType == TileID.TrapdoorOpen ||
               tileType == TileID.TallGateOpen;
    }

    /// <summary>
    /// Gets the mode label for Junction Box tiles.
    /// Junction boxes have 3 modes that control how wire signals are routed.
    /// </summary>
    private static string? GetJunctionBoxModeLabel(Tile tile)
    {
        if (!tile.HasTile || tile.TileType != TileID.WirePipe)
        {
            return null;
        }

        int mode = tile.TileFrameX / 18;
        return mode switch
        {
            0 => GetLocalizedWithFallback(
                "Mods.TerrariaAccess.TileStates.JunctionBoxStraight",
                "straight mode, wires pass through in the same direction"),
            1 => GetLocalizedWithFallback(
                "Mods.TerrariaAccess.TileStates.JunctionBoxCrossA",
                "cross mode A, down connects to left, up connects to right"),
            2 => GetLocalizedWithFallback(
                "Mods.TerrariaAccess.TileStates.JunctionBoxCrossB",
                "cross mode B, down connects to right, up connects to left"),
            _ => null,
        };
    }

    internal static int ResolveAnnouncementKey(int tileType)
    {
        if (tileType >= 0 && tileType < TileID.Sets.Conversion.Grass.Length &&
            (TileID.Sets.Conversion.Grass[tileType] || tileType == TileID.Dirt))
        {
            return TileID.Dirt;
        }

        return tileType;
    }

    /// <summary>
    /// Resolves announcement key with frame state for toggleable tiles.
    /// This ensures state changes (on/off) produce different keys.
    /// </summary>
    internal static int ResolveAnnouncementKey(int tileType, Tile tile)
    {
        int baseKey = ResolveAnnouncementKey(tileType);

        // For toggleable tiles, include frame state to distinguish on/off
        if (tileType == TileID.Lever)
        {
            bool isOn = tile.TileFrameX >= 36;
            return isOn ? (baseKey | 0x10000) : baseKey;
        }

        if (tileType == TileID.Timers)
        {
            bool isOn = tile.TileFrameY != 0;
            return isOn ? (baseKey | 0x10000) : baseKey;
        }

        // Logic Sensor: include on/off state so state changes trigger re-announcement
        if (tileType == TileID.LogicSensor)
        {
            bool isOn = tile.TileFrameX >= 18;
            return isOn ? (baseKey | 0x10000) : baseKey;
        }

        if (IsDoorTile(tileType))
        {
            bool isOpen = IsDoorOpen(tileType);
            return isOpen ? (baseKey | 0x10000) : baseKey;
        }

        // Junction Box: include mode in key so mode changes trigger re-announcement
        if (tileType == TileID.WirePipe)
        {
            int mode = tile.TileFrameX / 18;
            return baseKey | (mode << 16);
        }

        return baseKey;
    }

    internal static int ResolveAnnouncementKey(int tileType, Tile tile, int tileX, int tileY)
    {
        int baseKey = ResolveAnnouncementKey(tileType, tile);
        if (!tile.HasTile || !IsTreeTile(tileType))
        {
            return baseKey;
        }

        (int anchorX, int anchorY) = ResolveTreeInstanceAnchor(tileX, tileY, tileType);
        unchecked
        {
            return (baseKey * 397) ^ (anchorX * 31) ^ anchorY;
        }
    }

    internal static bool ShouldSuppressVariantNames(int announcementKey)
    {
        return announcementKey == TileID.Dirt;
    }

    internal static bool IsTreeTileType(int tileType)
    {
        return IsTreeTile(tileType);
    }

    private CursorDescriptor BuildDescriptor(int tileType, string? name)
    {
        string safeName = string.IsNullOrWhiteSpace(name) ? $"tile {tileType}" : name;
        ScreenReaderService.AnnouncementCategory category = GetAnnouncementCategory(tileType);
        bool isWall = category == ScreenReaderService.AnnouncementCategory.Wall;
        bool isAir = tileType == -1 || string.Equals(safeName, "Empty", StringComparison.OrdinalIgnoreCase);
        return new CursorDescriptor(tileType, safeName, category, isWall, isAir);
    }

    private static void OverrideChestName(int tileX, int tileY, int tileType, ref string? name)
    {
        if (!IsChestTile(tileType))
        {
            return;
        }

        int chestIndex = FindChestAtTile(tileX, tileY, tileType);
        if (chestIndex < 0 || chestIndex >= Main.chest.Length)
        {
            return;
        }

        Chest? chest = Main.chest[chestIndex];
        if (chest is null)
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(chest.name);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Chest";
        }

        name = $"\"{sanitized}\" {name}";
    }

    private static int FindChestAtTile(int tileX, int tileY, int tileType)
    {
        // First try the standard guessing method
        int chestIndex = Chest.FindChestByGuessing(tileX, tileY);
        if (chestIndex >= 0)
        {
            return chestIndex;
        }

        // Fallback: Find origin tile using TileObjectData and search directly
        if (!WorldGen.InWorld(tileX, tileY))
        {
            return -1;
        }

        Tile tile = Main.tile[tileX, tileY];
        if (!tile.HasTile)
        {
            return -1;
        }

        TileObjectData? tileData = TileObjectData.GetTileData(tileType, 0);
        if (tileData is null)
        {
            return -1;
        }

        // Calculate origin from frame coordinates
        int frameX = tile.TileFrameX;
        int frameY = tile.TileFrameY;
        int tileWidth = tileData.CoordinateWidth + tileData.CoordinatePadding;
        int tileHeight = tileData.CoordinateHeights[0] + tileData.CoordinatePadding;

        // Determine which sub-tile we're on
        int subX = (tileWidth > 0) ? (frameX % (tileData.Width * tileWidth)) / tileWidth : 0;
        int subY = (tileHeight > 0) ? (frameY % (tileData.Height * tileHeight)) / tileHeight : 0;

        int originX = tileX - subX;
        int originY = tileY - subY;

        // Try finding chest at calculated origin
        chestIndex = Chest.FindChest(originX, originY);
        if (chestIndex >= 0)
        {
            return chestIndex;
        }

        // Last resort: scan nearby for matching chest
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                chestIndex = Chest.FindChest(tileX + dx, tileY + dy);
                if (chestIndex >= 0)
                {
                    return chestIndex;
                }
            }
        }

        return -1;
    }

    private static bool IsChestTile(int tileType)
    {
        return tileType == TileID.Containers ||
               tileType == TileID.Containers2 ||
               tileType == TileID.Dressers;
    }

    /// <summary>
    /// Checks if the chest at the given tile coordinates is locked.
    /// Wraps Terraria's Chest.IsLocked with safety checks.
    /// </summary>
    private static bool IsChestLocked(int tileX, int tileY)
    {
        if (!WorldGen.InWorld(tileX, tileY))
        {
            return false;
        }

        try
        {
            return Chest.IsLocked(tileX, tileY);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the proper chest type name (e.g., "Wooden Chest", "Frozen Chest") using item lookup then map lookup.
    /// For locked chest styles, falls back to the unlocked counterpart's name since locked variants
    /// typically have no corresponding item and may return a generic map name.
    /// </summary>
    private static string ResolveChestTypeName(Tile tile, int tileType)
    {
        // Calculate chest style from frame - chests are 2 tiles wide (36 pixels), dressers are 3 wide (54 pixels)
        int frameWidth = tileType == TileID.Dressers ? 54 : 36;
        int style = tile.TileFrameX / frameWidth;

        // For locked chest styles, resolve via the unlocked counterpart first.
        // Locked styles have no corresponding item and MapHelper.TileToLookup returns
        // incorrect map object names (e.g., "Thorns", "Candle") for some styles.
        string genericFallback = tileType == TileID.Dressers ? "Dresser" : "Chest";
        if (TryGetUnlockedChestStyle(tileType, style, out int unlockedStyle))
        {
            string? unlockedName = TryResolveChestNameForStyle(tileType, unlockedStyle);
            if (!string.IsNullOrWhiteSpace(unlockedName))
            {
                return unlockedName;
            }
        }

        string? name = TryResolveChestNameForStyle(tileType, style);
        return !string.IsNullOrWhiteSpace(name) ? name : genericFallback;
    }

    /// <summary>
    /// Tries to resolve a chest name for a specific tile type and style via item lookup then map lookup.
    /// </summary>
    private static string? TryResolveChestNameForStyle(int tileType, int style)
    {
        if (TryResolveStyleItemType(tileType, style, out int itemType) && itemType > ItemID.None)
        {
            string itemName = Lang.GetItemNameValue(itemType);
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                return itemName;
            }
        }

        try
        {
            int lookup = MapHelper.TileToLookup(tileType, style);
            string? mapName = Lang.GetMapObjectName(lookup);
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                return mapName;
            }
        }
        catch
        {
            // Fall through
        }

        return null;
    }

    /// <summary>
    /// Maps locked chest tile styles to their unlocked counterparts.
    /// Based on Terraria's Chest.Unlock frame offset logic.
    /// </summary>
    private static bool TryGetUnlockedChestStyle(int tileType, int lockedStyle, out int unlockedStyle)
    {
        unlockedStyle = -1;

        if (tileType == TileID.Containers)
        {
            switch (lockedStyle)
            {
                case 2:  // Locked Gold Chest → Gold Chest
                case 4:  // Locked Shadow Chest → Shadow Chest
                case 36: // Locked Dungeon Chest variant
                case 38: // Locked Dungeon Chest variant
                case 40: // Locked Dungeon Chest variant
                    unlockedStyle = lockedStyle - 1;
                    return true;
                case 23: // Locked Jungle Chest
                case 24: // Locked Corruption Chest
                case 25: // Locked Crimson Chest
                case 26: // Locked Hallowed Chest
                case 27: // Locked Frozen Chest
                    unlockedStyle = lockedStyle - 5;
                    return true;
            }
        }
        else if (tileType == TileID.Containers2)
        {
            if (lockedStyle == 13) // Locked Desert Chest
            {
                unlockedStyle = lockedStyle - 1;
                return true;
            }
        }

        return false;
    }

    private static bool TryDescribeBanner(Tile tile, out string? name)
    {
        name = null;

        int style = TileObjectData.GetTileStyle(tile);
        if (style < 0)
        {
            return false;
        }

        int itemType = ResolveBannerItemType(style);
        if (itemType > ItemID.None)
        {
            string itemName = Lang.GetItemNameValue(itemType);
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                name = itemName;
                return true;
            }
        }

        int npcType = Item.BannerToNPC(style);
        if (npcType > NPCID.None)
        {
            string npcName = Lang.GetNPCNameValue(npcType);
            if (!string.IsNullOrWhiteSpace(npcName))
            {
                name = $"{npcName} banner";
                return true;
            }
        }

        return false;
    }

    private static bool TryDescribeTileFromItemPlacement(Tile tile, int tileType, out string? name)
    {
        name = null;

        if (!TryResolveTileStyle(tile, tileType, out int style))
        {
            return false;
        }

        if (TryLookupItemNameForStyle(tileType, style, out name))
        {
            return true;
        }

        return false;
    }

    internal static bool TryResolveTileStyle(Tile tile, int tileType, out int style)
    {
        style = -1;

        // Use frame-based style for tile types that use frame position for variant determination.
        if (FrameXBasedStyleTileTypes.Contains(tileType))
        {
            style = tile.TileFrameX / 18;
        }
        else if (FrameYBasedStyleTileTypes.Contains(tileType))
        {
            style = tile.TileFrameY / 18;
        }
        else if (IsChestTile(tileType))
        {
            // Chests are 2 tiles wide (36 pixels per style in frameX).
            // Dressers are 3 tiles wide (54 pixels per style in frameX).
            int frameWidth = tileType == TileID.Dressers ? 54 : 36;
            style = tile.TileFrameX / frameWidth;
            if (TryGetUnlockedChestStyle(tileType, style, out int unlockedStyle))
            {
                style = unlockedStyle;
            }
        }
        else if (tileType == TileID.ClosedDoor)
        {
            // Closed doors store styles in vertical 3-tile groups, wrapping to the
            // next frameX page every 36 styles. Any door segment can be inspected.
            style = (tile.TileFrameX / 54 * 36) + (tile.TileFrameY / 54);
            return true;
        }
        else if (tileType == TileID.OpenDoor)
        {
            // WorldGen.OpenDoor preserves the closed-door style as:
            //   frameY = style % 36 * 54 + segmentRow * 18
            //   frameX = direction/page data, with one style page every 72 px.
            style = (tile.TileFrameX / 72 * 36) + (tile.TileFrameY / 54);
            return true;
        }
        else
        {
            style = TileObjectData.GetTileStyle(tile);
            if (style < 0)
            {
                return false;
            }
        }

        // TileObjectData.GetTileStyle returns the frame-derived style, which for
        // alternate placements (e.g. a right-facing statue) equals placeStyle plus
        // the alternate's Style offset. Normalize it back to the base item style
        // when the adjusted style maps to a placed item.
        TileObjectData? baseData;
        try
        {
            baseData = TileObjectData.GetTileData(tileType, 0, 0);
        }
        catch
        {
            baseData = null;
        }

        if (baseData == null)
        {
            return true;
        }

        for (int alternateIndex = 1; alternateIndex <= MaxAlternatePlacementsToProbe; alternateIndex++)
        {
            TileObjectData? altData;
            try
            {
                altData = TileObjectData.GetTileData(tileType, 0, alternateIndex);
            }
            catch
            {
                break;
            }

            if (altData == null || ReferenceEquals(altData, baseData))
            {
                break;
            }

            int altOffset = altData.Style;
            if (altOffset <= 0)
            {
                continue;
            }

            int adjustedStyle = style - altOffset;
            if (adjustedStyle < 0)
            {
                continue;
            }

            if (TryLookupItemNameForStyle(tileType, adjustedStyle, out _))
            {
                style = adjustedStyle;
                return true;
            }
        }

        return true;
    }

    private const int MaxAlternatePlacementsToProbe = 8;

    private static bool TryLookupItemNameForStyle(int tileType, int style, out string? name)
    {
        name = null;
        if (!TryResolveStyleItemType(tileType, style, out int itemType) || itemType <= ItemID.None)
        {
            return false;
        }

        string itemName = Lang.GetItemNameValue(itemType);
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        name = itemName;
        return true;
    }

    private static bool TryResolveStyleItemType(int tileType, int style, out int itemType)
    {
        itemType = ItemID.None;

        // Map open door types to their closed equivalents for item lookup
        // (door items create closed door tiles, so open doors aren't in the mapping)
        int lookupTileType = tileType switch
        {
            TileID.OpenDoor => TileID.ClosedDoor,
            TileID.TrapdoorOpen => TileID.TrapdoorClosed,
            TileID.TallGateOpen => TileID.TallGateClosed,
            _ => tileType
        };

        if (!TileStyleToItemType.TryGetValue(lookupTileType, out Dictionary<int, int>? map))
        {
            return false;
        }

        return map.TryGetValue(Math.Max(0, style), out itemType);
    }

    private static int ResolveBannerItemType(int style)
    {
        if (TryResolveStyleItemType(TileID.Banners, style, out int itemType))
        {
            return itemType;
        }

        int fallback = Item.BannerToItem(style);
        if (fallback > ItemID.None)
        {
            if (!TileStyleToItemType.TryGetValue(TileID.Banners, out Dictionary<int, int>? map))
            {
                map = new Dictionary<int, int>();
                TileStyleToItemType[TileID.Banners] = map;
            }

            map[Math.Max(0, style)] = fallback;
        }

        return fallback;
    }

    private static Dictionary<int, Dictionary<int, int>> BuildTileStyleMap()
    {
        Dictionary<int, Dictionary<int, int>> map = new();
        HashSet<int> explicitTargets = new(StyledTileTypes);
        Item scratch = new();
        for (int type = 1; type < ItemLoader.ItemCount; type++)
        {
            try
            {
                scratch.SetDefaults(type, true);
            }
            catch
            {
                continue;
            }

            int tileType = scratch.createTile;
            if (tileType < 0)
            {
                continue;
            }

            bool shouldTrack = explicitTargets.Contains(tileType);
            if (!shouldTrack && tileType < TileID.Sets.Platforms.Length && TileID.Sets.Platforms[tileType])
            {
                shouldTrack = true;
            }

            if (!shouldTrack)
            {
                continue;
            }

            if (!map.TryGetValue(tileType, out Dictionary<int, int>? styleMap))
            {
                styleMap = new Dictionary<int, int>();
                map[tileType] = styleMap;
            }

            int style = Math.Max(0, scratch.placeStyle);
            styleMap[style] = type;
        }

        return map;
    }

    private static Dictionary<int, int> BuildWallItemMap()
    {
        Dictionary<int, int> map = new();
        Item scratch = new();
        for (int type = 1; type < ItemLoader.ItemCount; type++)
        {
            try
            {
                scratch.SetDefaults(type, true);
            }
            catch
            {
                continue;
            }

            int wallType = scratch.createWall;
            if (wallType <= WallID.None)
            {
                continue;
            }

            if (!map.ContainsKey(wallType))
            {
                map[wallType] = type;
            }
        }

        return map;
    }

    private static bool TryDescribeLiquid(Tile tile, out int tileType, out string? name)
    {
        tileType = -1;
        name = null;

        int liquidType = tile.LiquidType;
        string? key = liquidType switch
        {
            LiquidID.Water => "Mods.TerrariaAccess.CursorLiquids.Water",
            LiquidID.Lava => "Mods.TerrariaAccess.CursorLiquids.Lava",
            LiquidID.Honey => "Mods.TerrariaAccess.CursorLiquids.Honey",
            LiquidID.Shimmer => "Mods.TerrariaAccess.CursorLiquids.Shimmer",
            _ => null,
        };

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        string fallback = liquidType switch
        {
            LiquidID.Water => "Water",
            LiquidID.Lava => "Lava",
            LiquidID.Honey => "Honey",
            LiquidID.Shimmer => "Shimmer",
            _ => string.Empty,
        };

        string localizedName = Language.GetTextValue(key);
        if (string.IsNullOrWhiteSpace(localizedName) || string.Equals(localizedName, key, StringComparison.Ordinal))
        {
            localizedName = fallback;
        }

        if (string.IsNullOrWhiteSpace(localizedName))
        {
            return false;
        }

        tileType = LiquidDescriptorBaseTileType - liquidType;
        name = localizedName;
        return true;
    }

    private static bool TryDescribeWall(Tile tile, out int tileType, out string? name)
    {
        tileType = -1;
        name = null;

        int wallType = tile.WallType;
        if (wallType <= WallID.None)
        {
            return false;
        }

        if (WallTypeToItemType.TryGetValue(wallType, out int itemType) && itemType > ItemID.None)
        {
            string itemName = Lang.GetItemNameValue(itemType);
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                name = itemName;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = WallID.Search.GetName(wallType);
            name = TextSanitizer.Clean(name);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            name = StripUnsafeDescriptor(name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Wall {wallType}";
        }

        tileType = WallDescriptorBaseTileType - wallType;
        return true;
    }

    private static string StripUnsafeDescriptor(string name)
    {
        string cleaned = name.Trim();

        const string prefix = "Unsafe ";
        if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[prefix.Length..].TrimStart();
        }

        const string suffix = " unsafe";
        if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^suffix.Length].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(cleaned) ? name : cleaned;
    }

    private static string? GetTileShapeDescriptor(Tile tile)
    {
        if (!tile.HasTile)
        {
            return null;
        }

        if (tile.IsHalfBlock)
        {
            return GetLocalizedWithFallback("Mods.TerrariaAccess.TileShapes.HalfBlock", "half block");
        }

        return tile.Slope switch
        {
            SlopeType.SlopeDownLeft => GetLocalizedWithFallback("Mods.TerrariaAccess.TileShapes.SlopeDownLeft", "sloped down-left"),
            SlopeType.SlopeDownRight => GetLocalizedWithFallback("Mods.TerrariaAccess.TileShapes.SlopeDownRight", "sloped down-right"),
            SlopeType.SlopeUpLeft => GetLocalizedWithFallback("Mods.TerrariaAccess.TileShapes.SlopeUpLeft", "sloped up-left"),
            SlopeType.SlopeUpRight => GetLocalizedWithFallback("Mods.TerrariaAccess.TileShapes.SlopeUpRight", "sloped up-right"),
            _ => null,
        };
    }

    private static string GetLocalizedWithFallback(string key, string fallback)
    {
        string value = Language.GetTextValue(key);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// Gets the map legend option index for tiles that use frame-based variants.
    /// This allows herbs, dye plants, and exposed gems to display their specific names.
    /// </summary>
    private static int GetMapLookupOption(int tileType, Tile tile)
    {
        return tileType switch
        {
            TileID.ImmatureHerbs or TileID.MatureHerbs or TileID.BloomingHerbs
                => Math.Clamp(tile.TileFrameX / 18, 0, 6),
            TileID.DyePlants
                => tile.TileFrameX / 34,
            TileID.ExposedGems
                => Math.Clamp(tile.TileFrameX / 18, 0, 6),
            _ => 0
        };
    }

    /// <summary>
    /// Gets the growth stage label for herb tiles.
    /// </summary>
    private static string? GetHerbGrowthStageLabel(int tileType, int herbStyle)
    {
        if (tileType == TileID.MatureHerbs && WorldGen.IsHarvestableHerbWithSeed(tileType, herbStyle))
        {
            return GetLocalizedWithFallback("Mods.TerrariaAccess.HerbStages.Blooming", "blooming");
        }

        return tileType switch
        {
            TileID.ImmatureHerbs => GetLocalizedWithFallback("Mods.TerrariaAccess.HerbStages.Immature", "immature"),
            TileID.MatureHerbs => GetLocalizedWithFallback("Mods.TerrariaAccess.HerbStages.Mature", "mature"),
            TileID.BloomingHerbs => GetLocalizedWithFallback("Mods.TerrariaAccess.HerbStages.Blooming", "blooming"),
            _ => null
        };
    }

    /// <summary>
    /// Checks if the given tile type is any type of tree tile.
    /// </summary>
    private static bool IsTreeTile(int tileType)
    {
        return tileType == TileID.Trees ||
               tileType == TileID.MushroomTrees ||
               tileType == TileID.PalmTree ||
               tileType == TileID.PineTree ||
               tileType == TileID.VanityTreeSakura ||
               tileType == TileID.VanityTreeYellowWillow ||
               tileType == TileID.TreeAsh ||
               tileType == TileID.TreeTopaz ||
               tileType == TileID.TreeAmethyst ||
               tileType == TileID.TreeSapphire ||
               tileType == TileID.TreeEmerald ||
               tileType == TileID.TreeRuby ||
               tileType == TileID.TreeDiamond ||
               tileType == TileID.TreeAmber;
    }

    private static (int X, int Y) ResolveTreeInstanceAnchor(int tileX, int tileY, int treeTileType)
    {
        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return (tileX, tileY);
        }

        int x = tileX;
        int y = tileY;

        if (treeTileType == TileID.PalmTree)
        {
            while (y < Main.maxTilesY - 50)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (!t.HasTile || !IsTreeTile(t.TileType))
                {
                    break;
                }

                y++;
            }

            return (x, y);
        }

        Tile startTile = Framing.GetTileSafely(x, y);
        int frameCol = startTile.TileFrameX / 22;
        int frameRow = startTile.TileFrameY / 22;

        if (frameCol == 3 && frameRow <= 2)
            x++;
        else if (frameCol == 4 && frameRow >= 3 && frameRow <= 5)
            x--;
        else if (frameCol == 1 && frameRow >= 6 && frameRow <= 8)
            x--;
        else if (frameCol == 2 && frameRow >= 6 && frameRow <= 8)
            x++;
        else if (frameCol == 2 && frameRow >= 9)
            x++;
        else if (frameCol == 3 && frameRow >= 9)
            x--;

        while (y < Main.maxTilesY - 50)
        {
            Tile t = Framing.GetTileSafely(x, y);
            if (!t.HasTile || (!IsTreeTile(t.TileType) && !TileID.Sets.IsATreeTrunk[t.TileType]))
            {
                break;
            }

            y++;
        }

        return (x, y);
    }

    /// <summary>
    /// Gets the biome-specific tree name for a tree tile at the given coordinates.
    /// </summary>
    private static string? TryGetTreeName(int tileX, int tileY, Tile tile)
    {
        int tileType = tile.TileType;

        // Handle special tree types that don't depend on ground tile
        switch (tileType)
        {
            case TileID.MushroomTrees:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Mushroom", "Mushroom Tree");
            case TileID.PineTree:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Pine", "Pine Tree");
            case TileID.VanityTreeSakura:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Sakura", "Sakura Tree");
            case TileID.VanityTreeYellowWillow:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.YellowWillow", "Yellow Willow Tree");
            case TileID.TreeAsh:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Ash", "Ash Tree");
            case TileID.TreeTopaz:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Topaz", "Topaz Gem Tree");
            case TileID.TreeAmethyst:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Amethyst", "Amethyst Gem Tree");
            case TileID.TreeSapphire:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Sapphire", "Sapphire Gem Tree");
            case TileID.TreeEmerald:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Emerald", "Emerald Gem Tree");
            case TileID.TreeRuby:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Ruby", "Ruby Gem Tree");
            case TileID.TreeDiamond:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Diamond", "Diamond Gem Tree");
            case TileID.TreeAmber:
                return GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Amber", "Amber Gem Tree");
        }

        // For standard trees and palm trees, determine type from ground tile
        if (tileType == TileID.Trees || tileType == TileID.PalmTree)
        {
            TreeTypes treeType = GetTreeTypeFromPosition(tileX, tileY, tileType);
            return GetTreeNameFromType(treeType);
        }

        return null;
    }

    /// <summary>
    /// Finds the ground tile below a tree and determines the tree type.
    /// Mimics WorldGen.GetTreeBottom and WorldGen.GetTreeType.
    /// </summary>
    private static TreeTypes GetTreeTypeFromPosition(int tileX, int tileY, int treeTileType)
    {
        int x = tileX;
        int y = tileY;

        // For palm trees, just go straight down
        if (treeTileType == TileID.PalmTree)
        {
            while (y < Main.maxTilesY - 50)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (t.HasTile && t.TileType != TileID.PalmTree)
                {
                    break;
                }
                y++;
            }
        }
        else
        {
            // For standard trees, adjust x position based on frame to find trunk center
            Tile startTile = Framing.GetTileSafely(x, y);
            int frameCol = startTile.TileFrameX / 22;
            int frameRow = startTile.TileFrameY / 22;

            if (frameCol == 3 && frameRow <= 2)
                x++;
            else if (frameCol == 4 && frameRow >= 3 && frameRow <= 5)
                x--;
            else if (frameCol == 1 && frameRow >= 6 && frameRow <= 8)
                x--;
            else if (frameCol == 2 && frameRow >= 6 && frameRow <= 8)
                x++;
            else if (frameCol == 2 && frameRow >= 9)
                x++;
            else if (frameCol == 3 && frameRow >= 9)
                x--;

            // Go down until we find a non-tree tile
            while (y < Main.maxTilesY - 50)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (!t.HasTile)
                {
                    y++;
                    continue;
                }

                // Check if it's still a tree trunk tile
                if (TileID.Sets.IsATreeTrunk[t.TileType] || t.TileType == TileID.MushroomTrees)
                {
                    y++;
                    continue;
                }

                break;
            }
        }

        // Now we're at the ground tile - determine tree type
        Tile groundTile = Framing.GetTileSafely(x, y);
        if (!groundTile.HasTile)
        {
            return TreeTypes.None;
        }

        return GetTreeTypeFromGroundTile(groundTile.TileType);
    }

    /// <summary>
    /// Determines the tree type based on the ground tile type.
    /// Mirrors the logic from WorldGen.GetTreeType.
    /// </summary>
    private static TreeTypes GetTreeTypeFromGroundTile(int groundTileType)
    {
        return groundTileType switch
        {
            TileID.Grass or TileID.GolfGrass => TreeTypes.Forest,
            TileID.CorruptGrass => TreeTypes.Corrupt,
            TileID.MushroomGrass => TreeTypes.Mushroom,
            TileID.CrimsonGrass => TreeTypes.Crimson,
            TileID.JungleGrass => TreeTypes.Jungle,
            TileID.SnowBlock => TreeTypes.Snow,
            TileID.HallowedGrass or TileID.GolfGrassHallowed => TreeTypes.Hallowed,
            TileID.Sand => TreeTypes.Palm,
            TileID.Ebonsand => TreeTypes.PalmCorrupt,
            TileID.Crimsand => TreeTypes.PalmCrimson,
            TileID.Pearlsand => TreeTypes.PalmHallowed,
            TileID.Ash => TreeTypes.Ash,
            _ => TreeTypes.None
        };
    }

    /// <summary>
    /// Gets the localized tree name for a given tree type.
    /// </summary>
    private static string? GetTreeNameFromType(TreeTypes treeType)
    {
        return treeType switch
        {
            TreeTypes.Forest => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Forest", "Forest Tree"),
            TreeTypes.Corrupt => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Corrupt", "Corrupt Tree"),
            TreeTypes.Mushroom => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Mushroom", "Mushroom Tree"),
            TreeTypes.Crimson => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Crimson", "Crimson Tree"),
            TreeTypes.Jungle => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Jungle", "Jungle Tree"),
            TreeTypes.Snow => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Snow", "Boreal Tree"),
            TreeTypes.Hallowed => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Hallowed", "Hallowed Tree"),
            TreeTypes.Palm => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Palm", "Palm Tree"),
            TreeTypes.PalmCorrupt => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.PalmCorrupt", "Corrupt Palm Tree"),
            TreeTypes.PalmCrimson => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.PalmCrimson", "Crimson Palm Tree"),
            TreeTypes.PalmHallowed => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.PalmHallowed", "Hallowed Palm Tree"),
            TreeTypes.Ash => GetLocalizedWithFallback("Mods.TerrariaAccess.TreeNames.Ash", "Ash Tree"),
            _ => null
        };
    }

    internal static bool IsLikelyPlayerChat(string text)
    {
        return text.Contains(": ", StringComparison.Ordinal);
    }

    internal static bool IsHousingQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        return HousingQueryPhrases.Value.Contains(sanitized) ||
            sanitized.Contains("housing", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildHousingQueryPhraseSet()
    {
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSanitizedIfPresent(phrases, GetLangInterValue(39));
        AddSanitizedIfPresent(phrases, GetLangInterValue(41));
        AddSanitizedIfPresent(phrases, GetLangInterValue(42));

        for (int i = 0; i <= 120; i++)
        {
            string key = $"TownNPCHousingFailureReasons.{i}";
            string value = Language.GetTextValue(key);
            if (!string.Equals(value, key, StringComparison.Ordinal))
            {
                AddSanitizedIfPresent(phrases, value);
            }
        }

        return phrases;
    }

    private static string? GetLangInterValue(int index)
    {
        if (index < 0 || index >= Lang.inter.Length)
        {
            return null;
        }

        return Lang.inter[index].Value;
    }

    private static void AddSanitizedIfPresent(ISet<string> target, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            target.Add(sanitized);
        }
    }
}
