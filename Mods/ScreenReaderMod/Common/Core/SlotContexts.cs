#nullable enable
using Terraria.UI;

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// Common item slot context values from Terraria's ItemSlot.Context.
/// This provides a centralized reference for frequently-used context values.
/// </summary>
/// <remarks>
/// <para>
/// Terraria's ItemSlot.Context already provides these as constants, but this class
/// serves as documentation and a quick reference for common patterns in the mod.
/// </para>
/// <para>
/// Use these when you need to check or switch on context values without importing
/// Terraria.UI or when you need to document what contexts are expected.
/// </para>
/// </remarks>
public static class SlotContexts
{
    #region Inventory Contexts

    /// <summary>
    /// Standard inventory slot (context = 0).
    /// Main 10x5 inventory grid.
    /// </summary>
    public const int InventoryItem = ItemSlot.Context.InventoryItem;

    /// <summary>
    /// Hotbar slot (context = 13).
    /// First row of inventory, quick-access bar.
    /// </summary>
    public const int HotbarItem = ItemSlot.Context.HotbarItem;

    /// <summary>
    /// Coin slot in inventory (context = 1).
    /// The 4 coin slots at bottom of inventory.
    /// </summary>
    public const int InventoryCoin = ItemSlot.Context.InventoryCoin;

    /// <summary>
    /// Ammo slot in inventory (context = 2).
    /// The 4 ammo slots at bottom of inventory.
    /// </summary>
    public const int InventoryAmmo = ItemSlot.Context.InventoryAmmo;

    /// <summary>
    /// Trash slot (context = 6).
    /// Single trash slot for discarding items.
    /// </summary>
    public const int TrashItem = ItemSlot.Context.TrashItem;

    #endregion

    #region Equipment Contexts

    /// <summary>
    /// Armor equipment slot (context = 8).
    /// Helmet, chestplate, leggings slots.
    /// </summary>
    public const int EquipArmor = ItemSlot.Context.EquipArmor;

    /// <summary>
    /// Vanity armor slot (context = 9).
    /// Social/vanity armor slots.
    /// </summary>
    public const int EquipArmorVanity = ItemSlot.Context.EquipArmorVanity;

    /// <summary>
    /// Accessory equipment slot (context = 10).
    /// Main accessory slots.
    /// </summary>
    public const int EquipAccessory = ItemSlot.Context.EquipAccessory;

    /// <summary>
    /// Vanity accessory slot (context = 11).
    /// Social/vanity accessory slots.
    /// </summary>
    public const int EquipAccessoryVanity = ItemSlot.Context.EquipAccessoryVanity;

    /// <summary>
    /// Dye slot (context = 12).
    /// Armor and accessory dye slots.
    /// </summary>
    public const int EquipDye = ItemSlot.Context.EquipDye;

    /// <summary>
    /// Miscellaneous equipment dye slot (context = 25).
    /// Pet, mount, hook dye slots.
    /// </summary>
    public const int EquipMiscDye = ItemSlot.Context.EquipMiscDye;

    /// <summary>
    /// Grappling hook slot (context = 19).
    /// </summary>
    public const int EquipGrapple = ItemSlot.Context.EquipGrapple;

    /// <summary>
    /// Mount slot (context = 20).
    /// </summary>
    public const int EquipMount = ItemSlot.Context.EquipMount;

    /// <summary>
    /// Minecart slot (context = 21).
    /// </summary>
    public const int EquipMinecart = ItemSlot.Context.EquipMinecart;

    /// <summary>
    /// Pet slot (context = 22).
    /// </summary>
    public const int EquipPet = ItemSlot.Context.EquipPet;

    /// <summary>
    /// Light pet slot (context = 23).
    /// </summary>
    public const int EquipLight = ItemSlot.Context.EquipLight;

    #endregion

    #region Storage Contexts

    /// <summary>
    /// Chest item slot (context = 3).
    /// Any standard chest or container.
    /// </summary>
    public const int ChestItem = ItemSlot.Context.ChestItem;

    /// <summary>
    /// Piggy bank / Safe / Defender's Forge slot (context = 4).
    /// Personal storage items.
    /// </summary>
    public const int BankItem = ItemSlot.Context.BankItem;

    /// <summary>
    /// Void Vault slot (context = 26).
    /// </summary>
    public const int VoidItem = ItemSlot.Context.VoidItem;

    #endregion

    #region Shop Contexts

    /// <summary>
    /// NPC shop item slot (context = 15).
    /// Items for sale in NPC shops.
    /// </summary>
    public const int ShopItem = ItemSlot.Context.ShopItem;

    #endregion

    #region Crafting Contexts

    /// <summary>
    /// Crafting material display slot (context = 7).
    /// Shows required materials for selected recipe.
    /// </summary>
    public const int CraftingMaterial = ItemSlot.Context.CraftingMaterial;

    /// <summary>
    /// Guide item input slot (context = 5).
    /// Slot where you place item to see recipes.
    /// </summary>
    public const int GuideItem = ItemSlot.Context.GuideItem;

    /// <summary>
    /// Goblin Tinkerer reforge slot (context = 14).
    /// Slot where you place item to reforge.
    /// </summary>
    public const int PrefixItem = ItemSlot.Context.PrefixItem;

    #endregion

    #region Creative Mode Contexts

    /// <summary>
    /// Creative mode infinite items slot (context = 28).
    /// Items in journey mode research/duplication.
    /// </summary>
    public const int CreativeInfinite = ItemSlot.Context.CreativeInfinite;

    #endregion

    #region Context Groups (for pattern matching)

    /// <summary>
    /// All inventory-related contexts (main inventory, coins, ammo, hotbar).
    /// </summary>
    public static bool IsInventoryContext(int context)
    {
        return context is InventoryItem
            or HotbarItem
            or InventoryCoin
            or InventoryAmmo
            or TrashItem;
    }

    /// <summary>
    /// All equipment-related contexts (armor, accessories, dyes, etc.).
    /// </summary>
    public static bool IsEquipmentContext(int context)
    {
        return context is EquipArmor
            or EquipArmorVanity
            or EquipAccessory
            or EquipAccessoryVanity
            or EquipDye
            or EquipMiscDye
            or EquipGrapple
            or EquipMount
            or EquipMinecart
            or EquipPet
            or EquipLight;
    }

    /// <summary>
    /// All storage-related contexts (chests, banks, void vault).
    /// </summary>
    public static bool IsStorageContext(int context)
    {
        return context is ChestItem
            or BankItem
            or VoidItem;
    }

    /// <summary>
    /// All crafting-related contexts (materials, guide slot, reforge slot).
    /// </summary>
    public static bool IsCraftingContext(int context)
    {
        return context is CraftingMaterial
            or GuideItem
            or PrefixItem;
    }

    #endregion
}
