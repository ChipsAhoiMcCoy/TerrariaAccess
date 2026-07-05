#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;
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

    private const int PillarSolar = 0;
    private const int PillarVortex = 1;
    private const int PillarNebula = 2;
    private const int PillarStardust = 3;
    private const int PillarCount = 4;
    private const float FocusedPillarMaxDistanceSquared = 1800f * 1800f;

    private static readonly int[] DescendingThresholds =
    {
        100, 95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 10,
        5, 4, 3, 2, 1,
    };

    private static readonly LunarPillarInfo[] LunarPillars =
    {
        new(PillarSolar, NPCID.LunarTowerSolar, "Solar"),
        new(PillarVortex, NPCID.LunarTowerVortex, "Vortex"),
        new(PillarNebula, NPCID.LunarTowerNebula, "Nebula"),
        new(PillarStardust, NPCID.LunarTowerStardust, "Stardust"),
    };

    private static int _lastEventKind;
    private static int _lastWave;
    private static int _lastAnnouncedThreshold = int.MaxValue;
    private static readonly int[] LastPillarShieldThresholds = new int[PillarCount];
    private static readonly bool[] LastPillarActive = new bool[PillarCount];
    private static readonly bool[] LastPillarShielded = new bool[PillarCount];
    private static bool _lastLunarPillarsActive;
    private static int _lastFocusedPillar = -1;

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

        if (TryAnnounceCurrentLunarPillar())
        {
            return;
        }

        if (TryAnnounceCurrentMoonLordCountdown())
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
        ResetLunarTracking();
    }

    private static void TickAutomaticAnnouncements()
    {
        if (TickLunarPillarAnnouncements())
        {
            return;
        }

        if (NPC.MoonLordCountdown > 0)
        {
            return;
        }

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

    private static bool TickLunarPillarAnnouncements()
    {
        bool active = IsLunarPillarEventActive();
        if (!active)
        {
            ResetLunarTracking();
            return false;
        }

        if (!_lastLunarPillarsActive)
        {
            _lastLunarPillarsActive = true;
            InitializeLunarTrackingSnapshot();
            Announce(LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.Started",
                "Celestial pillars active. Pillar shields are up."));
            return true;
        }

        TickFocusedPillarAnnouncement();
        TickPillarShieldAnnouncements();
        return true;
    }

    private static void TickFocusedPillarAnnouncement()
    {
        if (!TryGetFocusedPillar(out int pillarIndex))
        {
            _lastFocusedPillar = -1;
            return;
        }

        if (pillarIndex == _lastFocusedPillar)
        {
            return;
        }

        _lastFocusedPillar = pillarIndex;
        Announce(BuildPillarStatusMessage(pillarIndex));
    }

    private static void TickPillarShieldAnnouncements()
    {
        foreach (LunarPillarInfo pillar in LunarPillars)
        {
            int index = pillar.Index;
            bool active = IsPillarActive(index);
            int shield = GetPillarShieldStrength(index);
            bool shielded = active && shield > 0;

            if (!active)
            {
                LastPillarActive[index] = false;
                LastPillarShielded[index] = false;
                LastPillarShieldThresholds[index] = int.MaxValue;
                continue;
            }

            if (!LastPillarActive[index])
            {
                LastPillarActive[index] = true;
                LastPillarShielded[index] = shielded;
                LastPillarShieldThresholds[index] = GetPillarShieldPercent(shield);
                continue;
            }

            if (!shielded && LastPillarShielded[index])
            {
                LastPillarShielded[index] = false;
                LastPillarShieldThresholds[index] = 0;
                Announce(BuildPillarShieldDownMessage(index));
                PlayPillarAttackableCue();
                continue;
            }

            if (!shielded)
            {
                LastPillarShielded[index] = false;
                continue;
            }

            int remainingPercent = GetPillarShieldPercent(shield);
            int threshold = FindCrossedThreshold(remainingPercent, LastPillarShieldThresholds[index]);
            if (threshold >= 0)
            {
                LastPillarShieldThresholds[index] = threshold;
                AnnouncePillarThresholdCrossing(index, threshold);
            }

            LastPillarShielded[index] = true;
        }
    }

    private static bool TryAnnounceCurrentLunarPillar()
    {
        if (!IsLunarPillarEventActive())
        {
            return false;
        }

        if (TryGetFocusedPillar(out int pillarIndex))
        {
            Announce(BuildPillarStatusMessage(pillarIndex));
            SyncPillarTracking(pillarIndex);
            return true;
        }

        Announce(BuildLunarSummaryMessage());
        InitializeLunarTrackingSnapshot();
        return true;
    }

    private static bool TryAnnounceCurrentMoonLordCountdown()
    {
        if (NPC.MoonLordCountdown <= 0)
        {
            return false;
        }

        int seconds = Math.Max(1, (int)Math.Ceiling(NPC.MoonLordCountdown / 60.0));
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.MoonLordCountdown",
            "Moon Lord awakening in {0} seconds.");
        Announce(string.Format(fmt, seconds));
        return true;
    }

    private static void AnnouncePillarThresholdCrossing(int pillarIndex, int threshold)
    {
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.ShieldRemaining",
            "{0} pillar shield {1} percent remaining.");
        Announce(string.Format(fmt, GetPillarDisplayName(pillarIndex), threshold));
    }

    private static string BuildPillarStatusMessage(int pillarIndex)
    {
        if (!IsPillarActive(pillarIndex))
        {
            string defeatedFmt = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.Defeated",
                "{0} pillar defeated.");
            return string.Format(defeatedFmt, GetPillarDisplayName(pillarIndex));
        }

        int shield = GetPillarShieldStrength(pillarIndex);
        if (shield <= 0)
        {
            return BuildPillarShieldDownMessage(pillarIndex);
        }

        int max = Math.Max(1, NPC.ShieldStrengthTowerMax);
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.ShieldStatus",
            "{0} pillar shield up. {1} percent remaining. Shield strength {2} of {3}.");
        return string.Format(fmt, GetPillarDisplayName(pillarIndex), GetPillarShieldPercent(shield), shield, max);
    }

    private static string BuildPillarShieldDownMessage(int pillarIndex)
    {
        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.ShieldDown",
            "{0} pillar shield down. Pillar is attackable.");
        return string.Format(fmt, GetPillarDisplayName(pillarIndex));
    }

    private static string BuildLunarSummaryMessage()
    {
        string[] parts = LunarPillars
            .Select(pillar => BuildPillarSummaryPart(pillar.Index))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        string status = parts.Length > 0
            ? string.Join(", ", parts)
            : LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.NoActivePillars",
                "no active pillars");

        string fmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.Summary",
            "Celestial pillars active. {0}.");
        return string.Format(fmt, status);
    }

    private static string BuildPillarSummaryPart(int pillarIndex)
    {
        string name = GetPillarDisplayName(pillarIndex);
        if (!IsPillarActive(pillarIndex))
        {
            string defeatedFmt = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.SummaryDefeated",
                "{0} defeated");
            return string.Format(defeatedFmt, name);
        }

        int shield = GetPillarShieldStrength(pillarIndex);
        if (shield <= 0)
        {
            string downFmt = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.SummaryAttackable",
                "{0} attackable");
            return string.Format(downFmt, name);
        }

        string shieldFmt = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.SummaryShielded",
            "{0} shield {1} percent");
        return string.Format(shieldFmt, name, GetPillarShieldPercent(shield));
    }

    private static void InitializeLunarTrackingSnapshot()
    {
        foreach (LunarPillarInfo pillar in LunarPillars)
        {
            SyncPillarTracking(pillar.Index);
        }
    }

    private static void SyncPillarTracking(int pillarIndex)
    {
        bool active = IsPillarActive(pillarIndex);
        int shield = GetPillarShieldStrength(pillarIndex);
        LastPillarActive[pillarIndex] = active;
        LastPillarShielded[pillarIndex] = active && shield > 0;
        LastPillarShieldThresholds[pillarIndex] = active
            ? GetPillarShieldPercent(shield)
            : int.MaxValue;
    }

    private static void ResetLunarTracking()
    {
        _lastLunarPillarsActive = false;
        _lastFocusedPillar = -1;

        for (int i = 0; i < PillarCount; i++)
        {
            LastPillarActive[i] = false;
            LastPillarShielded[i] = false;
            LastPillarShieldThresholds[i] = int.MaxValue;
        }
    }

    private static bool IsLunarPillarEventActive()
    {
        if (NPC.LunarApocalypseIsUp)
        {
            return true;
        }

        foreach (LunarPillarInfo pillar in LunarPillars)
        {
            if (IsPillarActive(pillar.Index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPillarActive(int pillarIndex)
    {
        return pillarIndex switch
        {
            PillarSolar => NPC.TowerActiveSolar || AnyActiveNpc(NPCID.LunarTowerSolar),
            PillarVortex => NPC.TowerActiveVortex || AnyActiveNpc(NPCID.LunarTowerVortex),
            PillarNebula => NPC.TowerActiveNebula || AnyActiveNpc(NPCID.LunarTowerNebula),
            PillarStardust => NPC.TowerActiveStardust || AnyActiveNpc(NPCID.LunarTowerStardust),
            _ => false,
        };
    }

    private static bool AnyActiveNpc(int npcType)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.type == npcType)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetPillarShieldStrength(int pillarIndex)
    {
        int shield = pillarIndex switch
        {
            PillarSolar => NPC.ShieldStrengthTowerSolar,
            PillarVortex => NPC.ShieldStrengthTowerVortex,
            PillarNebula => NPC.ShieldStrengthTowerNebula,
            PillarStardust => NPC.ShieldStrengthTowerStardust,
            _ => 0,
        };

        return Math.Clamp(shield, 0, Math.Max(0, NPC.ShieldStrengthTowerMax));
    }

    private static int GetPillarShieldPercent(int shieldStrength)
    {
        int max = Math.Max(1, NPC.ShieldStrengthTowerMax);
        return Math.Clamp((int)Math.Round((double)Math.Clamp(shieldStrength, 0, max) / max * 100.0), 0, 100);
    }

    private static bool TryGetFocusedPillar(out int pillarIndex)
    {
        pillarIndex = -1;
        if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers)
        {
            return false;
        }

        Player player = Main.player[Main.myPlayer];
        if (player is null || !player.active || player.dead)
        {
            return false;
        }

        if (player.ZoneTowerSolar && IsPillarActive(PillarSolar))
        {
            pillarIndex = PillarSolar;
            return true;
        }

        if (player.ZoneTowerVortex && IsPillarActive(PillarVortex))
        {
            pillarIndex = PillarVortex;
            return true;
        }

        if (player.ZoneTowerNebula && IsPillarActive(PillarNebula))
        {
            pillarIndex = PillarNebula;
            return true;
        }

        if (player.ZoneTowerStardust && IsPillarActive(PillarStardust))
        {
            pillarIndex = PillarStardust;
            return true;
        }

        float bestDistance = FocusedPillarMaxDistanceSquared;
        foreach (LunarPillarInfo pillar in LunarPillars)
        {
            if (!TryGetPillarCenter(pillar.NpcType, out Vector2 center))
            {
                continue;
            }

            float distance = Vector2.DistanceSquared(player.Center, center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                pillarIndex = pillar.Index;
            }
        }

        return pillarIndex >= 0;
    }

    private static bool TryGetPillarCenter(int npcType, out Vector2 center)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.type == npcType)
            {
                center = npc.Center;
                return true;
            }
        }

        center = Vector2.Zero;
        return false;
    }

    private static string GetPillarDisplayName(int pillarIndex)
    {
        string fallback = pillarIndex switch
        {
            PillarSolar => "Solar",
            PillarVortex => "Vortex",
            PillarNebula => "Nebula",
            PillarStardust => "Stardust",
            _ => "Celestial",
        };

        return LocalizationHelper.GetTextOrFallback(
            $"Mods.TerrariaAccess.WorldAnnouncements.EventProgress.LunarPillars.Names.{fallback}",
            fallback);
    }

    private static int FindCrossedThreshold(int remainingPercent, int lastAnnouncedThreshold)
    {
        foreach (int t in DescendingThresholds)
        {
            if (t < lastAnnouncedThreshold && remainingPercent <= t)
            {
                return t;
            }
        }

        return -1;
    }

    private static void PlayPillarAttackableCue()
    {
        try
        {
            float volume = MathHelper.Clamp((TerrariaAccessConfig.Instance?.GuidanceVolume ?? 1f) * 0.65f, 0f, 1f);
            InGameNarrationSystem.FootstepToneProvider.PlayCentered(880f, volume, useTriangleWave: false);
            InGameNarrationSystem.FootstepToneProvider.PlayCentered(1320f, volume * 0.7f, useTriangleWave: false);
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn($"[EventProgress] Pillar attackable cue failed: {ex.Message}");
        }
    }

    private static bool TryGetActiveEvent(out int eventKind, out int wave, out int remainingPercent)
    {
        eventKind = 0;
        wave = 0;
        remainingPercent = 0;

        if (Main.netMode == NetmodeID.MultiplayerClient &&
            TryGetSyncedActiveEvent(out eventKind, out wave, out remainingPercent))
        {
            return true;
        }

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

    private static bool TryGetSyncedActiveEvent(out int eventKind, out int wave, out int remainingPercent)
    {
        eventKind = Main.invasionProgressIcon;
        wave = Main.invasionProgressWave;
        remainingPercent = 0;

        if (!IsSyncedEventActive(eventKind))
        {
            return false;
        }

        int max = Main.invasionProgressMax;
        int done = Main.invasionProgress;
        if (max <= 0 || done < 0)
        {
            return false;
        }

        if (done > max)
        {
            done = max;
        }

        double remainingFraction = 1.0 - ((double)done / max);
        remainingPercent = Math.Clamp((int)Math.Round(remainingFraction * 100.0), 0, 100);
        return true;
    }

    private static bool IsSyncedEventActive(int eventKind)
    {
        return eventKind switch
        {
            EventKindFrostMoon => Main.snowMoon,
            EventKindPumpkinMoon => Main.pumpkinMoon,
            EventKindOldOnesArmy => DD2Event.Ongoing,
            EventKindGoblinArmy => Main.invasionType == eventKind - 3,
            EventKindFrostLegion => Main.invasionType == eventKind - 3,
            EventKindPirateInvasion => Main.invasionType == eventKind - 3,
            EventKindMartianMadness => Main.invasionType == eventKind - 3,
            _ => false,
        };
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

    private readonly record struct LunarPillarInfo(int Index, int NpcType, string NameKey);
}
