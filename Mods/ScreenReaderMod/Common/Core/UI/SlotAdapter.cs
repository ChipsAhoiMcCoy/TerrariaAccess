#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Core.UI;

/// <summary>
/// Adapter for inventory and storage slots, wrapping Terraria's Item in a slot context.
/// This unifies handling of inventory slots, chest slots, equipment slots, and other
/// item-holding UI elements.
/// </summary>
/// <remarks>
/// Terraria slot contexts (ItemSlot.Context) include:
/// - InventoryItem (0-49 in player.inventory)
/// - InventoryCoin (50-53)
/// - InventoryAmmo (54-57)
/// - ChestItem, BankItem, VoidItem (storage containers)
/// - EquipArmor, EquipAccessory, etc. (equipment panel)
/// - ShopItem (NPC shops)
/// - CraftingMaterial (crafting ingredient slots)
///
/// This adapter abstracts these differences while preserving context information.
/// </remarks>
public class SlotAdapter : IFocusable
{
    private readonly Item? _item;
    private readonly Item[]? _itemArray;
    private readonly int _slotIndex;
    private readonly int _context;
    private readonly string _regionName;
    private readonly Func<bool>? _isFocusedFunc;
    private readonly Func<string>? _labelFunc;

    /// <summary>
    /// Creates a slot adapter from an item array and slot index.
    /// </summary>
    /// <param name="items">The item array containing this slot.</param>
    /// <param name="slotIndex">The index within the array.</param>
    /// <param name="context">The ItemSlot.Context value.</param>
    /// <param name="regionName">The name of the region containing this slot.</param>
    /// <param name="isFocusedFunc">Optional function to check if this slot is focused.</param>
    /// <param name="labelFunc">Optional custom label function.</param>
    public SlotAdapter(
        Item[] items,
        int slotIndex,
        int context,
        string regionName,
        Func<bool>? isFocusedFunc = null,
        Func<string>? labelFunc = null)
    {
        _itemArray = items ?? throw new ArgumentNullException(nameof(items));
        _slotIndex = slotIndex;
        _context = context;
        _regionName = regionName ?? "Slots";
        _isFocusedFunc = isFocusedFunc;
        _labelFunc = labelFunc;
    }

    /// <summary>
    /// Creates a slot adapter from a single item.
    /// </summary>
    /// <param name="item">The item in this slot.</param>
    /// <param name="slotIndex">The slot index (for display purposes).</param>
    /// <param name="context">The ItemSlot.Context value.</param>
    /// <param name="regionName">The name of the region containing this slot.</param>
    /// <param name="isFocusedFunc">Optional function to check if this slot is focused.</param>
    /// <param name="labelFunc">Optional custom label function.</param>
    public SlotAdapter(
        Item item,
        int slotIndex,
        int context,
        string regionName,
        Func<bool>? isFocusedFunc = null,
        Func<string>? labelFunc = null)
    {
        _item = item;
        _slotIndex = slotIndex;
        _context = context;
        _regionName = regionName ?? "Slots";
        _isFocusedFunc = isFocusedFunc;
        _labelFunc = labelFunc;
    }

    /// <summary>
    /// Gets the item in this slot, resolving from the array if necessary.
    /// </summary>
    public Item? Item
    {
        get
        {
            if (_item is not null)
            {
                return _item;
            }

            if (_itemArray is not null && _slotIndex >= 0 && _slotIndex < _itemArray.Length)
            {
                return _itemArray[_slotIndex];
            }

            return null;
        }
    }

    /// <summary>
    /// Gets whether this slot contains an item (not air).
    /// </summary>
    public bool HasItem => Item is { IsAir: false };

    /// <summary>
    /// Gets the slot index.
    /// </summary>
    public int SlotIndex => _slotIndex;

    /// <summary>
    /// Gets the ItemSlot.Context value for this slot.
    /// </summary>
    public int Context => _context;

    /// <inheritdoc/>
    public bool IsFocused => _isFocusedFunc?.Invoke() ?? false;

    /// <inheritdoc/>
    public FocusableType ElementType => FocusableType.Slot;

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public string GetNarrationLabel()
    {
        // Use custom label function if provided
        if (_labelFunc is not null)
        {
            return _labelFunc();
        }

        Item? item = Item;
        if (item is null || item.IsAir)
        {
            return GetEmptySlotLabel();
        }

        return BuildItemLabel(item);
    }

    /// <inheritdoc/>
    public string? GetDetailedDescription()
    {
        Item? item = Item;
        if (item is null || item.IsAir)
        {
            return null;
        }

        // Return tooltip-style description
        return BuildItemTooltip(item);
    }

