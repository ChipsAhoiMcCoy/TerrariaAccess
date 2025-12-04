#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ScreenReaderMod.Common.Systems;

internal sealed class CursorDescriptorService
{
    private const int LiquidDescriptorBaseTileType = -1000;
    private const int WallDescriptorBaseTileType = -2000;

    private static readonly int[] StyledTileTypes =
    {
        TileID.Banners,
        TileID.Statues,
        TileID.AlphabetStatues,
        TileID.MushroomStatue,
        TileID.BoulderStatue,
        TileID.Painting2X3,
        TileID.Painting3X2,
        TileID.Painting3X3,
        TileID.Painting4X3,
        TileID.Painting6X4,
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
            descriptor = BuildDescriptor(tileType, name);
            return true;
        }

        if (tileType != TileID.Banners && TryDescribeTileFromItemPlacement(tile, tileType, out name))
        {
            descriptor = BuildDescriptor(tileType, name);
            return true;
        }

        try
        {
            int lookup = MapHelper.TileToLookup(tileType, 0);
            name = Lang.GetMapObjectName(lookup);
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

        descriptor = BuildDescriptor(tileType, name);
        return true;
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

    internal static bool ShouldSuppressVariantNames(int announcementKey)
    {
        return announcementKey == TileID.Dirt;
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

        int chestIndex = Chest.FindChestByGuessing(tileX, tileY);
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

    private static bool IsChestTile(int tileType)
    {
        return tileType == TileID.Containers ||
               tileType == TileID.Containers2 ||
               tileType == TileID.Dressers;
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

        int style = TileObjectData.GetTileStyle(tile);
        if (style < 0)
        {
            return false;
        }

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
        if (!TileStyleToItemType.TryGetValue(tileType, out Dictionary<int, int>? map))
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
            LiquidID.Water => "Mods.ScreenReaderMod.CursorLiquids.Water",
            LiquidID.Lava => "Mods.ScreenReaderMod.CursorLiquids.Lava",
            LiquidID.Honey => "Mods.ScreenReaderMod.CursorLiquids.Honey",
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
