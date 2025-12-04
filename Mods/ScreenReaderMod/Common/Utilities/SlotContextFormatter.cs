#nullable enable
using Terraria;

namespace ScreenReaderMod.Common.Utilities;

internal static class SlotContextFormatter
{
    public static string DescribeInventorySlot(int slot)
    {
        if (slot < 0)
        {
            return string.Empty;
        }

        if (slot < 10)
        {
            return $"Hotbar slot {slot + 1}";
        }

        if (slot < 50)
        {
            return $"Inventory slot {slot - 9}";
        }

        if (slot < 54)
        {
            return $"Coin slot {slot - 49}";
        }

        if (slot < 58)
        {
            return $"Ammo slot {slot - 53}";
        }

        return string.Empty;
    }

    public static string DescribeArmorSlot(int index)
    {
        return index switch
        {
            0 => "Helmet slot",
            1 => "Chestplate slot",
            2 => "Leggings slot",
            >= 3 and < 10 => $"Accessory slot {index - 2}",
            10 => "Vanity helmet slot",
            11 => "Vanity chestplate slot",
            12 => "Vanity leggings slot",
            >= 13 and < 20 => $"Vanity accessory slot {index - 12}",
            _ => $"Armor slot {index + 1}",
        };
    }

    public static string DescribeContainer(int chestIndex)
    {
        return chestIndex switch
        {
            >= 0 => "Chest",
            -2 => "Piggy bank",
            -3 => "Safe",
            -4 => "Defender's forge",
            -5 => "Void vault",
            _ => "Chest",
        };
    }
}