    /// <inheritdoc/>
    public FocusContext? GetFocusContext()
    {
        if (!IsFocused)
        {
            return null;
        }

        int totalSlots = _itemArray?.Length ?? 1;
        var context = FocusContext.ForInventorySlot(_regionName, _slotIndex, totalSlots, _context);
        return context;
    }

    /// <inheritdoc/>
    public string GetIdentifier()
    {
        Item? item = Item;
        if (item is null || item.IsAir)
        {
            return $"Slot_{_regionName}_{_slotIndex}_Empty";
        }

        return $"Slot_{_regionName}_{_slotIndex}_{item.type}_{item.prefix}_{item.stack}";
    }

    /// <summary>
    /// Gets the label for an empty slot based on context.
    /// </summary>
    protected virtual string GetEmptySlotLabel()
    {
        // Context-specific empty slot labels
        return _context switch
        {
            ItemSlot.Context.EquipArmor => GetEquipSlotLabel(_slotIndex, "Empty armor slot"),
            ItemSlot.Context.EquipArmorVanity => GetEquipSlotLabel(_slotIndex, "Empty vanity slot"),
            ItemSlot.Context.EquipAccessory => "Empty accessory slot",
            ItemSlot.Context.EquipAccessoryVanity => "Empty vanity accessory slot",
            ItemSlot.Context.EquipDye => "Empty dye slot",
            ItemSlot.Context.EquipMiscDye => "Empty misc dye slot",
            ItemSlot.Context.EquipGrapple => "Empty hook slot",
            ItemSlot.Context.EquipMount => "Empty mount slot",
            ItemSlot.Context.EquipMinecart => "Empty minecart slot",
            ItemSlot.Context.EquipPet => "Empty pet slot",
            ItemSlot.Context.EquipLight => "Empty light pet slot",
            ItemSlot.Context.TrashItem => "Empty trash slot",
            ItemSlot.Context.InventoryCoin => "Empty coin slot",
            ItemSlot.Context.InventoryAmmo => "Empty ammo slot",
            ItemSlot.Context.GuideItem => "Guide material slot",
            ItemSlot.Context.PrefixItem => "Reforge slot",
            _ => "Empty slot",
        };
    }

    /// <summary>
    /// Gets a specific equipment slot label.
    /// </summary>
    private static string GetEquipSlotLabel(int slot, string fallback)
    {
        return slot switch
        {
            0 => "Head slot",
            1 => "Chest slot",
            2 => "Legs slot",
            _ => fallback,
        };
    }

