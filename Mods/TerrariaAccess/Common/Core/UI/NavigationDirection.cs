#nullable enable

namespace TerrariaAccess.Common.Core.UI;

/// <summary>
/// Represents a direction for UI navigation.
/// These map to both keyboard and gamepad inputs for navigating UI elements.
/// </summary>
public enum NavigationDirection
{
    /// <summary>No navigation input detected.</summary>
    None = 0,

    /// <summary>Navigate up (keyboard up, gamepad d-pad up, left stick up).</summary>
    Up,

    /// <summary>Navigate down (keyboard down, gamepad d-pad down, left stick down).</summary>
    Down,

    /// <summary>Navigate left (keyboard left, gamepad d-pad left, left stick left).</summary>
    Left,

    /// <summary>Navigate right (keyboard right, gamepad d-pad right, left stick right).</summary>
    Right,

    /// <summary>Confirm/select current element (Enter, Space, gamepad A).</summary>
    Enter,

    /// <summary>Go back/cancel (Escape, gamepad B).</summary>
    Back,

    /// <summary>Tab to next element or section.</summary>
    TabNext,

    /// <summary>Tab to previous element or section.</summary>
    TabPrevious,
}
