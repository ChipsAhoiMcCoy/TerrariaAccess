#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// ModSystem that hooks into WiresUI to intercept the vanilla wire color radial menu.
/// This allows the accessible menu to take control before vanilla can open its radial menu.
/// </summary>
public sealed class WireColorMenuSystem : ModSystem
{
    private static FieldInfo? _radialField;
    private static FieldInfo? _radialActiveField;
    private static MethodInfo? _radialFlowerUpdateMethod;
    private static bool _reflectionInitialized;

    // Track right-click state for detecting release during Draw cycle
    private static bool _wasRightMouseDown;

    // Track whether right-click started while inventory was open (should not trigger menu)
    private static bool _rightClickStartedWhileInventoryOpen;

    // Track inventory state to detect toggles
    private static bool _wasPlayerInventoryOpen;
    private static bool _wasInventoryButtonDown;

    // Cooldown to prevent menu from immediately reopening after close
    private static int _closeCooldownFrames;
    private const int CloseCooldownDuration = 10; // ~167ms at 60fps

    // Cooldown after inventory toggle to prevent false triggers
    private static int _inventoryToggleCooldownFrames;
    private const int InventoryToggleCooldownDuration = 10; // ~167ms at 60fps

    public override void Load()
    {
        On_WiresUI.HandleWiresUI += HandleWiresUIHook;
    }

    public override void Unload()
    {
        On_WiresUI.HandleWiresUI -= HandleWiresUIHook;
        _radialField = null;
        _radialActiveField = null;
        _radialFlowerUpdateMethod = null;
        _reflectionInitialized = false;
    }

    /// <summary>
    /// Called when the menu is closed to start the cooldown timer.
    /// This prevents the menu from immediately reopening.
    /// </summary>
    public static void NotifyMenuClosed()
    {
        _closeCooldownFrames = CloseCooldownDuration;
        // Also reset right-click tracking to prevent false detection
        _wasRightMouseDown = false;
        _rightClickStartedWhileInventoryOpen = false;
    }

    private static void HandleWiresUIHook(On_WiresUI.orig_HandleWiresUI orig, SpriteBatch spriteBatch)
    {
        // Decrement cooldowns if active
        if (_closeCooldownFrames > 0)
        {
            _closeCooldownFrames--;
        }

        if (_inventoryToggleCooldownFrames > 0)
        {
            _inventoryToggleCooldownFrames--;
        }

        // Detect inventory state changes and button presses
        bool inventoryOpen = Main.playerInventory;
        bool inventoryButtonDown = PlayerInput.Triggers.Current.Inventory;

        // Start cooldown if inventory was just toggled or button was just pressed
        if (inventoryOpen != _wasPlayerInventoryOpen || (inventoryButtonDown && !_wasInventoryButtonDown))
        {
            _inventoryToggleCooldownFrames = InventoryToggleCooldownDuration;
        }

        _wasPlayerInventoryOpen = inventoryOpen;
        _wasInventoryButtonDown = inventoryButtonDown;

        AccessibleWireColorMenu menu = AccessibleWireColorMenu.Instance;

        // If accessible menu is already open, suppress vanilla radial completely
        if (menu.IsOpen)
        {
            // Set radial.active = false BEFORE vanilla runs, so it doesn't re-enable
            SuppressVanillaRadial();

            // Call orig so wire drawing still works, but radial will be inactive
            orig(spriteBatch);

            // Suppress again after in case vanilla reactivated it
            SuppressVanillaRadial();
            return;
        }

        // During any cooldown, suppress vanilla radial to prevent brief opening
        // This includes both close cooldown and inventory toggle cooldown
        if (_closeCooldownFrames > 0 || _inventoryToggleCooldownFrames > 0)
        {
            SuppressVanillaRadial();
            _wasRightMouseDown = Main.mouseRight;
            orig(spriteBatch);
            SuppressVanillaRadial();
            return;
        }

        // Check if we should intercept right-click to open our menu instead of vanilla's
        if (ShouldInterceptRightClick())
        {
            // Prevent vanilla from opening its radial
            SuppressVanillaRadial();

            // Open our accessible menu
            menu.Open();

            // Call orig for wire drawing, but radial is suppressed
            orig(spriteBatch);

            // Ensure radial stays suppressed
            SuppressVanillaRadial();
            return;
        }

        // If holding wire tool, always suppress vanilla radial - we handle it ourselves
        if (WiresUI.Settings.DrawToolModeUI)
        {
            SuppressVanillaRadial();
            _wasRightMouseDown = Main.mouseRight;
            orig(spriteBatch);
            SuppressVanillaRadial();
            return;
        }

        // Normal case - not holding wire tool, let vanilla handle everything
        _wasRightMouseDown = Main.mouseRight;
        orig(spriteBatch);
    }

