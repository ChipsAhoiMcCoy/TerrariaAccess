#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Services = ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Handles narration for the inventory UI, including items, slots, regions, and special buttons.
/// Uses composition of SlotFocusTracker, RegionDetector, ItemDescriber, and TooltipBuilder.
/// </summary>
internal sealed class InventoryNarrator
{
    #region Constants

    private const int InventoryOpenGracePeriod = 3;
    private const int ChestOpenGracePeriod = 10;
    private const int CraftingListLinkPointStart = 1500;
    private const int CraftingListLinkPointEnd = 2000;
    private const int GamepadCraftingGridStart = 700;
    private const int GamepadCraftingListStart = 1500;

    private static readonly Services.UiNarrationArea InventoryNarrationAreas =
        Services.UiNarrationArea.Inventory |
        Services.UiNarrationArea.Storage |
        Services.UiNarrationArea.Creative |
        Services.UiNarrationArea.Reforge |
        Services.UiNarrationArea.Shop |
        Services.UiNarrationArea.Guide;

    #endregion

    #region State

    private readonly MenuUiSelectionTracker _inGameUiTracker = new();
    private readonly NarrationHistory _narrationHistory = new();
    private readonly SlotFocusTracker _focusTracker;
    private readonly RegionDetector _regionDetector = new();
    private readonly SpecialSelectionHandler _specialSelections = new();

    private SlotFocus? _currentFocus;
    private string? _lastFocusKey;
    private ItemIdentity _lastAnnouncedItemIdentity;
    private bool _wasInventoryOpen;
    private int _lastChestIndex = -1;
    private int _inventoryOpenGraceFrames;
    private int _chestOpenGraceFrames;

