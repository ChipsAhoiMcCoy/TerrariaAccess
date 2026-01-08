#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

internal static class StatusCheckSystem
{
    private const int CycleCooldownFrames = 90; // ~1.5 seconds at 60fps

    private static int _lastPressFrame;
    private static int _cycleIndex;

    private static readonly BiomeDefinition[] OrderedBiomes =
    {
        new("Underworld", "Underworld", static player => player.ZoneUnderworldHeight),
        new("Temple", "Jungle Temple", static player => player.ZoneLihzhardTemple),
        new("Dungeon", "Dungeon", static player => player.ZoneDungeon),
        new("Sky", "Sky", static player => player.ZoneSkyHeight),
        new("Shimmer", "Shimmer", static player => player.ZoneShimmer),
        new("Jungle", "Jungle", static player => player.ZoneJungle),
        new("UndergroundDesert", "Underground Desert", static player => player.ZoneUndergroundDesert),
        new("Desert", "Desert", static player => player.ZoneDesert),
        new("Snow", "Snow", static player => player.ZoneSnow),
        new("Hallow", "Hallow", static player => player.ZoneHallow),
        new("Corruption", "Corruption", static player => player.ZoneCorrupt),
        new("Crimson", "Crimson", static player => player.ZoneCrimson),
        new("Glowshroom", "Glowing Mushroom", static player => player.ZoneGlowshroom),
        new("Granite", "Granite Cave", static player => player.ZoneGranite),
        new("Marble", "Marble Cave", static player => player.ZoneMarble),
        new("Meteor", "Meteorite", static player => player.ZoneMeteor),
        new("Hive", "Bee Hive", static player => player.ZoneHive),
        new("Graveyard", "Graveyard", static player => player.ZoneGraveyard),
        new("Beach", "Beach", static player => player.ZoneBeach),
        new("Forest", "Forest", static player => player.ZonePurity && player.ZoneOverworldHeight),
        new("CavernLayer", "Cavern Layer", static player => player.ZoneRockLayerHeight),
        new("Underground", "Underground", static player => player.ZoneDirtLayerHeight),
    };

    // Status items in order: Health, Mana, Armor, Biome, Time
    private const int StatusItemCount = 5;

    internal static void AnnounceStatus(Player player)
    {
        int currentFrame = (int)Main.GameUpdateCount;
        int framesSinceLastPress = currentFrame - _lastPressFrame;

        if (framesSinceLastPress <= CycleCooldownFrames && _lastPressFrame > 0)
        {
            // Cycling mode: announce just the next item
            _cycleIndex = (_cycleIndex + 1) % StatusItemCount;
            string singleItem = GetStatusItem(player, _cycleIndex);
            ScreenReaderService.Announce(singleItem, force: true);
        }
        else
        {
            // Full announcement mode: announce everything and reset cycle
            _cycleIndex = 0;
            string message = BuildFullStatusMessage(player);
            ScreenReaderService.Announce(message, force: true);
        }

        _lastPressFrame = currentFrame;
    }

    private static string GetStatusItem(Player player, int index)
    {
        return index switch
        {
            0 => GetHealthString(player),
            1 => GetManaString(player),
            2 => GetArmorString(player),
            3 => GetBiomeString(player),
            4 => GetTimeString(),
            _ => string.Empty,
        };
    }

    private static string BuildFullStatusMessage(Player player)
    {
        string health = GetHealthString(player);
        string mana = GetManaString(player);
        string armor = GetArmorString(player);
        string biome = GetBiomeString(player);
        string time = GetTimeString();

        return $"{health}. {mana}. {armor}. {biome}. {time}.";
    }

    private static string GetHealthString(Player player)
    {
        int healthCurrent = Math.Max(0, player.statLife);
        int healthMax = Math.Max(1, player.statLifeMax2);
        return $"Health {healthCurrent} of {healthMax}";
    }

    private static string GetManaString(Player player)
    {
        int manaMax = Math.Max(0, player.statManaMax2);
        return manaMax > 0
            ? $"Mana {Math.Max(0, Math.Min(player.statMana, manaMax))} of {manaMax}"
            : "Mana none";
    }

    private static string GetArmorString(Player player)
    {
        int defense = Math.Max(0, player.statDefense);
        return $"Defense {defense}";
    }

    private static string GetBiomeString(Player player)
    {
        string biomeName = DetermineBiomeName(player);
        return $"Biome: {biomeName}";
    }

    private static string GetTimeString()
    {
        string timeDesc = DescribeTime();
        return $"Time: {timeDesc}";
    }

    private static string DetermineBiomeName(Player player)
    {
        foreach (BiomeDefinition biome in OrderedBiomes)
        {
            if (biome.Predicate(player))
            {
                return LocalizationHelper.GetTextOrFallback(
                    $"Mods.ScreenReaderMod.WorldAnnouncements.BiomeNames.{biome.Key}",
                    biome.FallbackName);
            }
        }

        return "Unknown";
    }

    private static string DescribeTime()
    {
        double time = Main.time;
        double dayLength = Main.dayLength;
        double nightLength = Main.nightLength;
        double totalDay = dayLength + nightLength;

        if (!Main.dayTime)
        {
            time += dayLength;
        }

        double hours24 = (time / totalDay * 24.0) + 4.5;
        hours24 %= 24.0;

        int hours = (int)hours24;
        int minutes = (int)((hours24 - hours) * 60.0);

        string period = hours >= 12 ? "PM" : "AM";
        int displayHour = hours % 12;
        if (displayHour == 0)
        {
            displayHour = 12;
        }

        string phase = Main.dayTime ? "Daytime" : "Night";
        return $"{phase}, {displayHour}:{minutes:00} {period}";
    }

    private sealed record BiomeDefinition(string Key, string FallbackName, Func<Player, bool> Predicate);
}
