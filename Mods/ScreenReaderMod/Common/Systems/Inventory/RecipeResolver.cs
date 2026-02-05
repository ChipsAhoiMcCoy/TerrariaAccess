#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terraria;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Resolves and caches recipe lookups for the crafting UI.
/// </summary>
internal sealed class RecipeResolver
{
    private static int _recipeLookupVersion = -1;
    private static Dictionary<Item, int>? _recipeResultLookup;

    /// <summary>
    /// Attempts to get the currently focused recipe.
    /// </summary>
    public bool TryGetFocused(out RecipeFocus focus)
    {
        focus = default;
        int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
        if (availableCount <= 0)
        {
            return false;
        }

        int focusIndex = Utils.Clamp(Main.focusRecipe, 0, availableCount - 1);
        if (!TryGetRecipeEntry(focusIndex, availableCount, out Recipe recipe, out int recipeIndex))
        {
            return false;
        }

        focus = new RecipeFocus(recipe, recipeIndex, focusIndex, availableCount);
        return true;
    }

    /// <summary>
    /// Attempts to resolve a recipe focus from an item by matching identity.
    /// </summary>
    public bool TryResolve(Item item, out RecipeFocus focus)
    {
        focus = default;
        RecipeIdentity identity = RecipeIdentity.From(item);
        if (identity.Type <= 0)
        {
            return false;
        }

        int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
        if (availableCount <= 0)
        {
            return false;
        }

        for (int i = 0; i < availableCount; i++)
        {
            if (!TryGetRecipeEntry(i, availableCount, out Recipe recipe, out int recipeIndex))
            {
                continue;
            }

            RecipeIdentity candidateIdentity = RecipeIdentity.From(recipe.createItem);
            if (!candidateIdentity.Equals(identity))
            {
                continue;
            }

            focus = new RecipeFocus(recipe, recipeIndex, i, availableCount);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to resolve a recipe focus from an item reference using the lookup cache.
    /// </summary>
    public bool TryResolveFromReference(Item item, out RecipeFocus focus)
    {
        focus = default;
        if (!TryGetRecipeIndexForResultItem(item, out int recipeIndex))
        {
            return false;
        }

        return TryCreateFromRecipeIndex(recipeIndex, out focus);
    }

    /// <summary>
    /// Creates a focus from an available recipes index.
    /// </summary>
    public bool TryCreateFromAvailableIndex(int availableIndex, out RecipeFocus focus)
    {
        focus = default;
        int availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
        if (!TryGetRecipeEntry(availableIndex, availableCount, out Recipe recipe, out int recipeIndex))
        {
            return false;
        }

        focus = new RecipeFocus(recipe, recipeIndex, availableIndex, availableCount);
        return true;
    }

    private bool TryCreateFromRecipeIndex(int recipeIndex, out RecipeFocus focus)
    {
        focus = default;
        if (recipeIndex < 0)
        {
            return false;
        }

        Recipe[]? recipes = Main.recipe;
        if (recipes is null || recipeIndex >= recipes.Length)
        {
            return false;
        }

        Recipe recipe = recipes[recipeIndex];
        if (recipe is null || recipe.createItem is null || recipe.createItem.IsAir)
        {
            return false;
        }

        int available = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
        if (available <= 0)
        {
            return false;
        }

        for (int i = 0; i < available; i++)
        {
            if (Main.availableRecipe[i] != recipeIndex)
            {
                continue;
            }

            focus = new RecipeFocus(recipe, recipeIndex, i, available);
            return true;
        }

        return false;
    }

    private static bool TryGetRecipeEntry(int focusIndex, int availableCount, out Recipe recipe, out int recipeIndex)
    {
        recipe = null!;
        recipeIndex = -1;

        if (focusIndex < 0 || focusIndex >= availableCount || focusIndex >= Main.availableRecipe.Length)
        {
            return false;
        }

        recipeIndex = Main.availableRecipe[focusIndex];
        if (recipeIndex < 0 || recipeIndex >= Main.recipe.Length)
        {
            return false;
        }

        Recipe candidate = Main.recipe[recipeIndex];
        if (candidate is null || candidate.createItem is null || candidate.createItem.IsAir)
        {
            return false;
        }

        recipe = candidate;
        return true;
    }

    private static bool TryGetRecipeIndexForResultItem(Item item, out int recipeIndex)
    {
        recipeIndex = -1;
        if (item is null)
        {
            return false;
        }

        EnsureRecipeLookups();
        return _recipeResultLookup is not null && _recipeResultLookup.TryGetValue(item, out recipeIndex);
    }

    private static void EnsureRecipeLookups()
    {
        int version = Recipe.numRecipes;
        Recipe[]? recipes = Main.recipe;
        if (recipes is null)
        {
            _recipeLookupVersion = -1;
            _recipeResultLookup = null;
            return;
        }

        if (_recipeLookupVersion == version && _recipeResultLookup is not null)
        {
            return;
        }

        Dictionary<Item, int> resultLookup = new(ReferenceEqualityComparer<Item>.Instance);

        int totalRecipes = Math.Min(version, recipes.Length);
        for (int i = 0; i < totalRecipes; i++)
        {
            Recipe recipe = recipes[i];
            if (recipe is null)
            {
                continue;
            }

            Item result = recipe.createItem;
            if (result is not null && !result.IsAir)
            {
                resultLookup[result] = i;
            }
        }

        _recipeResultLookup = resultLookup;
        _recipeLookupVersion = version;
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
