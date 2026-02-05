#nullable enable
using System;
using Terraria;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Uniquely identifies an item by its type, prefix, stack count, and favorite status.
/// Used for deduplication of narration announcements.
/// </summary>
internal readonly struct ItemIdentity : IEquatable<ItemIdentity>
{
    public static ItemIdentity Empty => default;

    public ItemIdentity(int type, int prefix, int stack, bool favorited)
    {
        Type = type;
        Prefix = prefix;
        Stack = stack;
        Favorited = favorited;
    }

    public int Type { get; }
    public int Prefix { get; }
    public int Stack { get; }
    public bool Favorited { get; }

    public bool IsAir => Type <= 0 || Stack <= 0;

    public static ItemIdentity From(Item? item)
    {
        if (item is null || item.IsAir)
        {
            return Empty;
        }

        return new ItemIdentity(item.type, item.prefix, item.stack, item.favorited);
    }

    public bool Equals(ItemIdentity other)
    {
        return Type == other.Type &&
               Prefix == other.Prefix &&
               Stack == other.Stack &&
               Favorited == other.Favorited;
    }

    public override bool Equals(object? obj)
    {
        return obj is ItemIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Prefix, Stack, Favorited);
    }

    public static bool operator ==(ItemIdentity left, ItemIdentity right) => left.Equals(right);
    public static bool operator !=(ItemIdentity left, ItemIdentity right) => !left.Equals(right);
}
