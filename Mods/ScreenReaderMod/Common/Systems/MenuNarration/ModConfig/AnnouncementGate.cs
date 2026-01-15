#nullable enable
using System;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Single source for all mod config announcements. Prevents double announcements
/// by tracking last announcement, deduplicating, and providing frame-based suppression.
/// </summary>
internal sealed class AnnouncementGate
{
    private string? _lastAnnouncement;
    private int _suppressFrames;
    private int _lastMouseX;
    private int _lastMouseY;
    private int _navigationSuppressionFrames;

    // Minimum frames to suppress after navigation (prevents Terraria's mouse reset from triggering false movement)
    private const int MinNavigationSuppressionFrames = 45; // ~750ms at 60fps
    private const int MouseMoveThreshold = 50;

    /// <summary>
    /// The last announcement that was made through this gate.
    /// </summary>
    public string? LastAnnouncement => _lastAnnouncement;

    /// <summary>
    /// Whether announcements are currently suppressed.
    /// </summary>
    public bool IsSuppressed => _suppressFrames > 0 || _navigationSuppressionFrames > 0;

    /// <summary>
    /// Suppress announcements for the specified number of frames.
    /// </summary>
    public void SuppressForFrames(int frames)
    {
        _suppressFrames = Math.Max(_suppressFrames, frames);
    }

    /// <summary>
    /// Suppress announcements after navigation action.
    /// Uses minimum frame-based suppression that waits for intentional mouse movement.
    /// </summary>
    public void SuppressAfterNavigation()
    {
        _navigationSuppressionFrames = MinNavigationSuppressionFrames;
        _lastMouseX = Main.mouseX;
        _lastMouseY = Main.mouseY;
    }

    /// <summary>
    /// Tick the suppression timers. Call once per frame.
    /// </summary>
    public void Tick()
    {
        if (_suppressFrames > 0)
        {
            _suppressFrames--;
        }

        if (_navigationSuppressionFrames > 0)
        {
            _navigationSuppressionFrames--;

            // After minimum period, check for intentional mouse movement
            if (_navigationSuppressionFrames == 0)
            {
                int deltaX = Math.Abs(Main.mouseX - _lastMouseX);
                int deltaY = Math.Abs(Main.mouseY - _lastMouseY);

                // If no significant movement, keep suppression active
                if (deltaX <= MouseMoveThreshold && deltaY <= MouseMoveThreshold)
                {
                    _navigationSuppressionFrames = 1;
                }
            }
        }
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

        // Check suppression
        if (IsSuppressed)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[AnnouncementGate] Suppressed: '{text}'");
            return false;
        }

        // Check deduplication
        if (!force && string.Equals(text, _lastAnnouncement, StringComparison.Ordinal))
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[AnnouncementGate] Deduplicated: '{text}'");
            return false;
        }

        _lastAnnouncement = text;
        ScreenReaderMod.Instance?.Logger.Info($"[AnnouncementGate] Announcing: '{text}'");

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
        _suppressFrames = 0;
        _navigationSuppressionFrames = 0;
    }
}
