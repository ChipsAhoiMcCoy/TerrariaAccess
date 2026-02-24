#nullable enable
using System;

namespace TerrariaAccess.Common.Core;

/// <summary>
/// Categories for speech announcements with different rate limiting and priority rules.
/// Each category has its own suppression window to prevent spam.
/// </summary>
/// <remarks>
/// <para>
/// The speech controller uses these categories to apply per-category rate limiting.
/// For example, rapid tile scanning should not block important world event announcements.
/// </para>
/// <para>
/// Default suppression windows (configurable via SpeechController):
/// <list type="bullet">
/// <item><description>Default: 250ms</description></item>
/// <item><description>Tile: 150ms</description></item>
/// <item><description>Wall: 150ms</description></item>
/// <item><description>Pickup: 150ms</description></item>
/// <item><description>World: 2000ms (2 seconds)</description></item>
/// </list>
/// </para>
/// </remarks>
public enum AnnouncementCategory
{
    /// <summary>
    /// Default category for general announcements.
    /// Used for UI navigation, menu items, inventory slots, etc.
    /// Suppression window: 250ms.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Tile announcements (cursor over tiles, smart cursor targets).
    /// Rapid announcements during cursor movement.
    /// Suppression window: 150ms.
    /// </summary>
    Tile = 1,

    /// <summary>
    /// Wall announcements (cursor over walls when no tile present).
    /// Separate from tiles to allow independent rate limiting.
    /// Suppression window: 150ms.
    /// </summary>
    Wall = 2,

    /// <summary>
    /// Item pickup announcements.
    /// Separate category to prevent pickup spam from blocking other announcements.
    /// Suppression window: 150ms.
    /// </summary>
    Pickup = 3,

    /// <summary>
    /// World event announcements (blood moon, boss spawns, biome changes).
    /// Important announcements with longer suppression to avoid repeats.
    /// Suppression window: 2000ms (2 seconds).
    /// </summary>
    World = 4,
}

/// <summary>
/// Extension methods for AnnouncementCategory.
/// </summary>
public static class AnnouncementCategoryExtensions
{
    /// <summary>
    /// Gets the default suppression window for this category in milliseconds.
    /// </summary>
    public static int GetDefaultWindowMs(this AnnouncementCategory category)
    {
        return category switch
        {
            AnnouncementCategory.Default => 250,
            AnnouncementCategory.Tile => 150,
            AnnouncementCategory.Wall => 150,
            AnnouncementCategory.Pickup => 150,
            AnnouncementCategory.World => 2000,
            _ => 250
        };
    }

    /// <summary>
    /// Gets the default suppression window for this category as a TimeSpan.
    /// </summary>
    public static TimeSpan GetDefaultWindow(this AnnouncementCategory category)
    {
        return TimeSpan.FromMilliseconds(category.GetDefaultWindowMs());
    }

    /// <summary>
    /// Checks if this is a high-priority category that should not be easily suppressed.
    /// </summary>
    public static bool IsHighPriority(this AnnouncementCategory category)
    {
        return category == AnnouncementCategory.World;
    }

    /// <summary>
    /// Checks if this is a rapid-fire category that expects frequent updates.
    /// </summary>
    public static bool IsRapidFireCategory(this AnnouncementCategory category)
    {
        return category is AnnouncementCategory.Tile
            or AnnouncementCategory.Wall
            or AnnouncementCategory.Pickup;
    }
}
