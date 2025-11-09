#nullable enable
using System;
using System.Globalization;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class BiomeAnnouncementEmitter
    {
        private const int StableFramesRequired = 30;

        private readonly BiomeDefinition[] _orderedBiomes =
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

        private string? _lastAnnouncedKey;
        private string? _candidateKey;
        private int _candidateFrames;

        public void Reset()
        {
            _lastAnnouncedKey = null;
            _candidateKey = null;
            _candidateFrames = 0;
        }

        public void Update(Player player)
        {
            if (player is null || !player.active || player.dead || player.ghost)
            {
                return;
            }

            BiomeDefinition? current = DetermineBiome(player);
            if (current is null)
            {
                _candidateKey = null;
                _candidateFrames = 0;
                return;
            }

            if (_candidateKey == current.Key)
            {
                if (_candidateFrames < StableFramesRequired)
                {
                    _candidateFrames++;
                }
            }
            else
            {
                _candidateKey = current.Key;
                _candidateFrames = 1;
            }

            if (_candidateKey == _lastAnnouncedKey || _candidateFrames < StableFramesRequired)
            {
                return;
            }

            _lastAnnouncedKey = _candidateKey;
            AnnounceBiome(current);
        }

        private BiomeDefinition? DetermineBiome(Player player)
        {
            foreach (BiomeDefinition biome in _orderedBiomes)
            {
                if (biome.Predicate(player))
                {
                    return biome;
                }
            }

            return null;
        }

        private static void AnnounceBiome(BiomeDefinition biome)
        {
            string name = LocalizationHelper.GetTextOrFallback(
                $"Mods.ScreenReaderMod.WorldAnnouncements.BiomeNames.{biome.Key}",
                biome.FallbackName);

            string template = LocalizationHelper.GetTextOrFallback(
                "Mods.ScreenReaderMod.WorldAnnouncements.BiomeEntered",
                "Entered {0}.");

            string message = string.Format(CultureInfo.CurrentCulture, template, name);
            WorldAnnouncementService.Announce(message, force: true);
        }

        private sealed record BiomeDefinition(string Key, string FallbackName, Func<Player, bool> Predicate);
    }
}
