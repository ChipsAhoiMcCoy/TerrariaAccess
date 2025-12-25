#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;

namespace ScreenReaderMod.Common.Services;

internal static class ChatHistoryService
{
    private const int MaxEntries = 200;
    private const uint DuplicateWindowTicks = 2;
    private static readonly List<string> History = new(MaxEntries);
    private static string? _lastRecorded;
    private static uint _lastRecordedTick;

    internal static int Count => History.Count;
    internal static int LatestIndex => History.Count - 1;

    internal static void Record(string? message)
    {
        string sanitized = TextSanitizer.Clean(message ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        uint tick = Main.GameUpdateCount;
        if (!string.IsNullOrWhiteSpace(_lastRecorded) &&
            string.Equals(_lastRecorded, sanitized, StringComparison.Ordinal) &&
            tick - _lastRecordedTick <= DuplicateWindowTicks)
        {
            return;
        }

        History.Add(sanitized);
        _lastRecorded = sanitized;
        _lastRecordedTick = tick;
        if (History.Count > MaxEntries)
        {
            int overflow = History.Count - MaxEntries;
            History.RemoveRange(0, overflow);
        }
    }

    internal static string? GetMessage(int index)
    {
        if (index < 0 || index >= History.Count)
        {
            return null;
        }

        return History[index];
    }

    internal static void Reset()
    {
        History.Clear();
        _lastRecorded = null;
        _lastRecordedTick = 0;
    }
}
