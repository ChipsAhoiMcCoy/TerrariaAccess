#nullable enable
using System;
using System.Reflection;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class CraftingNarrator
    {
        // Guide state tracking
        private RecipeIdentity _lastGuideIdentity;
        private int _lastGuideRecipeCount;
        private bool _announcedEmptyGuideSlot;

        // Reforge state tracking
        private RecipeIdentity _lastReforgeIdentity;
        private bool _announcedEmptyReforge;
        private static bool _loggedReforgeReflectionWarning;

        /// <summary>
        /// Handles announcements for both Guide crafting menu and Goblin Tinkerer reforge menu.
        /// Called from the main Update loop when inventory is open.
        /// </summary>
        private void HandleGuideAndReforge(Player player)
        {
            bool inGuide = Main.InGuideCraftMenu;
            bool inReforge = Main.InReforgeMenu;

            if (inGuide)
            {
                UiAreaNarrationContext.RecordArea(UiNarrationArea.Guide);
                TryAnnounceGuideItem();
            }
            else
            {
                ResetGuideState();
            }

            if (inReforge)
            {
                UiAreaNarrationContext.RecordArea(UiNarrationArea.Reforge);
                TryAnnounceReforgeItem(player);
            }
            else
            {
                ResetReforgeState();
            }
        }

        /// <summary>
        /// Announces the item placed in the Guide's crafting slot and the number of available recipes.
        /// </summary>
        private void TryAnnounceGuideItem()
        {
            Item guideItem = Main.guideItem;
            RecipeIdentity identity = RecipeIdentity.From(guideItem);
            int recipeCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);

            if (identity.Type <= 0 || guideItem.IsAir)
            {
                if (!_announcedEmptyGuideSlot)
                {
                    ScreenReaderService.Announce("Guide: place a material in the crafting slot to see recipes.", force: true);
                    _announcedEmptyGuideSlot = true;
                }

                _lastGuideIdentity = default;
                _lastGuideRecipeCount = recipeCount;
                return;
            }

            bool identityChanged = !_lastGuideIdentity.Equals(identity);
            bool recipeCountChanged = recipeCount != _lastGuideRecipeCount;
            if (!identityChanged && !recipeCountChanged)
            {
                return;
            }

            _lastGuideIdentity = identity;
            _lastGuideRecipeCount = recipeCount;
            _announcedEmptyGuideSlot = false;

            string label = NarrationTextFormatter.ComposeItemLabel(guideItem, includeCountWhenSingular: true);
            string message = $"Guide: {label}";
            if (recipeCount > 0)
            {
                string suffix = recipeCount == 1 ? " recipe available" : " recipes available";
                message = $"{message}. {recipeCount}{suffix}";
            }

            NarrationInstrumentationContext.SetPendingKey($"guide:{identity.Type}:{identity.Prefix}");
            ScreenReaderService.Announce(message, force: true);
        }

        private void ResetGuideState()
        {
            _lastGuideIdentity = default;
            _lastGuideRecipeCount = 0;
            _announcedEmptyGuideSlot = false;
        }

        /// <summary>
        /// Announces the item placed in the Goblin Tinkerer's reforge slot, including cost and current prefix.
        /// </summary>
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

            string label = NarrationTextFormatter.ComposeItemLabel(reforgeItem, includeCountWhenSingular: true);
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

            NarrationInstrumentationContext.SetPendingKey($"reforge:{identity.Type}:{identity.Prefix}");
            ScreenReaderService.Announce(message, force: true);
            _lastReforgeIdentity = identity;
        }

        private void ResetReforgeState()
        {
            _lastReforgeIdentity = default;
            _announcedEmptyReforge = false;
        }

        /// <summary>
        /// Attempts to get the reforge price for an item using tModLoader's ItemLoader.
        /// Falls back to a calculated value if reflection fails.
        /// </summary>
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
                TerrariaAccess.Instance?.Logger.Warn($"[CraftingNarrator] Failed to resolve reforge price: {ex}");
            }

            // Fallback calculation: item value / 3
            price = Math.Max(1, Math.Max(item.value, 0) / 3);
            return price > 0;
        }
    }
}
