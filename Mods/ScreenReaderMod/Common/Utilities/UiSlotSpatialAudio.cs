#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Utilities;

internal static class UiSlotSpatialAudio
{
    public readonly record struct SlotPosition(int Row, int Column, int MaxRows, int MaxColumns);
    public readonly record struct SpatialTickParams(float Pan, float Pitch);

    private const float MaxPitch = 0.5f;
    private const float MinPitch = -0.5f;

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

        int recStart = Main.recStart;
        int localIndex = availableIndex - recStart;
        if (localIndex < 0)
        {
            localIndex = availableIndex;
        }

        int row = localIndex / 10;
        int col = localIndex % 10;

        position = new SlotPosition(Row: row, Column: col, MaxRows: 4, MaxColumns: 10);
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
