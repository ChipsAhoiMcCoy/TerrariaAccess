#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.Map;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Builds requirement messages for crafting recipes.
/// </summary>
internal static class RecipeRequirementBuilder
{
    private static readonly string[] NeedWaterMembers = { "needWater", "_needWater", "NeedWater" };
    private static readonly string[] NeedHoneyMembers = { "needHoney", "_needHoney", "NeedHoney" };
    private static readonly string[] NeedLavaMembers = { "needLava", "_needLava", "NeedLava" };
    private static readonly string[] NeedSnowBiomeMembers = { "needSnowBiome", "_needSnowBiome", "NeedSnowBiome" };
    private static readonly string[] NeedGraveyardBiomeMembers = { "needGraveyardBiome", "_needGraveyardBiome", "NeedGraveyardBiome" };
    private static readonly string[] AnyIronBarMembers = { "anyIronBar", "_anyIronBar", "AnyIronBar" };
    private static readonly string[] AnyWoodMembers = { "anyWood", "_anyWood", "AnyWood" };
    private static readonly string[] AnySandMembers = { "anySand", "_anySand", "AnySand" };
    private static readonly string[] AnyFragmentMembers = { "anyFragment", "_anyFragment", "AnyFragment" };
    private static readonly string[] AnyPressurePlateMembers = { "anyPressurePlate", "_anyPressurePlate", "AnyPressurePlate" };

    private static bool _loggedRecipeGroupReflectionWarning;
    private static bool _loggedRecipeFlagReflectionWarning;

    /// <summary>
    /// Builds a requirement message for a recipe.
    /// </summary>
    public static string? BuildMessage(Recipe recipe, out bool hadRequirements)
    {
        hadRequirements = false;
        if (recipe is null)
        {
            return null;
        }

        List<string> ingredientParts = BuildIngredientRequirementParts(recipe);
        List<string> stationParts = BuildStationRequirementParts(recipe);

        hadRequirements = ingredientParts.Count > 0 || stationParts.Count > 0;
        if (!hadRequirements)
        {
            return null;
        }

        List<string> segments = new();
        if (ingredientParts.Count > 0)
        {
            segments.Add($"Requires {string.Join(", ", ingredientParts)}");
        }

        if (stationParts.Count > 0)
        {
            string prefix = TextSanitizer.Clean(Lang.inter[22].Value);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "Required objects:";
            }

            segments.Add($"{prefix} {string.Join(", ", stationParts)}");
        }

