#nullable enable
using System;
using TerrariaAccess.Common.Utilities;
using Terraria;

namespace TerrariaAccess.Common.Systems.Inventory;

/// <summary>
/// Coordinates inventory, crafting, guide, and reforge narration.
/// This is the main entry point for the refactored inventory narration system.
/// Use this class instead of directly instantiating individual narrators.
/// </summary>
internal sealed class InventoryNarrationCoordinator
{
    private readonly SlotFocusTracker _focusTracker;
    private readonly RegionDetector _regionDetector;
    private readonly InventoryNarrator _inventoryNarrator;
    private readonly CraftingNarrator _craftingNarrator;
    private readonly GuideMenuNarrator _guideNarrator;
    private readonly ReforgeNarrator _reforgeNarrator;

    public InventoryNarrationCoordinator()
    {
        _focusTracker = new SlotFocusTracker();
        _regionDetector = new RegionDetector();
        _inventoryNarrator = new InventoryNarrator(_focusTracker);
        _craftingNarrator = new CraftingNarrator(_regionDetector);
        _guideNarrator = new GuideMenuNarrator();
        _reforgeNarrator = new ReforgeNarrator();

        // Wire up events
        _inventoryNarrator.InventoryOpened += OnInventoryOpened;
    }

    /// <summary>
    /// Event raised when the inventory transitions from closed to open.
    /// </summary>
    public event Action? InventoryOpened
    {
        add => _inventoryNarrator.InventoryOpened += value;
        remove => _inventoryNarrator.InventoryOpened -= value;
    }

    /// <summary>
    /// Event raised when the inventory transitions from open to closed.
    /// </summary>
    public event Action? InventoryClosed
    {
        add => _inventoryNarrator.InventoryClosed += value;
        remove => _inventoryNarrator.InventoryClosed -= value;
    }

    /// <summary>
    /// Event raised when a chest/storage container opens.
    /// </summary>
    public event Action<int>? ChestOpened
    {
        add => _inventoryNarrator.ChestOpened += value;
        remove => _inventoryNarrator.ChestOpened -= value;
    }

    /// <summary>
    /// Gets the slot focus tracker for external access (e.g., ItemSlot hooks).
    /// </summary>
    public SlotFocusTracker FocusTracker => _focusTracker;

    /// <summary>
    /// Gets the region detector for external access.
    /// </summary>
    public RegionDetector RegionDetector => _regionDetector;

    /// <summary>
    /// Gets the inventory narrator for direct access when needed.
    /// </summary>
    public InventoryNarrator InventoryNarrator => _inventoryNarrator;

    /// <summary>
    /// Gets the crafting narrator for direct access when needed.
    /// </summary>
    public CraftingNarrator CraftingNarrator => _craftingNarrator;

    /// <summary>
    /// Records a slot focus from an ItemSlot hook (array-based slot).
    /// </summary>
    public void RecordFocus(Item[] inventory, int context, int slot)
    {
        _inventoryNarrator.RecordFocus(inventory, context, slot);
    }

    /// <summary>
    /// Records a slot focus from an ItemSlot hook (single item).
    /// Also captures recipe hover for crafting contexts.
    /// </summary>
    public void RecordFocus(Item item, int context)
    {
        _inventoryNarrator.RecordFocus(item, context, (i, c) => _craftingNarrator.TryCaptureRecipeHover(i, c));
    }

    /// <summary>
    /// Records a mouse text snapshot for tooltip extraction.
    /// </summary>
    public void RecordMouseTextSnapshot(string? text)
    {
        MouseTextCapture.RecordSnapshot(text);
    }

    /// <summary>
    /// Main update method called each frame.
    /// </summary>
    public void Update(Player player)
    {
        if (Main.ingameOptionsWindow)
        {
            Reset();
            return;
        }

        if (!InventoryNarrator.IsInventoryUiOpen(player))
        {
            Reset();
            return;
        }

        // Update specialized narrators first
        _guideNarrator.Update();
        _reforgeNarrator.Update(player);

        // Update crafting narrator with context validation
        _craftingNarrator.Update(player, (point, ctx) =>
        {
            if (_inventoryNarrator.TryGetContextForLinkPoint(point, out int context))
            {
                return ItemSlotContextFacts.IsCraftingContext(context);
            }
            return false;
        });

        // Update main inventory narrator with crafting grid handler
        _inventoryNarrator.Update(player, (availableIndex) =>
            _craftingNarrator.TryFocusRecipeAtAvailableIndex(availableIndex));
    }

    /// <summary>
    /// Forces a complete reset of all narrator state.
    /// </summary>
    public void ForceReset()
    {
        Reset();
    }

    /// <summary>
    /// Resets static caches (call on mod unload).
    /// </summary>
    public static void ResetStaticCaches()
    {
        MouseTextCapture.Reset();
    }

    /// <summary>
    /// Checks if the inventory UI is currently open.
    /// </summary>
    public static bool IsInventoryUiOpen(Player player)
    {
        return InventoryNarrator.IsInventoryUiOpen(player);
    }

    /// <summary>
    /// Checks if a link point is a special inventory button.
    /// </summary>
    public static bool IsSpecialInventoryPoint(int point)
    {
        return InventoryNarrator.IsSpecialInventoryPoint(point);
    }

    /// <summary>
    /// Gets requirement tooltip details for an item if it's a craftable recipe result.
    /// </summary>
    public string? TryGetRequirementTooltipDetails(Item item, bool locationIsEmpty)
    {
        return _craftingNarrator.TryGetRequirementTooltipDetails(item, locationIsEmpty);
    }

    /// <summary>
    /// Attempts to capture a hovered recipe from an item.
    /// </summary>
    public bool TryCaptureHoveredRecipe(Item item)
    {
        return _craftingNarrator.TryCaptureHoveredRecipe(item);
    }

    /// <summary>
    /// Attempts to focus a recipe at the given available index.
    /// </summary>
    public bool TryFocusRecipeAtAvailableIndex(int availableIndex)
    {
        return _craftingNarrator.TryFocusRecipeAtAvailableIndex(availableIndex);
    }

    /// <summary>
    /// Tries to get the cached context for a link point.
    /// </summary>
    public bool TryGetContextForLinkPoint(int point, out int context)
    {
        return _inventoryNarrator.TryGetContextForLinkPoint(point, out context);
    }

    /// <summary>
    /// Tries to get the cached item and context for a link point.
    /// </summary>
    public bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
    {
        return _inventoryNarrator.TryGetItemForLinkPoint(point, out item, out context);
    }

    private void Reset()
    {
        _inventoryNarrator.ForceReset();
        _craftingNarrator.ForceReset();
        _guideNarrator.Reset();
        _reforgeNarrator.Reset();
    }

    private void OnInventoryOpened()
    {
        _craftingNarrator.OnInventoryOpened();
    }
}
