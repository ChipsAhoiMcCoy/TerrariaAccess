#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Generates descriptions and labels for items in the inventory UI.
/// Handles item names, stack counts, favorites, and price formatting.
/// </summary>
internal static class ItemDescriber
{
    /// <summary>
    /// Composes a full item label including name, stack, and favorite status.
    /// </summary>
    public static string ComposeLabel(Item item, bool includeCountWhenSingular = false)
    {
        string name = ComposeName(item);
        return NarrationStringCatalog.ItemLabel(name, item.stack, item.favorited, includeCountWhenSingular);
    }

    /// <summary>
    /// Composes just the item name with prefix handling.
    /// </summary>
    public static string ComposeName(Item item)
    {
        string name = TextSanitizer.Clean(item.AffixName());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(item.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(Lang.GetItemNameValue(item.type));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Item {item.type}";
        }

        return name;
    }

    /// <summary>
    /// Formats a coin value to a readable string.
    /// </summary>
    public static string FormatPrice(long value)
    {
        string coinText = CoinFormatter.ValueToCoinString(value);
        if (!string.IsNullOrWhiteSpace(coinText))
        {
            return coinText;
        }

        return value.ToString();
    }

    /// <summary>
    /// Builds shop price details for an item being purchased.
    /// </summary>
    public static string? BuildShopPriceDetails(Player player, Item item, SlotFocus? focus)
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

        ItemIdentity identity = ItemIdentity.From(item);
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

    /// <summary>
    /// Builds sell price details for an item being sold.
    /// </summary>
    public static string? BuildSellPriceDetails(Player player, Item item)
    {
        if (player is null || item is null || item.IsAir)
        {
            return null;
        }

        if (Main.npcShop <= 0)
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

    /// <summary>
    /// Builds reforge cost details for an item at the Goblin Tinkerer.
    /// </summary>
    public static string? BuildReforgePriceDetails(Player player, Item item, SlotFocus? focus)
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
        if (context != Terraria.UI.ItemSlot.Context.PrefixItem)
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
            if (items is not null && TryMatchItem(items, identity, out int shopSlot) &&
                shopSlot >= 0 && shopSlot < items.Length)
            {
                return items[shopSlot];
            }
        }

        return null;
    }

    private static bool TryMatchItem(Item[] items, ItemIdentity identity, out int index)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item is null || item.IsAir)
            {
                continue;
            }

            if (ItemIdentity.From(item).Equals(identity))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
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

            string cleaned = TextSanitizer.Clean(segment);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                segments.Add(cleaned);
            }
        }

        if (segments.Count == 0)
        {
            return price.ToString();
        }

        string result = string.Join(' ', segments);

        // Strip redundant "Buy price:" prefix
        string buyPricePrefix = Lang.tip[50].Value;
        if (!string.IsNullOrEmpty(buyPricePrefix) &&
            result.StartsWith(buyPricePrefix, StringComparison.OrdinalIgnoreCase))
        {
            result = result.Substring(buyPricePrefix.Length).TrimStart();
        }

        return result;
    }
}
