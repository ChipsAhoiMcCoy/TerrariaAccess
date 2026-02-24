#nullable enable

namespace TerrariaAccess.Common.Core.UI;

/// <summary>
/// Represents a UI element that can receive focus and be narrated.
/// This is the fundamental abstraction for any element that can be selected
/// or highlighted in the UI, whether it's a menu item, inventory slot, or button.
/// </summary>
/// <remarks>
/// Implementations wrap Terraria's various UI element types:
/// - Menu items (tracked via Main.menuMode, focusMenu, selectedMenu)
/// - Inventory slots (tracked via ItemSlot hooks and UILinkPointNavigator)
/// - UIElements (tracked via UserInterface hover)
/// - Crafting recipes
/// - NPC dialogue options
/// </remarks>
public interface IFocusable
{
    /// <summary>
    /// Gets the narration label to be spoken by the screen reader.
    /// This should be a concise, human-readable description of the element.
    /// </summary>
    /// <returns>
    /// A string suitable for screen reader output.
    /// Should never return null; return an empty string if no label is available.
    /// </returns>
    /// <example>
    /// - "Copper Shortsword, 7 damage, 3 knockback"
    /// - "Play World"
    /// - "Volume: 75%"
    /// - "Empty slot"
    /// </example>
    string GetNarrationLabel();

    /// <summary>
    /// Gets whether this element currently has focus.
    /// Focus indicates the element is highlighted and will be activated on Enter/confirm.
    /// </summary>
    bool IsFocused { get; }

    /// <summary>
    /// Gets contextual information about the current focus state.
    /// Returns null if the element is not focused or context is not available.
    /// </summary>
    /// <returns>
    /// A FocusContext containing region, position, and other metadata,
    /// or null if not applicable.
    /// </returns>
    FocusContext? GetFocusContext();

    /// <summary>
    /// Gets an optional detailed description for the element.
    /// This is used for elements that have additional information beyond the label,
    /// such as item tooltips or help text.
    /// </summary>
    /// <returns>
    /// An optional extended description, or null if not applicable.
    /// </returns>
    string? GetDetailedDescription() => null;

    /// <summary>
    /// Gets the unique identifier for this focusable element.
    /// Used for tracking focus changes and preventing duplicate announcements.
    /// </summary>
    /// <returns>
    /// A string that uniquely identifies this element within its container.
    /// Default implementation uses the narration label.
    /// </returns>
    string GetIdentifier() => GetNarrationLabel();

    /// <summary>
    /// Gets whether this element can currently be interacted with.
    /// Disabled elements may still be focusable but should be announced as unavailable.
    /// </summary>
    bool IsEnabled => true;

    /// <summary>
    /// Gets the type hint for this focusable element.
    /// Used to provide additional context in announcements.
    /// </summary>
    FocusableType ElementType => FocusableType.Generic;
}

/// <summary>
/// Categorizes the type of focusable element for announcement purposes.
/// </summary>
public enum FocusableType
{
    /// <summary>Generic focusable element with no special handling.</summary>
    Generic,

    /// <summary>A button that performs an action when activated.</summary>
    Button,

    /// <summary>An inventory or storage slot that can contain an item.</summary>
    Slot,

    /// <summary>A menu item in a list of options.</summary>
    MenuItem,

    /// <summary>A slider for adjusting a value (volume, etc.).</summary>
    Slider,

    /// <summary>A toggle or checkbox (on/off state).</summary>
    Toggle,

    /// <summary>A text input field.</summary>
    TextInput,

    /// <summary>A craftable recipe.</summary>
    Recipe,

    /// <summary>A list item in a scrollable list.</summary>
    ListItem,

    /// <summary>A tab for switching between views.</summary>
    Tab,
}
