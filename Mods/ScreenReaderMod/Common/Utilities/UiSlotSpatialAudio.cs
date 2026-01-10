#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Utilities;

internal static class UiSlotSpatialAudio
{
    public readonly record struct SlotPosition(int Row, int Column, int MaxRows, int MaxColumns);
    public readonly record struct SpatialTickParams(float Pan, float Pitch);
    public readonly record struct ScreenPosition(float X, float Y);

    private const float MaxPitch = 0.5f;
    private const float MinPitch = -0.5f;

    /// <summary>
    /// Tries to get the current UI focus position directly from Terraria's UILinkPointNavigator.
    /// This is the most accurate method as it uses the game's own position tracking.
    /// Works for gamepad navigation; for mouse, use TryGetMousePosition instead.
    /// </summary>
    public static bool TryGetCurrentLinkPointPosition(out ScreenPosition screenPos)
    {
        screenPos = default;

        // Check if gamepad UI navigation is active
        if (!PlayerInput.UsingGamepadUI)
        {
            return false;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (currentPoint < 0)
        {
            return false;
        }

        // Try to get the position from the UILinkPointNavigator
        if (!UILinkPointNavigator.Points.TryGetValue(currentPoint, out UILinkPoint? linkPoint) || linkPoint == null)
        {
            return false;
        }

        Vector2 position = linkPoint.Position;

        // Some link points (like crafting grid 700-1499) never have their positions set via
        // UILinkPointNavigator.SetPosition, so they remain at (0,0). Reject these as invalid.
        if (position.X <= 0f && position.Y <= 0f)
        {
            return false;
        }

        // Positions are already scaled by UIScale when set, so we need to unscale for our calculations
        // since we compare against screenWidth/screenHeight which are unscaled
        float uiScale = Main.UIScale;
        if (uiScale > 0f && uiScale != 1f)
        {
            position /= uiScale;
        }

        screenPos = new ScreenPosition(position.X, position.Y);
        return true;
    }

    /// <summary>
    /// Gets the current mouse/cursor position for spatial audio.
    /// During gamepad navigation, this returns the virtual cursor position which tracks the selected element.
    /// </summary>
    public static bool TryGetCursorPosition(out ScreenPosition screenPos)
    {
        // PlayerInput.MouseX/Y are synced to the selected UI element during gamepad navigation
        int mouseX = PlayerInput.MouseX;
        int mouseY = PlayerInput.MouseY;

        // Validate the position is on screen
        if (mouseX < 0 || mouseY < 0 || mouseX > Main.screenWidth || mouseY > Main.screenHeight)
        {
            screenPos = default;
            return false;
        }

        screenPos = new ScreenPosition(mouseX, mouseY);
        return true;
    }

    /// <summary>
    /// Gets the best available screen position for spatial audio.
    /// Priority: 1) UILinkPoint position (most accurate for gamepad)
    ///           2) Cursor position (synced during gamepad, actual mouse otherwise)
    ///           3) Calculated position based on context/slot (fallback)
    /// </summary>
    public static bool TryGetBestScreenPosition(int context, int slot, out ScreenPosition screenPos)
    {
        // First try: Get position from UILinkPointNavigator (most accurate for gamepad)
        if (TryGetCurrentLinkPointPosition(out screenPos))
        {
            return true;
        }

        // Second try: Get cursor position (works for both mouse and gamepad)
        if (TryGetCursorPosition(out screenPos))
        {
            return true;
        }

        // Fallback: Calculate position based on context and slot
        return TryGetSlotScreenPosition(context, slot, out screenPos);
    }

    // Standard inventory scale factors used by Terraria
    private const float MainInventoryScale = 0.85f;
    private const float ChestInventoryScale = 0.755f;
    private const float CoinAmmoScale = 0.6f;
    private const float SlotSize = 56f;

    /// <summary>
    /// Tries to get the actual screen position for a slot, based on Terraria's UI layout.
    /// This enables spatial audio to pan based on where elements actually appear on screen.
    /// </summary>
    public static bool TryGetSlotScreenPosition(int context, int slot, out ScreenPosition screenPos)
    {
        screenPos = default;
        if (slot < 0) return false;

        int absContext = context < 0 ? -context : context;
        int screenWidth = Main.screenWidth;
        int screenHeight = Main.screenHeight;

        switch (absContext)
        {
            case ItemSlot.Context.InventoryItem:
            case ItemSlot.Context.HotbarItem:
                return TryGetMainInventoryScreenPosition(slot, out screenPos);

            case ItemSlot.Context.InventoryCoin:
                // Coins: 4 vertical slots at X=497
                if (slot >= 4) return false;
                screenPos = new ScreenPosition(497f, 105f + slot * SlotSize * CoinAmmoScale);
                return true;

            case ItemSlot.Context.InventoryAmmo:
                // Ammo: 4 vertical slots at X=534 (right of coins)
                if (slot >= 4) return false;
                screenPos = new ScreenPosition(534f, 105f + slot * SlotSize * CoinAmmoScale);
                return true;

            case ItemSlot.Context.ChestItem:
            case ItemSlot.Context.BankItem:
            case ItemSlot.Context.VoidItem:
                // Chest/Bank: 10x4 grid below main inventory
                if (slot >= 40) return false;
                {
                    int col = slot % 10;
                    int row = slot / 10;
                    float invBottom = GetInvBottom();
                    screenPos = new ScreenPosition(
                        73f + col * SlotSize * ChestInventoryScale,
                        invBottom + row * SlotSize * ChestInventoryScale);
                }
                return true;

            case ItemSlot.Context.ShopItem:
                // Shop: 10x4 grid, same position as chest
                if (slot >= 40) return false;
                {
                    int col = slot % 10;
                    int row = slot / 10;
                    float invBottom = GetInvBottom();
                    screenPos = new ScreenPosition(
                        73f + col * SlotSize * ChestInventoryScale,
                        invBottom + row * SlotSize * ChestInventoryScale);
                }
                return true;

            case ItemSlot.Context.EquipArmor:
                // Armor slots: right side of screen (0-2 are helmet/chest/legs)
                if (slot >= 3) return false;
                {
                    float equipX = screenWidth - 92f;
                    float equipBaseY = GetEquipmentBaseY();
                    screenPos = new ScreenPosition(equipX, equipBaseY + slot * SlotSize * MainInventoryScale);
                }
                return true;

            case ItemSlot.Context.EquipAccessory:
                // Accessory slots: right side, below armor (slots 3+)
                if (slot >= 10) return false;
                {
                    float equipX = screenWidth - 92f;
                    float equipBaseY = GetEquipmentBaseY();
                    // Accessories start after armor (3 slots) with a 4px gap
                    int visualRow = slot + 3;
                    screenPos = new ScreenPosition(equipX, equipBaseY + visualRow * SlotSize * MainInventoryScale + 4f);
                }
                return true;

            case ItemSlot.Context.EquipArmorVanity:
                // Vanity armor: one column left of armor
                if (slot >= 3) return false;
                {
                    float equipX = screenWidth - 139f;
                    float equipBaseY = GetEquipmentBaseY();
                    screenPos = new ScreenPosition(equipX, equipBaseY + slot * SlotSize * MainInventoryScale);
                }
                return true;

            case ItemSlot.Context.EquipAccessoryVanity:
                // Vanity accessories: one column left of accessories
                if (slot >= 10) return false;
                {
                    float equipX = screenWidth - 139f;
                    float equipBaseY = GetEquipmentBaseY();
                    int visualRow = slot + 3;
                    screenPos = new ScreenPosition(equipX, equipBaseY + visualRow * SlotSize * MainInventoryScale + 4f);
                }
                return true;

            case ItemSlot.Context.EquipDye:
                // Dye slots: two columns left of armor
                if (slot >= 10) return false;
                {
                    float equipX = screenWidth - 186f;
                    float equipBaseY = GetEquipmentBaseY();
                    int visualRow = slot < 3 ? slot : slot + 3;
                    float yOffset = slot >= 3 ? 4f : 0f;
                    screenPos = new ScreenPosition(equipX, equipBaseY + visualRow * SlotSize * MainInventoryScale + yOffset);
                }
                return true;

            case ItemSlot.Context.TrashItem:
                // Trash slot: bottom-right area of main inventory grid
                screenPos = new ScreenPosition(
                    20f + 9 * SlotSize * MainInventoryScale,
                    20f + 4 * SlotSize * MainInventoryScale + 4f);
                return true;

            case ItemSlot.Context.GuideItem:
            case ItemSlot.Context.PrefixItem:
                // Guide/Reforge slot: center-ish of main inventory area
                screenPos = new ScreenPosition(
                    20f + 4 * SlotSize * MainInventoryScale,
                    20f + 2 * SlotSize * MainInventoryScale);
                return true;

            case ItemSlot.Context.CraftingMaterial:
                // Crafting materials: bottom-left area
                {
                    int col = slot % 10;
                    int row = slot / 10;
                    float craftY = screenHeight - 200f + row * SlotSize * ChestInventoryScale;
                    screenPos = new ScreenPosition(
                        20f + col * SlotSize * ChestInventoryScale,
                        craftY);
                }
                return true;

            default:
                return false;
        }
    }

    private static bool TryGetMainInventoryScreenPosition(int slot, out ScreenPosition screenPos)
    {
        screenPos = default;

        if (slot < 0 || slot >= 58) return false;

        // Hotbar and main inventory: slots 0-49 in a 10x5 grid
        if (slot < 50)
        {
            int col = slot % 10;
            int row = slot / 10;
            screenPos = new ScreenPosition(
                20f + col * SlotSize * MainInventoryScale,
                20f + row * SlotSize * MainInventoryScale);
            return true;
        }

        // Coins: slots 50-53
        if (slot < 54)
        {
            int coinIndex = slot - 50;
            screenPos = new ScreenPosition(497f, 105f + coinIndex * SlotSize * CoinAmmoScale);
            return true;
        }

        // Ammo: slots 54-57
        if (slot < 58)
        {
            int ammoIndex = slot - 54;
            screenPos = new ScreenPosition(534f, 105f + ammoIndex * SlotSize * CoinAmmoScale);
            return true;
        }

        return false;
    }

    private static float GetInvBottom()
    {
        // invBottom is typically 210, but can be 258 when crafting UI is expanded
        // For simplicity, we use the common value
        return Main.instance?.invBottom ?? 210f;
    }

    private static float GetEquipmentBaseY()
    {
        // Equipment panel starts at approximately Y = 174 + mH
        // mH is 256 when minimap overlay is visible (mapStyle == 1 and mapEnabled), otherwise 0
        // This pushes the equipment panel down when the minimap is showing
        int mH = 0;
        if (Main.mapEnabled && Main.mapStyle == 1)
        {
            mH = 256;
            // Adjust if screen is too small
            int recommendedPushUp = 600; // RecommendedEquipmentAreaPushUp constant
            if (mH + recommendedPushUp > Main.screenHeight)
            {
                mH = Main.screenHeight - recommendedPushUp;
            }
        }
        return 174f + mH;
    }

    /// <summary>
    /// Computes spatial audio parameters from screen position.
    /// Pan is based on horizontal position (-1 = left, +1 = right).
    /// Pitch is based on vertical position (+0.5 = top, -0.5 = bottom).
    /// </summary>
    public static SpatialTickParams ComputeSpatialParamsFromScreen(ScreenPosition screenPos)
    {
        int screenWidth = Main.screenWidth;
        int screenHeight = Main.screenHeight;

        // Normalize X to 0-1 range, then map to -1 to +1 for pan
        float normalizedX = MathHelper.Clamp(screenPos.X / screenWidth, 0f, 1f);
        float pan = (normalizedX * 2f) - 1f;

        // Normalize Y to 0-1 range, then invert for pitch (top = high, bottom = low)
        float normalizedY = MathHelper.Clamp(screenPos.Y / screenHeight, 0f, 1f);
        float pitch = MaxPitch - (normalizedY * (MaxPitch - MinPitch));

        return new SpatialTickParams(
            MathHelper.Clamp(pan, -1f, 1f),
            MathHelper.Clamp(pitch, MinPitch, MaxPitch));
    }

    public static bool TryGetSlotPosition(int context, int slot, out SlotPosition position)
    {
        position = default;

        if (slot < 0)
        {
            return false;
        }

        int absContext = context < 0 ? -context : context;

        switch (absContext)
        {
            case ItemSlot.Context.InventoryItem:
                return TryGetInventoryPosition(slot, out position);

            case ItemSlot.Context.InventoryCoin:
                position = new SlotPosition(Row: slot, Column: 10, MaxRows: 4, MaxColumns: 11);
                return true;

            case ItemSlot.Context.InventoryAmmo:
                position = new SlotPosition(Row: slot, Column: 11, MaxRows: 4, MaxColumns: 12);
                return true;

            case ItemSlot.Context.ChestItem:
            case ItemSlot.Context.BankItem:
            case ItemSlot.Context.VoidItem:
                if (slot >= 40)
                {
                    return false;
                }

                position = new SlotPosition(Row: slot / 10, Column: slot % 10, MaxRows: 4, MaxColumns: 10);
                return true;

            case ItemSlot.Context.ShopItem:
                if (slot >= 40)
                {
                    return false;
                }

                position = new SlotPosition(Row: slot / 10, Column: slot % 10, MaxRows: 4, MaxColumns: 10);
                return true;

            case ItemSlot.Context.EquipArmor:
            case ItemSlot.Context.EquipArmorVanity:
                position = new SlotPosition(Row: slot, Column: 0, MaxRows: 3, MaxColumns: 1);
                return true;

            case ItemSlot.Context.EquipAccessory:
            case ItemSlot.Context.EquipAccessoryVanity:
                int accessoryRow = slot < 3 ? slot : (slot - 3) + 3;
                position = new SlotPosition(Row: accessoryRow, Column: 0, MaxRows: 10, MaxColumns: 1);
                return true;

            case ItemSlot.Context.EquipDye:
                position = new SlotPosition(Row: slot, Column: 0, MaxRows: 10, MaxColumns: 1);
                return true;

            case ItemSlot.Context.TrashItem:
                position = new SlotPosition(Row: 4, Column: 9, MaxRows: 5, MaxColumns: 10);
                return true;

            case ItemSlot.Context.GuideItem:
            case ItemSlot.Context.PrefixItem:
                position = new SlotPosition(Row: 2, Column: 4, MaxRows: 5, MaxColumns: 10);
                return true;

            case ItemSlot.Context.CraftingMaterial:
                int craftRow = slot / 10;
                int craftCol = slot % 10;
                position = new SlotPosition(Row: craftRow, Column: craftCol, MaxRows: 4, MaxColumns: 10);
                return true;

            default:
                return false;
        }
    }

    private static bool TryGetInventoryPosition(int slot, out SlotPosition position)
    {
        position = default;

        if (slot < 0 || slot >= 58)
        {
            return false;
        }

        if (slot < 10)
        {
            position = new SlotPosition(Row: 0, Column: slot, MaxRows: 5, MaxColumns: 10);
            return true;
        }

        if (slot < 50)
        {
            int inventorySlot = slot - 10;
            int row = (inventorySlot / 10) + 1;
            int col = inventorySlot % 10;
            position = new SlotPosition(Row: row, Column: col, MaxRows: 5, MaxColumns: 10);
            return true;
        }

        if (slot < 54)
        {
            int coinIndex = slot - 50;
            position = new SlotPosition(Row: coinIndex, Column: 10, MaxRows: 4, MaxColumns: 11);
            return true;
        }

        if (slot < 58)
        {
            int ammoIndex = slot - 54;
            position = new SlotPosition(Row: ammoIndex, Column: 11, MaxRows: 4, MaxColumns: 12);
            return true;
        }

        return false;
    }

    public static bool TryGetCraftingGridPosition(int availableIndex, out SlotPosition position)
    {
        position = default;

        if (availableIndex < 0)
        {
            return false;
        }

        // Match Terraria's crafting grid constants from Main.cs
        const int CraftingSlotSpacing = 42;
        const int CraftingBaseX = 310;
        const int CraftingRightMargin = 280;

        int screenWidth = Main.screenWidth;
        int iconsPerRow = (screenWidth - CraftingBaseX - CraftingRightMargin) / CraftingSlotSpacing;
        if (iconsPerRow <= 0)
        {
            iconsPerRow = 10; // Fallback to reasonable default
        }

        int recStart = Main.recStart;
        int localIndex = availableIndex - recStart;
        if (localIndex < 0)
        {
            localIndex = availableIndex;
        }

        int row = localIndex / iconsPerRow;
        int col = localIndex % iconsPerRow;

        // MaxRows is estimated from screen height using Terraria's formula
        const int CraftingBaseY = 340;
        const int CraftingBottomMargin = 20;
        int screenHeight = Main.screenHeight;
        int iconsPerColumn = (screenHeight - CraftingBaseY - CraftingBottomMargin) / CraftingSlotSpacing;
        if (iconsPerColumn <= 0)
        {
            iconsPerColumn = 4; // Fallback to reasonable default
        }

        position = new SlotPosition(Row: row, Column: col, MaxRows: iconsPerColumn, MaxColumns: iconsPerRow);
        return true;
    }

    /// <summary>
    /// Gets the screen position for a crafting grid slot based on its available recipe index.
    /// The crafting grid appears at the bottom-left when expanded (recBigList = true).
    /// </summary>
    /// <remarks>
    /// Position values derived from Terraria's Main.cs crafting grid drawing code:
    /// - Base X = 310 (num81)
    /// - Base Y = 340 (num80)
    /// - Slot spacing = 42 pixels (num79)
    /// - Icons per row = (screenWidth - 310 - 280) / 42 (dynamic based on screen width)
    /// </remarks>
    public static bool TryGetCraftingGridScreenPosition(int availableIndex, out ScreenPosition screenPos)
    {
        screenPos = default;

        if (availableIndex < 0) return false;

        // Match Terraria's crafting grid constants from Main.cs
        const int CraftingSlotSpacing = 42;
        const int CraftingBaseX = 310;
        const int CraftingBaseY = 340;
        const int CraftingRightMargin = 280;

        int screenWidth = Main.screenWidth;
        int iconsPerRow = (screenWidth - CraftingBaseX - CraftingRightMargin) / CraftingSlotSpacing;
        if (iconsPerRow <= 0)
        {
            iconsPerRow = 10; // Fallback to reasonable default
        }

        int recStart = Main.recStart;
        int localIndex = availableIndex - recStart;
        if (localIndex < 0)
        {
            localIndex = availableIndex;
        }

        int col = localIndex % iconsPerRow;
        int row = localIndex / iconsPerRow;

        // Calculate screen position matching Terraria's drawing code
        // Position is at the top-left of the slot, add half slot size to center
        float x = CraftingBaseX + col * CraftingSlotSpacing + CraftingSlotSpacing * 0.5f;
        float y = CraftingBaseY + row * CraftingSlotSpacing + CraftingSlotSpacing * 0.5f;

        screenPos = new ScreenPosition(x, y);
        return true;
    }

    public static SpatialTickParams ComputeSpatialParams(SlotPosition position)
    {
        float pan = ComputePan(position.Column, position.MaxColumns);
        float pitch = ComputePitch(position.Row, position.MaxRows);
        return new SpatialTickParams(pan, pitch);
    }

    private static float ComputePan(int column, int maxColumns)
    {
        if (maxColumns <= 1)
        {
            return 0f;
        }

        float normalizedColumn = column / (float)(maxColumns - 1);
        float pan = (normalizedColumn * 2f) - 1f;
        return MathHelper.Clamp(pan, -1f, 1f);
    }

    private static float ComputePitch(int row, int maxRows)
    {
        if (maxRows <= 1)
        {
            return 0f;
        }

        float normalizedRow = row / (float)(maxRows - 1);
        float pitch = MaxPitch - (normalizedRow * (MaxPitch - MinPitch));
        return MathHelper.Clamp(pitch, MinPitch, MaxPitch);
    }
}