        string message = string.Join(". ", segments);
        return GlyphTagFormatter.Normalize(message);
    }

    private static List<string> BuildIngredientRequirementParts(Recipe recipe)
    {
        var parts = new List<string>();
        IList<Item>? requiredItems = recipe.requiredItem;
        if (requiredItems is null || requiredItems.Count == 0)
        {
            return parts;
        }

        for (int i = 0; i < requiredItems.Count; i++)
        {
            Item ingredient = requiredItems[i];
            if (ingredient is null)
            {
                continue;
            }

            if (ingredient.type == ItemID.None)
            {
                break;
            }

            if (ingredient.IsAir || ingredient.stack <= 0)
            {
                continue;
            }

            string? description = DescribeRequirement(recipe, ingredient, i);
            if (!string.IsNullOrWhiteSpace(description))
            {
                parts.Add(description);
            }
        }

        return parts;
    }

    private static List<string> BuildStationRequirementParts(Recipe recipe)
    {
        var results = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddUnique(string? value)
        {
            string cleaned = TextSanitizer.Clean(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            string normalized = GlyphTagFormatter.Normalize(cleaned);
            if (unique.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        IList<int>? requiredTiles = recipe.requiredTile;
        if (requiredTiles is not null)
        {
            for (int i = 0; i < requiredTiles.Count; i++)
            {
                int tileId = requiredTiles[i];
                if (tileId == -1)
                {
                    break;
                }

                AddUnique(ResolveRequiredTileLabel(tileId));
            }
        }

        if (TryGetRecipeBool(recipe, NeedWaterMembers, out bool needWater) && needWater)
        {
            AddUnique(Lang.inter[53].Value);
        }

        if (TryGetRecipeBool(recipe, NeedHoneyMembers, out bool needHoney) && needHoney)
        {
            AddUnique(Lang.inter[58].Value);
        }

        if (TryGetRecipeBool(recipe, NeedLavaMembers, out bool needLava) && needLava)
        {
            AddUnique(Lang.inter[56].Value);
        }

        if (TryGetRecipeBool(recipe, NeedSnowBiomeMembers, out bool needSnow) && needSnow)
        {
            AddUnique(Lang.inter[123].Value);
        }

        if (TryGetRecipeBool(recipe, NeedGraveyardBiomeMembers, out bool needGraveyard) && needGraveyard)
        {
            AddUnique(Lang.inter[124].Value);
        }

        if (recipe.Conditions is not null)
        {
            foreach (Condition condition in recipe.Conditions)
            {
                AddUnique(condition?.Description?.Value);
            }
        }

        return results;
    }

    private static string? ResolveRequiredTileLabel(int tileId)
    {
        if (tileId < 0)
        {
            return null;
        }

        try
        {
            int style = Recipe.GetRequiredTileStyle(tileId);
            int lookup = MapHelper.TileToLookup(tileId, style);
            string? mapObjectName = Lang.GetMapObjectName(lookup);
            if (!string.IsNullOrWhiteSpace(mapObjectName))
            {
                return TextSanitizer.Clean(mapObjectName);
            }
        }
        catch
        {
            // Ignore lookup failures
        }

        string? tileName = TileID.Search.GetName(tileId);
        if (!string.IsNullOrWhiteSpace(tileName))
        {
            return TextSanitizer.Clean(tileName);
        }

        return $"Tile {tileId}";
    }

    private static bool TryGetRecipeBool(Recipe recipe, string[] memberNames, out bool value)
    {
        value = false;
        if (recipe is null)
        {
            return false;
        }

        Type type = recipe.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string memberName in memberNames)
        {
            FieldInfo? field = type.GetField(memberName, flags);
            if (field is not null)
            {
                try
                {
                    object? result = field.GetValue(recipe);
                    if (result is bool boolValue)
                    {
                        value = boolValue;
                        return true;
                    }
                }
                catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                {
                    _loggedRecipeFlagReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[RecipeRequirementBuilder] Failed to read recipe field {memberName}: {ex}");
                }

                return false;
            }

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property is not null && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(bool))
            {
                try
                {
                    object? result = property.GetValue(recipe);
                    if (result is bool boolValue)
                    {
                        value = boolValue;
                        return true;
                    }
                }
                catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                {
                    _loggedRecipeFlagReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[RecipeRequirementBuilder] Failed to read recipe property {memberName}: {ex}");
                }

                return false;
            }
        }

        return false;
    }

    private static string? DescribeRequirement(Recipe recipe, Item ingredient, int index)
    {
        if (recipe is null ||
            ingredient is null ||
            ingredient.type == ItemID.None ||
            ingredient.stack <= 0 ||
            ingredient.IsAir)
        {
            return null;
        }

        string? label = ResolveRequirementLabel(recipe, ingredient, index);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Item {ingredient.type}";
        }

        string sanitized = GlyphTagFormatter.Normalize(label);
        int stack = Math.Max(1, ingredient.stack);
        return stack > 1 ? $"{stack} {sanitized}" : sanitized;
    }

    private static string? ResolveRequirementLabel(Recipe recipe, Item ingredient, int index)
    {
        if (recipe is null || ingredient is null)
        {
            return null;
        }

        if (TryResolveProcessGroupLabel(recipe, ingredient, out string? groupLabel))
        {
            return groupLabel;
        }

        string? anyLabel = ResolveAnyRequirementLabel(recipe, ingredient);
        if (!string.IsNullOrWhiteSpace(anyLabel))
        {
            return anyLabel;
        }

        int groupId = RecipeGroupResolver.GetAcceptedGroupId(recipe, index);
        if (groupId >= 0)
        {
            try
            {
                Dictionary<int, RecipeGroup>? groups = RecipeGroup.recipeGroups;
                if (groups is not null && groups.TryGetValue(groupId, out RecipeGroup? group) && group is not null)
                {
                    string? value = group.GetText();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return TextSanitizer.Clean(value);
                    }
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[RecipeRequirementBuilder] Failed to resolve recipe group {groupId}: {ex}");
            }
        }

        string name = TextSanitizer.Clean(ingredient.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(Lang.GetItemNameValue(ingredient.type));
        }

        return name;
    }

    private static bool TryResolveProcessGroupLabel(Recipe recipe, Item ingredient, out string? label)
    {
        label = null;
        try
        {
            if (recipe.ProcessGroupsForText(ingredient.type, out string? text) && !string.IsNullOrWhiteSpace(text))
            {
                label = TextSanitizer.Clean(text);
                return true;
            }
        }
        catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
        {
            _loggedRecipeGroupReflectionWarning = true;
            ScreenReaderMod.Instance?.Logger.Warn($"[RecipeRequirementBuilder] Failed to process recipe group text: {ex}");
        }

        return false;
    }

    private static string? ResolveAnyRequirementLabel(Recipe recipe, Item ingredient)
    {
        string prefix = TextSanitizer.Clean(Lang.misc[37].Value);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "Any";
        }

        static string CombineAnyLabel(string prefix, string? suffix)
        {
            string cleanedSuffix = TextSanitizer.Clean(suffix);
            if (string.IsNullOrWhiteSpace(cleanedSuffix))
            {
                return prefix;
            }

            return $"{prefix} {cleanedSuffix}";
        }

        if (TryGetRecipeBool(recipe, AnyIronBarMembers, out bool anyIronBar) && anyIronBar && ingredient.type == ItemID.IronBar)
        {
            return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.IronBar));
        }

        if (TryGetRecipeBool(recipe, AnyWoodMembers, out bool anyWood) && anyWood && ingredient.type == ItemID.Wood)
        {
            return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.Wood));
        }

        if (TryGetRecipeBool(recipe, AnySandMembers, out bool anySand) && anySand && ingredient.type == ItemID.SandBlock)
        {
            return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.SandBlock));
        }

        if (TryGetRecipeBool(recipe, AnyFragmentMembers, out bool anyFragment) && anyFragment && ingredient.type == ItemID.FragmentSolar)
        {
            return CombineAnyLabel(prefix, Lang.misc[51].Value);
        }

        const int PressurePlateItemId = 542;
        if (TryGetRecipeBool(recipe, AnyPressurePlateMembers, out bool anyPressurePlate) && anyPressurePlate && ingredient.type == PressurePlateItemId)
        {
            return CombineAnyLabel(prefix, Lang.misc[38].Value);
        }

        return null;
    }
}

