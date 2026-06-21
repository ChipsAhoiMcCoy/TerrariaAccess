#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems.Combat;

public sealed class BossAttackWarningSystem : ModSystem
{
    private const uint ShortCooldownFrames = 120;
    private const uint MediumCooldownFrames = 180;
    private const uint LongCooldownFrames = 240;

    private static readonly Dictionary<int, NpcAttackSnapshot> NpcSnapshots = new();
    private static readonly Dictionary<int, ProjectileSnapshot> ProjectileSnapshots = new();
    private static readonly Dictionary<string, uint> NextAnnouncementFrames = new();

    public override void OnWorldUnload()
    {
        Reset();
    }

    public override void Unload()
    {
        Reset();
    }

    internal static void ObserveNpc(NPC npc)
    {
        if (!CanWarn(out Player player))
        {
            return;
        }

        int currentAttack = (int)npc.ai[0];
        int currentTimer = (int)npc.ai[1];
        float currentLifeRatio = LifeRatio(npc);
        if (!NpcSnapshots.TryGetValue(npc.whoAmI, out NpcAttackSnapshot previous)
            || previous.Type != npc.type)
        {
            NpcSnapshots[npc.whoAmI] = new NpcAttackSnapshot(npc.type, currentAttack, currentTimer, currentLifeRatio);
            return;
        }

        switch (npc.type)
        {
            case NPCID.KingSlime:
                WarnKingSlime(npc, player);
                break;
            case NPCID.EyeofCthulhu:
                WarnEyeOfCthulhu(npc, previous, player);
                break;
            case NPCID.EaterofWorldsHead:
                WarnWormPressure("boss-warning-eater-head-pressure", npc, player, "Eater head close. Move perpendicular.");
                break;
            case NPCID.BrainofCthulhu:
                WarnBrainOfCthulhu(npc, previous);
                break;
            case NPCID.QueenBee:
                WarnQueenBee(npc, previous, player);
                break;
            case NPCID.SkeletronHead:
                WarnSkeletron(npc, previous, player);
                break;
            case NPCID.WallofFlesh:
            case NPCID.WallofFleshEye:
                WarnWallOfFlesh(npc, previous, player);
                break;
            case NPCID.Retinazer:
                WarnRetinazer(npc, previous, player);
                break;
            case NPCID.Spazmatism:
                WarnSpazmatism(npc, previous, player);
                break;
            case NPCID.TheDestroyer:
                WarnDestroyer(npc, player);
                break;
            case NPCID.SkeletronPrime:
                WarnSkeletronPrime(npc, previous, player);
                break;
            case NPCID.PrimeCannon:
                WarnPrimeCannon(npc, player);
                break;
            case NPCID.Plantera:
                WarnPlantera(npc, previous, player);
                break;
            case NPCID.Golem:
            case NPCID.GolemHead:
            case NPCID.GolemHeadFree:
                WarnGolem(npc, previous, player);
                break;
            case NPCID.MourningWood:
                WarnMourningWood(npc, player);
                break;
            case NPCID.Pumpking:
                WarnPumpking(npc, player);
                break;
            case NPCID.Everscream:
                WarnEverscream(npc, player);
                break;
            case NPCID.IceQueen:
                WarnIceQueen(npc, player);
                break;
            case NPCID.SantaNK1:
                WarnSanta(npc, player);
                break;
            case NPCID.MartianSaucer:
                WarnMartianSaucer(npc, previous, player);
                break;
            case NPCID.PirateShip:
            case NPCID.PirateShipCannon:
                WarnPirateShip(npc, player);
                break;
            case NPCID.LunarTowerSolar:
            case NPCID.LunarTowerVortex:
            case NPCID.LunarTowerNebula:
            case NPCID.LunarTowerStardust:
                WarnCelestialPillar(npc, player);
                break;
            case NPCID.SolarCrawltipedeHead:
                WarnWormPressure(
                    "boss-warning-solar-crawltipede-pressure",
                    npc,
                    player,
                    "Crawltipede head close. Stay grounded or move away.");
                break;
            case NPCID.MoonLordHead:
                WarnMoonLordHead(npc, previous);
                break;
            case NPCID.DukeFishron:
                WarnDukeFishron(npc, previous, player);
                break;
            case NPCID.HallowBoss:
                WarnEmpressOfLight(npc, previous, player);
                break;
            case NPCID.QueenSlimeBoss:
                WarnQueenSlime(npc, previous);
                break;
            case NPCID.Deerclops:
                WarnDeerclops(npc, previous);
                break;
            case NPCID.CultistBoss:
                WarnLunaticCultist(npc, previous);
                break;
            case NPCID.DD2DarkMageT1:
            case NPCID.DD2DarkMageT3:
                WarnDarkMage(npc, player);
                break;
            case NPCID.DD2OgreT2:
            case NPCID.DD2OgreT3:
                WarnOgre(npc, player);
                break;
            case NPCID.DD2Betsy:
                WarnBetsy(npc, player);
                break;
            case NPCID.BloodNautilus:
                WarnBloodNautilus(npc, player);
                break;
        }

        NpcSnapshots[npc.whoAmI] = new NpcAttackSnapshot(npc.type, currentAttack, currentTimer, currentLifeRatio);
    }

