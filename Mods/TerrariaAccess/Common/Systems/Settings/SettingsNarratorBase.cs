#nullable enable
using System;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Settings;

/// <summary>
/// Base class providing common functionality for settings narrators.
/// Handles announcement throttling, tick sounds, and category/option tracking.
/// </summary>
internal abstract class SettingsNarratorBase : ISettingsNarrator
{
    // Timing constants
    protected const int NoFocusAnnouncementDelayTicks = 12;
    protected const int NoFocusRepeatIntervalTicks = 90;
    protected const int MenuOpenSettleDelayTicks = 6;
    protected const int MinTickIntervalFrames = 5;

    // State tracking
    protected int MenuOpenSettleFrames;
    protected int NoFocusFrameCount;
    protected bool ForceCategoryAnnouncement;
    protected bool IsMenuActive;

    // Tick sound state
    private string? _lastTickKey;
    private uint _lastTickFrame;

    // Last announcement tracking for deduplication
    protected string? LastCategoryAnnouncement;
    protected string? LastOptionAnnouncement;

    /// <inheritdoc/>
    public abstract bool IsActive { get; }

    /// <inheritdoc/>
    public virtual void OnMenuOpened()
    {
        Reset();
        IsMenuActive = true;
        ForceCategoryAnnouncement = true;
        MenuOpenSettleFrames = MenuOpenSettleDelayTicks;
    }

    /// <inheritdoc/>
    public virtual void OnMenuClosed()
    {
        Reset();
        IsMenuActive = false;
    }

    /// <inheritdoc/>
    public abstract void Update();

    /// <inheritdoc/>
    public virtual void Reset()
    {
        MenuOpenSettleFrames = 0;
        NoFocusFrameCount = 0;
        ForceCategoryAnnouncement = false;
        _lastTickKey = null;
        _lastTickFrame = 0;
        LastCategoryAnnouncement = null;
        LastOptionAnnouncement = null;
    }

    /// <summary>
    /// Announces a category change if the category label is different from the last announced.
    /// </summary>
    /// <param name="categoryLabel">The category label to announce.</param>
    /// <param name="tickKey">A unique key for playing a tick sound (optional).</param>
    /// <returns>True if announcement was made.</returns>
    protected bool TryAnnounceCategory(string? categoryLabel, string? tickKey = null)
    {
        if (string.IsNullOrWhiteSpace(categoryLabel))
        {
            return false;
        }

        bool categoryChanged = !string.Equals(categoryLabel, LastCategoryAnnouncement, StringComparison.Ordinal);
        if (!categoryChanged && !ForceCategoryAnnouncement)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tickKey))
        {
            PlayTickIfNew(tickKey);
        }

        ScreenReaderService.Announce(categoryLabel, force: true);
        LastCategoryAnnouncement = categoryLabel;
        ForceCategoryAnnouncement = false;
        return true;
    }

    /// <summary>
    /// Announces an option if the description is different from the last announced.
    /// </summary>
    /// <param name="description">The option description to announce.</param>
    /// <param name="tickKey">A unique key for playing a tick sound (optional).</param>
    /// <param name="force">If true, bypass deduplication check.</param>
    /// <returns>True if announcement was made.</returns>
    protected bool TryAnnounceOption(string? description, string? tickKey = null, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        if (!force && string.Equals(description, LastOptionAnnouncement, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tickKey))
        {
            PlayTickIfNew(tickKey);
        }

        ScreenReaderService.Announce(description, force: force);
        LastOptionAnnouncement = description;
        return true;
    }

    /// <summary>
    /// Plays a menu tick sound if the key is different from the last played,
    /// and enough frames have passed since the last tick.
    /// </summary>
    /// <param name="key">A unique key identifying this tick event.</param>
    protected void PlayTickIfNew(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastTickKey, StringComparison.Ordinal))
        {
            return;
        }

        uint frame = Main.GameUpdateCount;
        uint age = frame >= _lastTickFrame ? frame - _lastTickFrame : uint.MaxValue - _lastTickFrame + frame + 1;
        if (age < MinTickIntervalFrames)
        {
            return;
        }

        _lastTickKey = key;
        _lastTickFrame = frame;
        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
    }

    /// <summary>
    /// Handles the "no focus" announcement pattern when nothing is selected.
    /// </summary>
    /// <param name="hasFocus">Whether any element currently has focus.</param>
    /// <param name="categoryLabel">The current category label for context.</param>
    /// <param name="tickKey">A unique key for the tick sound.</param>
    protected void HandleNoFocusAnnouncement(bool hasFocus, string? categoryLabel, string? tickKey)
    {
        if (hasFocus)
        {
            NoFocusFrameCount = 0;
            return;
        }

        NoFocusFrameCount++;
        bool readyToAnnounce = NoFocusFrameCount == NoFocusAnnouncementDelayTicks ||
            (NoFocusFrameCount > NoFocusAnnouncementDelayTicks &&
                (NoFocusFrameCount - NoFocusAnnouncementDelayTicks) % NoFocusRepeatIntervalTicks == 0);

        if (!readyToAnnounce)
        {
            return;
        }

        string fallbackLabel = string.IsNullOrWhiteSpace(categoryLabel) ? "Settings" : categoryLabel;
        string announcement = $"{fallbackLabel} (no option selected)";

        if (!string.IsNullOrWhiteSpace(tickKey))
        {
            PlayTickIfNew(tickKey);
        }

        ScreenReaderService.Announce(announcement, force: true);
    }

    /// <summary>
    /// Returns true if the menu is still settling after being opened.
    /// Call this to skip processing during the settle period.
    /// </summary>
    protected bool IsSettling()
    {
        if (MenuOpenSettleFrames > 0)
        {
            MenuOpenSettleFrames--;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Logs a debug message with the narrator's prefix.
    /// </summary>
    protected void LogDebug(string message)
    {
        TerrariaAccess.Instance?.Logger.Debug($"[{GetType().Name}] {message}");
    }
}
