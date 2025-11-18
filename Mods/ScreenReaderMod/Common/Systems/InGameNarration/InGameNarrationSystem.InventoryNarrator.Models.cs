#nullable enable
using System;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class InventoryNarrator
    {
        private readonly struct SlotFocus
        {
            public SlotFocus(Item[]? items, Item? singleItem, int context, int slot)
            {
                Items = items;
                SingleItem = singleItem;
                Context = context;
                Slot = slot;
            }

            public Item[]? Items { get; }
            public Item? SingleItem { get; }
            public int Context { get; }
            public int Slot { get; }
        }

        private readonly struct ItemIdentity : IEquatable<ItemIdentity>
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

            public static ItemIdentity From(Item item)
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
        }

        private enum NarrationKind
        {
            MouseItem,
            HoverItem,
            EmptySlot,
            Tooltip,
            UiHover,
            SpecialSelection,
            Count,
        }

        private readonly record struct NarrationCue(
            NarrationKind Kind,
            string Message,
            ItemIdentity Identity,
            string? Location,
            string? Tooltip,
            string? Details,
            int SlotSignature)
        {
            public static NarrationCue ForMouse(ItemIdentity identity, string message)
            {
                return new NarrationCue(NarrationKind.MouseItem, message, identity, null, null, null, -1);
            }

            public static NarrationCue ForItem(ItemIdentity identity, string message, string? location, string? tooltip, string? details, int slotSignature)
            {
                return new NarrationCue(NarrationKind.HoverItem, message, identity, location, tooltip, details, slotSignature);
            }

            public static NarrationCue ForEmpty(string message, string location, int slotSignature)
            {
                return new NarrationCue(NarrationKind.EmptySlot, message, ItemIdentity.Empty, location, null, null, slotSignature);
            }

            public static NarrationCue ForTooltip(string message)
            {
                return new NarrationCue(NarrationKind.Tooltip, message, ItemIdentity.Empty, null, message, null, -1);
            }

            public static NarrationCue ForUi(string message)
            {
                return new NarrationCue(NarrationKind.UiHover, message, ItemIdentity.Empty, null, message, null, -1);
            }

            public static NarrationCue ForSpecial(string label)
            {
                return new NarrationCue(NarrationKind.SpecialSelection, label, ItemIdentity.Empty, null, label, null, -1);
            }
        }

        private sealed class NarrationHistory
        {
            private readonly NarrationCue?[] _lastCues = new NarrationCue?[(int)NarrationKind.Count];

            public bool TryStore(in NarrationCue cue)
            {
                int index = (int)cue.Kind;
                NarrationCue? previous = _lastCues[index];
                if (previous.HasValue && previous.Value.Equals(cue))
                {
                    return false;
                }

                _lastCues[index] = cue;
                return true;
            }

            public void Reset(NarrationKind kind)
            {
                _lastCues[(int)kind] = null;
            }

            public void ResetAll()
            {
                for (int i = 0; i < _lastCues.Length; i++)
                {
                    _lastCues[i] = null;
                }
            }
        }

        private readonly record struct HoverTarget(
            Item Item,
            ItemIdentity Identity,
            string Location,
            string RawTooltip,
            string NormalizedTooltip,
            SlotFocus? Focus,
            bool AllowMouseText)
        {
            public bool HasItem => !Identity.IsAir;
            public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
            public bool HasTooltip => !string.IsNullOrWhiteSpace(NormalizedTooltip);
        }
    }
}
