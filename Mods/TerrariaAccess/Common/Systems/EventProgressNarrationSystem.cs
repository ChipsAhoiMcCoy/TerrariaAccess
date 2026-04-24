#nullable enable
using System;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

public sealed class EventProgressNarrationSystem : ModSystem
{
    private const int EventKindFrostMoon = 1;
    private const int EventKindPumpkinMoon = 2;
    private const int EventKindOldOnesArmy = 3;
    private const int EventKindGoblinArmy = 4;
    private const int EventKindFrostLegion = 5;
    private const int EventKindPirateInvasion = 6;
    private const int EventKindMartianMadness = 7;

    private static readonly int[] DescendingThresholds =
    {
        100, 95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 10,
        5, 4, 3, 2, 1,
    };

    private static int _lastEventKind;
    private static int _lastWave;
    private static int _lastAnnouncedThreshold = int.MaxValue;

    public override void OnWorldLoad()
    {
        ResetTracking();
    }

    public override void OnWorldUnload()
    {
        ResetTracking();
    }

    public override void PostUpdateEverything()
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        TickAutomaticAnnouncements();
    }

    internal static void AnnounceCurrent()
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        if (!TryGetActiveEvent(out int eventKind, out int wave, out int remainingPercent))
        {
            ScreenReaderService.Announce(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.NoActiveEvent",
                    "No active events."),
                force: true,
                category: ScreenReaderService.AnnouncementCategory.World);
            return;
        }

        // Sync tracking so the automatic narrator does not immediately re-announce the same threshold.
        _lastEventKind = eventKind;
        _lastWave = wave;
        _lastAnnouncedThreshold = remainingPercent;

        AnnounceStatus(eventKind, wave, remainingPercent);
    }

    private static void ResetTracking()
    {
        _lastEventKind = 0;
        _lastWave = 0;
        _lastAnnouncedThreshold = int.MaxValue;
    }

    private static void TickAutomaticAnnouncements()
    {
        if (!TryGetActiveEvent(out int eventKind, out int wave, out int remainingPercent))
        {
            if (_lastEventKind != 0)
            {
                // Event ended. Vanilla/WorldAnnouncementService handles the "defeated"/"retreated" line.
                ResetTracking();
            }
            return;
        }

        if (eventKind != _lastEventKind)
        {
            _lastEventKind = eventKind;
            _lastWave = wave;
            _lastAnnouncedThreshold = remainingPercent;
            AnnounceStatus(eventKind, wave, remainingPercent);
            return;
        }

        if (wave != _lastWave && wave > 0)
        {
            _lastWave = wave;
            _lastAnnouncedThreshold = remainingPercent;
            AnnounceWaveChange(eventKind, wave, remainingPercent);
            return;
        }

        int threshold = FindCrossedThreshold(remainingPercent);
        if (threshold >= 0)
        {
            _lastAnnouncedThreshold = threshold;
            AnnounceThresholdCrossing(threshold);
        }
    }

    private static int FindCrossedThreshold(int remainingPercent)
    {
        foreach (int t in DescendingThresholds)
        {
            if (t < _lastAnnouncedThreshold && remainingPercent <= t)
            {
                return t;
            }
        }
        return -1;
    }

    private static void AnnounceStatus(int eventKind, int wave, int remainingPercent)
    {
        string eventName = GetEventName(eventKind);
        string message;
        if (wave > 0)
        {
            string fmt = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.StatusWithWave",
                "{0} wave {1}. {2} percent remaining.");
            message = string.Format(fmt, eventName, wave, remainingPercent);
        }
        else
        {
            string fmt = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.Status",
                "{0}. {1} percent remaining.");
            message = string.Format(fmt, eventName, remainingPercent);
        }

        Announce(message);
    }

    private static void AnnounceWaveChange(int eventKind, int wave, int remainingPercent)
    {
        string eventName = GetEventName(eventKind);
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.WaveChange",
            "{0} wave {1}. {2} percent remaining.");
        Announce(string.Format(fmt, eventName, wave, remainingPercent));
    }

    private static void AnnounceThresholdCrossing(int threshold)
    {
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.Remaining",
            "{0} percent remaining.");
        Announce(string.Format(fmt, threshold));
    }

    private static void Announce(string message)
    {
        ScreenReaderService.Announce(
            message,
            force: true,
            category: ScreenReaderService.AnnouncementCategory.World);
    }

    private static bool TryGetActiveEvent(out int eventKind, out int wave, out int remainingPercent)
    {
        eventKind = 0;
        wave = 0;
        remainingPercent = 0;

        int done;
        int max;

        if (Main.snowMoon)
        {
            eventKind = EventKindFrostMoon;
            wave = NPC.waveNumber;
            if (!TryGetMoonWaveProgress(wave, out done, out max))
            {
                return false;
            }
        }
        else if (Main.pumpkinMoon)
        {
            eventKind = EventKindPumpkinMoon;
            wave = NPC.waveNumber;
            if (!TryGetMoonWaveProgress(wave, out done, out max))
            {
                return false;
            }
        }
        else if (DD2Event.Ongoing)
        {
            eventKind = EventKindOldOnesArmy;
            wave = Main.invasionProgressWave;
            done = Math.Max(0, Main.invasionProgress);
            max = Main.invasionProgressMax;
            if (max <= 0)
            {
                return false;
            }
        }
        else if (Main.invasionType > 0 && Main.invasionSizeStart > 0)
        {
            eventKind = Main.invasionType + 3;
            wave = 0;
            done = Main.invasionSizeStart - Math.Max(0, Main.invasionSize);
            max = Main.invasionSizeStart;
        }
        else
        {
            return false;
        }

        if (max <= 0 || done < 0)
        {
            return false;
        }

        if (done > max)
        {
            done = max;
        }

        double remainingFraction = 1.0 - ((double)done / max);
        int pct = (int)Math.Round(remainingFraction * 100.0);
        if (pct < 0)
        {
            pct = 0;
        }
        else if (pct > 100)
        {
            pct = 100;
        }

        remainingPercent = pct;
        return true;
    }

    private static bool TryGetMoonWaveProgress(int waveNumber, out int done, out int max)
    {
        done = 0;
        max = 0;

        int[] lookup = NPC.MoonEventRequiredPointsPerWaveLookup;
        if (lookup == null || waveNumber < 0 || waveNumber >= lookup.Length)
        {
            return false;
        }

        max = lookup[waveNumber];
        if (max <= 0)
        {
            // Final sentinel wave (index 15 for pumpkin, 20 for frost) stores 0 — no progress bar then.
            return false;
        }

        done = Math.Max(0, (int)NPC.waveKills);
        return true;
    }

    private static string GetEventName(int eventKind)
    {
        return eventKind switch
        {
            EventKindFrostMoon => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.FrostMoon",
                "Frost Moon"),
            EventKindPumpkinMoon => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.PumpkinMoon",
                "Pumpkin Moon"),
            EventKindOldOnesArmy => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.OldOnesArmy",
                "Old One's Army"),
            EventKindGoblinArmy => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.GoblinArmy",
                "Goblin Army"),
            EventKindFrostLegion => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.FrostLegion",
                "Frost Legion"),
            EventKindPirateInvasion => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.PirateInvasion",
                "Pirate Invasion"),
            EventKindMartianMadness => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.MartianMadness",
                "Martian Madness"),
            _ => LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.EventNames.Unknown",
                "Event"),
        };
    }
}
