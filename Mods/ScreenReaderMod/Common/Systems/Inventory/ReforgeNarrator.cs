#nullable enable
using System;
using System.Reflection;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Handles narration for the Goblin Tinkerer's reforge menu.
/// Announces items placed in the reforge slot with cost and prefix information.
/// </summary>
internal sealed class ReforgeNarrator
{
    private RecipeIdentity _lastReforgeIdentity;
    private bool _announcedEmptyReforge;
    private static bool _loggedReforgeReflectionWarning;

    /// <summary>
    /// Updates reforge menu narration. Should be called each frame when inventory is open.
    /// </summary>
    public void Update(Player player)
    {
        if (!Main.InReforgeMenu)
        {
            Reset();
            return;
        }

        UiAreaNarrationContext.RecordArea(UiNarrationArea.Reforge);
        TryAnnounceReforgeItem(player);
    }

    /// <summary>
    /// Resets all state.
    /// </summary>
    public void Reset()
    {
        _lastReforgeIdentity = default;
        _announcedEmptyReforge = false;
    }

    private void TryAnnounceReforgeItem(Player player)
    {
        Item reforgeItem = Main.reforgeItem;
        RecipeIdentity identity = RecipeIdentity.From(reforgeItem);

        if (identity.Type <= 0 || reforgeItem.IsAir)
        {
            if (!_announcedEmptyReforge)
            {
                ScreenReaderService.Announce("Place an item to reforge.");
                _announcedEmptyReforge = true;
            }

            _lastReforgeIdentity = default;
            return;
        }

        bool changed = !_lastReforgeIdentity.Equals(identity);
        if (!changed)
        {
            return;
        }

        _announcedEmptyReforge = false;

        string label = ItemDescriber.ComposeLabel(reforgeItem, includeCountWhenSingular: true);
        string message = $"Reforge {label}";
        if (TryGetReforgePrice(reforgeItem, out long price) && price > 0)
        {
            string coins = CoinFormatter.ValueToCoinString(price);
            if (!string.IsNullOrWhiteSpace(coins))
            {
                message = $"{message}. Cost {coins}";
            }
        }

        string prefixName = TextSanitizer.Clean(reforgeItem.prefix > 0 ? Lang.prefix[reforgeItem.prefix].Value : string.Empty);
        if (!string.IsNullOrWhiteSpace(prefixName))
        {
            message = $"{message}. Current prefix {prefixName}";
        }

        ScreenReaderService.Announce(message, force: true);
        _lastReforgeIdentity = identity;
    }

    private static bool TryGetReforgePrice(Item item, out long price)
    {
        price = 0;
        if (item is null || item.IsAir)
        {
            return false;
        }

        try
        {
            MethodInfo? method = typeof(ItemLoader).GetMethod(
                "ReforgePrice",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Item) },
                modifiers: null);
            if (method is not null)
            {
                object? result = method.Invoke(null, new object[] { item });
                switch (result)
                {
                    case int intValue when intValue > 0:
                        price = intValue;
                        return true;
                    case long longValue when longValue > 0:
                        price = longValue;
                        return true;
                }
            }
        }
        catch (Exception ex) when (!_loggedReforgeReflectionWarning)
        {
            _loggedReforgeReflectionWarning = true;
            ScreenReaderMod.Instance?.Logger.Warn($"[ReforgeNarrator] Failed to resolve reforge price: {ex}");
        }

        // Fallback calculation: item value / 3
        price = Math.Max(1, Math.Max(item.value, 0) / 3);
        return price > 0;
    }
}
