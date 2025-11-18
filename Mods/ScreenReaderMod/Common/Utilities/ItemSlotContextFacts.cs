#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ScreenReaderMod.Common.Services;
using Terraria.UI;

namespace ScreenReaderMod.Common.Utilities;

internal static class ItemSlotContextFacts
{
    private static readonly Lazy<Dictionary<int, string>> ContextNames = new(BuildContextNames);

    public static UiNarrationArea ResolveArea(int context)
    {
        int normalized = Math.Abs(context);
        if (normalized == 0)
        {
            return UiNarrationArea.Inventory;
        }

        if (!ContextNames.Value.TryGetValue(normalized, out string? name) || name is null)
        {
            return UiNarrationArea.Inventory;
        }

        if (Contains(name, "CRAFT"))
        {
            return UiNarrationArea.Crafting;
        }

        if (Contains(name, "GUIDE"))
        {
            return UiNarrationArea.Guide;
        }

        if (Contains(name, "FORGE"))
        {
            return UiNarrationArea.Reforge;
        }

        if (Contains(name, "SHOP"))
        {
            return UiNarrationArea.Shop;
        }

        if (Contains(name, "CHEST") || Contains(name, "BANK") || Contains(name, "VAULT"))
        {
            return UiNarrationArea.Storage;
        }

        if (Contains(name, "CREATIVE"))
        {
            return UiNarrationArea.Creative;
        }

        return UiNarrationArea.Inventory;
    }

    public static bool IsCraftingContext(int context)
    {
        UiNarrationArea area = ResolveArea(context);
        return area == UiNarrationArea.Crafting || area == UiNarrationArea.Guide;
    }

    private static Dictionary<int, string> BuildContextNames()
    {
        var map = new Dictionary<int, string>();
        FieldInfo[] fields = typeof(ItemSlot.Context).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (FieldInfo field in fields)
        {
            if (field.FieldType != typeof(int))
            {
                continue;
            }

            try
            {
                object? rawValue = field.GetValue(null);
                if (rawValue is not int contextValue)
                {
                    continue;
                }

                int normalized = Math.Abs(contextValue);
                if (normalized < 0)
                {
                    continue;
                }

                map[normalized] = field.Name ?? string.Empty;
            }
            catch
            {
                // Swallow individual reflection failures so we can continue building the map.
            }
        }

        return map;
    }

    private static bool Contains(string source, string token)
    {
        return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
