#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WorldEventNarrator
    {
        private sealed class InvasionMonitor
        {
            private readonly int _invasionId;
            private readonly string _approachingKey;
            private readonly string _approachingFallback;
            private readonly string _arrivedKey;
            private readonly string _arrivedFallback;
            private readonly string _defeatedKey;
            private readonly string _defeatedFallback;
            private readonly string _retreatedKey;
            private readonly string _retreatedFallback;

            private bool _approachAnnounced;
            private bool _arrivalAnnounced;
            private bool _wasActive;

            public InvasionMonitor(
                int invasionId,
                string approachingKey,
                string approachingFallback,
                string arrivedKey,
                string arrivedFallback,
                string defeatedKey,
                string defeatedFallback,
                string retreatedKey,
                string retreatedFallback)
            {
                _invasionId = invasionId;
                _approachingKey = approachingKey;
                _approachingFallback = approachingFallback;
                _arrivedKey = arrivedKey;
                _arrivedFallback = arrivedFallback;
                _defeatedKey = defeatedKey;
                _defeatedFallback = defeatedFallback;
                _retreatedKey = retreatedKey;
                _retreatedFallback = retreatedFallback;
            }

            public void Reset()
            {
                _approachAnnounced = false;
                _arrivalAnnounced = false;
                _wasActive = false;
            }

            public void InitializeFromWorld()
            {
                bool active = Main.invasionType == _invasionId;
                _approachAnnounced = active;
                _arrivalAnnounced = active && IsInvasionAtSpawn();
                _wasActive = active;
            }

            public void Update()
            {
                bool active = Main.invasionType == _invasionId;
                if (active)
                {
                    if (!_approachAnnounced)
                    {
                        AnnounceWorldEvent(_approachingKey, _approachingFallback);
                        _approachAnnounced = true;
                    }

                    bool atSpawn = IsInvasionAtSpawn();
                    if (atSpawn && !_arrivalAnnounced)
                    {
                        AnnounceWorldEvent(_arrivedKey, _arrivedFallback);
                        _arrivalAnnounced = true;
                    }

                    _wasActive = true;
                    return;
                }

                if (!_wasActive)
                {
                    return;
                }

                string key = _arrivalAnnounced ? _defeatedKey : _retreatedKey;
                string fallback = _arrivalAnnounced ? _defeatedFallback : _retreatedFallback;
                AnnounceWorldEvent(key, fallback);

                Reset();
            }
        }

        private readonly HashSet<int> _previousTownNpcTypes = new();
        private readonly HashSet<int> _currentTownNpcTypes = new();

        private readonly InvasionMonitor[] _invasionMonitors =
        {
            new InvasionMonitor(
                InvasionID.GoblinArmy,
                "Mods.ScreenReaderMod.WorldAnnouncements.GoblinArmyApproaching", "A goblin army is approaching.",
                "Mods.ScreenReaderMod.WorldAnnouncements.GoblinArmyArrived", "The goblin army has arrived.",
                "Mods.ScreenReaderMod.WorldAnnouncements.GoblinArmyDefeated", "The goblin army has been defeated.",
                "Mods.ScreenReaderMod.WorldAnnouncements.GoblinArmyRetreated", "The goblin army has departed."),
            new InvasionMonitor(
                InvasionID.PirateInvasion,
                "Mods.ScreenReaderMod.WorldAnnouncements.PirateArmyApproaching", "A pirate invasion is approaching.",
                "Mods.ScreenReaderMod.WorldAnnouncements.PirateArmyArrived", "The pirates have invaded!",
                "Mods.ScreenReaderMod.WorldAnnouncements.PirateArmyDefeated", "The pirates have been defeated.",
                "Mods.ScreenReaderMod.WorldAnnouncements.PirateArmyRetreated", "The pirates have retreated."),
            new InvasionMonitor(
                InvasionID.SnowLegion,
                "Mods.ScreenReaderMod.WorldAnnouncements.FrostLegionApproaching", "The Frost Legion is approaching.",
                "Mods.ScreenReaderMod.WorldAnnouncements.FrostLegionArrived", "The Frost Legion has arrived.",
                "Mods.ScreenReaderMod.WorldAnnouncements.FrostLegionDefeated", "The Frost Legion has been defeated.",
                "Mods.ScreenReaderMod.WorldAnnouncements.FrostLegionRetreated", "The Frost Legion has departed."),
            new InvasionMonitor(
                InvasionID.MartianMadness,
                "Mods.ScreenReaderMod.WorldAnnouncements.MartianMadnessApproaching", "Martians are approaching!",
                "Mods.ScreenReaderMod.WorldAnnouncements.MartianMadnessArrived", "Martians have landed!",
                "Mods.ScreenReaderMod.WorldAnnouncements.MartianMadnessDefeated", "The Martian invasion has been defeated.",
                "Mods.ScreenReaderMod.WorldAnnouncements.MartianMadnessRetreated", "The Martians have retreated."),
        };

        private bool _initializedTownNpcSnapshot;
        private bool _wasBloodMoon;
        private bool _wasEclipse;
        private bool _wasPumpkinMoon;
        private bool _wasFrostMoon;
        private bool _wasRain;
        private bool _wasSandstorm;
        private bool _wasSlimeRain;
        private bool _wasLanternNight;
        private bool _wasParty;
        private bool _wasDd2Event;

        public void Reset()
        {
            _previousTownNpcTypes.Clear();
            _currentTownNpcTypes.Clear();
            _initializedTownNpcSnapshot = false;
            _wasBloodMoon = false;
            _wasEclipse = false;
            _wasPumpkinMoon = false;
            _wasFrostMoon = false;
            _wasRain = false;
            _wasSandstorm = false;
            _wasSlimeRain = false;
            _wasLanternNight = false;
            _wasParty = false;
            _wasDd2Event = false;

            foreach (InvasionMonitor monitor in _invasionMonitors)
            {
                monitor.Reset();
            }
        }

        public void InitializeFromWorld()
        {
            Reset();

            _wasBloodMoon = Main.bloodMoon;
            _wasEclipse = Main.eclipse;
            _wasPumpkinMoon = Main.pumpkinMoon;
            _wasFrostMoon = Main.snowMoon;
            _wasRain = Main.raining;
            _wasSandstorm = Sandstorm.Happening;
            _wasSlimeRain = Main.slimeRain;
            _wasLanternNight = LanternNight.LanternsUp;
            _wasParty = BirthdayParty.PartyIsUp;
            _wasDd2Event = DD2Event.Ongoing;

            foreach (InvasionMonitor monitor in _invasionMonitors)
            {
                monitor.InitializeFromWorld();
            }

            SnapshotTownNpcTypes();
        }

        public void Update()
        {
            if (Main.dedServ || Main.gameMenu)
            {
                return;
            }

            EnsureTownNpcSnapshot();

            AnnounceSimpleEvent(ref _wasBloodMoon, Main.bloodMoon, "Mods.ScreenReaderMod.WorldAnnouncements.BloodMoonStart", "The Blood Moon is rising.", "Mods.ScreenReaderMod.WorldAnnouncements.BloodMoonEnd", "The Blood Moon has ended.");
            AnnounceSimpleEvent(ref _wasEclipse, Main.eclipse, "Mods.ScreenReaderMod.WorldAnnouncements.SolarEclipseStart", "A solar eclipse has begun.", "Mods.ScreenReaderMod.WorldAnnouncements.SolarEclipseEnd", "The solar eclipse has ended.");
            AnnounceSimpleEvent(ref _wasPumpkinMoon, Main.pumpkinMoon, "Mods.ScreenReaderMod.WorldAnnouncements.PumpkinMoonStart", "The Pumpkin Moon is rising.", "Mods.ScreenReaderMod.WorldAnnouncements.PumpkinMoonEnd", "The Pumpkin Moon has ended.");
            AnnounceSimpleEvent(ref _wasFrostMoon, Main.snowMoon, "Mods.ScreenReaderMod.WorldAnnouncements.FrostMoonStart", "The Frost Moon is rising.", "Mods.ScreenReaderMod.WorldAnnouncements.FrostMoonEnd", "The Frost Moon has ended.");
            AnnounceSimpleEvent(ref _wasRain, Main.raining, "Mods.ScreenReaderMod.WorldAnnouncements.RainStart", "It has started raining.", "Mods.ScreenReaderMod.WorldAnnouncements.RainEnd", "The rain has stopped.");
            AnnounceSimpleEvent(ref _wasSandstorm, Sandstorm.Happening, "Mods.ScreenReaderMod.WorldAnnouncements.SandstormStart", "A sandstorm has begun.", "Mods.ScreenReaderMod.WorldAnnouncements.SandstormEnd", "The sandstorm has ended.");
            AnnounceSimpleEvent(ref _wasSlimeRain, Main.slimeRain, "Mods.ScreenReaderMod.WorldAnnouncements.SlimeRainStart", "Slime is falling from the sky!", "Mods.ScreenReaderMod.WorldAnnouncements.SlimeRainEnd", "The slime rain has ended.");
            AnnounceSimpleEvent(ref _wasLanternNight, LanternNight.LanternsUp, "Mods.ScreenReaderMod.WorldAnnouncements.LanternNightStart", "Lantern Night has begun.", "Mods.ScreenReaderMod.WorldAnnouncements.LanternNightEnd", "Lantern Night has ended.");
            AnnounceSimpleEvent(ref _wasParty, BirthdayParty.PartyIsUp, "Mods.ScreenReaderMod.WorldAnnouncements.PartyStart", "It's a party!", "Mods.ScreenReaderMod.WorldAnnouncements.PartyEnd", "The party is over.");
            AnnounceSimpleEvent(ref _wasDd2Event, DD2Event.Ongoing, "Mods.ScreenReaderMod.WorldAnnouncements.OldOnesArmyStart", "The Old One's Army is advancing.", "Mods.ScreenReaderMod.WorldAnnouncements.OldOnesArmyEnd", "The Old One's Army has been defeated.");

            foreach (InvasionMonitor monitor in _invasionMonitors)
            {
                monitor.Update();
            }

            AnnounceTownNpcArrivals();
        }

        private void AnnounceTownNpcArrivals()
        {
            _currentTownNpcTypes.Clear();

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || !npc.townNPC)
                {
                    continue;
                }

                int type = npc.type;
                if (!_currentTownNpcTypes.Add(type))
                {
                    continue;
                }

                if (_previousTownNpcTypes.Contains(type))
                {
                    continue;
                }

                string npcName = npc.GivenOrTypeName;
                if (string.IsNullOrWhiteSpace(npcName))
                {
                    npcName = Lang.GetNPCNameValue(type);
                }

                if (string.IsNullOrWhiteSpace(npcName))
                {
                    npcName = $"NPC {type}";
                }

                npcName = TextSanitizer.Clean(npcName);

                string message = Language.GetTextValue("Mods.ScreenReaderMod.WorldAnnouncements.NpcArrived", npcName);
                if (string.IsNullOrWhiteSpace(message) || string.Equals(message, "Mods.ScreenReaderMod.WorldAnnouncements.NpcArrived", StringComparison.Ordinal))
                {
                    message = $"{npcName} has arrived.";
                }

                message = TextSanitizer.Clean(message);
                WorldAnnouncementService.Announce(message, force: true);
            }

            _previousTownNpcTypes.Clear();
            _previousTownNpcTypes.UnionWith(_currentTownNpcTypes);
        }

        private void EnsureTownNpcSnapshot()
        {
            if (_initializedTownNpcSnapshot)
            {
                return;
            }

            SnapshotTownNpcTypes();
        }

        private void SnapshotTownNpcTypes()
        {
            _previousTownNpcTypes.Clear();

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || !npc.townNPC)
                {
                    continue;
                }

                _previousTownNpcTypes.Add(npc.type);
            }

            _initializedTownNpcSnapshot = true;
        }

        private static void AnnounceSimpleEvent(ref bool previousState, bool currentState, string startKey, string startFallback, string endKey, string endFallback)
        {
            if (currentState && !previousState)
            {
                AnnounceWorldEvent(startKey, startFallback);
            }
            else if (!currentState && previousState)
            {
                AnnounceWorldEvent(endKey, endFallback);
            }

            previousState = currentState;
        }

        private static void AnnounceWorldEvent(string key, string fallback)
        {
            string message = LocalizationHelper.GetTextOrFallback(key, fallback);
            WorldAnnouncementService.Announce(message, force: true);
        }

        private static bool IsInvasionAtSpawn()
        {
            if (Main.invasionType <= 0)
            {
                return false;
            }

            double difference = Math.Abs(Main.invasionX - Main.spawnTileX);
            if (difference <= 1.0)
            {
                return true;
            }

            return Main.invasionProgress > 0;
        }
    }
}
