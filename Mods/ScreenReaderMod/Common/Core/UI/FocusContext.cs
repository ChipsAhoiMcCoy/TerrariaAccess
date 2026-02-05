#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Core.UI;

/// <summary>
/// Provides contextual information about what is currently focused in the UI.
/// This abstraction captures the semantic meaning of focus independent of
/// Terraria's specific implementation details (menu modes, link points, etc.).
/// </summary>
public sealed class FocusContext
{
    /// <summary>
    /// Creates a new focus context.
    /// </summary>
    /// <param name="region">The UI region containing the focused element.</param>
    /// <param name="elementId">A unique identifier for the focused element within its region.</param>
    /// <param name="elementIndex">The zero-based index of the element in a list or grid, or -1 if not applicable.</param>
    /// <param name="totalElements">Total number of elements in the current list or grid, or -1 if not applicable.</param>
    public FocusContext(
        string region,
        string elementId,
        int elementIndex = -1,
        int totalElements = -1)
    {
        Region = region ?? throw new ArgumentNullException(nameof(region));
        ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
        ElementIndex = elementIndex;
        TotalElements = totalElements;
        Properties = new Dictionary<string, object>();
    }

    /// <summary>
    /// The name of the UI region containing the focused element.
    /// Examples: "Inventory", "Hotbar", "CraftingGrid", "MainMenu", "SettingsMenu"
    /// </summary>
    public string Region { get; }

    /// <summary>
    /// A unique identifier for the focused element within its region.
    /// This could be a slot number, button name, or other distinguishing identifier.
    /// </summary>
    public string ElementId { get; }

    /// <summary>
    /// The zero-based index of the element within its container (list, grid, etc.).
    /// Returns -1 if the element is not part of an indexed collection.
    /// </summary>
    public int ElementIndex { get; }

    /// <summary>
    /// The total number of elements in the current container (list, grid, etc.).
    /// Returns -1 if not applicable.
    /// </summary>
    public int TotalElements { get; }

    /// <summary>
    /// Additional properties providing extra context about the focus.
    /// Keys are property names, values are the associated data.
    /// </summary>
    /// <remarks>
    /// Common properties include:
    /// - "SlotContext": Terraria's ItemSlot.Context value for inventory slots
    /// - "ChestIndex": Index of the open chest
    /// - "MenuMode": Current Main.menuMode value
    /// - "Row": Row index for grid layouts
    /// - "Column": Column index for grid layouts
    /// </remarks>
    public IDictionary<string, object> Properties { get; }

    /// <summary>
    /// Gets a property value by key, returning the default if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the property value.</typeparam>
    /// <param name="key">The property key to look up.</param>
    /// <param name="defaultValue">Value to return if the key is not found or type doesn't match.</param>
    /// <returns>The property value or the default.</returns>
    public T GetProperty<T>(string key, T defaultValue = default!)
    {
        if (Properties.TryGetValue(key, out object? value) && value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Creates a position label like "3 of 10" or "Row 2, Column 4" for screen reader announcement.
    /// Returns null if position information is not available.
    /// </summary>
    public string? GetPositionLabel()
    {
        // Check for grid layout (row/column)
        if (Properties.TryGetValue("Row", out object? rowObj) && rowObj is int row &&
            Properties.TryGetValue("Column", out object? colObj) && colObj is int column)
        {
            int totalRows = GetProperty<int>("TotalRows", -1);
            int totalColumns = GetProperty<int>("TotalColumns", -1);

            if (totalRows > 0 && totalColumns > 0)
            {
                return $"Row {row + 1} of {totalRows}, Column {column + 1} of {totalColumns}";
            }

            return $"Row {row + 1}, Column {column + 1}";
        }

        // Check for linear list
        if (ElementIndex >= 0)
        {
            if (TotalElements > 0)
            {
                return $"{ElementIndex + 1} of {TotalElements}";
            }

            return $"Item {ElementIndex + 1}";
        }

        return null;
    }

    /// <summary>
    /// Checks if this focus context represents the same UI location as another.
    /// </summary>
    /// <param name="other">The other focus context to compare with.</param>
    /// <returns>True if both contexts represent the same UI location.</returns>
    public bool IsSameLocation(FocusContext? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Region, other.Region, StringComparison.Ordinal) &&
               string.Equals(ElementId, other.ElementId, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        string position = GetPositionLabel() ?? "";
        if (!string.IsNullOrEmpty(position))
        {
            return $"{Region}/{ElementId} ({position})";
        }

        return $"{Region}/{ElementId}";
    }

    #region Well-Known Property Keys

    /// <summary>Terraria's ItemSlot.Context value for inventory slots.</summary>
    public const string PropertySlotContext = "SlotContext";

    /// <summary>Index of the currently open chest (-1 to -5 for banks, 0+ for world chests).</summary>
    public const string PropertyChestIndex = "ChestIndex";

    /// <summary>Current Main.menuMode value.</summary>
    public const string PropertyMenuMode = "MenuMode";

    /// <summary>Row index for grid layouts (zero-based).</summary>
    public const string PropertyRow = "Row";

    /// <summary>Column index for grid layouts (zero-based).</summary>
    public const string PropertyColumn = "Column";

    /// <summary>Total number of rows in a grid layout.</summary>
    public const string PropertyTotalRows = "TotalRows";

    /// <summary>Total number of columns in a grid layout.</summary>
    public const string PropertyTotalColumns = "TotalColumns";

    /// <summary>UILinkPointNavigator.CurrentPoint value for gamepad navigation.</summary>
    public const string PropertyLinkPoint = "LinkPoint";

    /// <summary>Whether the focused element is currently selected (vs just highlighted).</summary>
    public const string PropertyIsSelected = "IsSelected";

    /// <summary>Whether the focused element is disabled/unavailable.</summary>
    public const string PropertyIsDisabled = "IsDisabled";

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a focus context for an inventory slot.
    /// </summary>
    public static FocusContext ForInventorySlot(string region, int slotIndex, int totalSlots, int slotContext)
    {
        var context = new FocusContext(region, $"Slot_{slotIndex}", slotIndex, totalSlots);
        context.Properties[PropertySlotContext] = slotContext;
        return context;
    }

    /// <summary>
    /// Creates a focus context for a storage slot (chest, bank, etc.).
    /// </summary>
    public static FocusContext ForStorageSlot(string containerName, int slotIndex, int totalSlots, int chestIndex)
    {
        var context = new FocusContext($"Storage_{containerName}", $"Slot_{slotIndex}", slotIndex, totalSlots);
        context.Properties[PropertyChestIndex] = chestIndex;
        return context;
    }

    /// <summary>
    /// Creates a focus context for a menu item.
    /// </summary>
    public static FocusContext ForMenuItem(string menuName, string itemId, int itemIndex = -1, int totalItems = -1)
    {
        return new FocusContext($"Menu_{menuName}", itemId, itemIndex, totalItems);
    }

    /// <summary>
    /// Creates a focus context for a grid cell.
    /// </summary>
    public static FocusContext ForGridCell(string gridName, int row, int column, int totalRows, int totalColumns)
    {
        int linearIndex = row * totalColumns + column;
        int totalCells = totalRows * totalColumns;

        var context = new FocusContext(gridName, $"Cell_{row}_{column}", linearIndex, totalCells);
        context.Properties[PropertyRow] = row;
        context.Properties[PropertyColumn] = column;
        context.Properties[PropertyTotalRows] = totalRows;
        context.Properties[PropertyTotalColumns] = totalColumns;
        return context;
    }

    #endregion
}
