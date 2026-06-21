#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.GameContent.UI.Elements;

namespace TerrariaAccess.Common.Systems.Journey;

internal static class JourneyReflection
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

    private static readonly Lazy<FieldInfo?> SacrificeSlotsField = new(() =>
        typeof(Terraria.GameContent.Creative.CreativeUI).GetField("_itemSlotsForUI", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> UiStateField = new(() =>
        typeof(Terraria.GameContent.Creative.CreativeUI).GetField("_uiState", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> InfiniteItemsWindowField = new(() =>
        ResolveType("Terraria.GameContent.UI.States.UICreativePowersMenu")
            ?.GetField("_infiniteItemsWindow", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> InfiniteItemsSearchBarField = new(() =>
        ResolveType("Terraria.GameContent.UI.Elements.UICreativeInfiniteItemsDisplay")
            ?.GetField("_searchBar", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> InfiniteItemsSearchStringField = new(() =>
        ResolveType("Terraria.GameContent.UI.Elements.UICreativeInfiniteItemsDisplay")
            ?.GetField("_searchString", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> InfiniteItemsGridField = new(() =>
        ResolveType("Terraria.GameContent.UI.Elements.UICreativeInfiniteItemsDisplay")
            ?.GetField("_itemGrid", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> DynamicItemIdsAvailableToShowField = new(() =>
        ResolveType("Terraria.GameContent.UI.Elements.UIDynamicItemCollection")
            ?.GetField("_itemIdsAvailableToShow", InstanceNonPublic));

    private static readonly Lazy<MethodInfo?> DynamicItemGridParametersMethod = new(() =>
        ResolveType("Terraria.GameContent.UI.Elements.UIDynamicItemCollection")
            ?.GetMethod("GetGridParameters", InstanceNonPublic));

    public static Item? TryGetSacrificeItem()
    {
        if (SacrificeSlotsField.Value?.GetValue(Main.CreativeMenu) is not Item[] slots || slots.Length == 0)
        {
            return null;
        }

        return slots[0];
    }

    public static Item[]? TryGetSacrificeSlotsArray()
    {
        return SacrificeSlotsField.Value?.GetValue(Main.CreativeMenu) as Item[];
    }

    public static object? TryGetPowersMenuUiState()
    {
        return UiStateField.Value?.GetValue(Main.CreativeMenu);
    }

    public static UISearchBar? TryGetInfiniteItemsSearchBar()
    {
        object? window = TryGetInfiniteItemsWindow();
        return window is null ? null : InfiniteItemsSearchBarField.Value?.GetValue(window) as UISearchBar;
    }

    public static string? TryGetInfiniteItemsSearchString()
    {
        object? window = TryGetInfiniteItemsWindow();
        return window is null ? null : InfiniteItemsSearchStringField.Value?.GetValue(window) as string;
    }

    public static bool TryGetInfiniteItemsItemForLinkPoint(int point, out Item? item)
    {
        item = null;

        if (Main.CreativeMenu?.Enabled != true || TryGetCurrentPowersCategoryOption() != 1)
        {
            return false;
        }

        // Link IDs are assigned as: main strip, search field, 11 category filters,
        // then the visible duplicate item grid.
        int searchPoint = Main.CreativeMenu.GamepadPointIdForInfiniteItemSearchHack;
        if (searchPoint < 0)
        {
            searchPoint = 10007;
        }

        const int filterCount = 11;
        int firstItemPoint = searchPoint + 1 + filterCount;
        int visibleOffset = point - firstItemPoint;
        if (visibleOffset < 0)
        {
            return false;
        }

        object? window = TryGetInfiniteItemsWindow();
        object? grid = window is null ? null : InfiniteItemsGridField.Value?.GetValue(window);
        if (grid is null)
        {
            return false;
        }

        if (DynamicItemIdsAvailableToShowField.Value?.GetValue(grid) is not IList<int> itemIds ||
            itemIds.Count == 0)
        {
            return false;
        }

        int startItemIndex = 0;
        if (DynamicItemGridParametersMethod.Value is MethodInfo gridParametersMethod)
        {
            object[] args = { 0, 0, 0, 0 };
            try
            {
                gridParametersMethod.Invoke(grid, args);
                startItemIndex = Convert.ToInt32(args[2]);
            }
            catch
            {
                startItemIndex = 0;
            }
        }

        int itemIndex = startItemIndex + visibleOffset;
        if ((uint)itemIndex >= (uint)itemIds.Count)
        {
            return false;
        }

        int itemId = itemIds[itemIndex];
        if (itemId <= 0)
        {
            return false;
        }

        Item resolved = new();
        resolved.SetDefaults(itemId);
        item = resolved;
        return !resolved.IsAir;
    }

    public static int? TryGetCurrentPowersCategoryOption()
    {
        return TryGetMenuTreeCurrentOption("_mainCategory");
    }

    public static int? TryGetTimePowersSubcategoryOption()
    {
        return TryGetMenuTreeCurrentOption("_timeCategory");
    }

    public static int? TryGetWeatherPowersSubcategoryOption()
    {
        return TryGetMenuTreeCurrentOption("_weatherCategory");
    }

    public static int? TryGetPersonalPowersSubcategoryOption()
    {
        return TryGetMenuTreeCurrentOption("_personalCategory");
    }

    private static int? TryGetMenuTreeCurrentOption(string fieldName)
    {
        object? uiState = TryGetPowersMenuUiState();
        if (uiState is null) return null;

        FieldInfo? categoryField = uiState.GetType().GetField(fieldName, InstanceNonPublic);
        object? category = categoryField?.GetValue(uiState);
        if (category is null) return null;

        PropertyInfo? prop = category.GetType().GetProperty("CurrentOption", InstancePublic);
        object? value = prop?.GetValue(category);
        if (value is null)
        {
            FieldInfo? field = category.GetType().GetField("CurrentOption", InstancePublic);
            value = field?.GetValue(category);
        }

        if (value is null) return null;
        try
        {
            return System.Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetInfiniteItemsWindow()
    {
        object? uiState = TryGetPowersMenuUiState();
        return uiState is null ? null : InfiniteItemsWindowField.Value?.GetValue(uiState);
    }

    private static Type? ResolveType(string fullName)
    {
        Type? found = Type.GetType(fullName);
        if (found is not null) return found;

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
            }
            catch
            {
            }
        }

        return null;
    }
}