    private static bool ShouldInterceptRightClick()
    {
        // Check if holding multicolor wrench or wire kite
        if (!WiresUI.Settings.DrawToolModeUI)
        {
            _wasRightMouseDown = Main.mouseRight;
            _rightClickStartedWhileInventoryOpen = false;
            return false;
        }

        // Detect right-click release
        bool rightDown = Main.mouseRight;
        bool rightJustPressed = rightDown && !_wasRightMouseDown;
        bool rightJustReleased = _wasRightMouseDown && !rightDown;

        // Track if right-click started while inventory was open
        // This prevents B button from opening wire menu when used to close inventory
        if (rightJustPressed && Main.playerInventory)
        {
            _rightClickStartedWhileInventoryOpen = true;
        }

        // Reset the flag when right-click is released
        bool wasStartedWhileInventoryOpen = _rightClickStartedWhileInventoryOpen;
        if (rightJustReleased)
        {
            _rightClickStartedWhileInventoryOpen = false;
        }

        _wasRightMouseDown = rightDown;

        if (!rightJustReleased)
        {
            return false;
        }

        // Don't open menu if the right-click/B-button was pressed while inventory was open
        // This prevents closing inventory with B from also opening the wire menu
        if (wasStartedWhileInventoryOpen)
        {
            return false;
        }

        // Check blocking conditions
        Player player = Main.LocalPlayer;
        if (player is null || !player.active || player.dead)
        {
            return false;
        }

        if (!Main.mouseItem.IsAir)
        {
            return false;
        }

        // Don't check mouseInterface - vanilla might have set it
        // if (player.mouseInterface) return false;

        if (Main.HoveringOverAnNPC || player.talkNPC != -1)
        {
            return false;
        }

        if (Main.SmartInteractShowingGenuine)
        {
            return false;
        }

        // Don't intercept if player recently toggled inventory or is pressing inventory button
        if (_inventoryToggleCooldownFrames > 0 || PlayerInput.Triggers.Current.Inventory)
        {
            return false;
        }

        return true;
    }

    private static void SuppressVanillaRadial()
    {
        EnsureReflectionInitialized();

        if (_radialField is null || _radialActiveField is null)
        {
            return;
        }

        try
        {
            object? radial = _radialField.GetValue(null);
            if (radial is not null)
            {
                _radialActiveField.SetValue(radial, false);
            }
        }
        catch
        {
            // Silently ignore reflection failures
        }
    }

    private static void EnsureReflectionInitialized()
    {
        if (_reflectionInitialized)
        {
            return;
        }

        _reflectionInitialized = true;

        try
        {
            // WiresUI.radial is a private static field
            _radialField = typeof(WiresUI).GetField("radial", BindingFlags.NonPublic | BindingFlags.Static);

            if (_radialField is not null)
            {
                Type? radialType = _radialField.FieldType;
                _radialActiveField = radialType?.GetField("active", BindingFlags.Public | BindingFlags.Instance);
                _radialFlowerUpdateMethod = radialType?.GetMethod("FlowerUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            }
        }
        catch
        {
            // Silently ignore reflection failures
        }
    }
}
