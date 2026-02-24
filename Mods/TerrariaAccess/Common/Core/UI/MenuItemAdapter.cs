#nullable enable
using System;
using System.Collections.Generic;

namespace TerrariaAccess.Common.Core.UI;

/// <summary>
/// Adapter for Terraria's main menu items.
/// These are the classic menu items rendered using Main.menuItem strings,
/// tracked via Main.menuMode, focusMenu, and selectedMenu.
/// </summary>
/// <remarks>
/// Main menu items work differently from UIElements:
/// - They are rendered as strings in Main.menuItem[]
/// - Focus is tracked via focusMenu (internal) and selectedMenu
/// - Selection uses menuItemScale[] for visual feedback
/// - Navigation is purely keyboard/gamepad driven (Up/Down/Enter)
/// </remarks>
public class MenuItemAdapter : IFocusable
{
    private readonly string _label;
    private readonly int _index;
    private readonly int _totalItems;
    private readonly string _menuName;
    private readonly Func<bool>? _isFocusedFunc;
    private readonly Func<bool>? _isEnabledFunc;

    /// <summary>
    /// Creates a menu item adapter.
    /// </summary>
    /// <param name="label">The display text for this menu item.</param>
    /// <param name="index">The zero-based index in the menu.</param>
    /// <param name="totalItems">Total number of items in the menu.</param>
    /// <param name="menuName">Name of the containing menu.</param>
    /// <param name="isFocusedFunc">Optional function to check if this item is focused.</param>
    /// <param name="isEnabledFunc">Optional function to check if this item is enabled.</param>
    public MenuItemAdapter(
        string label,
        int index,
        int totalItems,
        string menuName,
        Func<bool>? isFocusedFunc = null,
        Func<bool>? isEnabledFunc = null)
    {
        _label = label ?? string.Empty;
        _index = index;
        _totalItems = totalItems;
        _menuName = menuName ?? "Menu";
        _isFocusedFunc = isFocusedFunc;
        _isEnabledFunc = isEnabledFunc;
    }

    /// <inheritdoc/>
    public string GetNarrationLabel() => _label;

    /// <inheritdoc/>
    public bool IsFocused => _isFocusedFunc?.Invoke() ?? false;

    /// <inheritdoc/>
    public bool IsEnabled => _isEnabledFunc?.Invoke() ?? true;

    /// <inheritdoc/>
    public FocusableType ElementType => FocusableType.MenuItem;

    /// <inheritdoc/>
    public FocusContext? GetFocusContext()
    {
        if (!IsFocused)
        {
            return null;
        }

        return FocusContext.ForMenuItem(_menuName, _label, _index, _totalItems);
    }

    /// <inheritdoc/>
    public string GetIdentifier() => $"MenuItem_{_menuName}_{_index}";
}

/// <summary>
/// Represents a navigable menu composed of menu items.
/// Wraps Terraria's main menu system to provide INavigable support.
/// </summary>
public class MenuRegionAdapter : INavigable
{
    private readonly List<MenuItemAdapter> _items = new();
    private readonly Func<int> _getCurrentIndex;
    private readonly Action<int>? _setCurrentIndex;
    private readonly Func<bool>? _isActiveFunc;

    /// <summary>
    /// Creates a menu region adapter.
    /// </summary>
    /// <param name="regionName">Name of this menu region.</param>
    /// <param name="getCurrentIndex">Function to get the currently focused item index.</param>
    /// <param name="setCurrentIndex">Optional function to set the focused item index.</param>
    /// <param name="isActiveFunc">Optional function to check if the menu is active.</param>
    public MenuRegionAdapter(
        string regionName,
        Func<int> getCurrentIndex,
        Action<int>? setCurrentIndex = null,
        Func<bool>? isActiveFunc = null)
    {
        RegionName = regionName ?? throw new ArgumentNullException(nameof(regionName));
        _getCurrentIndex = getCurrentIndex ?? throw new ArgumentNullException(nameof(getCurrentIndex));
        _setCurrentIndex = setCurrentIndex;
        _isActiveFunc = isActiveFunc;
    }

    /// <inheritdoc/>
    public string RegionName { get; }

    /// <inheritdoc/>
    public bool IsActive => _isActiveFunc?.Invoke() ?? true;

    /// <inheritdoc/>
    public int CurrentFocusIndex => _getCurrentIndex();

    /// <inheritdoc/>
    public NavigationBehavior Behavior { get; init; } = new()
    {
        Layout = NavigationLayout.Linear,
        WrapsVertically = false,
        AllowsExitNavigation = true,
        AnnounceRegionOnEntry = true,
    };

    /// <summary>
    /// Adds a menu item to this region.
    /// </summary>
    /// <param name="label">The display text for the item.</param>
    /// <param name="isEnabled">Optional function to check if the item is enabled.</param>
    public void AddItem(string label, Func<bool>? isEnabled = null)
    {
        int index = _items.Count;
        int total = index + 1; // Will be updated when items are finalized
        var item = new MenuItemAdapter(
            label,
            index,
            total,
            RegionName,
            () => CurrentFocusIndex == index,
            isEnabled);
        _items.Add(item);
    }

    /// <summary>
    /// Sets all items at once, replacing any existing items.
    /// </summary>
    /// <param name="labels">The labels for all menu items.</param>
    public void SetItems(IEnumerable<string> labels)
    {
        _items.Clear();
        int index = 0;
        var labelList = new List<string>(labels);
        int total = labelList.Count;

        foreach (string label in labelList)
        {
            int capturedIndex = index;
            var item = new MenuItemAdapter(
                label,
                capturedIndex,
                total,
                RegionName,
                () => CurrentFocusIndex == capturedIndex);
            _items.Add(item);
            index++;
        }
    }

    /// <inheritdoc/>
    public bool TryNavigate(NavigationDirection direction)
    {
        if (!IsActive || _items.Count == 0)
        {
            return false;
        }

        int current = CurrentFocusIndex;
        int target = direction switch
        {
            NavigationDirection.Up or NavigationDirection.TabPrevious => current - 1,
            NavigationDirection.Down or NavigationDirection.TabNext => current + 1,
            _ => -1,
        };

        if (target < 0)
        {
            if (Behavior.WrapsVertically)
            {
                target = _items.Count - 1;
            }
            else
            {
                return false;
            }
        }
        else if (target >= _items.Count)
        {
            if (Behavior.WrapsVertically)
            {
                target = 0;
            }
            else
            {
                return false;
            }
        }

        return TrySetFocus(target);
    }

    /// <inheritdoc/>
    public IFocusable? GetCurrentFocus()
    {
        int index = CurrentFocusIndex;
        if (index < 0 || index >= _items.Count)
        {
            return null;
        }

        return _items[index];
    }

    /// <inheritdoc/>
    public IReadOnlyList<IFocusable> GetFocusableElements() => _items;

    /// <inheritdoc/>
    public bool TrySetFocus(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _setCurrentIndex?.Invoke(index);
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetFocus(IFocusable element)
    {
        int index = _items.IndexOf(element as MenuItemAdapter ?? new MenuItemAdapter("", -1, 0, ""));
        if (index < 0)
        {
            return false;
        }

        return TrySetFocus(index);
    }
}
