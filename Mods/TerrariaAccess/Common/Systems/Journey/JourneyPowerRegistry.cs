#nullable enable
using System.Collections.Generic;

namespace TerrariaAccess.Common.Systems.Journey;

internal enum JourneyPowerKind
{
    Toggle,
    Slider,
    OneShot,
    Shared,
}

internal readonly record struct JourneyPowerEntry(
    string Key,
    JourneyPowerKind Kind,
    string LocSuffix,
    string FallbackLabel);

internal static class JourneyPowerRegistry
{
    public static readonly IReadOnlyList<JourneyPowerEntry> All = new JourneyPowerEntry[]
    {
        new("godmode", JourneyPowerKind.Toggle, "Godmode", "Godmode"),
        new("time_setfrozen", JourneyPowerKind.Shared, "FreezeTime", "Freeze time"),
        new("wind_setfrozen", JourneyPowerKind.Shared, "FreezeWind", "Freeze wind"),
        new("rain_setfrozen", JourneyPowerKind.Shared, "FreezeRain", "Freeze rain"),
        new("increaseplacementrange", JourneyPowerKind.Toggle, "PlacementRange", "Increased placement range"),
        new("stopbiomespread", JourneyPowerKind.Shared, "StopBiomeSpread", "Stop biome spread"),
        new("time_setspeed", JourneyPowerKind.Slider, "TimeRate", "Time speed"),
        new("wind_setstrength", JourneyPowerKind.Slider, "WindStrength", "Wind strength"),
        new("rain_setstrength", JourneyPowerKind.Slider, "RainStrength", "Rain strength"),
        new("setspawnrate", JourneyPowerKind.Slider, "SpawnRate", "NPC spawn rate"),
        new("setdifficulty", JourneyPowerKind.Slider, "Difficulty", "Enemy difficulty"),
        new("time_setdawn", JourneyPowerKind.OneShot, "SetDawn", "Set time to dawn"),
        new("time_setnoon", JourneyPowerKind.OneShot, "SetNoon", "Set time to noon"),
        new("time_setdusk", JourneyPowerKind.OneShot, "SetDusk", "Set time to dusk"),
        new("time_setmidnight", JourneyPowerKind.OneShot, "SetMidnight", "Set time to midnight"),
    };

    public static bool TryFind(string key, out JourneyPowerEntry entry)
    {
        foreach (JourneyPowerEntry e in All)
        {
            if (e.Key == key)
            {
                entry = e;
                return true;
            }
        }

        entry = default;
        return false;
    }
}
