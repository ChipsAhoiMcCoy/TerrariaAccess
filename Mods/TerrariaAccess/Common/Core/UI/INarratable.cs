#nullable enable
using System;

namespace TerrariaAccess.Common.Core.UI;

/// <summary>
/// Extends IFocusable with additional narration capabilities and state tracking.
/// Elements implementing this interface can provide more detailed narration control,
/// including change detection, announcement priorities, and custom formatting.
/// </summary>
public interface INarratable : IFocusable
{
    /// <summary>
    /// Gets the priority of this element's narration.
    /// Higher priority announcements may interrupt lower priority ones.
    /// </summary>
    NarrationPriority Priority => NarrationPriority.Normal;

    /// <summary>
    /// Gets the category for this narration, used for rate limiting
    /// and coordination with other announcements.
    /// </summary>
    NarrationCategory Category => NarrationCategory.Default;

    /// <summary>
    /// Gets whether the element's state has changed since the last narration.
    /// Used to determine if a re-announcement is needed.
    /// </summary>
    /// <returns>True if the element should be re-narrated, false otherwise.</returns>
    bool HasChanged();

    /// <summary>
    /// Marks the current state as narrated, resetting the change tracking.
    /// Called after successful narration to prevent duplicate announcements.
    /// </summary>
    void MarkNarrated();

    /// <summary>
    /// Gets the announcement text with optional context prefix.
    /// </summary>
    /// <param name="includeContext">Whether to include region/position context.</param>
    /// <returns>The formatted announcement text.</returns>
    string GetAnnouncement(bool includeContext = true)
    {
        string label = GetNarrationLabel();

        if (!includeContext)
        {
            return label;
        }

        FocusContext? context = GetFocusContext();
        if (context is null)
        {
            return label;
        }

        string? position = context.GetPositionLabel();
        if (!string.IsNullOrEmpty(position))
        {
            return $"{label}, {position}";
        }

        return label;
    }

    /// <summary>
    /// Gets any prefix text that should be spoken before the main label.
    /// Commonly used for region announcements when transitioning between UI areas.
    /// </summary>
    /// <returns>Optional prefix text, or null if none.</returns>
    string? GetNarrationPrefix() => null;

    /// <summary>
    /// Gets the timestamp of when this element was last narrated.
    /// Used for debouncing rapid changes.
    /// </summary>
    DateTime? LastNarrated { get; }

    /// <summary>
    /// Gets the minimum interval between narrations for this element.
    /// Helps prevent overwhelming the user with rapid announcements.
    /// </summary>
    TimeSpan MinNarrationInterval => TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Priority levels for narration announcements.
/// Higher priority announcements may interrupt or queue ahead of lower priority ones.
/// </summary>
public enum NarrationPriority
{
    /// <summary>Low priority - can be skipped if higher priority announcements are pending.</summary>
    Low = 0,

    /// <summary>Normal priority - standard announcements.</summary>
    Normal = 1,

    /// <summary>High priority - important state changes that should be announced soon.</summary>
    High = 2,

    /// <summary>Critical priority - urgent information that should interrupt other speech.</summary>
    Critical = 3,
}

/// <summary>
/// Categories for narration, used for rate limiting and coordination.
/// Each category has independent rate limiting to prevent flooding.
/// </summary>
public enum NarrationCategory
{
    /// <summary>Default category for general UI announcements.</summary>
    Default,

    /// <summary>Tile and world information narration.</summary>
    Tile,

    /// <summary>Wall information narration.</summary>
    Wall,

    /// <summary>Item pickup announcements.</summary>
    Pickup,

    /// <summary>World state announcements (biome changes, events, etc.).</summary>
    World,

    /// <summary>Menu navigation announcements.</summary>
    Menu,

    /// <summary>Inventory slot announcements.</summary>
    Inventory,

    /// <summary>Crafting UI announcements.</summary>
    Crafting,

    /// <summary>NPC dialogue and interaction announcements.</summary>
    Dialogue,

    /// <summary>Combat-related announcements (damage, health, etc.).</summary>
    Combat,
}

/// <summary>
/// Extension methods for narration interfaces.
/// </summary>
public static class NarratableExtensions
{
    /// <summary>
    /// Checks if enough time has passed since the last narration to allow a new one.
    /// </summary>
    public static bool CanNarrateNow(this INarratable element)
    {
        if (!element.LastNarrated.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - element.LastNarrated.Value >= element.MinNarrationInterval;
    }

    /// <summary>
    /// Gets a combined announcement with prefix if available.
    /// </summary>
    public static string GetFullAnnouncement(this INarratable element, bool includeContext = true)
    {
        string? prefix = element.GetNarrationPrefix();
        string main = element.GetAnnouncement(includeContext);

        if (string.IsNullOrEmpty(prefix))
        {
            return main;
        }

        return $"{prefix}. {main}";
    }
}
