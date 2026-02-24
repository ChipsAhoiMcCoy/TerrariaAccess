#nullable enable
using System;
using System.Globalization;
using Terraria;

namespace TerrariaAccess.Common.Systems.Inventory;

/// <summary>
/// Tracks recent narration announcements to prevent duplicate announcements.
/// </summary>
internal sealed class NarrationHistory
{
    private static readonly bool HistoryDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_HISTORY"));
    private readonly HistoryEntry?[] _lastCues = new HistoryEntry?[(int)NarrationKind.Count];

    /// <summary>
    /// Attempts to store a cue in history. Returns true if the cue is new (should be announced).
    /// </summary>
    public bool TryStore(in NarrationCue cue)
    {
        if (NarrationHistorySettings.IsDisabled)
        {
            _lastCues[(int)cue.Kind] = new HistoryEntry(cue, Main.GameUpdateCount);
            return true;
        }

        int index = (int)cue.Kind;
        HistoryEntry? previous = _lastCues[index];
        uint now = Main.GameUpdateCount;

        if (previous.HasValue &&
            previous.Value.Cue.Equals(cue) &&
            !NarrationHistorySettings.HasExpired(previous.Value.Frame, now))
        {
            return false;
        }

        if (HistoryDebugEnabled && previous.HasValue)
        {
            LogHistoryChange(previous.Value.Cue, cue);
        }

        _lastCues[index] = new HistoryEntry(cue, now);
        return true;
    }

    /// <summary>
    /// Resets history for a specific narration kind.
    /// </summary>
    public void Reset(NarrationKind kind)
    {
        _lastCues[(int)kind] = null;
    }

    /// <summary>
    /// Resets all history entries.
    /// </summary>
    public void ResetAll()
    {
        Array.Clear(_lastCues, 0, _lastCues.Length);
    }

    private static void LogHistoryChange(NarrationCue prev, NarrationCue cue)
    {
        string reason = "unknown";
        if (prev.Kind != cue.Kind) reason = $"Kind: {prev.Kind} vs {cue.Kind}";
        else if (prev.Message != cue.Message) reason = $"Message differs";
        else if (!prev.Identity.Equals(cue.Identity)) reason = $"Identity differs";
        else if (prev.Location != cue.Location) reason = $"Location differs";
        else if (prev.Tooltip != cue.Tooltip) reason = $"Tooltip differs";
        else if (prev.Details != cue.Details) reason = $"Details differs";
        else if (prev.SlotSignature != cue.SlotSignature) reason = $"SlotSignature differs";

        TerrariaAccess.Instance?.Logger.Info($"[HistoryDebug] New cue allowed - {reason}");
    }

    private readonly record struct HistoryEntry(NarrationCue Cue, uint Frame);
}

/// <summary>
/// Configuration for narration history behavior.
/// </summary>
internal static class NarrationHistorySettings
{
    private const string DisabledEnvVar = "SRM_NARRATION_HISTORY_DISABLED";
    private const string MaxAgeEnvVar = "SRM_NARRATION_HISTORY_MAX_AGE";

    public static readonly bool IsDisabled = ParseBool(DisabledEnvVar);
    public static readonly uint MaxAgeFrames = ParseUInt(MaxAgeEnvVar);

    public static bool HasExpired(uint storedFrame, uint currentFrame)
    {
        if (MaxAgeFrames == 0)
        {
            return false;
        }

        uint age = currentFrame >= storedFrame
            ? currentFrame - storedFrame
            : uint.MaxValue - storedFrame + currentFrame + 1;

        return age >= MaxAgeFrames;
    }

    private static bool ParseBool(string envVar)
    {
        string? value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static uint ParseUInt(string envVar)
    {
        string? value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
        {
            return parsed;
        }

        return 0;
    }
}
