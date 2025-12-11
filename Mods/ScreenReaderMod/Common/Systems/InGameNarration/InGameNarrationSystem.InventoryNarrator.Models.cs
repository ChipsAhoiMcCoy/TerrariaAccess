#nullable enable
using System;
using System.Globalization;
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
            private readonly HistoryEntry?[] _lastCues = new HistoryEntry?[(int)NarrationKind.Count];

            public bool TryStore(in NarrationCue cue)
            {
                if (NarrationHistorySettings.IsDisabled)
                {
                    _lastCues[(int)cue.Kind] = new HistoryEntry(cue, Main.GameUpdateCount);
                    return true;
                }

                int index = (int)cue.Kind;
                HistoryEntry? previous = _lastCues[index];
                uint now = Main.GameUpdateCount;

                if (previous.HasValue &&
                    previous.Value.Cue.Equals(cue) &&
                    !NarrationHistorySettings.HasExpired(previous.Value.Frame, now))
                {
                    return false;
                }

                _lastCues[index] = new HistoryEntry(cue, now);
                return true;
            }

            public void Reset(NarrationKind kind)
            {
                _lastCues[(int)kind] = null;
            }

            public void ResetAll()
            {
                Array.Clear(_lastCues, 0, _lastCues.Length);
            }
        }

        private static class NarrationHistorySettings
        {
            private const string DisabledEnvVar = "SRM_NARRATION_HISTORY_DISABLED";
            private const string MaxAgeEnvVar = "SRM_NARRATION_HISTORY_MAX_AGE";

            public static readonly bool IsDisabled = ParseBool(DisabledEnvVar);
            public static readonly uint MaxAgeFrames = ParseUInt(MaxAgeEnvVar);

            public static bool HasExpired(uint storedFrame, uint currentFrame)
            {
                if (MaxAgeFrames == 0)
                {
                    return false;
                }

                uint age = currentFrame >= storedFrame
                    ? currentFrame - storedFrame
                    : uint.MaxValue - storedFrame + currentFrame + 1;

                return age >= MaxAgeFrames;
            }

            private static bool ParseBool(string envVar)
            {
                string? value = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            private static uint ParseUInt(string envVar)
            {
                string? value = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return 0;
                }

                if (uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
                {
                    return parsed;
                }

                return 0;
            }
        }

        private readonly record struct HistoryEntry(NarrationCue Cue, uint Frame);

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
