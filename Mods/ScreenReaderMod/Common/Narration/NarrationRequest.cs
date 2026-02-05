#nullable enable
using ScreenReaderMod.Common.Services;
using AnnouncementCategory = ScreenReaderMod.Common.Services.ScreenReaderService.AnnouncementCategory;

namespace ScreenReaderMod.Common.Narration;

/// <summary>
/// Represents a request to announce text through the screen reader.
/// Encapsulates all parameters needed for the announcement including
/// priority, category, and control flags.
/// </summary>
public readonly struct NarrationRequest
{
    /// <summary>
    /// The text to be announced.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Priority level for this announcement.
    /// </summary>
    public NarrationPriority Priority { get; }

    /// <summary>
    /// Category for rate limiting (e.g., Tile, Pickup, World).
    /// </summary>
    public AnnouncementCategory Category { get; }

    /// <summary>
    /// When true, bypasses deduplication checks.
    /// </summary>
    public bool Force { get; }

    /// <summary>
    /// When true, allows announcement even when speech is muted.
    /// </summary>
    public bool AllowWhenMuted { get; }

    /// <summary>
    /// When true, requests that the screen reader interrupt current speech.
    /// </summary>
    public bool RequestInterrupt { get; }

    /// <summary>
    /// Optional key for deduplication tracking.
    /// If null, the text itself is used for deduplication.
    /// </summary>
    public string? DeduplicationKey { get; }

    public NarrationRequest(
        string text,
        NarrationPriority priority = NarrationPriority.Normal,
        AnnouncementCategory category = AnnouncementCategory.Default,
        bool force = false,
        bool allowWhenMuted = false,
        bool requestInterrupt = true,
        string? deduplicationKey = null)
    {
        Text = text;
        Priority = priority;
        Category = category;
        Force = force;
        AllowWhenMuted = allowWhenMuted;
        RequestInterrupt = requestInterrupt;
        DeduplicationKey = deduplicationKey;
    }

    /// <summary>
    /// Creates a standard announcement with normal priority.
    /// </summary>
    public static NarrationRequest Standard(string text, bool force = false)
        => new(text, NarrationPriority.Normal, force: force);

    /// <summary>
    /// Creates a high priority announcement (e.g., region changes, mode transitions).
    /// </summary>
    public static NarrationRequest HighPriority(string text)
        => new(text, NarrationPriority.High, force: true);

    /// <summary>
    /// Creates a critical announcement that should always be spoken.
    /// </summary>
    public static NarrationRequest Critical(string text)
        => new(text, NarrationPriority.Critical, force: true, requestInterrupt: true);

    /// <summary>
    /// Creates a tile-category announcement with appropriate rate limiting.
    /// </summary>
    public static NarrationRequest Tile(string text, bool force = false)
        => new(text, NarrationPriority.Normal, AnnouncementCategory.Tile, force);

    /// <summary>
    /// Creates a pickup-category announcement with appropriate rate limiting.
    /// </summary>
    public static NarrationRequest Pickup(string text)
        => new(text, NarrationPriority.Normal, AnnouncementCategory.Pickup);

    /// <summary>
    /// Creates a world event announcement with appropriate rate limiting.
    /// </summary>
    public static NarrationRequest World(string text)
        => new(text, NarrationPriority.High, AnnouncementCategory.World, force: true);

    /// <summary>
    /// Returns a copy of this request with a different priority.
    /// </summary>
    public NarrationRequest WithPriority(NarrationPriority priority)
        => new(Text, priority, Category, Force, AllowWhenMuted, RequestInterrupt, DeduplicationKey);

    /// <summary>
    /// Returns a copy of this request with force enabled.
    /// </summary>
    public NarrationRequest WithForce()
        => new(Text, Priority, Category, force: true, AllowWhenMuted, RequestInterrupt, DeduplicationKey);

    /// <summary>
    /// Returns a copy of this request with a specific deduplication key.
    /// </summary>
    public NarrationRequest WithKey(string key)
        => new(Text, Priority, Category, Force, AllowWhenMuted, RequestInterrupt, key);
}
