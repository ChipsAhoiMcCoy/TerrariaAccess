#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Utilities;

internal static class WorldTileSoundResolver
{
    public static bool TryResolveCursorSound(
        int tileX,
        int tileY,
        out SoundStyle style,
        out float volumeScale,
        out float pitchOffset)
    {
        style = default;
        volumeScale = 1f;
        pitchOffset = 0f;

        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return false;
        }

        Tile tile = Main.tile[tileX, tileY];
        if (tile.LiquidAmount > 0 && TryResolveLiquidSound(tile, out style, out volumeScale, out pitchOffset))
        {
            return true;
        }

        if (tile.HasTile)
        {
            return TryResolveTileHitSound(tile, out style, out volumeScale, out pitchOffset);
        }

        if (tile.WallType > WallID.None)
        {
            return TryResolveWallHitSound(tile, out style, out volumeScale, out pitchOffset);
        }

        return false;
    }

    public static bool TryResolveTileHitSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
    {
        volumeScale = 1f;
        pitchOffset = 0f;

        ModTile? modTile = TileLoader.GetTile(tile.TileType);
        if (modTile is not null)
        {
            if (modTile.HitSound is SoundStyle modHitSound)
            {
                style = modHitSound;
                return true;
            }
        }

        int type = tile.TileType;
        if (type == TileID.Crystals || type == TileID.LargePiles2)
        {
            style = SoundID.Item27;
            return true;
        }

        if (type == TileID.Traps || type == TileID.PlanterBox)
        {
            style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
            return true;
        }

        if (IsWoodLikeTile(type))
        {
            style = SoundID.Dig;
            volumeScale = 0.85f;
            pitchOffset = -0.1f;
            return true;
        }

        if (IsPlantLikeTile(type))
        {
            style = SoundID.Grass;
            volumeScale = 0.8f;
            return true;
        }

        if (type == TileID.MinecartTrack)
        {
            style = SoundID.Item52;
            return true;
        }

        if (type == TileID.Chimney || type == TileID.Explosives || type == TileID.Grate)
        {
            style = SoundID.NPCHit4;
            return true;
        }

        if (type == TileID.ClosedDoor && tile.TileFrameX >= 54)
        {
            style = SoundID.NPCHit4;
            return true;
        }

        if (IsGlassLikeTile(type))
        {
            style = SoundID.Shatter;
            volumeScale = 0.8f;
            return true;
        }

        if (IsMetalLikeTile(type))
        {
            style = SoundID.Tink;
            volumeScale = 0.75f;
            pitchOffset = 0.08f;
            return true;
        }

        if (type == TileID.Pots || type == TileID.PotsSuspended)
        {
            style = SoundID.Dig;
            volumeScale = 0.9f;
            return true;
        }

        if (type == TileID.BreakableIce)
        {
            style = SoundID.Item27;
            volumeScale = 0.85f;
            return true;
        }

        if ((type == TileID.MagicalIceBlock || type == TileID.ShimmerBlock || type == TileID.Larva) &&
            !Main.tileSolid[type])
        {
            style = SoundID.Item27;
            volumeScale = 0.85f;
            pitchOffset = 0.08f;
            return true;
        }

        if (IsIceLikeTile(type))
        {
            style = SoundID.Item50;
            volumeScale = 0.85f;
            pitchOffset = -0.08f;
            return true;
        }

        if (IsSnowLikeTile(type))
        {
            style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
            volumeScale = 0.75f;
            pitchOffset = 0.05f;
            return true;
        }

        if (IsGrassBlockTile(type))
        {
            style = SoundID.Grass;
            volumeScale = 0.75f;
            pitchOffset = -0.05f;
            return true;
        }

        if (IsSandLikeTile(type))
        {
            style = SoundID.Dig;
            volumeScale = 0.65f;
            pitchOffset = 0.18f;
            return true;
        }

        if (IsOrganicLikeTile(type))
        {
            style = SoundID.Dig;
            volumeScale = 0.75f;
            pitchOffset = -0.08f;
            return true;
        }

        if (IsDirtLikeTile(type))
        {
            style = SoundID.Dig;
            volumeScale = 0.72f;
            pitchOffset = 0.08f;
            return true;
        }

        if (IsHardStoneLikeTile(type))
        {
            style = SoundID.Tink;
            volumeScale = 0.7f;
            pitchOffset = -0.16f;
            return true;
        }

        if (IsBrickLikeTile(type))
        {
            style = SoundID.Tink;
            volumeScale = 0.78f;
            pitchOffset = -0.08f;
            return true;
        }

        if (IsSolidFullTile(type))
        {
            style = SoundID.Dig;
            volumeScale = 0.85f;
            pitchOffset = -0.03f;
            return true;
        }

        style = SoundID.Dig;
        volumeScale = 0.8f;
        return true;
    }

    public static bool TryResolveWallHitSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
    {
        volumeScale = 1f;
        pitchOffset = 0f;

        ModWall? modWall = WallLoader.GetWall(tile.WallType);
        if (modWall is not null)
        {
            if (modWall.HitSound is SoundStyle modHitSound)
            {
                style = modHitSound;
                return true;
            }
        }

        int wall = tile.WallType;
        if (wall == WallID.Glass ||
            wall == WallID.BlueStainedGlass ||
            wall == WallID.GreenStainedGlass ||
            wall == WallID.PurpleStainedGlass ||
            wall == WallID.RedStainedGlass ||
            wall == WallID.YellowStainedGlass ||
            wall == WallID.RainbowStainedGlass ||
            wall == WallID.Confetti ||
            wall == WallID.ConfettiBlack)
        {
            style = SoundID.Shatter;
            volumeScale = 0.75f;
            return true;
        }

        if ((wall >= WallID.CopperPlating && wall <= WallID.TinPlating) ||
            wall == WallID.IronFence ||
            wall == WallID.MetalFence ||
            wall == WallID.WroughtIronFence ||
            wall == WallID.LunarRustBrickWall)
        {
            style = SoundID.Tink;
            volumeScale = 0.75f;
            pitchOffset = 0.08f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.Ice, wall))
        {
            style = SoundID.Item50;
            volumeScale = 0.75f;
            pitchOffset = -0.08f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.Snow, wall))
        {
            style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
            volumeScale = 0.7f;
            pitchOffset = 0.05f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.Grass, wall))
        {
            style = SoundID.Grass;
            volumeScale = 0.7f;
            pitchOffset = -0.05f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.PureSand, wall) ||
            WallSetContains(WallID.Sets.Conversion.HardenedSand, wall) ||
            WallSetContains(WallID.Sets.Conversion.Sandstone, wall))
        {
            style = SoundID.Dig;
            volumeScale = 0.65f;
            pitchOffset = 0.15f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.Dirt, wall))
        {
            style = SoundID.Dig;
            volumeScale = 0.7f;
            pitchOffset = 0.08f;
            return true;
        }

        if (WallSetContains(WallID.Sets.Conversion.Stone, wall) ||
            WallSetContains(WallID.Sets.Conversion.NewWall1, wall) ||
            WallSetContains(WallID.Sets.Conversion.NewWall2, wall) ||
            WallSetContains(WallID.Sets.Conversion.NewWall3, wall) ||
            WallSetContains(WallID.Sets.Conversion.NewWall4, wall) ||
            MainWallSetContains(Main.wallDungeon, wall))
        {
            style = SoundID.Tink;
            volumeScale = 0.7f;
            pitchOffset = -0.12f;
            return true;
        }

        style = SoundID.Dig;
        volumeScale = 0.75f;
        return true;
    }

    private static bool TryResolveLiquidSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
    {
        switch (tile.LiquidType)
        {
            case LiquidID.Lava:
                style = SoundID.Splash;
                volumeScale = 0.75f;
                pitchOffset = -0.22f;
                return true;
            case LiquidID.Honey:
                style = SoundID.SplashWeak;
                volumeScale = 0.7f;
                pitchOffset = -0.12f;
                return true;
            case LiquidID.Shimmer:
                style = Main.rand.NextBool() ? SoundID.ShimmerWeak1 : SoundID.ShimmerWeak2;
                volumeScale = 0.75f;
                pitchOffset = -0.08f;
                return true;
            case LiquidID.Water:
                style = tile.LiquidAmount < 128 ? SoundID.SplashWeak : SoundID.Splash;
                volumeScale = 0.65f + MathHelper.Clamp(tile.LiquidAmount / 255f, 0f, 1f) * 0.35f;
                pitchOffset = 0f;
                return true;
            default:
                style = default;
                volumeScale = 0f;
                pitchOffset = 0f;
                return false;
        }
    }

    private static bool IsGlassLikeTile(int type)
    {
        return type == TileID.Glass ||
            type == TileID.GlassKiln ||
            type == TileID.Waterfall ||
            type == TileID.Lavafall ||
            type == TileID.Confetti ||
            type == TileID.ConfettiBlack ||
            type == TileID.Bubble ||
            type == TileID.CrystalBlock ||
            type == TileID.SandFallBlock ||
            type == TileID.SnowFallBlock;
    }

    private static bool IsIceLikeTile(int type)
    {
        return TileSetContains(TileID.Sets.Conversion.Ice, type) ||
            TileSetContains(TileID.Sets.IceSkateSlippery, type) ||
            type == TileID.IceBlock ||
            type == TileID.CorruptIce ||
            type == TileID.HallowedIce ||
            type == TileID.FleshIce ||
            type == TileID.FrozenSlimeBlock ||
            type == TileID.MagicalIceBlock;
    }

    private static bool IsSnowLikeTile(int type)
    {
        return TileSetContains(TileID.Sets.Conversion.Snow, type) ||
            type == TileID.SnowBlock ||
            type == TileID.SnowBrick ||
            type == TileID.Slush ||
            type == TileID.SnowCloud;
    }

    private static bool IsSandLikeTile(int type)
    {
        return MainTileSetContains(Main.tileSand, type) ||
            TileSetContains(TileID.Sets.Conversion.Sand, type) ||
            TileSetContains(TileID.Sets.Conversion.HardenedSand, type) ||
            TileSetContains(TileID.Sets.Conversion.Sandstone, type) ||
            TileSetContains(TileID.Sets.isDesertBiomeSand, type) ||
            type == TileID.Sand ||
            type == TileID.Ebonsand ||
            type == TileID.Pearlsand ||
            type == TileID.Crimsand ||
            type == TileID.Silt ||
            type == TileID.HardenedSand ||
            type == TileID.Sandstone ||
            type == TileID.SandstoneBrick ||
            type == TileID.SandStoneSlab ||
            type == TileID.SandstoneColumn;
    }

    private static bool IsGrassBlockTile(int type)
    {
        return TileSetContains(TileID.Sets.Conversion.Grass, type) ||
            TileSetContains(TileID.Sets.Conversion.JungleGrass, type) ||
            TileSetContains(TileID.Sets.Conversion.MushroomGrass, type) ||
            TileSetContains(TileID.Sets.Conversion.GolfGrass, type) ||
            TileSetContains(TileID.Sets.Grass, type) ||
            type == TileID.Grass ||
            type == TileID.CorruptGrass ||
            type == TileID.JungleGrass ||
            type == TileID.MushroomGrass ||
            type == TileID.HallowedGrass ||
            type == TileID.CrimsonGrass ||
            type == TileID.AshGrass;
    }

    private static bool IsDirtLikeTile(int type)
    {
        return TileSetContains(TileID.Sets.Conversion.Dirt, type) ||
            TileSetContains(TileID.Sets.CanBeDugByShovel, type) ||
            type == TileID.Dirt ||
            type == TileID.ClayBlock ||
            type == TileID.DirtiestBlock;
    }

    private static bool IsOrganicLikeTile(int type)
    {
        return type == TileID.Mud ||
            type == TileID.Mudstone ||
            type == TileID.Ash ||
            type == TileID.CactusBlock ||
            type == TileID.MushroomBlock ||
            type == TileID.SlimeBlock ||
            type == TileID.PinkSlimeBlock ||
            type == TileID.FleshBlock ||
            type == TileID.HoneyBlock ||
            type == TileID.CrispyHoneyBlock ||
            type == TileID.Hive ||
            type == TileID.Cloud ||
            type == TileID.RainCloud ||
            type == TileID.LeafBlock;
    }

    private static bool IsHardStoneLikeTile(int type)
    {
        return MainTileSetContains(Main.tileStone, type) ||
            TileSetContains(TileID.Sets.Conversion.Stone, type) ||
            TileSetContains(TileID.Sets.Stone, type) ||
            TileSetContains(TileID.Sets.Ore, type) ||
            MainTileOreFinderPriority(type) > 0 ||
            type == TileID.Stone ||
            type == TileID.Ebonstone ||
            type == TileID.Pearlstone ||
            type == TileID.Crimstone ||
            type == TileID.Obsidian ||
            type == TileID.Marble ||
            type == TileID.MarbleBlock ||
            type == TileID.MarbleColumn ||
            type == TileID.Granite ||
            type == TileID.GraniteBlock ||
            type == TileID.GraniteColumn ||
            type == TileID.StoneSlab ||
            type == TileID.ActiveStoneBlock ||
            type == TileID.InactiveStoneBlock ||
            type == TileID.Boulder;
    }

    private static bool IsBrickLikeTile(int type)
    {
        return MainTileSetContains(Main.tileBrick, type) ||
            type == TileID.GrayBrick ||
            type == TileID.RedBrick ||
            type == TileID.BlueDungeonBrick ||
            type == TileID.GreenDungeonBrick ||
            type == TileID.PinkDungeonBrick ||
            type == TileID.DemoniteBrick ||
            type == TileID.ObsidianBrick ||
            type == TileID.HellstoneBrick ||
            type == TileID.PearlstoneBrick ||
            type == TileID.EbonstoneBrick ||
            type == TileID.CrimstoneBrick ||
            type == TileID.LihzahrdBrick ||
            type == TileID.ChlorophyteBrick;
    }

    private static bool IsSolidFullTile(int type)
    {
        return MainTileSetContains(Main.tileSolid, type) &&
            !MainTileSetContains(Main.tileSolidTop, type);
    }

    private static bool IsWoodLikeTile(int type)
    {
        return TileSetContains(TileID.Sets.IsATreeTrunk, type) ||
            type == TileID.Trees ||
            type == TileID.PalmTree ||
            type == TileID.PineTree ||
            type == TileID.VanityTreeSakura ||
            type == TileID.VanityTreeYellowWillow ||
            type == TileID.TreeAsh ||
            type == TileID.TreeTopaz ||
            type == TileID.TreeAmethyst ||
            type == TileID.TreeSapphire ||
            type == TileID.TreeEmerald ||
            type == TileID.TreeRuby ||
            type == TileID.TreeDiamond ||
            type == TileID.TreeAmber ||
            type == TileID.Bamboo ||
            type == TileID.BambooBlock ||
            type == TileID.Cactus ||
            type == TileID.CactusBlock ||
            type == TileID.MushroomTrees ||
            type == TileID.WoodBlock ||
            type == TileID.LivingWood;
    }

    private static bool IsPlantLikeTile(int type)
    {
        return MainTileSetContains(Main.tileAlch, type) ||
            type == TileID.Plants ||
            type == TileID.Plants2 ||
            type == TileID.CorruptPlants ||
            type == TileID.CrimsonPlants ||
            type == TileID.HallowedPlants ||
            type == TileID.HallowedPlants2 ||
            type == TileID.JunglePlants ||
            type == TileID.JunglePlants2 ||
            type == TileID.MushroomPlants ||
            type == TileID.OasisPlants ||
            type == TileID.Cattail ||
            type == TileID.LilyPad ||
            type == TileID.Vines ||
            type == TileID.JungleVines ||
            type == TileID.CrimsonVines ||
            type == TileID.CorruptVines ||
            type == TileID.HallowedVines ||
            type == TileID.GolfGrass ||
            type == TileID.GolfGrassHallowed ||
            type == TileID.Seaweed ||
            type == TileID.SeaOats ||
            type == TileID.ImmatureHerbs ||
            type == TileID.MatureHerbs ||
            type == TileID.BloomingHerbs ||
            type == TileID.DyePlants ||
            type == TileID.Sunflower ||
            type == TileID.Pumpkins ||
            type == TileID.ClayPot ||
            type == TileID.PottedPlants1 ||
            type == TileID.PottedPlants2 ||
            type == TileID.PottedLavaPlants ||
            type == TileID.PottedLavaPlantTendrils ||
            type == TileID.PottedCrystalPlants ||
            type == TileID.AbigailsFlower ||
            type == TileID.PlanterBox ||
            type == TileID.LawnFlamingo;
    }

    private static bool IsMetalLikeTile(int type)
    {
        return type == TileID.Anvils ||
            type == TileID.AdamantiteForge ||
            type == TileID.MythrilAnvil ||
            type == TileID.Hellforge ||
            type == TileID.Furnaces ||
            type == TileID.MetalBars ||
            type == TileID.Containers ||
            type == TileID.Containers2 ||
            type == TileID.Safes ||
            type == TileID.PiggyBank ||
            type == TileID.MusicBoxes ||
            type == TileID.MinecartTrack ||
            type == TileID.LunarOre ||
            type == TileID.LunarCraftingStation ||
            type == TileID.HeavyWorkBench ||
            type == TileID.TinPlating ||
            type == TileID.CopperPlating;
    }

    private static bool TileSetContains(bool[] set, int type)
    {
        return type >= 0 && type < set.Length && set[type];
    }

    private static bool MainTileSetContains(bool[] set, int type)
    {
        return type >= 0 && type < set.Length && set[type];
    }

    private static bool WallSetContains(bool[] set, int wall)
    {
        return wall >= 0 && wall < set.Length && set[wall];
    }

    private static bool MainWallSetContains(bool[] set, int wall)
    {
        return wall >= 0 && wall < set.Length && set[wall];
    }

    private static short MainTileOreFinderPriority(int type)
    {
        return type >= 0 && type < Main.tileOreFinderPriority.Length
            ? Main.tileOreFinderPriority[type]
            : (short)0;
    }
}
