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
            return $"Slot {slot + 1}";
        }

        if (slot < 50)
        {
            return $"Slot {slot - 9}";
        }

        if (slot < 54)
        {
            return $"Slot {slot - 49}";
        }

        if (slot < 58)
        {
            return $"Slot {slot - 53}";
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
        if (chestIndex >= 0)
        {
            // Check for custom chest name
            if (Main.chest is not null && chestIndex < Main.chest.Length)
            {
                Chest? chest = Main.chest[chestIndex];
                if (chest is not null && !string.IsNullOrWhiteSpace(chest.name))
                {
                    return chest.name;
                }
            }

            return "Chest";
        }

        return chestIndex switch
        {
            -2 => "Piggy Bank",
            -3 => "Safe",
            -4 => "Defender's Forge",
            -5 => "Void Vault",
            _ => "Chest",
        };
    }
}
