#nullable enable
using System;
using System.Collections.Generic;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems;

internal static class StatusCheckSystem
{
    private const int CycleCooldownFrames = 90; // ~1.5 seconds at 60fps

    private static int _lastPressFrame;
    private static int _cycleIndex;

    // Sub-cycling state for stepping through individual buffs once _cycleIndex has reached the Buffs step.
    // _buffSubIndex == -1 means we have not yet entered the sub-cycle (next press announces "N buffs").
    // _focusedBuffSlot is the slot index (into player.buffType[]) that the cancel keybind should act on.
    private static int _buffSubIndex = -1;
    private static int[] _buffSnapshotSlots = Array.Empty<int>();
    private static int _focusedBuffSlot = -1;

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

    // Status items in order: Health, Mana, Armor, Biome, Time, Buffs
    private const int StatusItemCount = 6;

    internal static void AnnounceStatus(Player player)
    {
        int currentFrame = (int)Main.GameUpdateCount;
        int framesSinceLastPress = currentFrame - _lastPressFrame;

        if (framesSinceLastPress <= CycleCooldownFrames && _lastPressFrame > 0)
        {
            // Cycling mode: either advance the main cycle, or sub-step through individual buffs.
            if (_cycleIndex == BuffsCycleIndex && _buffSnapshotSlots.Length > 0)
            {
                AdvanceBuffSubCycle(player);
            }
            else
            {
                _cycleIndex = (_cycleIndex + 1) % StatusItemCount;
                if (_cycleIndex == BuffsCycleIndex)
                {
                    EnterBuffSubCycle(player);
                }
                else
                {
                    ResetBuffSubCycle();
                    string singleItem = GetStatusItem(player, _cycleIndex);
                    ScreenReaderService.Announce(singleItem, force: true);
                }
            }
        }
        else
        {
            // Full announcement mode: announce everything and reset cycle
            _cycleIndex = 0;
            ResetBuffSubCycle();
            string message = BuildFullStatusMessage(player);
            ScreenReaderService.Announce(message, force: true);
        }

        _lastPressFrame = currentFrame;
    }

    private const int BuffsCycleIndex = 5;

    private static void EnterBuffSubCycle(Player player)
    {
        _buffSnapshotSlots = CaptureBuffSnapshot(player);
        if (_buffSnapshotSlots.Length == 0)
        {
            _buffSubIndex = -1;
            _focusedBuffSlot = -1;
            ScreenReaderService.Announce("No buffs", force: true);
            return;
        }

        _buffSubIndex = -1;
        _focusedBuffSlot = -1;
        int count = _buffSnapshotSlots.Length;
        string countLabel = count == 1 ? "buff" : "buffs";
        ScreenReaderService.Announce($"{count} {countLabel}", force: true);
    }

    private static void AdvanceBuffSubCycle(Player player)
    {
        int nextSubIndex = _buffSubIndex + 1;
        if (nextSubIndex >= _buffSnapshotSlots.Length)
        {
            // Exhausted the snapshot — wrap back to Health and announce it.
            _cycleIndex = 0;
            ResetBuffSubCycle();
            string singleItem = GetStatusItem(player, _cycleIndex);
            ScreenReaderService.Announce(singleItem, force: true);
            return;
        }

        _buffSubIndex = nextSubIndex;
        AnnounceFocusedBuff(player);
    }

    private static void ResetBuffSubCycle()
    {
        _buffSubIndex = -1;
        _focusedBuffSlot = -1;
        _buffSnapshotSlots = Array.Empty<int>();
    }

    private static int[] CaptureBuffSnapshot(Player player)
    {
        List<int> slots = new();
        for (int i = 0; i < Player.MaxBuffs; i++)
        {
            int buffType = player.buffType[i];
            int buffTime = player.buffTime[i];
            if (buffType > 0 && buffTime > 0 && buffType != BuffID.MonsterBanner)
            {
                slots.Add(i);
            }
        }
        return slots.ToArray();
    }

    private static void AnnounceFocusedBuff(Player player)
    {
        int slot = _buffSnapshotSlots[_buffSubIndex];
        int buffType = player.buffType[slot];
        int buffTime = player.buffTime[slot];

        // The snapshotted buff may have expired or shifted while the user was reading.
        // Fall back to looking up by slot; if it no longer matches the captured buff, just announce by slot contents.
        if (buffType <= 0 || buffTime <= 0)
        {
            _focusedBuffSlot = -1;
            string position = $"Buff {_buffSubIndex + 1} of {_buffSnapshotSlots.Length}";
            ScreenReaderService.Announce($"{position}: expired", force: true);
            return;
        }

        _focusedBuffSlot = slot;

        string name = Lang.GetBuffName(buffType);
        if (string.IsNullOrEmpty(name))
        {
            name = $"Buff {buffType}";
        }

        string timeString = FormatBuffTime(buffTime);
        string cancelNote = IsBuffCancellable(player, buffType) ? string.Empty : ", cannot cancel";

        string prefix = $"Buff {_buffSubIndex + 1} of {_buffSnapshotSlots.Length}: {name}";
        string announcement = string.IsNullOrEmpty(timeString)
            ? $"{prefix}{cancelNote}"
            : $"{prefix}, {timeString}{cancelNote}";

        ScreenReaderService.Announce(announcement, force: true);
    }

