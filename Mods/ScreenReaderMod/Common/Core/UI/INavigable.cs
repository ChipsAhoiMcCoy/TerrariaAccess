#nullable enable
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Core.UI;

/// <summary>
/// Represents a UI region that supports keyboard/gamepad navigation.
/// Navigable regions contain focusable elements and handle navigation between them.
/// </summary>
/// <remarks>
/// This interface abstracts navigation logic from Terraria's specific mechanisms:
/// - Main menus use menuFocus/selectedMenu with Up/Down keys
/// - Inventory uses UILinkPointNavigator with directional navigation
/// - UIElements use mouse hover with occasional keyboard support
///
/// By implementing INavigable, different UI regions can share common navigation
/// handling while hiding their implementation details.
/// </remarks>
public interface INavigable
{
    /// <summary>
    /// Gets the name of this navigable region for announcement purposes.
    /// </summary>
    string RegionName { get; }

    /// <summary>
    /// Attempts to navigate in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to navigate.</param>
    /// <returns>True if navigation occurred, false if at boundary or navigation not possible.</returns>
    bool TryNavigate(NavigationDirection direction);

    /// <summary>
    /// Gets the currently focused element in this region.
    /// </summary>
    /// <returns>The focused element, or null if nothing is focused.</returns>
    IFocusable? GetCurrentFocus();

    /// <summary>
    /// Gets all focusable elements in this region.
    /// </summary>
    /// <returns>A read-only list of focusable elements in navigation order.</returns>
    IReadOnlyList<IFocusable> GetFocusableElements();

    /// <summary>
    /// Gets whether this region is currently active (visible and accepting input).
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the current focus index within the list of focusable elements.
    /// Returns -1 if nothing is focused or focus cannot be determined.
    /// </summary>
    int CurrentFocusIndex { get; }

    /// <summary>
    /// Attempts to set focus to the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to focus.</param>
    /// <returns>True if focus was successfully set, false otherwise.</returns>
    bool TrySetFocus(int index);

    /// <summary>
    /// Attempts to set focus to a specific element.
    /// </summary>
    /// <param name="element">The element to focus.</param>
    /// <returns>True if focus was successfully set, false otherwise.</returns>
    bool TrySetFocus(IFocusable element);

    /// <summary>
    /// Gets the navigation behavior for this region.
    /// </summary>
    NavigationBehavior Behavior => NavigationBehavior.Default;

    /// <summary>
    /// Called when navigation enters this region from another region.
    /// </summary>
    /// <param name="fromDirection">The direction from which focus entered.</param>
    void OnEnter(NavigationDirection fromDirection) { }

    /// <summary>
    /// Called when navigation leaves this region to another region.
    /// </summary>
    /// <param name="toDirection">The direction in which focus is leaving.</param>
    void OnExit(NavigationDirection toDirection) { }
}

/// <summary>
/// Describes how navigation behaves at region boundaries and between elements.
/// </summary>
public sealed class NavigationBehavior
{
    /// <summary>
    /// Default navigation behavior: linear navigation, stop at boundaries.
    /// </summary>
    public static NavigationBehavior Default { get; } = new();

    /// <summary>
    /// Whether navigation wraps from the end back to the beginning.
    /// </summary>
    public bool WrapsHorizontally { get; init; }

    /// <summary>
    /// Whether navigation wraps vertically (for grid layouts).
    /// </summary>
    public bool WrapsVertically { get; init; }

    /// <summary>
    /// Whether this region allows navigation to exit to adjacent regions.
    /// </summary>
    public bool AllowsExitNavigation { get; init; } = true;

    /// <summary>
    /// The layout mode for this region.
    /// </summary>
    public NavigationLayout Layout { get; init; } = NavigationLayout.Linear;

    /// <summary>
    /// For grid layouts, the number of columns.
    /// </summary>
    public int GridColumns { get; init; } = 1;

    /// <summary>
    /// Whether to announce the region name when entering.
    /// </summary>
    public bool AnnounceRegionOnEntry { get; init; } = true;

    /// <summary>
    /// Whether to announce position within the region.
    /// </summary>
    public bool AnnouncePosition { get; init; } = true;
}

/// <summary>
/// Describes how elements are laid out for navigation purposes.
/// </summary>
public enum NavigationLayout
{
    /// <summary>Elements in a single line (horizontal or vertical list).</summary>
    Linear,

    /// <summary>Elements in a 2D grid.</summary>
    Grid,

    /// <summary>Free-form layout where navigation is context-dependent.</summary>
    FreeForm,
}

/// <summary>
/// Extension methods for INavigable.
/// </summary>
public static class NavigableExtensions
{
    /// <summary>
    /// Gets whether there are more elements in the specified direction.
    /// </summary>
    public static bool CanNavigate(this INavigable region, NavigationDirection direction)
    {
        if (!region.IsActive)
        {
            return false;
        }

        int currentIndex = region.CurrentFocusIndex;
        int count = region.GetFocusableElements().Count;

        if (count == 0)
        {
            return false;
        }

        return direction switch
        {
            NavigationDirection.Up or NavigationDirection.Left or NavigationDirection.TabPrevious =>
                currentIndex > 0 || region.Behavior.WrapsVertically || region.Behavior.WrapsHorizontally,

            NavigationDirection.Down or NavigationDirection.Right or NavigationDirection.TabNext =>
                currentIndex < count - 1 || region.Behavior.WrapsVertically || region.Behavior.WrapsHorizontally,

            _ => false,
        };
    }

    /// <summary>
    /// Navigates to the first element in the region.
    /// </summary>
    public static bool NavigateToFirst(this INavigable region)
    {
        return region.TrySetFocus(0);
    }

    /// <summary>
    /// Navigates to the last element in the region.
    /// </summary>
    public static bool NavigateToLast(this INavigable region)
    {
        var elements = region.GetFocusableElements();
        if (elements.Count == 0)
        {
            return false;
        }

        return region.TrySetFocus(elements.Count - 1);
    }

    /// <summary>
    /// Gets the element at a specific row and column for grid-based regions.
    /// </summary>
    public static IFocusable? GetElementAtPosition(this INavigable region, int row, int column)
    {
        if (region.Behavior.Layout != NavigationLayout.Grid)
        {
            return null;
        }

        int columns = region.Behavior.GridColumns;
        if (columns <= 0)
        {
            return null;
        }

        int index = row * columns + column;
        var elements = region.GetFocusableElements();

        if (index < 0 || index >= elements.Count)
        {
            return null;
        }

        return elements[index];
    }
}
