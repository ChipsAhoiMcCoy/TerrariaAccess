#nullable enable
using System;
using Terraria;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Represents a focused recipe in the crafting UI.
/// </summary>
internal readonly struct RecipeFocus
{
    public RecipeFocus(Recipe recipe, int recipeIndex, int focusIndex, int availableCount)
    {
        Recipe = recipe;
        RecipeIndex = recipeIndex;
        FocusIndex = focusIndex;
        AvailableCount = availableCount;
    }

    public Recipe Recipe { get; }
    public int RecipeIndex { get; }
    public int FocusIndex { get; }
    public int AvailableCount { get; }
    public Item Result => Recipe.createItem;
}

/// <summary>
/// Identity of a recipe result for deduplication.
/// </summary>
internal readonly struct RecipeIdentity : IEquatable<RecipeIdentity>
{
    public RecipeIdentity(int type, int prefix, int stack)
    {
        Type = type;
        Prefix = prefix;
        Stack = stack;
    }

    public int Type { get; }
    public int Prefix { get; }
    public int Stack { get; }

    public static RecipeIdentity From(Item? item)
    {
        if (item is null || item.IsAir)
        {
            return default;
        }

        return new RecipeIdentity(item.type, item.prefix, item.stack);
    }

    public bool Equals(RecipeIdentity other)
    {
        return Type == other.Type &&
               Prefix == other.Prefix &&
               Stack == other.Stack;
    }

    public override bool Equals(object? obj)
    {
        return obj is RecipeIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Prefix, Stack);
    }

    public static bool operator ==(RecipeIdentity left, RecipeIdentity right) => left.Equals(right);
    public static bool operator !=(RecipeIdentity left, RecipeIdentity right) => !left.Equals(right);
}
