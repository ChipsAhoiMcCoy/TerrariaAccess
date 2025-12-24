#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class InventoryNarrator
    {
        private readonly MenuUiSelectionTracker _inGameUiTracker = new();
        private readonly NarrationHistory _narrationHistory = new();
        private static readonly FocusTracker _focusTracker = new();
        private SlotFocus? _currentFocus;
        private string? _lastFocusKey;
        private const UiNarrationArea InventoryNarrationAreas =
            UiNarrationArea.Inventory |
            UiNarrationArea.Storage |
            UiNarrationArea.Creative |
            UiNarrationArea.Reforge |
            UiNarrationArea.Shop |
            UiNarrationArea.Guide;

        private static readonly bool NarrationDebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_NARRATION"));

        private static readonly Lazy<FieldInfo?> MouseTextCacheField = new(() =>
            typeof(Main).GetField("_mouseTextCache", BindingFlags.Instance | BindingFlags.NonPublic));

        private static FieldInfo? _mouseTextCursorField;
        private static FieldInfo? _mouseTextIsValidField;
        private static string? _capturedMouseText;
        private static uint _capturedMouseTextFrame;

        public static void RecordFocus(Item[] inventory, int context, int slot)
        {
            SlotFocus focus = new(inventory, null, context, slot);
            _focusTracker.Capture(focus);
        }

        public static void RecordFocus(Item item, int context)
        {
            SlotFocus focus = new(null, item, context, -1);
            CraftingNarrator.TryCaptureRecipeHover(item, context);
            _focusTracker.Capture(focus);
        }

        private static bool ShouldCaptureFocusForContext(int context)
        {
            return true;
        }

        internal static void RecordMouseTextSnapshot(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _capturedMouseText = null;
                _capturedMouseTextFrame = 0;
                return;
            }

            _capturedMouseText = text.Trim();
            _capturedMouseTextFrame = Main.GameUpdateCount;
        }

        internal static void ResetStaticCaches()
        {
            _focusTracker.ClearAll();
            _capturedMouseText = null;
            _capturedMouseTextFrame = 0;
            LoggedUnknownInventoryPoints.Clear();
            SpecialSelectionRepeat.Clear();
            _lastOptionsStateHash = int.MinValue;
        }

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

        public void Update(Player player)
        {
            if (Main.ingameOptionsWindow)
            {
                Reset();
                return;
            }

            if (!IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            bool usingGamepad = PlayerInput.UsingGamepadUI;
            if (usingGamepad)
            {
                ClearSpecialLinkPointFocus();
            }

            SlotFocus? nextFocus = _focusTracker.Consume(usingGamepad);

            _currentFocus = nextFocus.HasValue && IsFocusValid(nextFocus.Value) ? nextFocus : null;

            HandleMouseItem();
            HandleHoverItem(player);
        }

        internal static bool IsInventoryUiOpen(Player player)
        {
            return Main.playerInventory ||
                   player.chest != -1 ||
                   Main.npcShop != 0 ||
                   Main.InGuideCraftMenu ||
                   Main.InReforgeMenu;
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

            string message = $"Holding {NarrationTextFormatter.ComposeItemLabel(mouse)}";
            TryAnnounceCue(
                NarrationCue.ForMouse(identity, message),
                allowedAreas: InventoryNarrationAreas | UiNarrationArea.Crafting | UiNarrationArea.Guide);
        }

        private void HandleHoverItem(Player player)
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
                SpecialSelectionRepeat.Clear();
            }
            if (inGamepadCraftingGrid)
            {
                UiAreaNarrationContext.RecordArea(UiNarrationArea.Crafting);
                if (craftingAvailableIndex >= 0 &&
                    CraftingNarrator.TryFocusRecipeAtAvailableIndex(craftingAvailableIndex))
                {
                    PlayTickIfNew($"craft-{craftingAvailableIndex}");
                    ResetHoverSlotsAndTooltips();
                    return;
                }
            }

            SlotFocus? focus = (selectingSpecial || inGamepadCraftingGrid) ? null : _currentFocus;
            Item? focusedItem = (selectingSpecial || inGamepadCraftingGrid) ? null : GetItemFromFocus(focus);
            if (focus.HasValue)
            {
                UiAreaNarrationContext.RecordSlotContext(focus.Value.Context);
            }
            else if (selectingSpecial)
            {
                UiAreaNarrationContext.RecordArea(UiNarrationArea.Inventory);
            }
            bool usingGamepadFocus = usingGamepad && !selectingSpecial && focusedItem is not null;

            Item hover = ResolveHoverItem(usingGamepad, usingGamepadFocus, focusedItem);
            ItemIdentity identity = ItemIdentity.From(hover);
            string location = DescribeLocation(player, identity, focus);

            bool allowRecipeHoverCapture = !focus.HasValue && string.IsNullOrWhiteSpace(location);
            if (allowRecipeHoverCapture && !identity.IsAir && CraftingNarrator.TryCaptureHoveredRecipe(hover))
            {
                ResetHoverSlotsAndTooltips();
                return;
            }

            string rawTooltip = ResolveRawTooltip(usingGamepad, usingGamepadFocus, hover);
            string normalizedTooltip = GlyphTagFormatter.Normalize(rawTooltip);

            if (TryAnnounceSpecialSelection(identity.IsAir, location))
            {
                return;
            }

            HoverTarget target = new(hover, identity, location, rawTooltip, normalizedTooltip, focus, AllowMouseText: !usingGamepadFocus);
            string focusKey = BuildFocusKey(target, focus, inGamepadCraftingGrid ? craftingAvailableIndex : (int?)null);
            PlayTickIfNew(focusKey);

            if (target.HasItem)
            {
                AnnounceItemHover(player, target);
                return;
            }

            if (TryAnnounceEmptySlot(target))
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
                return GetHoverNameForItem(hover);
            }

            if (usingGamepad)
            {
                return string.Empty;
            }

            return Main.hoverItemName ?? string.Empty;
        }

        private void AnnounceItemHover(Player player, HoverTarget target)
        {
            string label = NarrationTextFormatter.ComposeItemLabel(target.Item);
            string message = string.IsNullOrEmpty(target.Location) ? label : $"{label}, {target.Location}";
            string? details = BuildTooltipDetails(target.Item, target.RawTooltip, allowMouseText: target.AllowMouseText);
            string? requirementDetails = CraftingNarrator.TryGetRequirementTooltipDetails(target.Item, string.IsNullOrWhiteSpace(target.Location));
            details = MergeDetails(details, requirementDetails);
            string? priceDetails = BuildShopPriceDetails(player, target.Item, target.Identity, target.Focus);
            details = MergeDetails(details, priceDetails);
            string? sellDetails = BuildSellPriceDetails(player, target.Item, target.Identity);
            details = MergeDetails(details, sellDetails);
            string? reforgeDetails = BuildReforgePriceDetails(player, target.Item, target.Focus);
            details = MergeDetails(details, reforgeDetails);

            string combined = NarrationTextFormatter.CombineItemAnnouncement(message, details);
            int slotSignature = ComputeSlotSignature(target.Focus);
            if (TryAnnounceCue(NarrationCue.ForItem(target.Identity, combined, target.Location, target.NormalizedTooltip, details, slotSignature), focus: target.Focus))
            {
                _narrationHistory.Reset(NarrationKind.EmptySlot);
                _narrationHistory.Reset(NarrationKind.Tooltip);
            }
        }

        private static string? MergeDetails(string? existing, string? addition)
        {
            if (string.IsNullOrWhiteSpace(addition))
            {
                return existing;
            }

            return string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}. {addition}";
        }

        private bool TryAnnounceEmptySlot(HoverTarget target)
        {
            if (!target.HasLocation)
            {
                return false;
            }

            string message = $"Empty, {target.Location}";
            int slotSignature = ComputeSlotSignature(target.Focus);
            if (TryAnnounceCue(NarrationCue.ForEmpty(message, target.Location, slotSignature), focus: target.Focus))
            {
                _narrationHistory.Reset(NarrationKind.HoverItem);
                _narrationHistory.Reset(NarrationKind.Tooltip);
            }

            return true;
        }

        private bool TryAnnounceMouseText()
        {
            string? mouseText = TryGetMouseText();
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

            if (TryAnnounceCue(NarrationCue.ForTooltip(target.NormalizedTooltip)))
            {
                ResetHoverSlotCues();
            }
        }

        public static bool TryGetContextForLinkPoint(int point, out int context)
        {
            return _focusTracker.TryGetContextForLinkPoint(point, out context);
        }

        public static bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
        {
            return _focusTracker.TryGetItemForLinkPoint(point, out item, out context);
        }

        private static bool IsFocusValid(SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                int index = focus.Slot;
                return (uint)index < (uint)items.Length;
            }

            return focus.SingleItem is not null;
        }

        private static Item? GetItemFromFocus(SlotFocus? focus)
        {
            if (!focus.HasValue)
            {
                return null;
            }

            SlotFocus value = focus.Value;

            if (value.Items is Item[] items)
            {
                int index = value.Slot;
                if ((uint)index < (uint)items.Length)
                {
                    return items[index];
                }

                return null;
            }

            return value.SingleItem;
        }

        private static string GetHoverNameForItem(Item item)
        {
            return NarrationTextFormatter.ComposeItemName(item);
        }

        private void Reset()
        {
            _narrationHistory.ResetAll();
            _currentFocus = null;
            _focusTracker.ClearAll();
            _inGameUiTracker.Reset();
            UiAreaNarrationContext.Clear();
            _lastFocusKey = null;
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

        private void PlayTickIfNew(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || string.Equals(key, _lastFocusKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastFocusKey = key;
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        public void ForceReset()
        {
            Reset();
        }

        private static string DescribeLocation(Player player, ItemIdentity identity, SlotFocus? focus)
        {
            if (focus.HasValue)
            {
                string focused = DescribeFocusedSlot(player, focus.Value);
                if (!string.IsNullOrWhiteSpace(focused))
                {
                    return focused;
                }
            }

            if (TryMatch(player.inventory, identity, out int inventoryIndex))
            {
                if (inventoryIndex < 10)
                {
                    return $"Hotbar slot {inventoryIndex + 1}";
                }

                if (inventoryIndex < 50)
                {
                    return $"Inventory slot {inventoryIndex - 9}";
                }

                if (inventoryIndex < 54)
                {
                    return $"Coin slot {inventoryIndex - 49}";
                }

                if (inventoryIndex < 58)
                {
                    return $"Ammo slot {inventoryIndex - 53}";
                }
            }

            if (Matches(player.trashItem, identity))
            {
                return "Trash slot";
            }

            if (TryMatch(player.armor, identity, out int armorIndex))
            {
                return SlotContextFormatter.DescribeArmorSlot(armorIndex);
            }

            if (TryMatch(player.dye, identity, out int dyeIndex))
            {
                return $"Dye slot {dyeIndex + 1}";
            }

            if (TryMatch(player.miscEquips, identity, out int miscIndex))
            {
                return $"Misc equipment slot {miscIndex + 1}";
            }

            if (TryMatch(player.miscDyes, identity, out int miscDyeIndex))
            {
                return $"Misc dye slot {miscDyeIndex + 1}";
            }

            int chestIndex = player.chest;
            if (chestIndex != -1)
            {
                string container = SlotContextFormatter.DescribeContainer(chestIndex);
                Item[]? containerItems = GetContainerItems(player, chestIndex);
                if (containerItems is not null && TryMatch(containerItems, identity, out int containerSlot))
                {
                    return $"{container} slot {containerSlot + 1}";
                }

                return container;
            }

            if (Main.npcShop > 0)
            {
                Chest[]? shops = Main.instance?.shop;
                if (shops is not null && Main.npcShop < shops.Length)
                {
                    Item[]? shopItems = shops[Main.npcShop]?.item;
                    if (shopItems is not null && TryMatch(shopItems, identity, out int shopSlot))
                    {
                        return $"Shop slot {shopSlot + 1}";
                    }
                }
            }

            return string.Empty;
        }

        private static bool IsPlayerInventoryItem(Player player, ItemIdentity identity)
        {
            return TryMatch(player.inventory, identity, out _);
        }

        private static string DescribeFocusedSlot(Player player, SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                if (ReferenceEquals(items, player.inventory))
                {
                    return SlotContextFormatter.DescribeInventorySlot(focus.Slot);
                }

                if (ReferenceEquals(items, player.bank.item))
                {
                    return $"Piggy bank slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank2.item))
                {
                    return $"Safe slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank3.item))
                {
                    return $"Defender's forge slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank4.item))
                {
                    return $"Void vault slot {focus.Slot + 1}";
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
                            string container = SlotContextFormatter.DescribeContainer(i);
                            return focus.Slot >= 0 ? $"{container} slot {focus.Slot + 1}" : container;
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
                            return focus.Slot >= 0 ? $"Shop slot {focus.Slot + 1}" : "Shop slot";
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

        private const int GamepadCraftingGridStart = 700;
        private const int GamepadCraftingListStart = 1500;

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

        private static string? BuildShopPriceDetails(Player player, Item item, ItemIdentity identity, SlotFocus? focus)
        {
            if (player is null || item is null || item.IsAir)
            {
                return null;
            }

            Chest[]? shops = Main.instance?.shop;
            if (shops is null || shops.Length == 0)
            {
                return null;
            }

            if (!focus.HasValue && Main.npcShop <= 0)
            {
                return null;
            }

            Item? referencedItem = TryResolveShopItem(identity, focus, shops);
            if (referencedItem is null || referencedItem.IsAir)
            {
                return null;
            }

            if (referencedItem.shopSpecialCurrency >= 0 &&
                CustomCurrencyManager.TryGetCurrencySystem(referencedItem.shopSpecialCurrency, out CustomCurrencySystem? customSystem))
            {
                string? customCurrencyText = FormatCustomCurrencyPrice(customSystem, referencedItem);
                return string.IsNullOrWhiteSpace(customCurrencyText) ? null : $"Costs {customCurrencyText}";
            }

            long coinPrice = GetDiscountedCoinPrice(player, referencedItem);
            if (coinPrice <= 0)
            {
                return null;
            }

            string coinText = CoinFormatter.ValueToCoinString(coinPrice);
            return string.IsNullOrWhiteSpace(coinText) ? null : $"Costs {coinText}";
        }

        private static string? BuildSellPriceDetails(Player player, Item item, ItemIdentity identity)
        {
            if (player is null || item is null || item.IsAir)
            {
                return null;
            }

            if (Main.npcShop <= 0)
            {
                return null;
            }

            if (!IsPlayerInventoryItem(player, identity))
            {
                return null;
            }

            long sellPrice = GetSellPrice(player, item);
            if (sellPrice <= 0)
            {
                return null;
            }

            string coins = CoinFormatter.ValueToCoinString(sellPrice);
            return string.IsNullOrWhiteSpace(coins) ? null : $"Sells for {coins}";
        }

        private static long GetSellPrice(Player player, Item item)
        {
            if (player is null || item is null)
            {
                return 0;
            }

            try
            {
                player.GetItemExpectedPrice(item, out long priceForSelling, out long _);
                if (priceForSelling > 0)
                {
                    return priceForSelling;
                }
            }
            catch
            {
                // Ignore failures and fall back below.
            }

            long unitValue = Math.Max(0, item.value);
            if (unitValue <= 0)
            {
                return 0;
            }

            int stack = Math.Max(1, item.stack);
            long totalValue = unitValue * (long)stack;
            if (totalValue <= 0)
            {
                return 0;
            }

            return totalValue / 5;
        }

        private static string? BuildReforgePriceDetails(Player player, Item item, SlotFocus? focus)
        {
            if (player is null || item is null || item.IsAir)
            {
                return null;
            }

            if (!Main.InReforgeMenu)
            {
                return null;
            }

            if (!focus.HasValue)
            {
                return null;
            }

            int context = Math.Abs(focus.Value.Context);
            if (context != ItemSlot.Context.PrefixItem)
            {
                return null;
            }

            if (item.maxStack != 1)
            {
                return null;
            }

            long reforgeCost = GetReforgeCost(player, item);
            if (reforgeCost <= 0)
            {
                return null;
            }

            string coinText = CoinFormatter.ValueToCoinString(reforgeCost);
            return string.IsNullOrWhiteSpace(coinText) ? null : $"Reforge cost {coinText}";
        }

        private static long GetReforgeCost(Player player, Item item)
        {
            if (player is null || item is null || item.IsAir)
            {
                return 0;
            }

            long cost = item.value;
            if (cost <= 0)
            {
                return 1;
            }

            if (player.discountAvailable)
            {
                cost = (long)(cost * 0.8);
            }

            cost = (long)(cost * player.currentShoppingSettings.PriceAdjustment);
            cost /= 3;

            return Math.Max(1, cost);
        }

        private static Item? TryResolveShopItem(ItemIdentity identity, SlotFocus? focus, Chest[] shops)
        {
            if (focus.HasValue && focus.Value.Slot >= 0 && focus.Value.Items is Item[] focusItems)
            {
                for (int i = 0; i < shops.Length; i++)
                {
                    Item[]? shopItems = shops[i]?.item;
                    if (ReferenceEquals(shopItems, focusItems))
                    {
                        int slot = focus.Value.Slot;
                        if (slot >= 0 && shopItems is not null && slot < shopItems.Length)
                        {
                            return shopItems[slot];
                        }
                    }
                }
            }

            int activeShopIndex = Main.npcShop;
            if (activeShopIndex > 0 && activeShopIndex < shops.Length)
            {
                Item[]? items = shops[activeShopIndex]?.item;
                if (items is not null && TryMatch(items, identity, out int shopSlot) && shopSlot >= 0 && shopSlot < items.Length)
                {
                    return items[shopSlot];
                }
            }

            return null;
        }

        private static long GetDiscountedCoinPrice(Player player, Item item)
        {
            if (player is null || item is null)
            {
                return 0;
            }

            try
            {
                player.GetItemExpectedPrice(item, out long _, out long priceForBuying);
                if (priceForBuying > 0)
                {
                    return priceForBuying;
                }
            }
            catch
            {
                // Fallback to raw values below.
            }

            long? customPrice = item.shopCustomPrice;
            if (customPrice is long explicitPrice && explicitPrice > 0)
            {
                return explicitPrice;
            }

            return item.value;
        }

        private static string? FormatCustomCurrencyPrice(CustomCurrencySystem system, Item item)
        {
            if (system is null || item is null)
            {
                return null;
            }

            long price = 0;

            try
            {
                system.GetItemExpectedPrice(item, out long _, out long currencyPrice);
                price = currencyPrice;
            }
            catch
            {
                price = 0;
            }

            if (price <= 0)
            {
                price = item.shopCustomPrice ?? 0;
            }

            if (price <= 0)
            {
                return null;
            }

            string[] lines = new string[4];
            int lineCount = 0;
            try
            {
                system.GetPriceText(lines, ref lineCount, price);
            }
            catch
            {
                // Swallow and fall back to numeric display.
            }

            if (lineCount <= 0)
            {
                return price.ToString();
            }

            var segments = new List<string>(lineCount);
            for (int i = 0; i < lineCount && i < lines.Length; i++)
            {
                string? segment = lines[i];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                string cleaned = GlyphTagFormatter.Normalize(segment).Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    segments.Add(cleaned);
                }
            }

            if (segments.Count == 0)
            {
                return price.ToString();
            }

            return string.Join(' ', segments);
        }

        private static Item[]? GetContainerItems(Player player, int chestIndex)
        {
            if (chestIndex >= 0 && chestIndex < Main.chest.Length)
            {
                return Main.chest[chestIndex]?.item;
            }

            return chestIndex switch
            {
                -2 => player.bank.item,
                -3 => player.bank2.item,
                -4 => player.bank3.item,
                -5 => player.bank4.item,
                _ => null,
            };
        }

        private static bool TryMatch(Item[] items, ItemIdentity identity, out int index)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (Matches(items[i], identity))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static bool Matches(Item item, ItemIdentity identity)
        {
            if (item is null || item.IsAir)
            {
                return false;
            }

            return ItemIdentity.From(item).Equals(identity);
        }

        private static string? TryGetMouseText()
        {
            string? captured = TryGetCapturedMouseText();
            if (!string.IsNullOrWhiteSpace(captured))
            {
                return captured;
            }

            Main? main = Main.instance;
            if (main is null)
            {
                return null;
            }

            FieldInfo? cacheField = MouseTextCacheField.Value;
            if (cacheField is null)
            {
                return null;
            }

            object? cache = cacheField.GetValue(main);
            if (cache is null)
            {
                return null;
            }

            Type cacheType = cache.GetType();
            _mouseTextCursorField ??= cacheType.GetField("cursorText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _mouseTextIsValidField ??= cacheType.GetField("isValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_mouseTextCursorField?.GetValue(cache) is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            return null;
        }

        private static string? TryGetCapturedMouseText()
        {
            if (string.IsNullOrWhiteSpace(_capturedMouseText) || _capturedMouseTextFrame == 0)
            {
                return null;
            }

            uint current = Main.GameUpdateCount;
            uint frame = _capturedMouseTextFrame;
            uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
            if (age <= 2)
            {
                return _capturedMouseText;
            }

            _capturedMouseText = null;
            _capturedMouseTextFrame = 0;
            return null;
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

            if (TryAnnounceCue(NarrationCue.ForUi(cleaned), allowedAreas: UiNarrationArea.Unknown))
            {
                ResetHoverSlotCues();
            }

            return true;
        }

        private bool TryAnnounceCue(
            in NarrationCue cue,
            bool force = false,
            UiNarrationArea allowedAreas = InventoryNarrationAreas,
            SlotFocus? focus = null)
        {
            if (!_narrationHistory.TryStore(cue))
            {
                LogNarrationDebug("history-suppressed", cue, allowedAreas, focus);
                return false;
            }

            if (!UiAreaNarrationContext.IsActiveArea(allowedAreas))
            {
                LogNarrationDebug("area-blocked", cue, allowedAreas, focus);
                return false;
            }

            NarrationInstrumentationContext.SetPendingKey(BuildInstrumentationKey(cue));
            ScreenReaderService.Announce(cue.Message, force);
            return true;
        }

        private void ResetHoverSlotCues()
        {
            _narrationHistory.Reset(NarrationKind.HoverItem);
            _narrationHistory.Reset(NarrationKind.EmptySlot);
        }

        private static void LogNarrationDebug(string reason, in NarrationCue cue, UiNarrationArea allowedAreas, SlotFocus? focus = null)
        {
            if (!NarrationDebugEnabled)
            {
                return;
            }

            string activeArea = UiAreaNarrationContext.ActiveArea.ToString();
            int focusContext = focus?.Context ?? -1;
            string focusLabel = string.Empty;
            Player? player = Main.LocalPlayer;
            if (player is not null && focus.HasValue)
            {
                focusLabel = DescribeFocusedSlot(player, focus.Value);
            }

            ScreenReaderMod.Instance?.Logger.Info(
                $"[InventoryNarration][Debug] {reason}: kind={cue.Kind} type={cue.Identity.Type} prefix={cue.Identity.Prefix} stack={cue.Identity.Stack} fav={(cue.Identity.Favorited ? 1 : 0)} slotSig={cue.SlotSignature} allowedAreas={allowedAreas} activeArea={activeArea} focusContext={focusContext} focusLabel='{focusLabel}' location='{cue.Location ?? string.Empty}' message='{cue.Message}'");
        }

        private void ResetHoverSlotsAndTooltips()
        {
            ResetHoverSlotCues();
            _narrationHistory.Reset(NarrationKind.Tooltip);
        }

        private static string BuildInstrumentationKey(in NarrationCue cue)
        {
            return cue.Kind switch
            {
                NarrationKind.MouseItem => $"mouse:{cue.Identity.Type}:{cue.Identity.Prefix}:{cue.Identity.Stack}:{(cue.Identity.Favorited ? 1 : 0)}",
                NarrationKind.HoverItem => $"hover:{cue.SlotSignature}:{cue.Identity.Type}:{cue.Identity.Prefix}:{cue.Identity.Stack}:{(cue.Identity.Favorited ? 1 : 0)}",
                NarrationKind.EmptySlot => $"empty:{cue.SlotSignature}",
                NarrationKind.Tooltip => $"tooltip:{SanitizeKey(cue.Message)}",
                NarrationKind.UiHover => $"ui:{SanitizeKey(cue.Message)}",
                NarrationKind.SpecialSelection => $"special:{SanitizeKey(cue.Message)}",
                _ => $"other:{SanitizeKey(cue.Message)}",
            };
        }

        private static string SanitizeKey(string? value)
        {
            string normalized = GlyphTagFormatter.Normalize(value ?? string.Empty).Trim();
            if (normalized.Length > 120)
            {
                normalized = normalized[..120];
            }

            return normalized;
        }

    }
}
