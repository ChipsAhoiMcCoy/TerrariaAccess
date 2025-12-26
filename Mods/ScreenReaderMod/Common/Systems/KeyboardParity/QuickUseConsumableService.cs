#nullable enable
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Handles quick-use consumable item logic for keyboard parity mode.
/// Allows players to quickly use healing items, mana potions, and buff items
/// from inventory using a keybind.
/// </summary>
internal static class QuickUseConsumableService
{
    private static bool _wasQuickUseKeyDown;

    /// <summary>
    /// Processes the quick-use keybind and uses the focused consumable item if valid.
    /// Should be called during inventory UI updates.
    /// </summary>
    internal static void TryApplyQuickUse()
    {
        ModKeybind? keybind = ControllerParityKeybinds.InventoryQuickUse;
        if (keybind is null)
        {
            _wasQuickUseKeyDown = false;
            return;
        }

        bool isPressed = keybind.Current || VirtualTriggerService.IsKeybindPressedRaw(keybind);
        bool justPressed = isPressed && !_wasQuickUseKeyDown;
        _wasQuickUseKeyDown = isPressed;

        if (!justPressed)
        {
            return;
        }

        // Get the currently focused inventory slot from UILinkPointNavigator
        // Link points 0-49 correspond directly to inventory slots 0-49
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (currentPoint < 0 || currentPoint > 49)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active || player.dead || player.cursed || player.CCed)
        {
            return;
        }

        // Check that cursor is empty (can't quick-use while holding an item)
        if (Main.mouseItem.stack > 0)
        {
            return;
        }

        Item item = player.inventory[currentPoint];
        if (item is null || item.IsAir || item.stack <= 0)
        {
            return;
        }

        // Check if this item can be quick-used (similar to Item.CanBeQuickUsed)
        bool canQuickUse = item.healLife > 0 || item.healMana > 0 ||
                           (item.buffType > 0 && item.buffTime > 0);
        if (!canQuickUse)
        {
            return;
        }

        // Use the item directly (modeled after Player.QuickBuff)
        TryUseConsumableItem(player, item);
    }

    /// <summary>
    /// Resets the quick-use key tracking state.
    /// Call this when the feature is disabled or during cleanup.
    /// </summary>
    internal static void ResetState()
    {
        _wasQuickUseKeyDown = false;
    }

    private static void TryUseConsumableItem(Player player, Item item)
    {
        // Check if the player can use this item
        if (!CombinedHooks.CanUseItem(player, item))
        {
            return;
        }

        // Handle healing items
        if (item.healLife > 0 && player.statLife < player.statLifeMax2)
        {
            player.statLife += item.healLife;
            if (player.statLife > player.statLifeMax2)
            {
                player.statLife = player.statLifeMax2;
            }
            player.HealEffect(item.healLife);
        }

        if (item.healMana > 0 && player.statMana < player.statManaMax2)
        {
            player.statMana += item.healMana;
            if (player.statMana > player.statManaMax2)
            {
                player.statMana = player.statManaMax2;
            }
            player.ManaEffect(item.healMana);
        }

        // Handle buff items
        if (item.buffType > 0)
        {
            int buffTime = item.buffTime;
            if (buffTime == 0)
            {
                buffTime = 3600; // Default 1 minute
            }
            player.AddBuff(item.buffType, buffTime);
        }

        // Call mod hooks
        ItemLoader.UseItem(item, player);

        // Play sound
        if (item.UseSound.HasValue)
        {
            SoundEngine.PlaySound(item.UseSound.Value, player.Center);
        }

        // Consume the item if it's consumable
        if (item.consumable && ItemLoader.ConsumeItem(item, player))
        {
            item.stack--;
            if (item.stack <= 0)
            {
                item.TurnToAir();
            }
        }

        // Update recipes
        Recipe.FindRecipes();
    }
}