    // Returns true when the Delete press was consumed by a focused buff (cancelled or reported as non-cancellable),
    // false when no buff is currently focused. The caller uses the return value to decide whether to fall through
    // to the waypoint-delete path.
    internal static bool TryCancelFocusedBuff(Player player)
    {
        if (_focusedBuffSlot < 0 || _focusedBuffSlot >= Player.MaxBuffs)
        {
            return false;
        }

        int slot = _focusedBuffSlot;
        int buffType = player.buffType[slot];
        int buffTime = player.buffTime[slot];
        if (buffType <= 0 || buffTime <= 0)
        {
            _focusedBuffSlot = -1;
            return false;
        }

        string name = Lang.GetBuffName(buffType);
        if (string.IsNullOrEmpty(name))
        {
            name = $"Buff {buffType}";
        }

        if (!IsBuffCancellable(player, buffType))
        {
            ScreenReaderService.Announce($"Cannot cancel {name}", force: true);
            return true;
        }

        player.DelBuff(slot);
        ScreenReaderService.Announce($"Cancelled {name}", force: true);

        // Force a fresh status read on the next Backspace.
        _cycleIndex = 0;
        ResetBuffSubCycle();
        _lastPressFrame = 0;
        return true;
    }

    private static bool IsBuffCancellable(Player player, int buffType)
    {
        if (buffType <= 0)
        {
            return false;
        }

        if (Main.debuff[buffType])
        {
            return false;
        }

        // Hardcoded non-cancellable buffs — matches Main.TryRemovingBuff.
        if (buffType == BuffID.LeafCrystal || buffType == BuffID.SoulDrain)
        {
            return false;
        }

        // Permanent/equipment buffs whose timer doesn't tick down — cancellation would be instantly re-applied.
        if (buffType < BuffID.Sets.TimeLeftDoesNotDecrease.Length
            && BuffID.Sets.TimeLeftDoesNotDecrease[buffType])
        {
            return false;
        }

        // Mount buffs: vanilla routes these to TryDismount rather than DelBuff. Keep the cancel key single-purpose
        // and mark mount buffs as non-cancellable; the user can dismount via normal movement.
        if (player.mount != null && player.mount.Active && player.mount.CheckBuff(buffType))
        {
            return false;
        }

        return true;
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
            5 => GetBuffStringDetailed(player),
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
        string buffs = GetBuffString(player);

        return $"{health}. {mana}. {armor}. {biome}. {time}. {buffs}.";
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

    private static string GetBuffString(Player player)
    {
        int buffCount = CountActiveBuffs(player);
        return buffCount == 1 ? "1 buff" : $"{buffCount} buffs";
    }

    private static string GetBuffStringDetailed(Player player)
    {
        List<string> buffNames = GetActiveBuffNames(player);

        if (buffNames.Count == 0)
        {
            return "No buffs";
        }

        string buffList = string.Join(", ", buffNames);
        string countLabel = buffNames.Count == 1 ? "buff" : "buffs";
        return $"{buffNames.Count} {countLabel}: {buffList}";
    }

    private static int CountActiveBuffs(Player player)
    {
        int count = 0;
        for (int i = 0; i < Player.MaxBuffs; i++)
        {
            int buffType = player.buffType[i];
            if (buffType > 0 && player.buffTime[i] > 0 && buffType != BuffID.MonsterBanner)
            {
                count++;
            }
        }
        return count;
    }

    private static List<string> GetActiveBuffNames(Player player)
    {
        List<string> names = new();
        for (int i = 0; i < Player.MaxBuffs; i++)
        {
            int buffType = player.buffType[i];
            int buffTime = player.buffTime[i];
            if (buffType > 0 && buffTime > 0 && buffType != BuffID.MonsterBanner)
            {
                string buffName = Lang.GetBuffName(buffType);
                if (!string.IsNullOrEmpty(buffName))
                {
                    string timeString = FormatBuffTime(buffTime);
                    if (!string.IsNullOrEmpty(timeString))
                    {
                        names.Add($"{buffName} {timeString}");
                    }
                    else
                    {
                        names.Add(buffName);
                    }
                }
            }
        }
        return names;
    }

    private static string FormatBuffTime(int buffTimeInTicks)
    {
        // Infinite buffs have very high tick values or special handling
        // Terraria uses int.MaxValue or similar for infinite buffs
        if (buffTimeInTicks <= 0 || buffTimeInTicks >= 3600 * 60 * 24) // More than 24 hours = effectively infinite
        {
            return string.Empty;
        }

        int totalSeconds = buffTimeInTicks / 60; // 60 ticks per second
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        if (minutes > 0 && seconds > 0)
        {
            string minLabel = minutes == 1 ? "minute" : "minutes";
            string secLabel = seconds == 1 ? "second" : "seconds";
            return $"{minutes} {minLabel} {seconds} {secLabel}";
        }
        else if (minutes > 0)
        {
            string minLabel = minutes == 1 ? "minute" : "minutes";
            return $"{minutes} {minLabel}";
        }
        else if (seconds > 0)
        {
            string secLabel = seconds == 1 ? "second" : "seconds";
            return $"{seconds} {secLabel}";
        }

        return string.Empty;
    }

    private static string DetermineBiomeName(Player player)
    {
        foreach (BiomeDefinition biome in OrderedBiomes)
        {
            if (biome.Predicate(player))
            {
                return LocalizationHelper.GetTextOrFallback(
                    $"Mods.TerrariaAccess.WorldAnnouncements.BiomeNames.{biome.Key}",
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

        if (hours24 < 4.5)
        {
            return "Late night";
        }
        if (hours24 < 7.0)
        {
            return "Dawn";
        }
        if (hours24 < 11.0)
        {
            return "Morning";
        }
        if (hours24 < 13.0)
        {
            return "Noon";
        }
        if (hours24 < 17.0)
        {
            return "Afternoon";
        }
        if (hours24 < 19.5)
        {
            return "Evening";
        }
        if (hours24 < 23.0)
        {
            return "Night";
        }
        return "Midnight";
    }

    private sealed record BiomeDefinition(string Key, string FallbackName, Func<Player, bool> Predicate);
}
