#nullable enable
using System;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class InventoryNarrator
    {
        #region Region Tracking State

        private static InventoryRegion _lastAnnouncedRegion = InventoryRegion.None;
        private static string? _lastAnnouncedStorageContainer;

        #endregion

        #region Link Point Constants

        // Link point ranges for crafting UI in gamepad mode
        private const int CraftingGridLinkPointStart = 700;
        private const int CraftingGridLinkPointEnd = 1500;
        private const int CraftingListLinkPointStart = 1500;
        private const int CraftingListLinkPointEnd = 2000;

        #endregion

        #region Region Resolution

        /// <summary>
        /// Resolves the inventory region from a slot focus and player context.
        /// </summary>
        private static InventoryRegion ResolveRegion(SlotFocus? focus, Player player)
        {
            if (!focus.HasValue)
            {
                return InventoryRegion.None;
            }

            SlotFocus f = focus.Value;
            int context = Math.Abs(f.Context);
            int slot = f.Slot;

            switch (context)
            {
                case ItemSlot.Context.HotbarItem:
                    return InventoryRegion.Hotbar;

                case ItemSlot.Context.InventoryItem:
                    if (f.Items is not null && ReferenceEquals(f.Items, player.inventory))
                    {
                        if (slot < 10) return InventoryRegion.Hotbar;
                        if (slot < 50) return InventoryRegion.Inventory;
                        if (slot < 54) return InventoryRegion.Coins;
                        if (slot < 58) return InventoryRegion.Ammo;
                    }
                    return InventoryRegion.Inventory;

                case ItemSlot.Context.InventoryCoin:
                    return InventoryRegion.Coins;

                case ItemSlot.Context.InventoryAmmo:
                    return InventoryRegion.Ammo;

                case ItemSlot.Context.EquipArmor:
                case ItemSlot.Context.EquipArmorVanity:
                case ItemSlot.Context.EquipAccessory:
                case ItemSlot.Context.EquipAccessoryVanity:
                case ItemSlot.Context.EquipDye:
                case ItemSlot.Context.EquipMiscDye:
                case ItemSlot.Context.EquipGrapple:
                case ItemSlot.Context.EquipMount:
                case ItemSlot.Context.EquipMinecart:
                case ItemSlot.Context.EquipPet:
                case ItemSlot.Context.EquipLight:
                    return InventoryRegion.CharacterPanel;

                case ItemSlot.Context.TrashItem:
                    return InventoryRegion.InventoryExtras;

                case ItemSlot.Context.CraftingMaterial:
                case ItemSlot.Context.GuideItem:
                case ItemSlot.Context.PrefixItem:
                    return InventoryRegion.Crafting;

                case ItemSlot.Context.ChestItem:
                case ItemSlot.Context.BankItem:
                case ItemSlot.Context.VoidItem:
                    return InventoryRegion.Storage;

                case ItemSlot.Context.ShopItem:
                    return InventoryRegion.Shop;

                default:
                    return InventoryRegion.None;
            }
        }

        /// <summary>
        /// Resolves region from link point ID when no slot focus is available.
        /// Used for crafting grid/list detection.
        /// </summary>
        private static InventoryRegion ResolveRegionFromLinkPoint(int point)
        {
            // Crafting grid (when recBigList is active): 700-1499
            if (point >= CraftingGridLinkPointStart && point < CraftingGridLinkPointEnd && Main.recBigList)
            {
                return InventoryRegion.CraftingGrid;
            }

            // Crafting list (normal view): 1500-1999
            if (point >= CraftingListLinkPointStart && point < CraftingListLinkPointEnd)
            {
                return InventoryRegion.CraftingList;
            }

            return InventoryRegion.None;
        }

        #endregion

        #region Region Display Names

        /// <summary>
        /// Gets the display name for the crafting region without updating tracking state.
        /// Used by CraftingNarrator to force a region prefix on first entry to crafting.
        /// </summary>
        internal static string? GetCraftingRegionDisplayName(bool isGridMode)
        {
            InventoryRegion targetRegion = isGridMode ? InventoryRegion.CraftingGrid : InventoryRegion.CraftingList;
            return GetRegionDisplayName(targetRegion);
        }

        /// <summary>
        /// Gets the region prefix if the region has changed, and updates the last announced region.
        /// Used by CraftingNarrator to prepend region prefix to crafting announcements.
        /// </summary>
        internal static string? TryGetAndUpdateCraftingRegionPrefix(bool isGridMode)
        {
            InventoryRegion targetRegion = isGridMode ? InventoryRegion.CraftingGrid : InventoryRegion.CraftingList;
            if (targetRegion == _lastAnnouncedRegion)
            {
                return null;
            }

            _lastAnnouncedRegion = targetRegion;
            return GetRegionDisplayName(targetRegion);
        }

        private static string? GetRegionDisplayName(InventoryRegion region)
        {
            return region switch
            {
                InventoryRegion.Hotbar => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Hotbar", "Hotbar"),
                InventoryRegion.Inventory => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Inventory", "Inventory"),
                InventoryRegion.Coins => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Coins", "Coins"),
                InventoryRegion.Ammo => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Ammo", "Ammo"),
                InventoryRegion.CharacterPanel => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.CharacterPanel", "Character Panel"),
                InventoryRegion.InventoryExtras => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.InventoryExtras", "Inventory Extras"),
                InventoryRegion.Crafting => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Crafting", "Crafting"),
                InventoryRegion.CraftingGrid => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.CraftingGrid", "Crafting Grid"),
                InventoryRegion.CraftingList => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.CraftingList", "Crafting"),
                InventoryRegion.Storage => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Storage", "Storage"),
                InventoryRegion.Shop => LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.InventoryRegions.Shop", "Shop"),
                _ => null,
            };
        }

        #endregion

        #region Storage Container Names

        /// <summary>
        /// Gets the name of the storage container (chest, piggy bank, safe, etc.) from focus.
        /// </summary>
        private static string? TryGetStorageContainerName(SlotFocus? focus, Player player)
        {
            if (!focus.HasValue)
            {
                // Fallback to player's current chest index
                int chestIndex = player.chest;
                if (chestIndex == -1)
                {
                    return null;
                }

                return SlotContextFormatter.DescribeContainer(chestIndex);
            }

            SlotFocus f = focus.Value;

            // Check if focus is on a storage container by examining the items array
            if (f.Items is Item[] items)
            {
                if (ReferenceEquals(items, player.bank.item))
                {
                    return SlotContextFormatter.DescribeContainer(-2);
                }

                if (ReferenceEquals(items, player.bank2.item))
                {
                    return SlotContextFormatter.DescribeContainer(-3);
                }

                if (ReferenceEquals(items, player.bank3.item))
                {
                    return SlotContextFormatter.DescribeContainer(-4);
                }

                if (ReferenceEquals(items, player.bank4.item))
                {
                    return SlotContextFormatter.DescribeContainer(-5);
                }

                // Check world chests
                if (Main.chest is not null)
                {
                    for (int i = 0; i < Main.chest.Length; i++)
                    {
                        if (ReferenceEquals(Main.chest[i]?.item, items))
                        {
                            return SlotContextFormatter.DescribeContainer(i);
                        }
                    }
                }
            }

            // Fallback based on context
            int context = Math.Abs(f.Context);
            if (context == ItemSlot.Context.BankItem)
            {
                // Could be any bank, use player's chest index
                return SlotContextFormatter.DescribeContainer(player.chest);
            }

            if (context == ItemSlot.Context.ChestItem)
            {
                return SlotContextFormatter.DescribeContainer(player.chest);
            }

            if (context == ItemSlot.Context.VoidItem)
            {
                return SlotContextFormatter.DescribeContainer(-5);
            }

            return null;
        }

        /// <summary>
        /// Gets the item array for a container by index.
        /// Handles both world chests and player banks.
        /// </summary>
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

        #endregion
    }
}
