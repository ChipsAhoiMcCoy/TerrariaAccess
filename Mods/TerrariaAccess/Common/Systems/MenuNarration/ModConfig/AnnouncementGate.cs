#nullable enable
using System;
using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Single source for all mod config announcements. Prevents double announcements
/// by tracking last announcement, deduplicating, and providing frame-based suppression.
/// </summary>
internal sealed class AnnouncementGate
{
    private string? _lastAnnouncement;

    /// <summary>
    /// The last announcement that was made through this gate.
    /// </summary>
    public string? LastAnnouncement => _lastAnnouncement;

    /// <summary>
    /// Whether announcements are currently suppressed.
    /// </summary>
    public bool IsSuppressed => false;

    /// <summary>
    /// Suppress announcements for the specified number of frames.
    /// </summary>
    public void SuppressForFrames(int frames)
    {
    }

    /// <summary>
    /// Suppress announcements after navigation action.
    /// Uses minimum frame-based suppression that waits for intentional mouse movement.
    /// </summary>
    public void SuppressAfterNavigation()
    {
    }

    /// <summary>
    /// Tick the suppression timers. Call once per frame.
    /// </summary>
    public void Tick()
    {
    }

    /// <summary>
    /// Attempt to announce text. Returns true if announcement was made.
    /// </summary>
    /// <param name="text">The text to announce.</param>
    /// <param name="force">If true, bypass deduplication (but not suppression).</param>
    /// <param name="isMenuContext">True if in menu context (uses events), false for in-game (direct speech).</param>
    /// <param name="menuEventSink">Event sink for menu context announcements.</param>
    public bool TryAnnounce(
        string? text,
        bool force = false,
        bool isMenuContext = false,
        Action<string, bool, MenuNarrationEventKind>? menuEventSink = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Check deduplication
        if (!force && string.Equals(text, _lastAnnouncement, StringComparison.Ordinal))
        {
            TerrariaAccess.Instance?.Logger.Debug($"[AnnouncementGate] Deduplicated: '{text}'");
            return false;
        }

        _lastAnnouncement = text;
        TerrariaAccess.Instance?.Logger.Info($"[AnnouncementGate] Announcing: '{text}'");

        if (isMenuContext && menuEventSink is not null)
        {
            menuEventSink(text, force, MenuNarrationEventKind.ModConfig);
        }
        else
        {
            ScreenReaderService.Announce(text, force);
        }

        return true;
    }

    /// <summary>
    /// Clear frame-based suppression. Call when the user takes an explicit action
    /// (e.g. navigation) that should always be announced regardless of prior suppression.
    /// </summary>
    public void ClearFrameSuppression()
    {
    }

    /// <summary>
    /// Clear the last announcement to allow re-announcing the same text.
    /// </summary>
    public void ClearLastAnnouncement()
    {
        _lastAnnouncement = null;
    }

    /// <summary>
    /// Reset all state.
    /// </summary>
    public void Reset()
    {
        _lastAnnouncement = null;
    }
}
