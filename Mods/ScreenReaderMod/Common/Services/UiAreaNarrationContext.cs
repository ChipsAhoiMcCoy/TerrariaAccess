#nullable enable
using System;
using Terraria;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Services;

[Flags]
internal enum UiNarrationArea
{
    Unknown = 0,
    Inventory = 1 << 0,
    Storage = 1 << 1,
    Crafting = 1 << 2,
    Guide = 1 << 3,
    Reforge = 1 << 4,
    Creative = 1 << 5,
    Shop = 1 << 6,
    Dialogue = 1 << 7,
    Settings = 1 << 8,
}

internal static class UiAreaNarrationContext
{
    private const uint MaxInactiveFrames = 30;

    private static UiNarrationArea _activeArea = UiNarrationArea.Unknown;
    private static uint _lastUpdateFrame;

    public static UiNarrationArea ActiveArea
    {
        get
        {
            TrimIfStale();
            return _activeArea;
        }
    }

    public static void RecordSlotContext(int context)
    {
        UiNarrationArea area = ItemSlotContextFacts.ResolveArea(context);
        RecordArea(area);
    }

    public static void RecordArea(UiNarrationArea area)
    {
        if (area == UiNarrationArea.Unknown)
        {
            return;
        }

        _activeArea = area;
        _lastUpdateFrame = Main.GameUpdateCount;
    }

    public static bool IsActiveArea(UiNarrationArea allowedAreas)
    {
        TrimIfStale();
        if (_activeArea == UiNarrationArea.Unknown || allowedAreas == UiNarrationArea.Unknown)
        {
            return true;
        }

        return (_activeArea & allowedAreas) != 0;
    }

    public static void Clear()
    {
        _activeArea = UiNarrationArea.Unknown;
        _lastUpdateFrame = 0;
    }

    private static void TrimIfStale()
    {
        if (_activeArea == UiNarrationArea.Unknown || _lastUpdateFrame == 0)
        {
            return;
        }

        uint current = Main.GameUpdateCount;
        uint frame = _lastUpdateFrame;
        uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
        if (age > MaxInactiveFrames)
        {
            _activeArea = UiNarrationArea.Unknown;
            _lastUpdateFrame = 0;
        }
    }
}