    internal static void ObserveProjectile(Projectile projectile)
    {
        if (!projectile.hostile || !CanWarn(out Player player))
        {
            return;
        }

        if (!IsNewProjectileInstance(projectile))
        {
            return;
        }

        switch (projectile.type)
        {
            case ProjectileID.CannonballHostile:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.PirateShip, NPCID.PirateShipCannon },
                    "boss-warning-pirate-ship-cannonball",
                    "Pirate ship cannonball. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.OneEyedPirate:
            case ProjectileID.SoulscourgePirate:
            case ProjectileID.PirateCaptain:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.PirateShip, NPCID.PirateShipCannon },
                    "boss-warning-pirate-ship-projectile",
                    "Pirate ship projectile. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.Stinger:
            case ProjectileID.QueenBeeStinger:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.QueenBee },
                    "boss-warning-queen-bee-stingers",
                    "Queen Bee stingers. Keep moving vertically.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.EyeLaser:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Retinazer, NPCID.SkeletronPrime, NPCID.PrimeLaser, NPCID.Golem, NPCID.GolemHead, NPCID.GolemHeadFree },
                    "boss-warning-eye-laser",
                    "Laser shot. Keep moving.",
                    ShortCooldownFrames);
                break;
            case ProjectileID.CursedFlameHostile:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Spazmatism },
                    "boss-warning-spazmatism-flame",
                    "Cursed flames. Move away from the stream.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.DeathLaser:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.WallofFlesh, NPCID.WallofFleshEye, NPCID.TheDestroyer },
                    "boss-warning-death-laser",
                    "Death laser volley. Keep moving.",
                    ShortCooldownFrames);
                break;
            case ProjectileID.BombSkeletronPrime:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.SkeletronPrime, NPCID.PrimeCannon },
                    "boss-warning-prime-bombs",
                    "Prime bombs falling. Move out.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.RocketSkeleton:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.SantaNK1, NPCID.SkeletronPrime, NPCID.PrimeCannon },
                    "boss-warning-boss-rocket",
                    "Rocket incoming. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.GolemFist:
                WarnProjectile(
                    "boss-warning-golem-fist",
                    $"Golem fist from {HorizontalSide(projectile.Center, player)}. Jump or move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.SeedPlantera:
            case ProjectileID.PoisonSeedPlantera:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Plantera },
                    "boss-warning-plantera-seeds",
                    "Plantera seed volley. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.ThornBall:
            case ProjectileID.SporeCloud:
            case ProjectileID.SporeGas:
            case ProjectileID.SporeGas2:
            case ProjectileID.SporeGas3:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Plantera },
                    "boss-warning-plantera-hazards",
                    "Plantera hazards nearby. Move to open space.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.FlamingWood:
            case ProjectileID.GreekFire1:
            case ProjectileID.GreekFire2:
            case ProjectileID.GreekFire3:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.MourningWood },
                    "boss-warning-mourning-wood-fire",
                    "Mourning Wood fire. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.FlamingJack:
            case ProjectileID.FlamingScythe:
            case ProjectileID.HorsemanPumpkin:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Pumpking },
                    "boss-warning-pumpking-projectile",
                    "Pumpking projectile. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.PineNeedleHostile:
            case ProjectileID.OrnamentHostile:
            case ProjectileID.OrnamentHostileShrapnel:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.Everscream },
                    "boss-warning-everscream-projectile",
                    "Everscream projectiles. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.FrostWave:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.IceQueen },
                    "boss-warning-ice-queen-frost-wave",
                    "Frost wave. Jump or move through the gap.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.SantaBombs:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.SantaNK1 },
                    "boss-warning-santa-bombs",
                    "Santa bombs falling. Move out.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.SaucerDeathray:
                WarnProjectile(
                    "boss-warning-saucer-deathray",
                    IsLaserNearPlayer(projectile, player)
                        ? "Saucer deathray on you. Move sideways."
                        : "Saucer deathray sweeping. Keep moving sideways.",
                    ShortCooldownFrames);
                break;
            case ProjectileID.SaucerLaser:
            case ProjectileID.SaucerMissile:
            case ProjectileID.SaucerScrap:
                WarnProjectile(
                    "boss-warning-saucer-projectiles",
                    "Saucer projectiles incoming. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.TowerDamageBolt:
                WarnProjectileIfBossActive(
                    CelestialPillarTypes,
                    "boss-warning-pillar-bolt",
                    "Pillar energy bolt. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.SolarFlareRay:
            case ProjectileID.SolarWhipSword:
            case ProjectileID.SolarWhipSwordExplosion:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.LunarTowerSolar },
                    "boss-warning-solar-pillar-projectile",
                    "Solar pillar hazard. Stay grounded and keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.VortexLaser:
            case ProjectileID.VortexVortexLightning:
            case ProjectileID.VortexLightning:
            case ProjectileID.VortexAcid:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.LunarTowerVortex },
                    "boss-warning-vortex-pillar-projectile",
                    "Vortex pillar shot. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.NebulaBolt:
            case ProjectileID.NebulaEye:
            case ProjectileID.NebulaSphere:
            case ProjectileID.NebulaLaser:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.LunarTowerNebula },
                    "boss-warning-nebula-pillar-projectile",
                    "Nebula pillar magic. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.StardustSoldierLaser:
            case ProjectileID.StardustJellyfishSmall:
            case ProjectileID.StardustTowerMark:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.LunarTowerStardust },
                    "boss-warning-stardust-pillar-projectile",
                    "Stardust pillar shot. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.DD2DarkMageBolt:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.DD2DarkMageT1, NPCID.DD2DarkMageT3 },
                    "boss-warning-dark-mage-bolt",
                    "Dark Mage bolt. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.DD2OgreStomp:
            case ProjectileID.DD2OgreSpit:
            case ProjectileID.DD2OgreSmash:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.DD2OgreT2, NPCID.DD2OgreT3 },
                    "boss-warning-ogre-projectile",
                    "Ogre attack. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.DD2BetsyFireball:
                WarnProjectile(
                    "boss-warning-betsy-fireball",
                    "Betsy fireball. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.PhantasmalDeathray:
                WarnProjectile(
                    "boss-warning-moon-lord-deathray-active",
                    IsLaserNearPlayer(projectile, player)
                        ? "Deathray line on you. Move sideways."
                        : "Deathray sweeping. Keep moving sideways.",
                    ShortCooldownFrames);
                break;
            case ProjectileID.DD2BetsyFlameBreath:
                WarnProjectile(
                    "boss-warning-betsy-flame-breath",
                    $"Flame breath from {HorizontalSide(projectile.Center, player)}. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.BloodNautilusShot:
            case ProjectileID.BloodNautilusTears:
                WarnProjectileIfBossActive(
                    new int[] { NPCID.BloodNautilus },
                    "boss-warning-blood-nautilus-projectile",
                    "Blood Nautilus shots. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.QueenSlimeSmash:
                WarnProjectile(
                    "boss-warning-queen-slime-slam-projectile",
                    "Slam impact. Move away.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.QueenSlimeGelAttack:
                WarnProjectile(
                    "boss-warning-queen-slime-gel",
                    "Gel burst. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.DeerclopsIceSpike:
                if (Vector2.DistanceSquared(projectile.Center, player.Center) <= 320f * 320f)
                {
                    WarnProjectile(
                        "boss-warning-deerclops-ice-spike",
                        "Ice spike near you. Move away.",
                        ShortCooldownFrames);
                }

                break;
            case ProjectileID.InsanityShadowHostile:
                WarnProjectile(
                    "boss-warning-deerclops-shadow-hands",
                    "Shadow hands. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.HallowBossRainbowStreak:
            case ProjectileID.HallowBossLastingRainbow:
                WarnProjectile(
                    "boss-warning-empress-rainbow",
                    "Rainbow wall. Keep moving.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.FairyQueenLance:
            case ProjectileID.FairyQueenSunDance:
                WarnProjectile(
                    "boss-warning-empress-light-swords",
                    "Light swords forming. Move between lines.",
                    MediumCooldownFrames);
                break;
            case ProjectileID.CultistBossLightningOrb:
            case ProjectileID.CultistBossLightningOrbArc:
                WarnProjectile(
                    "boss-warning-cultist-lightning",
                    "Lightning orb. Move away.",
                    MediumCooldownFrames);
                break;
        }
    }

    private static void WarnKingSlime(NPC npc, Player player)
    {
        if (npc.velocity.Y > 7f && npc.Center.Y < player.Center.Y && Math.Abs(npc.Center.X - player.Center.X) < 220f)
        {
            TryAnnounce(
                "boss-warning-king-slime-fall",
                "King Slime falling above. Move aside.",
                MediumCooldownFrames);
        }
    }

    private static void WarnEyeOfCthulhu(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-eye-phase-two",
                "Eye of Cthulhu phase two. Fast charges incoming.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-eye-charge",
            npc,
            player,
            $"Eye charge from {HorizontalSide(npc.Center, player)}. Move vertically.",
            MediumCooldownFrames);
    }

    private static void WarnBrainOfCthulhu(NPC npc, NpcAttackSnapshot previous)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-brain-phase-two",
                "Brain phase two. Clones active. Track the real one.",
                LongCooldownFrames);
        }
    }

    private static void WarnQueenBee(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredAttack(npc, previous, 1) || EnteredAttack(npc, previous, 3))
        {
            TryAnnounce(
                "boss-warning-queen-bee-charge",
                $"Queen Bee charge from {HorizontalSide(npc.Center, player)}. Move vertically.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 2))
        {
            TryAnnounce(
                "boss-warning-queen-bee-stinger-charge",
                "Queen Bee stinger burst. Keep moving.",
                MediumCooldownFrames);
        }
    }

    private static void WarnSkeletron(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-skeletron-low-health",
                "Skeletron is faster. Keep distance.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-skeletron-spin",
            npc,
            player,
            $"Skeletron spinning from {HorizontalSide(npc.Center, player)}. Move away.",
            LongCooldownFrames);
    }

    private static void WarnWallOfFlesh(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.25f))
        {
            TryAnnounce(
                "boss-warning-wall-low-health",
                "Wall of Flesh speeding up. Keep running.",
                LongCooldownFrames);
        }

        if (Math.Abs(npc.Center.X - player.Center.X) <= 360f)
        {
            string side = HorizontalSide(npc.Center, player);
            TryAnnounce(
                "boss-warning-wall-close",
                $"Wall close on your {side}. Keep moving away.",
                LongCooldownFrames);
        }
    }

    private static void WarnRetinazer(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.4f))
        {
            TryAnnounce(
                "boss-warning-retinazer-phase-two",
                "Retinazer phase two. Laser barrages incoming.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-retinazer-charge",
            npc,
            player,
            $"Retinazer charge from {HorizontalSide(npc.Center, player)}. Move vertically.",
            MediumCooldownFrames);
    }

    private static void WarnSpazmatism(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.4f))
        {
            TryAnnounce(
                "boss-warning-spazmatism-phase-two",
                "Spazmatism phase two. Flame breath incoming.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-spazmatism-charge",
            npc,
            player,
            $"Spazmatism charge from {HorizontalSide(npc.Center, player)}. Move vertically.",
            MediumCooldownFrames);
    }

    private static void WarnDestroyer(NPC npc, Player player)
    {
        WarnWormPressure(
            "boss-warning-destroyer-head-pressure",
            npc,
            player,
            "Destroyer head close. Move away from the body line.");
    }

    private static void WarnSkeletronPrime(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-prime-low-health",
                "Skeletron Prime is faster. Watch bombs and lasers.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-prime-spin",
            npc,
            player,
            $"Skeletron Prime spinning from {HorizontalSide(npc.Center, player)}. Move away.",
            LongCooldownFrames);
    }

    private static void WarnPrimeCannon(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 640f * 640f)
        {
            TryAnnounce(
                "boss-warning-prime-cannon",
                "Prime cannon nearby. Watch for bombs.",
                LongCooldownFrames);
        }
    }

    private static void WarnPlantera(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-plantera-phase-two",
                "Plantera phase two. Keep moving around her.",
                LongCooldownFrames);
        }

        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 280f * 280f)
        {
            TryAnnounce(
                "boss-warning-plantera-close",
                "Plantera close. Move to open space.",
                LongCooldownFrames);
        }
    }

    private static void WarnGolem(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-golem-phase-two",
                "Golem phase two. Lasers and fists incoming.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-golem-jump",
            npc,
            player,
            "Golem jumping toward you. Move away.",
            LongCooldownFrames);
    }

    private static void WarnMourningWood(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-mourning-wood-close",
            npc,
            player,
            "Mourning Wood advancing. Keep distance.",
            LongCooldownFrames);
    }

    private static void WarnPumpking(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-pumpking-swipe",
            npc,
            player,
            $"Pumpking rush from {HorizontalSide(npc.Center, player)}. Move away.",
            MediumCooldownFrames);
    }

    private static void WarnEverscream(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-everscream-close",
            npc,
            player,
            "Everscream advancing. Keep distance.",
            LongCooldownFrames);
    }

    private static void WarnIceQueen(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-ice-queen-dash",
            npc,
            player,
            $"Ice Queen dash from {HorizontalSide(npc.Center, player)}. Move vertically.",
            MediumCooldownFrames);
    }

    private static void WarnSanta(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 720f * 720f)
        {
            TryAnnounce(
                "boss-warning-santa-close",
                "Santa tank nearby. Watch rockets and bombs.",
                LongCooldownFrames);
        }
    }

    private static void WarnMartianSaucer(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredLifeRatio(npc, previous, 0.5f))
        {
            TryAnnounce(
                "boss-warning-saucer-phase-two",
                "Martian Saucer phase two. Deathray risk high.",
                LongCooldownFrames);
        }

        WarnFastApproach(
            "boss-warning-saucer-close",
            npc,
            player,
            "Martian Saucer overhead. Keep moving.",
            LongCooldownFrames);
    }

    private static void WarnPirateShip(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 720f * 720f)
        {
            TryAnnounce(
                "boss-warning-pirate-ship-close",
                "Pirate ship overhead. Watch cannonballs.",
                LongCooldownFrames);
        }
    }

    private static void WarnCelestialPillar(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) > 960f * 960f)
        {
            return;
        }

        string pillarName = npc.type switch
        {
            NPCID.LunarTowerSolar => "Solar pillar",
            NPCID.LunarTowerVortex => "Vortex pillar",
            NPCID.LunarTowerNebula => "Nebula pillar",
            NPCID.LunarTowerStardust => "Stardust pillar",
            _ => "Celestial pillar"
        };

        string guidance = npc.type == NPCID.LunarTowerSolar
            ? "Stay grounded and keep moving."
            : "Keep moving and clear enemies.";

        TryAnnounce(
            $"boss-warning-pillar-close-{npc.type}",
            $"{pillarName} nearby. {guidance}",
            LongCooldownFrames);
    }

    private static void WarnMoonLordHead(NPC npc, NpcAttackSnapshot previous)
    {
        if (EnteredAttack(npc, previous, 1))
        {
            TryAnnounce(
                "boss-warning-moon-lord-deathray-charge",
                "Moon Lord deathray charging above. Move sideways.",
                LongCooldownFrames);
        }
    }

    private static void WarnDukeFishron(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        int attack = (int)npc.ai[0];
        if ((attack == 1 || attack == 4 || attack == 6 || attack == 7 || attack == 8)
            && previous.Attack != attack)
        {
            string side = HorizontalSide(npc.Center, player);
            TryAnnounce(
                "boss-warning-duke-fishron-charge",
                $"Duke Fishron charge from {side}. Jump or move up.",
                MediumCooldownFrames);
        }
    }

    private static void WarnEmpressOfLight(NPC npc, NpcAttackSnapshot previous, Player player)
    {
        if (EnteredAttack(npc, previous, 1))
        {
            TryAnnounce(
                "boss-warning-empress-dash",
                $"Empress dash from {HorizontalSide(npc.Center, player)}. Jump.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 2))
        {
            TryAnnounce(
                "boss-warning-empress-rainbow-charge",
                "Rainbow attack charging. Keep moving.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 4))
        {
            TryAnnounce(
                "boss-warning-empress-lance-charge",
                "Light swords forming. Move between lines.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 5))
        {
            TryAnnounce(
                "boss-warning-empress-ring-charge",
                "Rainbow ring forming. Keep moving.",
                MediumCooldownFrames);
        }
    }

    private static void WarnQueenSlime(NPC npc, NpcAttackSnapshot previous)
    {
        if (EnteredAttack(npc, previous, 4))
        {
            TryAnnounce(
                "boss-warning-queen-slime-slam",
                "Queen Slime preparing a slam. Move away.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 5))
        {
            TryAnnounce(
                "boss-warning-queen-slime-gel-charge",
                "Queen Slime gel burst charging. Keep moving.",
                MediumCooldownFrames);
        }
    }

    private static void WarnDeerclops(NPC npc, NpcAttackSnapshot previous)
    {
        if (EnteredAttack(npc, previous, 1))
        {
            TryAnnounce(
                "boss-warning-deerclops-forward-spikes",
                "Ice spikes ahead. Jump or move back.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 4))
        {
            TryAnnounce(
                "boss-warning-deerclops-both-side-spikes",
                "Ice spikes on both sides. Jump.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 2))
        {
            TryAnnounce(
                "boss-warning-deerclops-shadow-charge",
                "Shadow hands charging. Keep moving.",
                LongCooldownFrames);
        }
    }

    private static void WarnLunaticCultist(NPC npc, NpcAttackSnapshot previous)
    {
        if (EnteredAttack(npc, previous, 2))
        {
            TryAnnounce(
                "boss-warning-cultist-ice",
                "Cultist ice mist. Move away from the cloud.",
                LongCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 3))
        {
            TryAnnounce(
                "boss-warning-cultist-fireballs",
                "Cultist fireballs. Keep moving.",
                MediumCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 4))
        {
            TryAnnounce(
                "boss-warning-cultist-lightning-charge",
                "Cultist lightning orb charging. Move away.",
                LongCooldownFrames);
        }
        else if (EnteredAttack(npc, previous, 5))
        {
            TryAnnounce(
                "boss-warning-cultist-ritual",
                "Cultist ritual. Find the real cultist.",
                LongCooldownFrames);
        }
    }

    private static void WarnDarkMage(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 700f * 700f)
        {
            TryAnnounce(
                "boss-warning-dark-mage-cast",
                "Dark Mage casting nearby. Keep moving.",
                LongCooldownFrames);
        }
    }

    private static void WarnOgre(NPC npc, Player player)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 420f * 420f)
        {
            TryAnnounce(
                "boss-warning-ogre-close",
                "Ogre close. Move away before the stomp.",
                LongCooldownFrames);
        }
    }

    private static void WarnBetsy(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-betsy-dive",
            npc,
            player,
            $"Betsy dive from {HorizontalSide(npc.Center, player)}. Move away.",
            MediumCooldownFrames);
    }

    private static void WarnBloodNautilus(NPC npc, Player player)
    {
        WarnFastApproach(
            "boss-warning-blood-nautilus-close",
            npc,
            player,
            "Blood Nautilus close. Keep distance.",
            LongCooldownFrames);
    }

    private static bool EnteredAttack(NPC npc, NpcAttackSnapshot previous, int attack)
    {
        return (int)npc.ai[0] == attack && previous.Attack != attack;
    }

    private static bool EnteredLifeRatio(NPC npc, NpcAttackSnapshot previous, float threshold)
    {
        return previous.LifeRatio > threshold && LifeRatio(npc) <= threshold;
    }

    private static bool CanWarn(out Player player)
    {
        player = null!;

        if (Main.dedServ
            || Main.gameMenu
            || Main.gamePaused
            || TerrariaAccessConfig.Instance?.BossWarningsEnabled == false)
        {
            return false;
        }

        int playerIndex = Main.myPlayer;
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
        {
            return false;
        }

        player = Main.player[playerIndex];
        return player.active && !player.dead && !player.ghost;
    }

    private static bool IsNewProjectileInstance(Projectile projectile)
    {
        int whoAmI = projectile.whoAmI;
        int identity = projectile.identity;
        int timeLeft = projectile.timeLeft;
        if (ProjectileSnapshots.TryGetValue(whoAmI, out ProjectileSnapshot previous)
            && previous.Type == projectile.type
            && previous.Identity == identity
            && timeLeft <= previous.TimeLeft + 5)
        {
            ProjectileSnapshots[whoAmI] = new ProjectileSnapshot(projectile.type, identity, timeLeft);
            return false;
        }

        ProjectileSnapshots[whoAmI] = new ProjectileSnapshot(projectile.type, identity, timeLeft);
        return true;
    }

    private static float LifeRatio(NPC npc)
    {
        return npc.lifeMax <= 0 ? 1f : Math.Clamp((float)npc.life / npc.lifeMax, 0f, 1f);
    }

    private static void WarnWormPressure(string key, NPC npc, Player player, string message)
    {
        if (Vector2.DistanceSquared(npc.Center, player.Center) <= 240f * 240f)
        {
            TryAnnounce(key, message, LongCooldownFrames);
        }
    }

    private static void WarnFastApproach(string key, NPC npc, Player player, string message, uint cooldownFrames)
    {
        if (npc.velocity.LengthSquared() < 64f)
        {
            return;
        }

        Vector2 toPlayer = player.Center - npc.Center;
        if (toPlayer.LengthSquared() < 0.01f || toPlayer.LengthSquared() > 720f * 720f)
        {
            return;
        }

        toPlayer.Normalize();
        if (Vector2.Dot(npc.velocity, toPlayer) >= 3f)
        {
            TryAnnounce(key, message, cooldownFrames);
        }
    }

    private static bool IsLaserNearPlayer(Projectile projectile, Player player)
    {
        Vector2 direction = projectile.velocity;
        if (direction.LengthSquared() < 0.01f)
        {
            return Vector2.DistanceSquared(projectile.Center, player.Center) <= 160f * 160f;
        }

        direction.Normalize();
        Vector2 toPlayer = player.Center - projectile.Center;
        float distanceFromLine = Math.Abs(toPlayer.X * direction.Y - toPlayer.Y * direction.X);
        float distanceAlongLine = Vector2.Dot(toPlayer, direction);
        return distanceFromLine <= 96f && distanceAlongLine >= -160f;
    }

    private static string HorizontalSide(Vector2 source, Player player)
    {
        float delta = source.X - player.Center.X;
        if (Math.Abs(delta) < 48f)
        {
            return "nearby";
        }

        return delta < 0f ? "left" : "right";
    }

    private static void WarnProjectile(string key, string message, uint cooldownFrames)
    {
        TryAnnounce(key, message, cooldownFrames);
    }

    private static void WarnProjectileIfBossActive(int[] npcTypes, string key, string message, uint cooldownFrames)
    {
        if (AnyActiveNpc(npcTypes))
        {
            TryAnnounce(key, message, cooldownFrames);
        }
    }

    private static bool AnyActiveNpc(params int[] npcTypes)
    {
        for (int npcIndex = 0; npcIndex < Main.maxNPCs; npcIndex++)
        {
            NPC npc = Main.npc[npcIndex];
            if (!npc.active)
            {
                continue;
            }

            for (int typeIndex = 0; typeIndex < npcTypes.Length; typeIndex++)
            {
                if (npc.type == npcTypes[typeIndex])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly int[] CelestialPillarTypes =
    {
        NPCID.LunarTowerSolar,
        NPCID.LunarTowerVortex,
        NPCID.LunarTowerNebula,
        NPCID.LunarTowerStardust
    };

    private static void TryAnnounce(string key, string message, uint cooldownFrames)
    {
        uint currentFrame = Main.GameUpdateCount;
        if (NextAnnouncementFrames.TryGetValue(key, out uint nextFrame) && currentFrame < nextFrame)
        {
            return;
        }

        NextAnnouncementFrames[key] = currentFrame + cooldownFrames;
        ScreenReaderService.Announce(
            message,
            force: true,
            category: ScreenReaderService.AnnouncementCategory.World,
            requestInterrupt: true);
    }

    private static void Reset()
    {
        NpcSnapshots.Clear();
        ProjectileSnapshots.Clear();
        NextAnnouncementFrames.Clear();
    }

    private readonly record struct NpcAttackSnapshot(int Type, int Attack, int Timer, float LifeRatio);
    private readonly record struct ProjectileSnapshot(int Type, int Identity, int TimeLeft);
}

public sealed class BossAttackWarningGlobalNPC : GlobalNPC
{
    public override void PostAI(NPC npc)
    {
        BossAttackWarningSystem.ObserveNpc(npc);
    }
}

public sealed class BossAttackWarningGlobalProjectile : GlobalProjectile
{
    public override void PostAI(Projectile projectile)
    {
        BossAttackWarningSystem.ObserveProjectile(projectile);
    }
}
