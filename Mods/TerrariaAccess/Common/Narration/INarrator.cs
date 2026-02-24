#nullable enable
using TerrariaAccess.Common.Services;
using AnnouncementCategory = TerrariaAccess.Common.Services.ScreenReaderService.AnnouncementCategory;

namespace TerrariaAccess.Common.Narration;

/// <summary>
/// Base interface for all narrators in the accessibility system.
/// Narrators are responsible for tracking focus and announcing changes
/// in specific UI areas or game contexts.
/// </summary>
public interface INarrator
{
    /// <summary>
    /// Unique name for this narrator. Used for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Default priority level for this narrator's announcements.
    /// </summary>
    NarrationPriority Priority { get; }

    /// <summary>
    /// Whether this narrator is currently active and should process updates.
    /// Narrators can return false to temporarily disable themselves based on context.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Called each frame to update narrator state and potentially make announcements.
    /// </summary>
    /// <param name="context">Context information about the current game/UI state.</param>
    void Update(NarratorContext context);

    /// <summary>
    /// Resets all internal state. Called when context changes significantly
    /// (e.g., menu opened/closed, inventory toggled, etc.).
    /// </summary>
    void Reset();
}

/// <summary>
/// Extended interface for narrators that support suppression.
/// Some narrators may need to be temporarily suppressed by other narrators
/// or external systems.
/// </summary>
public interface ISuppressableNarrator : INarrator
{
    /// <summary>
    /// Whether this narrator is currently suppressed.
    /// </summary>
    bool IsSuppressed { get; }

    /// <summary>
    /// Suppresses this narrator until Resume() is called.
    /// </summary>
    void Suppress();

    /// <summary>
    /// Resumes this narrator after suppression.
    /// </summary>
    void Resume();
}

/// <summary>
/// Extended interface for narrators that can be notified of events from other narrators.
/// Enables coordination between narrators without tight coupling.
/// </summary>
public interface IEventAwareNarrator : INarrator
{
    /// <summary>
    /// Called when a significant event occurs that may affect this narrator's state.
    /// </summary>
    /// <param name="eventName">Name of the event (e.g., "InventoryOpened", "MenuClosed").</param>
    /// <param name="data">Optional event data.</param>
    void OnEvent(string eventName, object? data = null);
}

/// <summary>
/// Context information passed to narrators during Update().
/// </summary>
public readonly struct NarratorContext
{
    /// <summary>
    /// The current game frame number.
    /// </summary>
    public uint FrameNumber { get; init; }

    /// <summary>
    /// Whether the game is paused.
    /// </summary>
    public bool IsPaused { get; init; }

    /// <summary>
    /// Whether we're in a menu (main menu, not in-game).
    /// </summary>
    public bool InMenu { get; init; }

    /// <summary>
    /// Whether gamepad UI navigation is active.
    /// </summary>
    public bool UsingGamepad { get; init; }

    /// <summary>
    /// Whether the inventory is currently open.
    /// </summary>
    public bool InventoryOpen { get; init; }

    /// <summary>
    /// The announcement category to use if not overridden.
    /// </summary>
    public AnnouncementCategory? Category { get; init; }

    /// <summary>
    /// Whether tracing/diagnostics are enabled.
    /// </summary>
    public bool TraceEnabled { get; init; }
}
