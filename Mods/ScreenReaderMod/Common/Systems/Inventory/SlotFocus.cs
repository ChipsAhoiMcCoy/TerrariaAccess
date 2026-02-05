#nullable enable
using Terraria;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Represents a focused slot in the inventory UI.
/// Can represent either a slot within an item array or a single item reference.
/// </summary>
internal readonly struct SlotFocus
{
    public SlotFocus(Item[]? items, Item? singleItem, int context, int slot)
    {
        Items = items;
        SingleItem = singleItem;
        Context = context;
        Slot = slot;
    }

    /// <summary>
    /// The item array containing the focused slot (e.g., player.inventory, chest.item).
    /// Null when focusing on a single item reference.
    /// </summary>
    public Item[]? Items { get; }

    /// <summary>
    /// A single item reference when not part of an array (e.g., crafting result hover).
    /// Null when focusing on a slot within an array.
    /// </summary>
    public Item? SingleItem { get; }

    /// <summary>
    /// The ItemSlot context identifier (e.g., ItemSlot.Context.InventoryItem).
    /// </summary>
    public int Context { get; }

    /// <summary>
    /// The slot index within the Items array. -1 when using SingleItem.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// Validates that this focus refers to a valid item.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Items is Item[] items)
            {
                return (uint)Slot < (uint)items.Length;
            }
            return SingleItem is not null;
        }
    }

    /// <summary>
    /// Gets the item this focus refers to, or null if invalid.
    /// </summary>
    public Item? GetItem()
    {
        if (Items is Item[] items)
        {
            int index = Slot;
            if ((uint)index < (uint)items.Length)
            {
                return items[index];
            }
            return null;
        }
        return SingleItem;
    }
}
