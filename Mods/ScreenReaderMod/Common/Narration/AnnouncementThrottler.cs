#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Narration;

/// <summary>
/// Provides rate limiting and deduplication for announcements.
/// Prevents announcement spam by tracking recent announcements and enforcing cooldowns.
/// </summary>
public sealed class AnnouncementThrottler
{
    private readonly Dictionary<string, uint> _lastAnnouncementFrame = new();
    private readonly Dictionary<string, string> _lastAnnouncementText = new();
    private string? _lastGlobalText;
    private uint _lastGlobalFrame;

    /// <summary>
    /// Default cooldown in frames (approximately 100ms at 60fps).
    /// </summary>
    public uint DefaultCooldownFrames { get; set; } = 6;

    /// <summary>
    /// Checks if an announcement should be throttled (suppressed).
    /// </summary>
    /// <param name="text">The text to announce.</param>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="force">When true, bypasses throttling.</param>
    /// <returns>True if the announcement should be suppressed.</returns>
    public bool ShouldThrottle(string? text, uint currentFrame, bool force = false)
    {
        if (force)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // Check global duplicate
        if (string.Equals(text, _lastGlobalText, StringComparison.Ordinal))
        {
            uint elapsed = GetElapsedFrames(currentFrame, _lastGlobalFrame);
            if (elapsed < DefaultCooldownFrames)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an announcement with a specific key should be throttled.
    /// Uses key-based tracking for more precise deduplication.
    /// </summary>
    /// <param name="key">Unique key for this announcement type.</param>
    /// <param name="text">The text to announce.</param>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="cooldownFrames">Custom cooldown for this key (0 to use default).</param>
    /// <param name="force">When true, bypasses throttling.</param>
    /// <returns>True if the announcement should be suppressed.</returns>
    public bool ShouldThrottle(string key, string? text, uint currentFrame, uint cooldownFrames = 0, bool force = false)
    {
        if (force)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return ShouldThrottle(text, currentFrame, force);
        }

        uint effectiveCooldown = cooldownFrames > 0 ? cooldownFrames : DefaultCooldownFrames;

        // Check key-specific cooldown
        if (_lastAnnouncementFrame.TryGetValue(key, out uint lastFrame))
        {
            uint elapsed = GetElapsedFrames(currentFrame, lastFrame);
            if (elapsed < effectiveCooldown)
            {
                // Check if text is different - allow if text changed even during cooldown
                if (_lastAnnouncementText.TryGetValue(key, out string? lastText) &&
                    string.Equals(text, lastText, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Records that an announcement was made (call after successful announcement).
    /// </summary>
    /// <param name="text">The text that was announced.</param>
    /// <param name="currentFrame">The current frame number.</param>
    public void Record(string? text, uint currentFrame)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _lastGlobalText = text;
        _lastGlobalFrame = currentFrame;
    }

    /// <summary>
    /// Records that a keyed announcement was made.
    /// </summary>
    /// <param name="key">The announcement key.</param>
    /// <param name="text">The text that was announced.</param>
    /// <param name="currentFrame">The current frame number.</param>
    public void Record(string key, string? text, uint currentFrame)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Record(text, currentFrame);
            return;
        }

        _lastAnnouncementFrame[key] = currentFrame;
        if (!string.IsNullOrWhiteSpace(text))
        {
            _lastAnnouncementText[key] = text;
        }

        Record(text, currentFrame);
    }

    /// <summary>
    /// Checks if enough time has passed since the last announcement with this key.
    /// Does not check text content - only timing.
    /// </summary>
    public bool IsOnCooldown(string key, uint currentFrame, uint cooldownFrames = 0)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!_lastAnnouncementFrame.TryGetValue(key, out uint lastFrame))
        {
            return false;
        }

        uint effectiveCooldown = cooldownFrames > 0 ? cooldownFrames : DefaultCooldownFrames;
        uint elapsed = GetElapsedFrames(currentFrame, lastFrame);
        return elapsed < effectiveCooldown;
    }

    /// <summary>
    /// Gets the last announced text for a key.
    /// </summary>
    public string? GetLastText(string key)
    {
        return _lastAnnouncementText.TryGetValue(key, out string? text) ? text : null;
    }

    /// <summary>
    /// Gets the frame number of the last announcement for a key.
    /// Returns 0 if no announcement has been made with this key.
    /// </summary>
    public uint GetLastFrame(string key)
    {
        return _lastAnnouncementFrame.TryGetValue(key, out uint frame) ? frame : 0;
    }

    /// <summary>
    /// Clears all throttling state.
    /// </summary>
    public void Clear()
    {
        _lastAnnouncementFrame.Clear();
        _lastAnnouncementText.Clear();
        _lastGlobalText = null;
        _lastGlobalFrame = 0;
    }

    /// <summary>
    /// Clears throttling state for a specific key.
    /// </summary>
    public void Clear(string key)
    {
        _lastAnnouncementFrame.Remove(key);
        _lastAnnouncementText.Remove(key);
    }

    /// <summary>
    /// Clears all keys that start with a given prefix.
    /// Useful for clearing all state related to a specific context.
    /// </summary>
    public void ClearPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        var keysToRemove = new List<string>();
        foreach (var key in _lastAnnouncementFrame.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _lastAnnouncementFrame.Remove(key);
            _lastAnnouncementText.Remove(key);
        }
    }

    private static uint GetElapsedFrames(uint current, uint last)
    {
        // Handle frame counter wraparound
        return current >= last
            ? current - last
            : uint.MaxValue - last + current + 1;
    }
}
