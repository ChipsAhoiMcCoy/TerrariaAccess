#nullable enable
using System;
using System.Reflection;
using Terraria;

namespace TerrariaAccess.Common.Systems.Journey;

internal static class JourneyReflection
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

    private static readonly Lazy<FieldInfo?> SacrificeSlotsField = new(() =>
        typeof(Terraria.GameContent.Creative.CreativeUI).GetField("_itemSlotsForUI", InstanceNonPublic));

    private static readonly Lazy<FieldInfo?> UiStateField = new(() =>
        typeof(Terraria.GameContent.Creative.CreativeUI).GetField("_uiState", InstanceNonPublic));

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
}
