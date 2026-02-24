#nullable enable

namespace TerrariaAccess.Common.Narration;

/// <summary>
/// Priority levels for narration announcements.
/// Higher priority announcements can interrupt or take precedence over lower priority ones.
/// </summary>
public enum NarrationPriority
{
    /// <summary>
    /// Lowest priority - background information, hints, ambient narration.
    /// Will be suppressed if any higher priority announcement is pending.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Default priority for most UI narration (inventory, crafting, settings).
    /// Standard navigation feedback.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Higher priority for important state changes (region transitions, mode changes).
    /// Will interrupt Normal priority announcements.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical announcements that should always be spoken (damage, danger, errors).
    /// Will interrupt all lower priority announcements.
    /// </summary>
    Critical = 3,
}
