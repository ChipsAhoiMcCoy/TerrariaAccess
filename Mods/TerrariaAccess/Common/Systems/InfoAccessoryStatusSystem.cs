#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

internal static class InfoAccessoryStatusSystem
{
    private const int CycleCooldownFrames = 90; // ~1.5 seconds at 60fps

    private static int _lastPressFrame;
    private static int _cycleIndex;

    private static readonly InfoDisplayDefinition[] OrderedDisplays =
    {
        new(InfoDisplay.Watches, "Time", player => player.accWatch > 0),
        new(InfoDisplay.Compass, "Compass", player => player.accCompass > 0),
        new(InfoDisplay.DepthMeter, "Depth Meter", player => player.accDepthMeter > 0),
        new(InfoDisplay.Radar, "Radar", player => player.accThirdEye),
        new(InfoDisplay.TallyCounter, "Tally Counter", player => player.accJarOfSouls),
        new(InfoDisplay.LifeformAnalyzer, "Lifeform Analyzer", player => player.accCritterGuide),
        new(InfoDisplay.Stopwatch, "Stopwatch", player => player.accStopwatch),
        new(InfoDisplay.DPSMeter, "DPS Meter", player => player.accDreamCatcher),
        new(InfoDisplay.MetalDetector, "Metal Detector", player => player.accOreFinder),
        new(InfoDisplay.FishFinder, "Fishing Power", player => player.accFishFinder),
        new(InfoDisplay.WeatherRadio, "Weather", player => player.accWeatherRadio),
        new(InfoDisplay.Sextant, "Moon Phase", player => player.accCalendar),
    };

    internal static void Announce(Player player)
    {
        // Refresh info accessory flags on demand so inventory-based accessories
        // are reflected even if vanilla hasn't rebuilt the info display state yet.
        player.RefreshInfoAccs();

        List<string> announcements = BuildAnnouncements(player);
        if (announcements.Count == 0)
        {
            _cycleIndex = 0;
            _lastPressFrame = (int)Main.GameUpdateCount;
            ScreenReaderService.Announce("No informational accessories active", force: true);
            return;
        }

        int currentFrame = (int)Main.GameUpdateCount;
        int framesSinceLastPress = currentFrame - _lastPressFrame;

        if (framesSinceLastPress <= CycleCooldownFrames && _lastPressFrame > 0)
        {
            _cycleIndex = (_cycleIndex + 1) % announcements.Count;
            ScreenReaderService.Announce(announcements[_cycleIndex], force: true);
        }
        else
        {
            _cycleIndex = 0;
            ScreenReaderService.Announce(string.Join(". ", announcements) + ".", force: true);
        }

        _lastPressFrame = currentFrame;
    }

    private static List<string> BuildAnnouncements(Player player)
    {
        List<string> announcements = new();

        foreach (InfoDisplayDefinition definition in OrderedDisplays)
        {
            string? announcement = TryBuildInfoDisplayAnnouncement(player, definition);
            if (!string.IsNullOrWhiteSpace(announcement))
            {
                announcements.Add(announcement);
            }
        }

        string? mechanicalLens = TryBuildMechanicalLensAnnouncement(player);
        if (!string.IsNullOrWhiteSpace(mechanicalLens))
        {
            announcements.Add(mechanicalLens);
        }

        string? mechanicalRuler = TryBuildMechanicalRulerAnnouncement();
        if (!string.IsNullOrWhiteSpace(mechanicalRuler))
        {
            announcements.Add(mechanicalRuler);
        }

        return announcements;
    }

    private static string? TryBuildInfoDisplayAnnouncement(Player player, InfoDisplayDefinition definition)
    {
        if (!definition.IsAvailable(player))
        {
            return null;
        }

        string value = TextSanitizer.Clean(BuildDisplayValue(player, definition));
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{GetDisplayLabel(definition)} active";
        }