    /// <summary>
    /// Builds the narration label for an item.
    /// </summary>
    protected virtual string BuildItemLabel(Item item)
    {
        var parts = new List<string>();

        // Item name (with prefix if applicable)
        string name = item.Name ?? item.HoverName ?? $"Item {item.type}";
        if (item.prefix > 0)
        {
            // Prefix is already included in HoverName
            if (!string.IsNullOrEmpty(item.HoverName))
            {
                name = item.HoverName;
            }
        }

        parts.Add(name);

        // Stack count (if stackable and > 1)
        if (item.stack > 1)
        {
            parts.Add($"x{item.stack}");
        }

        // Favorited status
        if (item.favorited)
        {
            parts.Add("Favorited");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Builds detailed tooltip text for an item.
    /// </summary>
    protected virtual string? BuildItemTooltip(Item item)
    {
        var parts = new List<string>();

        // Damage and knockback for weapons
        if (item.damage > 0)
        {
            string damageType = GetDamageTypeName(item);
            parts.Add($"{item.damage} {damageType} damage");

            if (item.knockBack > 0)
            {
                parts.Add($"{item.knockBack:0.#} knockback");
            }
        }

        // Defense for armor
        if (item.defense > 0)
        {
            parts.Add($"{item.defense} defense");
        }

        // Healing for potions
        if (item.healLife > 0)
        {
            parts.Add($"Restores {item.healLife} life");
        }

        if (item.healMana > 0)
        {
            parts.Add($"Restores {item.healMana} mana");
        }

        // Buff duration
        if (item.buffTime > 0)
        {
            int seconds = item.buffTime / 60;
            parts.Add($"{seconds} second buff");
        }

        // Value
        if (item.value > 0)
        {
            parts.Add($"Value: {FormatCoinValue(item.value)}");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(". ", parts);
    }

    /// <summary>
    /// Gets the damage type name for an item.
    /// </summary>
    private static string GetDamageTypeName(Item item)
    {
        if (item.DamageType is null)
        {
            return "base";
        }

        string typeName = item.DamageType.DisplayName?.Value ?? item.DamageType.GetType().Name;
        return typeName.Replace("DamageClass", "").ToLowerInvariant();
    }

    /// <summary>
    /// Formats a copper value as coins.
    /// </summary>
    private static string FormatCoinValue(long copper)
    {
        var parts = new List<string>();

        long platinum = copper / 1_000_000;
        copper %= 1_000_000;
        long gold = copper / 10_000;
        copper %= 10_000;
        long silver = copper / 100;
        copper %= 100;

        if (platinum > 0) parts.Add($"{platinum} platinum");
        if (gold > 0) parts.Add($"{gold} gold");
        if (silver > 0) parts.Add($"{silver} silver");
        if (copper > 0) parts.Add($"{copper} copper");

        return parts.Count > 0 ? string.Join(" ", parts) : "0 copper";
    }
}

/// <summary>
/// Represents a navigable region of inventory/storage slots.
/// </summary>
public class SlotRegionAdapter : INavigable
{
    private readonly List<SlotAdapter> _slots = new();
    private readonly Func<int> _getCurrentIndex;
    private readonly Action<int>? _setCurrentIndex;
    private readonly Func<bool>? _isActiveFunc;

    /// <summary>
    /// Creates a slot region adapter.
    /// </summary>
    /// <param name="regionName">Name of this slot region.</param>
    /// <param name="getCurrentIndex">Function to get the current focus index.</param>
    /// <param name="setCurrentIndex">Optional function to set the focus index.</param>
    /// <param name="isActiveFunc">Optional function to check if the region is active.</param>
    public SlotRegionAdapter(
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
        Layout = NavigationLayout.Grid,
        GridColumns = 10,
        WrapsHorizontally = true,
        WrapsVertically = false,
        AnnounceRegionOnEntry = true,
        AnnouncePosition = true,
    };

    /// <summary>
    /// Populates the region with slots from an item array.
    /// </summary>
    /// <param name="items">The item array.</param>
    /// <param name="startIndex">Starting index in the array.</param>
    /// <param name="count">Number of slots to include.</param>
    /// <param name="context">The ItemSlot.Context value.</param>
    public void SetSlots(Item[] items, int startIndex, int count, int context)
    {
        _slots.Clear();

        for (int i = 0; i < count; i++)
        {
            int slotIndex = startIndex + i;
            int capturedIndex = i;
            var adapter = new SlotAdapter(
                items,
                slotIndex,
                context,
                RegionName,
                () => CurrentFocusIndex == capturedIndex);
            _slots.Add(adapter);
        }
    }

    /// <summary>
    /// Adds a single slot to the region.
    /// </summary>
    public void AddSlot(SlotAdapter slot)
    {
        _slots.Add(slot);
    }

    /// <inheritdoc/>
    public bool TryNavigate(NavigationDirection direction)
    {
        if (!IsActive || _slots.Count == 0)
        {
            return false;
        }

        int current = CurrentFocusIndex;
        int columns = Behavior.GridColumns;
        int target = direction switch
        {
            NavigationDirection.Left => current - 1,
            NavigationDirection.Right => current + 1,
            NavigationDirection.Up => current - columns,
            NavigationDirection.Down => current + columns,
            NavigationDirection.TabPrevious => current - 1,
            NavigationDirection.TabNext => current + 1,
            _ => -1,
        };

        // Handle wrapping
        if (target < 0)
        {
            if (direction is NavigationDirection.Left or NavigationDirection.TabPrevious && Behavior.WrapsHorizontally)
            {
                // Wrap to end of current row
                int row = current / columns;
                target = Math.Min((row + 1) * columns - 1, _slots.Count - 1);
            }
            else if (direction == NavigationDirection.Up && Behavior.WrapsVertically)
            {
                // Wrap to bottom row, same column
                int col = current % columns;
                int lastRow = (_slots.Count - 1) / columns;
                target = Math.Min(lastRow * columns + col, _slots.Count - 1);
            }
            else
            {
                return false;
            }
        }
        else if (target >= _slots.Count)
        {
            if (direction is NavigationDirection.Right or NavigationDirection.TabNext && Behavior.WrapsHorizontally)
            {
                // Wrap to start of current row
                int row = current / columns;
                target = row * columns;
            }
            else if (direction == NavigationDirection.Down && Behavior.WrapsVertically)
            {
                // Wrap to top row, same column
                int col = current % columns;
                target = col;
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
        if (index < 0 || index >= _slots.Count)
        {
            return null;
        }

        return _slots[index];
    }

    /// <inheritdoc/>
    public IReadOnlyList<IFocusable> GetFocusableElements() => _slots;

    /// <inheritdoc/>
    public bool TrySetFocus(int index)
    {
        if (index < 0 || index >= _slots.Count)
        {
            return false;
        }

        _setCurrentIndex?.Invoke(index);
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetFocus(IFocusable element)
    {
        if (element is not SlotAdapter slot)
        {
            return false;
        }

        int index = _slots.IndexOf(slot);
        if (index < 0)
        {
            return false;
        }

        return TrySetFocus(index);
    }
}