    private static readonly bool NarrationDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_NARRATION"));
    private static readonly bool InputDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));
    private static int _lastLoggedFocusLinkPoint = -999;
    private static string? _lastLoggedFocusItemName;

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the inventory transitions from closed to open.
    /// </summary>
    public event Action? InventoryOpened;

    /// <summary>
    /// Event raised when the inventory transitions from open to closed.
    /// </summary>
    public event Action? InventoryClosed;

    /// <summary>
    /// Event raised when a chest/storage container opens.
    /// </summary>
    public event Action<int>? ChestOpened;

    #endregion

    #region Constructor

    public InventoryNarrator(SlotFocusTracker focusTracker)
    {
        _focusTracker = focusTracker ?? throw new ArgumentNullException(nameof(focusTracker));
    }

    #endregion

    #region Public API

    /// <summary>
    /// Records a slot focus from an ItemSlot hook (array-based slot).
    /// </summary>
    public void RecordFocus(Item[] inventory, int context, int slot)
    {
        SlotFocus focus = new(inventory, null, context, slot);
        _focusTracker.Capture(focus);
    }

    /// <summary>
    /// Records a slot focus from an ItemSlot hook (single item).
    /// </summary>
    public void RecordFocus(Item item, int context, Action<Item, int>? craftingRecipeHoverCallback = null)
    {
        SlotFocus focus = new(null, item, context, -1);
        craftingRecipeHoverCallback?.Invoke(item, context);
        _focusTracker.Capture(focus);
    }

    /// <summary>
    /// Main update method called each frame when the player inventory might be open.
    /// </summary>
    public void Update(Player player, Func<int, bool>? craftingGridFocusHandler = null)
    {
        if (Main.ingameOptionsWindow)
        {
            Reset();
            return;
        }

        bool isInventoryOpen = IsInventoryUiOpen(player);
        if (!isInventoryOpen)
        {
            if (_wasInventoryOpen)
            {
                OnInventoryJustClosed();
            }
            _wasInventoryOpen = false;
            Reset();
            return;
        }

        // Detect inventory just opened
        if (!_wasInventoryOpen)
        {
            _wasInventoryOpen = true;
            OnInventoryJustOpened();
        }

        // Detect chest/storage transition
        int currentChest = player.chest;
        if (currentChest != _lastChestIndex)
        {
            if (_lastChestIndex == -1 && currentChest != -1)
            {
                ChestOpened?.Invoke(currentChest);
                _chestOpenGraceFrames = ChestOpenGracePeriod;
            }
            _lastChestIndex = currentChest;
        }

        if (_chestOpenGraceFrames > 0)
        {
            _chestOpenGraceFrames--;
        }

        bool usingGamepad = PlayerInput.UsingGamepadUI;
        if (usingGamepad)
        {
            ClearSpecialLinkPointFocus();
        }

        SlotFocus? nextFocus = _focusTracker.Consume(usingGamepad);
        _currentFocus = nextFocus.HasValue && nextFocus.Value.IsValid ? nextFocus : null;

        LogFocusDebugState(usingGamepad, nextFocus);

        HandleMouseItem();
        HandleHoverItem(player, craftingGridFocusHandler);
    }

    /// <summary>
    /// Forces a complete reset of all state.
    /// </summary>
    public void ForceReset()
    {
        Reset();
    }

    /// <summary>
    /// Gets the current slot focus.
    /// </summary>
    public SlotFocus? CurrentFocus => _currentFocus;

    /// <summary>
    /// Gets the focus tracker for external access (e.g., CraftingNarrator).
    /// </summary>
    public SlotFocusTracker FocusTracker => _focusTracker;

    /// <summary>
    /// Gets the region detector for external access.
    /// </summary>
    public RegionDetector RegionDetector => _regionDetector;

    /// <summary>
    /// Checks if the inventory UI is currently open.
    /// </summary>
    public static bool IsInventoryUiOpen(Player player)
    {
        return Main.playerInventory ||
               player.chest != -1 ||
               Main.npcShop != 0 ||
               Main.InGuideCraftMenu ||
               Main.InReforgeMenu;
    }

    /// <summary>
    /// Checks if a link point is a special inventory button.
    /// </summary>
    public static bool IsSpecialInventoryPoint(int point)
    {
        return SpecialSelectionHandler.IsSpecialInventoryPoint(point);
    }

    /// <summary>
    /// Tries to get the cached context for a link point.
    /// </summary>
    public bool TryGetContextForLinkPoint(int point, out int context)
    {
        return _focusTracker.TryGetContextForLinkPoint(point, out context);
    }

    /// <summary>
    /// Tries to get the cached item and context for a link point.
    /// </summary>
    public bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
    {
        return _focusTracker.TryGetItemForLinkPoint(point, out item, out context);
    }

    #endregion

    #region Private Methods

    private void ClearSpecialLinkPointFocus()
    {
        int point = UILinkPointNavigator.CurrentPoint;
        if (point < 0 || !IsSpecialInventoryPoint(point))
        {
            return;
        }

        _currentFocus = null;
        _focusTracker.ClearSpecialLinkPoint(point);
    }

    private void HandleMouseItem()
    {
        Item mouse = Main.mouseItem;
        ItemIdentity identity = ItemIdentity.From(mouse);
        if (identity.IsAir)
        {
            _narrationHistory.Reset(NarrationKind.MouseItem);
            return;
        }

        string message = $"Holding {ItemDescriber.ComposeLabel(mouse)}";
        TryAnnounceCue(
            NarrationCue.ForMouse(identity, message),
            allowedAreas: InventoryNarrationAreas | Services.UiNarrationArea.Crafting | Services.UiNarrationArea.Guide);
    }

    private void HandleHoverItem(Player player, Func<int, bool>? craftingGridFocusHandler)
    {
        if (Main.editChest)
        {
            ResetHoverSlotsAndTooltips();
            return;
        }

        bool usingGamepad = PlayerInput.UsingGamepadUI;
        int currentPoint = usingGamepad ? UILinkPointNavigator.CurrentPoint : -1;
        int craftingAvailableIndex = -1;
        bool selectingSpecial = usingGamepad && currentPoint >= 0 && IsSpecialInventoryPoint(currentPoint);
        bool inGamepadCraftingGrid = usingGamepad && TryGetGamepadCraftingAvailableIndex(currentPoint, out craftingAvailableIndex);

        if (!selectingSpecial)
        {
            _specialSelections.Clear();
        }

        if (inGamepadCraftingGrid)
        {
            UiAreaNarrationContext.RecordArea(Services.UiNarrationArea.Crafting);
            if (craftingAvailableIndex >= 0 && craftingGridFocusHandler != null && craftingGridFocusHandler(craftingAvailableIndex))
            {
                PlayCraftingTickIfNew($"craft-{craftingAvailableIndex}", craftingAvailableIndex);
                ResetHoverSlotsAndTooltips();
                return;
            }
        }

        // Handle crafting list link points
        bool inGamepadCraftingList = usingGamepad &&
            currentPoint >= CraftingListLinkPointStart &&
            currentPoint < CraftingListLinkPointEnd;
        if (inGamepadCraftingList)
        {
            UiAreaNarrationContext.RecordArea(Services.UiNarrationArea.Crafting);
        }

        SlotFocus? focus = (selectingSpecial || inGamepadCraftingGrid || inGamepadCraftingList) ? null : _currentFocus;
        Item? focusedItem = (selectingSpecial || inGamepadCraftingGrid || inGamepadCraftingList) ? null : focus?.GetItem();

        if (focus.HasValue)
        {
            UiAreaNarrationContext.RecordSlotContext(focus.Value.Context);
        }
        else if (selectingSpecial)
        {
            UiAreaNarrationContext.RecordArea(Services.UiNarrationArea.Inventory);
        }

        bool usingGamepadFocus = usingGamepad && !selectingSpecial && focusedItem is not null;
        Item hover = ResolveHoverItem(usingGamepad, usingGamepadFocus, focusedItem);
        ItemIdentity identity = ItemIdentity.From(hover);
        string location = DescribeLocation(player, identity, focus);

        string rawTooltip = ResolveRawTooltip(usingGamepad, usingGamepadFocus, hover);
        string normalizedTooltip = GlyphTagFormatter.Normalize(rawTooltip);

        if (TryAnnounceSpecialSelection(player, identity.IsAir, location))
        {
            return;
        }

        HoverTarget target = new(hover, identity, location, rawTooltip, normalizedTooltip, focus, AllowMouseText: !usingGamepadFocus);
        string focusKey = BuildFocusKey(target, focus, inGamepadCraftingGrid ? craftingAvailableIndex : (int?)null);
        PlayTickIfNew(focusKey, focus);

        if (target.HasItem)
        {
            if (_chestOpenGraceFrames > 0 && player.chest != -1)
            {
                _chestOpenGraceFrames = 0;
            }
            AnnounceItemHover(player, target);
            return;
        }

        if (TryAnnounceEmptySlot(player, target))
        {
            return;
        }

        if (TryAnnounceMouseText())
        {
            return;
        }

        if (TryAnnounceInGameUiHover())
        {
            return;
        }

        TryAnnounceTooltipFallback(target);
    }

    private void AnnounceItemHover(Player player, HoverTarget target)
    {
        InventoryRegion currentRegion = RegionDetector.ResolveRegion(target.Focus, player);

        if (currentRegion == InventoryRegion.None && PlayerInput.UsingGamepadUI)
        {
            currentRegion = RegionDetector.ResolveRegionFromLinkPoint(UILinkPointNavigator.CurrentPoint);
        }

        string? regionPrefix = _regionDetector.GetRegionPrefixIfChanged(currentRegion, target.Focus, player);

        string label = ItemDescriber.ComposeLabel(target.Item);
        string message = string.IsNullOrEmpty(target.Location) ? label : $"{label}, {target.Location}";

        string? details = TooltipBuilder.BuildDetails(target.Item, target.RawTooltip, allowMouseText: target.AllowMouseText);
        string? priceDetails = ItemDescriber.BuildShopPriceDetails(player, target.Item, target.Focus);
        details = MergeDetails(details, priceDetails);
        string? sellDetails = ItemDescriber.BuildSellPriceDetails(player, target.Item);
        details = MergeDetails(details, sellDetails);
        string? reforgeDetails = ItemDescriber.BuildReforgePriceDetails(player, target.Item, target.Focus);
        details = MergeDetails(details, reforgeDetails);

        string combined = CombineItemAnnouncement(message, details);
        int slotSignature = ComputeSlotSignature(target.Focus);
        if (TryAnnounceCue(NarrationCue.ForItem(target.Identity, combined, target.Location, target.NormalizedTooltip, details, slotSignature), focus: target.Focus, regionPrefix: regionPrefix))
        {
            _lastAnnouncedItemIdentity = target.Identity;
            _narrationHistory.Reset(NarrationKind.EmptySlot);
            _narrationHistory.Reset(NarrationKind.Tooltip);
        }
    }

    private bool TryAnnounceEmptySlot(Player player, HoverTarget target)
    {
        if (_chestOpenGraceFrames > 0)
        {
            return false;
        }

        if (!target.HasLocation)
        {
            return false;
        }

        InventoryRegion currentRegion = RegionDetector.ResolveRegion(target.Focus, player);

        if (currentRegion == InventoryRegion.None && PlayerInput.UsingGamepadUI)
        {
            currentRegion = RegionDetector.ResolveRegionFromLinkPoint(UILinkPointNavigator.CurrentPoint);
        }

        string? regionPrefix = _regionDetector.GetRegionPrefixIfChanged(currentRegion, target.Focus, player);

        string message = $"Empty, {target.Location}";
        int slotSignature = ComputeSlotSignature(target.Focus);
        if (TryAnnounceCue(NarrationCue.ForEmpty(message, target.Location, slotSignature), focus: target.Focus, regionPrefix: regionPrefix))
        {
            _narrationHistory.Reset(NarrationKind.HoverItem);
            _narrationHistory.Reset(NarrationKind.Tooltip);
        }

        return true;
    }

    private bool TryAnnounceMouseText()
    {
        if (_inventoryOpenGraceFrames > 0)
        {
            _inventoryOpenGraceFrames--;
            return false;
        }

        string? mouseText = MouseTextCapture.TryGetMouseText();
        if (string.IsNullOrWhiteSpace(mouseText))
        {
            return false;
        }

        string trimmedMouseText = GlyphTagFormatter.Normalize(mouseText.Trim());
        if (TryAnnounceCue(NarrationCue.ForTooltip(trimmedMouseText)))
        {
            ResetHoverSlotCues();
        }

        return true;
    }

    private void TryAnnounceTooltipFallback(HoverTarget target)
    {
        if (!target.HasTooltip)
        {
            return;
        }

        if (!target.Identity.IsAir && target.Identity.Equals(_lastAnnouncedItemIdentity))
        {
            return;
        }

        if (TryAnnounceCue(NarrationCue.ForTooltip(target.NormalizedTooltip)))
        {
            _lastAnnouncedItemIdentity = target.Identity;
            ResetHoverSlotCues();
        }
    }

    private bool TryAnnounceSpecialSelection(Player player, bool hoverIsAir, string? location)
    {
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        string? label = _specialSelections.GetLabel(currentPoint, hoverIsAir, location);
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (!_specialSelections.ShouldAnnounce(currentPoint))
        {
            return true;
        }

        InventoryRegion currentRegion = _specialSelections.ResolveRegion(currentPoint);
        string? regionPrefix = null;
        if (currentRegion != InventoryRegion.None && currentRegion != _regionDetector.LastAnnouncedRegion)
        {
            regionPrefix = RegionDetector.GetRegionDisplayName(currentRegion);
        }

        PlayTickIfNew($"special-{currentPoint}");
        _currentFocus = null;
        _focusTracker.ClearSpecialLinkPoint(currentPoint);

        ResetHoverSlotsAndTooltips();
        _narrationHistory.Reset(NarrationKind.SpecialSelection);
        UiAreaNarrationContext.RecordArea(Services.UiNarrationArea.Inventory);
        _specialSelections.Record(currentPoint);
        TryAnnounceCue(NarrationCue.ForSpecial(label), force: true, regionPrefix: regionPrefix);
        return true;
    }

    private bool TryAnnounceInGameUiHover()
    {
        if (!_inGameUiTracker.TryGetHoverLabel(Main.InGameUI, out MenuUiLabel hover))
        {
            return false;
        }

        string cleaned = TextSanitizer.Clean(hover.Text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (hover.IsNew)
        {
            _narrationHistory.Reset(NarrationKind.UiHover);
        }

        if (TryAnnounceCue(NarrationCue.ForUi(cleaned), allowedAreas: Services.UiNarrationArea.Unknown))
        {
            ResetHoverSlotCues();
        }

        return true;
    }

    private static Item ResolveHoverItem(bool usingGamepad, bool usingGamepadFocus, Item? focusedItem)
    {
        if (usingGamepadFocus && focusedItem is not null)
        {
            return focusedItem;
        }

        return usingGamepad ? new Item() : Main.HoverItem;
    }

    private static string ResolveRawTooltip(bool usingGamepad, bool usingGamepadFocus, Item hover)
    {
        if (usingGamepadFocus)
        {
            return ItemDescriber.ComposeName(hover);
        }

        if (usingGamepad)
        {
            return string.Empty;
        }

        return Main.hoverItemName ?? string.Empty;
    }

    private string DescribeLocation(Player player, ItemIdentity identity, SlotFocus? focus)
    {
        if (focus.HasValue)
        {
            string focused = DescribeFocusedSlot(player, focus.Value);
            if (!string.IsNullOrWhiteSpace(focused))
            {
                return focused;
            }
        }

        // Fallback location resolution from player inventories
        if (TryMatchItem(player.inventory, identity, out int inventoryIndex))
        {
            return SlotContextFormatter.DescribeInventorySlot(inventoryIndex);
        }

        if (MatchesItem(player.trashItem, identity))
        {
            return "Trash slot";
        }

        if (TryMatchItem(player.armor, identity, out int armorIndex))
        {
            return SlotContextFormatter.DescribeArmorSlot(armorIndex);
        }

        if (TryMatchItem(player.dye, identity, out int dyeIndex))
        {
            return $"Dye slot {dyeIndex + 1}";
        }

        if (TryMatchItem(player.miscEquips, identity, out int miscIndex))
        {
            return $"Misc equipment slot {miscIndex + 1}";
        }

        if (TryMatchItem(player.miscDyes, identity, out int miscDyeIndex))
        {
            return $"Misc dye slot {miscDyeIndex + 1}";
        }

        int chestIndex = player.chest;
        if (chestIndex != -1)
        {
            Item[]? containerItems = RegionDetector.GetContainerItems(player, chestIndex);
            if (containerItems is not null && TryMatchItem(containerItems, identity, out int containerSlot))
            {
                return $"Slot {containerSlot + 1}";
            }

            if (PlayerInput.UsingGamepadUI)
            {
                int currentPoint = UILinkPointNavigator.CurrentPoint;
                if (currentPoint >= 400 && currentPoint < 440)
                {
                    int slotFromPoint = currentPoint - 400;
                    return $"Slot {slotFromPoint + 1}";
                }
            }

            return string.Empty;
        }

        if (Main.npcShop > 0)
        {
            Chest[]? shops = Main.instance?.shop;
            if (shops is not null && Main.npcShop < shops.Length)
            {
                Item[]? shopItems = shops[Main.npcShop]?.item;
                if (shopItems is not null && TryMatchItem(shopItems, identity, out int shopSlot))
                {
                    return $"Slot {shopSlot + 1}";
                }
            }
        }

        return string.Empty;
    }

    private static string DescribeFocusedSlot(Player player, SlotFocus focus)
    {
        if (focus.Items is Item[] items)
        {
            if (ReferenceEquals(items, player.inventory))
            {
                return SlotContextFormatter.DescribeInventorySlot(focus.Slot);
            }

            if (ReferenceEquals(items, player.bank.item) ||
                ReferenceEquals(items, player.bank2.item) ||
                ReferenceEquals(items, player.bank3.item) ||
                ReferenceEquals(items, player.bank4.item))
            {
                return $"Slot {focus.Slot + 1}";
            }

            if (ReferenceEquals(items, player.armor))
            {
                return SlotContextFormatter.DescribeArmorSlot(focus.Slot);
            }

            if (ReferenceEquals(items, player.dye))
            {
                return $"Dye slot {focus.Slot + 1}";
            }

            if (ReferenceEquals(items, player.miscEquips))
            {
                return $"Misc equipment slot {focus.Slot + 1}";
            }

            if (ReferenceEquals(items, player.miscDyes))
            {
                return $"Misc dye slot {focus.Slot + 1}";
            }

            if (Main.chest is not null)
            {
                for (int i = 0; i < Main.chest.Length; i++)
                {
                    if (ReferenceEquals(Main.chest[i]?.item, items))
                    {
                        return focus.Slot >= 0 ? $"Slot {focus.Slot + 1}" : "Slot";
                    }
                }
            }

            Chest[]? shops = Main.instance?.shop;
            if (shops is not null)
            {
                for (int i = 0; i < shops.Length; i++)
                {
                    if (ReferenceEquals(shops[i]?.item, items))
                    {
                        return focus.Slot >= 0 ? $"Slot {focus.Slot + 1}" : "Slot";
                    }
                }
            }
        }

        int context = Math.Abs(focus.Context);
        if (context == ItemSlot.Context.TrashItem)
        {
            return "Trash slot";
        }

        if (context == ItemSlot.Context.CraftingMaterial)
        {
            return "Crafting slot";
        }

        if (context == ItemSlot.Context.PrefixItem)
        {
            return "Reforge slot";
        }

        return string.Empty;
    }

    private static bool TryMatchItem(Item[] items, ItemIdentity identity, out int index)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (MatchesItem(items[i], identity))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool MatchesItem(Item item, ItemIdentity identity)
    {
        if (item is null || item.IsAir)
        {
            return false;
        }

        return ItemIdentity.From(item).Equals(identity);
    }

    private static bool TryGetGamepadCraftingAvailableIndex(int point, out int availableIndex)
    {
        availableIndex = -1;
        if (!Main.recBigList || Main.numAvailableRecipes <= 0)
        {
            return false;
        }

        if (point < GamepadCraftingGridStart || point >= GamepadCraftingListStart)
        {
            return false;
        }

        int localIndex = point - GamepadCraftingGridStart;
        if (localIndex < 0)
        {
            return false;
        }

        int start = Math.Clamp(Main.recStart, 0, Main.availableRecipe.Length - 1);
        int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
        int candidate = start + localIndex;
        if (candidate < 0 || candidate >= availableCount)
        {
            return false;
        }

        availableIndex = candidate;
        return true;
    }

    private static int ComputeSlotSignature(SlotFocus? focus)
    {
        if (!focus.HasValue)
        {
            return -1;
        }

        SlotFocus value = focus.Value;
        int slot = value.Slot;
        int signature = HashCode.Combine(value.Context, slot);

        if (value.Items is not null)
        {
            return HashCode.Combine(signature, RuntimeHelpers.GetHashCode(value.Items));
        }

        if (value.SingleItem is not null)
        {
            return HashCode.Combine(signature, RuntimeHelpers.GetHashCode(value.SingleItem));
        }

        return signature;
    }

    private static string BuildFocusKey(HoverTarget target, SlotFocus? focus, int? craftingIndex)
    {
        if (craftingIndex.HasValue && craftingIndex.Value >= 0)
        {
            return $"craft-{craftingIndex.Value}";
        }

        if (focus.HasValue)
        {
            SlotFocus value = focus.Value;
            return $"slot-{value.Context}-{value.Slot}-{target.Identity.Type}-{target.Identity.Prefix}-{target.Identity.Stack}";
        }

        if (target.Identity.Type > 0)
        {
            return $"item-{target.Identity.Type}-{target.Identity.Prefix}-{target.Identity.Stack}-{target.Location}";
        }

        if (!string.IsNullOrWhiteSpace(target.Location))
        {
            return $"loc-{target.Location}";
        }

        return string.Empty;
    }

    private static string? MergeDetails(string? existing, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        string separator = HasTerminalPunctuation(existing) ? " " : ". ";
        return $"{existing}{separator}{addition}";
    }

    private static bool HasTerminalPunctuation(string text)
    {
        text = text.TrimEnd();
        if (text.Length == 0)
        {
            return false;
        }

        char last = text[^1];
        return last == '.' || last == '!' || last == '?' || last == ':' || last == ')';
    }

    private static string CombineItemAnnouncement(string message, string? details)
    {
        string normalizedMessage = GlyphTagFormatter.Normalize(message.Trim());
        if (string.IsNullOrWhiteSpace(details))
        {
            return normalizedMessage;
        }

        string normalizedDetails = GlyphTagFormatter.Normalize(details.Trim());
        if (!HasTerminalPunctuation(normalizedMessage))
        {
            normalizedMessage += '.';
        }

        return $"{normalizedMessage} {normalizedDetails}";
    }

    private void PlayTickIfNew(string key, SlotFocus? focus = null)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastFocusKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastFocusKey = key;
        UiTickSoundPlayer.PlaySpatialTick(0f, 0f); // Simplified - actual spatial audio handled elsewhere
    }

    private void PlayCraftingTickIfNew(string key, int craftingAvailableIndex)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastFocusKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastFocusKey = key;
        UiTickSoundPlayer.PlaySpatialTick(0f, 0f);
    }

    private bool TryAnnounceCue(
        in NarrationCue cue,
        bool force = false,
        Services.UiNarrationArea allowedAreas = Services.UiNarrationArea.Unknown,
        SlotFocus? focus = null,
        string? regionPrefix = null)
    {
        if (allowedAreas == Services.UiNarrationArea.Unknown)
        {
            allowedAreas = InventoryNarrationAreas;
        }

        if (!_narrationHistory.TryStore(cue))
        {
            return false;
        }

        if (!UiAreaNarrationContext.IsActiveArea(allowedAreas))
        {
            return false;
        }

        string message = string.IsNullOrWhiteSpace(regionPrefix)
            ? cue.Message
            : $"{regionPrefix}. {cue.Message}";

        ScreenReaderService.Announce(message, force);
        return true;
    }

    private void ResetHoverSlotCues()
    {
        _narrationHistory.Reset(NarrationKind.HoverItem);
        _narrationHistory.Reset(NarrationKind.EmptySlot);
    }

    private void ResetHoverSlotsAndTooltips()
    {
        ResetHoverSlotCues();
        _narrationHistory.Reset(NarrationKind.Tooltip);
    }

    private void Reset()
    {
        _narrationHistory.ResetAll();
        _currentFocus = null;
        _focusTracker.ClearAll();
        _inGameUiTracker.Reset();
        UiAreaNarrationContext.Clear();
        _lastFocusKey = null;
        _lastAnnouncedItemIdentity = default;
        _inventoryOpenGraceFrames = 0;
        _lastChestIndex = -1;
        _regionDetector.Reset();
        _specialSelections.Clear();
    }

    private void OnInventoryJustOpened()
    {
        UiAreaNarrationContext.RecordArea(Services.UiNarrationArea.Inventory);
        _inventoryOpenGraceFrames = InventoryOpenGracePeriod;
        InventoryOpened?.Invoke();
    }

    private void OnInventoryJustClosed()
    {
        Main.recBigList = false;
        Main.recStart = 0;
        InventoryClosed?.Invoke();
    }

    private void LogFocusDebugState(bool usingGamepad, SlotFocus? focus)
    {
        if (!InputDebugEnabled)
        {
            return;
        }

        int currentLinkPoint = UILinkPointNavigator.CurrentPoint;
        string? itemName = null;

        if (focus.HasValue)
        {
            Item? item = focus.Value.GetItem();
            if (item is not null && !item.IsAir)
            {
                itemName = ItemDescriber.ComposeName(item);
            }
        }

        bool linkPointChanged = currentLinkPoint != _lastLoggedFocusLinkPoint;
        bool itemChanged = !string.Equals(itemName, _lastLoggedFocusItemName, StringComparison.Ordinal);

        if (!linkPointChanged && !itemChanged)
        {
            return;
        }

        _lastLoggedFocusLinkPoint = currentLinkPoint;
        _lastLoggedFocusItemName = itemName;

        ScreenReaderMod.Instance?.Logger.Info(
            $"[FocusDebug] linkPoint={currentLinkPoint} item='{itemName ?? "none"}' " +
            $"context={focus?.Context ?? -1} slot={focus?.Slot ?? -1} usingGamepad={usingGamepad}");
    }

    #endregion

    #region Nested Types

    private readonly record struct HoverTarget(
        Item Item,
        ItemIdentity Identity,
        string Location,
        string RawTooltip,
        string NormalizedTooltip,
        SlotFocus? Focus,
        bool AllowMouseText)
    {
        public bool HasItem => !Identity.IsAir;
        public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
        public bool HasTooltip => !string.IsNullOrWhiteSpace(NormalizedTooltip);
    }

    #endregion
}