        string label = GetDisplayLabel(definition);
        return $"{label}: {value}";
    }

    private static string BuildDisplayValue(Player player, InfoDisplayDefinition definition)
    {
        if (definition.Display == InfoDisplay.Watches)
        {
            return BuildWatchValue(player);
        }

        if (definition.Display == InfoDisplay.Compass)
        {
            return BuildCompassValue(player);
        }

        if (definition.Display == InfoDisplay.DepthMeter)
        {
            return BuildDepthMeterValue(player);
        }

        if (definition.Display == InfoDisplay.WeatherRadio)
        {
            return BuildWeatherValue();
        }

        if (definition.Display == InfoDisplay.Sextant)
        {
            return BuildMoonPhaseValue();
        }

        if (definition.Display == InfoDisplay.FishFinder)
        {
            return BuildFishFinderValue(player);
        }

        if (definition.Display == InfoDisplay.MetalDetector)
        {
            return BuildMetalDetectorValue();
        }

        if (definition.Display == InfoDisplay.LifeformAnalyzer)
        {
            return BuildLifeformAnalyzerValue(player);
        }

        if (definition.Display == InfoDisplay.Radar)
        {
            return BuildRadarValue(player);
        }

        if (definition.Display == InfoDisplay.TallyCounter)
        {
            return BuildTallyCounterValue(player);
        }

        if (definition.Display == InfoDisplay.DPSMeter)
        {
            return BuildDpsMeterValue(player);
        }

        if (definition.Display == InfoDisplay.Stopwatch)
        {
            return BuildStopwatchValue(player);
        }

        Color displayColor = default;
        Color shadowColor = default;
        return definition.Display.DisplayValue(ref displayColor, ref shadowColor);
    }

    private static string GetDisplayLabel(InfoDisplayDefinition definition)
    {
        string label = TextSanitizer.Clean(definition.Display.DisplayName?.Value);
        return string.IsNullOrWhiteSpace(label) ? definition.FallbackLabel : label;
    }

    private static string? TryBuildMechanicalLensAnnouncement(Player player)
    {
        if (!player.InfoAccMechShowWires)
        {
            return null;
        }

        string mode = TextSanitizer.Clean(BuilderToggle.HideAllWires.DisplayValue());
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Mechanical Lens active";
        }

        return $"Mechanical Lens: {mode}";
    }

    private static string? TryBuildMechanicalRulerAnnouncement()
    {
        if (!BuilderToggle.RulerGrid.Active())
        {
            return null;
        }

        string state = TextSanitizer.Clean(BuilderToggle.RulerGrid.DisplayValue());
        if (string.IsNullOrWhiteSpace(state))
        {
            return "Mechanical Ruler active";
        }

        return $"Mechanical Ruler: {state}";
    }

    internal static string BuildWatchValue(Player player)
    {
        string period = Language.GetTextValue("GameUI.TimeAtMorning");
        double currentTime = Main.time;

        if (!Main.dayTime)
        {
            currentTime += 54000.0;
        }

        currentTime = currentTime / 86400.0 * 24.0;
        currentTime = currentTime - 7.5 - 12.0;
        if (currentTime < 0.0)
        {
            currentTime += 24.0;
        }

        if (currentTime >= 12.0)
        {
            period = Language.GetTextValue("GameUI.TimePastMorning");
        }

        int hour = (int)currentTime;
        int minute = (int)((currentTime - hour) * 60.0);
        string minuteText = minute.ToString("00");

        if (hour > 12)
        {
            hour -= 12;
        }

        if (hour == 0)
        {
            hour = 12;
        }

        if (player.accWatch == 1)
        {
            minuteText = "00";
        }

        if (player.accWatch == 2)
        {
            minuteText = minute < 30 ? "00" : "30";
        }

        return $"{hour}:{minuteText} {period}";
    }

    private static string BuildCompassValue(Player player)
    {
        int offset = (int)((player.position.X + (player.width / 2f)) * 2f / 16f - Main.maxTilesX);
        if (offset > 0)
        {
            return Language.GetTextValue("GameUI.CompassEast", offset);
        }

        if (offset < 0)
        {
            return Language.GetTextValue("GameUI.CompassWest", -offset);
        }

        return Language.GetTextValue("GameUI.CompassCenter");
    }

    private static string BuildDepthMeterValue(Player player)
    {
        int depth = (int)(((player.position.Y + player.height) * 2f / 16f) - Main.worldSurface * 2.0);
        float worldScale = Main.maxTilesX / 4200f;
        worldScale *= worldScale;
        int cavernPadding = 1200;
        float skyCheck = (float)((player.Center.Y / 16f - (65f + 10f * worldScale)) / (Main.worldSurface / 5.0));

        string layer = player.position.Y > (Main.maxTilesY - 204) * 16f
            ? Language.GetTextValue("GameUI.LayerUnderworld")
            : (player.position.Y > Main.rockLayer * 16.0 + (cavernPadding / 2f) + 16f
                ? Language.GetTextValue("GameUI.LayerCaverns")
                : (depth > 0
                    ? Language.GetTextValue("GameUI.LayerUnderground")
                    : (skyCheck >= 1f
                        ? Language.GetTextValue("GameUI.LayerSurface")
                        : Language.GetTextValue("GameUI.LayerSpace"))));

        int absoluteDepth = Math.Abs(depth);
        string depthText = absoluteDepth != 0
            ? Language.GetTextValue("GameUI.Depth", absoluteDepth)
            : Language.GetTextValue("GameUI.DepthLevel");

        return $"{depthText} {layer}";
    }

    private static string BuildWeatherValue()
    {
        string value;
        if (Main.IsItStorming)
        {
            value = Language.GetTextValue("GameUI.Storm");
        }
        else if ((double)Main.maxRaining > 0.6)
        {
            value = Language.GetTextValue("GameUI.HeavyRain");
        }
        else if ((double)Main.maxRaining >= 0.2)
        {
            value = Language.GetTextValue("GameUI.Rain");
        }
        else if (Main.maxRaining > 0f)
        {
            value = Language.GetTextValue("GameUI.LightRain");
        }
        else if (Main.cloudBGActive > 0f)
        {
            value = Language.GetTextValue("GameUI.Overcast");
        }
        else if (Main.numClouds > 90)
        {
            value = Language.GetTextValue("GameUI.MostlyCloudy");
        }
        else if (Main.numClouds > 55)
        {
            value = Language.GetTextValue("GameUI.Cloudy");
        }
        else if (Main.numClouds <= 15)
        {
            value = Language.GetTextValue("GameUI.Clear");
        }
        else
        {
            value = Language.GetTextValue("GameUI.PartlyCloudy");
        }

        int windSpeed = (int)(Main.windSpeedCurrent * 50f);
        if (windSpeed < 0)
        {
            value += Language.GetTextValue("GameUI.EastWind", Math.Abs(windSpeed));
        }
        else if (windSpeed > 0)
        {
            value += Language.GetTextValue("GameUI.WestWind", windSpeed);
        }

        if (Sandstorm.Happening)
        {
            if (Main.GlobalTimeWrappedHourly % 10f >= 5f)
            {
                value = Language.GetTextValue("GameUI.Sandstorm");
            }

            value += " +";
        }

        return value;
    }

    internal static string BuildMoonPhaseValue()
    {
        return Main.moonPhase switch
        {
            0 => Language.GetTextValue("GameUI.FullMoon"),
            1 => Language.GetTextValue("GameUI.WaningGibbous"),
            2 => Language.GetTextValue("GameUI.ThirdQuarter"),
            3 => Language.GetTextValue("GameUI.WaningCrescent"),
            4 => Language.GetTextValue("GameUI.NewMoon"),
            5 => Language.GetTextValue("GameUI.WaxingCrescent"),
            6 => Language.GetTextValue("GameUI.FirstQuarter"),
            7 => Language.GetTextValue("GameUI.WaxingGibbous"),
            _ => string.Empty,
        };
    }

    private static string BuildFishFinderValue(Player player)
    {
        bool hasBobberOut = false;
        for (int i = 0; i < Main.projectile.Length; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile.active && projectile.owner == Main.myPlayer && projectile.bobber)
            {
                hasBobberOut = true;
                break;
            }
        }

        if (hasBobberOut)
        {
            return player.displayedFishingInfo;
        }

        PlayerFishingConditions fishingConditions = player.GetFishingConditions();
        return fishingConditions.BaitItemType != 2673
            ? player.displayedFishingInfo = Language.GetTextValue("GameUI.FishingPower", fishingConditions.FinalFishingLevel)
            : Language.GetTextValue("GameUI.FishingWarning");
    }

    private static string BuildMetalDetectorValue()
    {
        if (Main.SceneMetrics.bestOre <= 0)
        {
            return Language.GetTextValue("GameUI.NoTreasureNearby");
        }

        int baseOption = 0;
        int tileType = Main.SceneMetrics.bestOre;
        if (Main.SceneMetrics.ClosestOrePosition.HasValue)
        {
            Point bestOrePosition = Main.SceneMetrics.ClosestOrePosition.Value;
            Tile tile = Framing.GetTileSafely(bestOrePosition);
            if (tile.HasTile)
            {
                MapHelper.GetTileBaseOption(bestOrePosition.X, bestOrePosition.Y, tile.TileType, tile, ref baseOption);
                tileType = tile.TileType;
                if (TileID.Sets.BasicChest[tileType] || TileID.Sets.BasicChestFake[tileType])
                {
                    baseOption = 0;
                }
            }
        }

        return Language.GetTextValue("GameUI.OreDetected", Lang.GetMapObjectName(MapHelper.TileToLookup(tileType, baseOption)));
    }

    private static string BuildLifeformAnalyzerValue(Player player)
    {
        const int searchRadius = 1300;
        int highestRarity = 0;
        int npcIndex = -1;

        if (player.accCritterGuideCounter <= 0)
        {
            player.accCritterGuideCounter = 15;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && npc.rarity > highestRarity && Vector2.Distance(npc.Center, player.Center) < searchRadius)
                {
                    npcIndex = i;
                    highestRarity = npc.rarity;
                }
            }

            player.accCritterGuideNumber = (byte)npcIndex;
        }
        else
        {
            player.accCritterGuideCounter--;
            npcIndex = player.accCritterGuideNumber;
        }

        if (npcIndex >= 0 && npcIndex < Main.maxNPCs)
        {
            NPC npc = Main.npc[npcIndex];
            if (npc.active && npc.rarity > 0)
            {
                return npc.GivenOrTypeName;
            }
        }

        return Language.GetTextValue("GameUI.NoRareCreatures");
    }

    private static string BuildRadarValue(Player player)
    {
        const int searchRadius = 2000;

        if (player.accThirdEyeCounter == 0)
        {
            player.accThirdEyeNumber = 0;
            player.accThirdEyeCounter = 15;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && !npc.friendly && npc.damage > 0 && npc.lifeMax > 5 && !npc.dontCountMe &&
                    Vector2.Distance(npc.Center, player.Center) < searchRadius)
                {
                    player.accThirdEyeNumber++;
                }
            }
        }
        else
        {
            player.accThirdEyeCounter--;
        }

        return player.accThirdEyeNumber switch
        {
            0 => Language.GetTextValue("GameUI.NoEnemiesNearby"),
            1 => Language.GetTextValue("GameUI.OneEnemyNearby"),
            _ => Language.GetTextValue("GameUI.EnemiesNearby", player.accThirdEyeNumber),
        };
    }

    private static string BuildTallyCounterValue(Player player)
    {
        int lastCreatureHit = player.lastCreatureHit;
        if (lastCreatureHit <= 0)
        {
            return Language.GetTextValue("GameUI.NoKillCount");
        }

        return $"{Lang.GetNPCNameValue(Item.BannerToNPC(lastCreatureHit))}: {NPC.killCount[lastCreatureHit]}";
    }

    private static string BuildDpsMeterValue(Player player)
    {
        player.checkDPSTime();
        int dps = player.getDPS();
        return dps == 0
            ? Language.GetTextValue("GameUI.NoDPS")
            : Language.GetTextValue("GameUI.DPS", dps);
    }

    private static string BuildStopwatchValue(Player player)
    {
        Vector2 velocity = player.velocity + player.instantMovementAccumulatedThisFrame;
        if (player.mount.Active && player.mount.IsConsideredASlimeMount && player.velocity.Y != 0f && !player.SlimeDontHyperJump)
        {
            velocity.Y += player.velocity.Y;
        }

        int sampleCount = (int)(1f + velocity.Length() * 6f);
        if (sampleCount > player.speedSlice.Length)
        {
            sampleCount = player.speedSlice.Length;
        }

        for (int i = sampleCount - 1; i > 0; i--)
        {
            player.speedSlice[i] = player.speedSlice[i - 1];
        }

        player.speedSlice[0] = velocity.Length();
        float averageSpeed = 0f;
        for (int i = 0; i < player.speedSlice.Length; i++)
        {
            if (i < sampleCount)
            {
                averageSpeed += player.speedSlice[i];
            }
            else
            {
                player.speedSlice[i] = averageSpeed / sampleCount;
            }
        }

        averageSpeed /= sampleCount;
        float speed = averageSpeed * 216000f / 42240f;

        if (!player.merman && !player.ignoreWater)
        {
            if (player.honeyWet)
            {
                speed /= 4f;
            }
            else if (player.shimmerWet)
            {
                speed *= 0.375f;
            }
            else if (player.wet && !player.trident)
            {
                speed /= 2f;
            }
        }

        return Language.GetTextValue("GameUI.Speed", Math.Round(speed));
    }

    private sealed record InfoDisplayDefinition(InfoDisplay Display, string FallbackLabel, Func<Player, bool> IsAvailable);
}