/// <summary>
/// Resolves recipe group IDs using reflection.
/// </summary>
internal static class RecipeGroupResolver
{
    private static readonly Lazy<Dictionary<int, int>> RecipeGroupLookup = new(DiscoverRecipeGroupLookup);
    private static readonly Func<Recipe, int, int>? AcceptedGroupResolver = CreateAcceptedGroupResolver();
    private static bool _loggedReflectionWarning;

    public static int GetAcceptedGroupId(Recipe recipe, int index)
    {
        if (recipe is null || index < 0)
        {
            return -1;
        }

        if (AcceptedGroupResolver is not null)
        {
            try
            {
                int value = AcceptedGroupResolver(recipe, index);
                if (value >= 0)
                {
                    return value;
                }
            }
            catch (Exception ex) when (!_loggedReflectionWarning)
            {
                _loggedReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[RecipeGroupResolver] Failed to inspect recipe accepted groups: {ex}");
            }
        }

        IList<Item>? requiredItems = recipe.requiredItem;
        if (requiredItems is not null &&
            index >= 0 &&
            index < requiredItems.Count &&
            RecipeGroupLookup.Value.TryGetValue(requiredItems[index].type, out int fallbackGroup) &&
            fallbackGroup >= 0)
        {
            return fallbackGroup;
        }

        return -1;
    }

    private static Dictionary<int, int> DiscoverRecipeGroupLookup()
    {
        var result = new Dictionary<int, int>();

        try
        {
            Type groupType = typeof(RecipeGroup);
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] candidateNames =
            {
                "recipeGroupIDs",
                "recipeGroupLookup",
                "recipeGroupLookupTable",
                "_recipeGroupIDs",
                "_recipeGroupLookup"
            };

            foreach (string fieldName in candidateNames)
            {
                FieldInfo? field = groupType.GetField(fieldName, flags);
                if (field is null)
                {
                    continue;
                }

                object? value = field.GetValue(null);
                if (value is IDictionary dictionary)
                {
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is int key && entry.Value is int id)
                        {
                            result[key] = id;
                        }
                    }
                }
                else if (value is IEnumerable<KeyValuePair<int, int>> pairs)
                {
                    foreach (KeyValuePair<int, int> pair in pairs)
                    {
                        result[pair.Key] = pair.Value;
                    }
                }

                if (result.Count > 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (!_loggedReflectionWarning)
        {
            _loggedReflectionWarning = true;
            ScreenReaderMod.Instance?.Logger.Warn($"[RecipeGroupResolver] Failed to discover recipe group lookup: {ex}");
        }

        return result;
    }

    private static Func<Recipe, int, int>? CreateAcceptedGroupResolver()
    {
        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] candidateFields = { "acceptedGroup", "_acceptedGroup", "AcceptedGroup" };

            foreach (string fieldName in candidateFields)
            {
                FieldInfo? field = typeof(Recipe).GetField(fieldName, flags);
                if (field is null)
                {
                    continue;
                }

                if (field.FieldType == typeof(int[]))
                {
                    return (recipe, index) =>
                    {
                        if (recipe is null || index < 0)
                        {
                            return -1;
                        }

                        if (field.GetValue(recipe) is int[] groups && index < groups.Length)
                        {
                            return groups[index];
                        }

                        return -1;
                    };
                }

                if (typeof(IList<int>).IsAssignableFrom(field.FieldType))
                {
                    return (recipe, index) =>
                    {
                        if (recipe is null || index < 0)
                        {
                            return -1;
                        }

                        if (field.GetValue(recipe) is IList<int> list && index < list.Count)
                        {
                            return list[index];
                        }

                        return -1;
                    };
                }
            }
        }
        catch (Exception ex) when (!_loggedReflectionWarning)
        {
            _loggedReflectionWarning = true;
            ScreenReaderMod.Instance?.Logger.Warn($"[RecipeGroupResolver] Failed to bind accepted group resolver: {ex}");
        }

        return null;
    }
}
