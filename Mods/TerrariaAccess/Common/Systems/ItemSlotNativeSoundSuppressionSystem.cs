#nullable enable
using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Suppresses native Terraria item-slot sounds only when Terraria Access injects virtual
/// inventory click input. Normal Terraria mouse/gamepad item-slot sounds are left untouched.
/// </summary>
internal sealed class ItemSlotNativeSoundSuppressionSystem : ModSystem
{
    private static uint s_lastReplacementCueFrame;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_ItemSlot.LeftClick_ItemArray_int_int += HandleLeftClickArray;
        On_ItemSlot.LeftClick_refItem_int += HandleLeftClickRef;
        On_ItemSlot.RightClick_ItemArray_int_int += HandleRightClickArray;
        On_ItemSlot.RightClick_refItem_int += HandleRightClickRef;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_ItemSlot.LeftClick_ItemArray_int_int -= HandleLeftClickArray;
        On_ItemSlot.LeftClick_refItem_int -= HandleLeftClickRef;
        On_ItemSlot.RightClick_ItemArray_int_int -= HandleRightClickArray;
        On_ItemSlot.RightClick_refItem_int -= HandleRightClickRef;
        s_lastReplacementCueFrame = 0;
    }

    private static void HandleLeftClickArray(On_ItemSlot.orig_LeftClick_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        if (!NativeSoundSuppression.ShouldSuppressItemSlotClick())
        {
            orig(inv, context, slot);
            return;
        }

        ItemSlotClickSnapshot before = ItemSlotClickSnapshot.Capture(inv, slot);
        NativeSoundSuppression.RunItemSlotClick(() => orig(inv, context, slot));
        PlayCueIfChanged(before, ItemSlotClickSnapshot.Capture(inv, slot));
    }

    private static void HandleLeftClickRef(On_ItemSlot.orig_LeftClick_refItem_int orig, ref Item inv, int context)
    {
        if (!NativeSoundSuppression.ShouldSuppressItemSlotClick())
        {
            orig(ref inv, context);
            return;
        }

        ItemSlotClickSnapshot before = ItemSlotClickSnapshot.Capture(inv);
        float previousSoundVolume = NativeSoundSuppression.BeginSynchronousSuppression();
        try
        {
            orig(ref inv, context);
        }
        finally
        {
            NativeSoundSuppression.EndSynchronousSuppression(previousSoundVolume);
        }

        PlayCueIfChanged(before, ItemSlotClickSnapshot.Capture(inv));
    }

    private static void HandleRightClickArray(On_ItemSlot.orig_RightClick_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        if (!NativeSoundSuppression.ShouldSuppressItemSlotClick())
        {
            orig(inv, context, slot);
            return;
        }

        ItemSlotClickSnapshot before = ItemSlotClickSnapshot.Capture(inv, slot);
        NativeSoundSuppression.RunItemSlotClick(() => orig(inv, context, slot));
        PlayCueIfChanged(before, ItemSlotClickSnapshot.Capture(inv, slot));
    }

    private static void HandleRightClickRef(On_ItemSlot.orig_RightClick_refItem_int orig, ref Item inv, int context)
    {
        if (!NativeSoundSuppression.ShouldSuppressItemSlotClick())
        {
            orig(ref inv, context);
            return;
        }

        ItemSlotClickSnapshot before = ItemSlotClickSnapshot.Capture(inv);
        float previousSoundVolume = NativeSoundSuppression.BeginSynchronousSuppression();
        try
        {
            orig(ref inv, context);
        }
        finally
        {
            NativeSoundSuppression.EndSynchronousSuppression(previousSoundVolume);
        }

        PlayCueIfChanged(before, ItemSlotClickSnapshot.Capture(inv));
    }

    private static void PlayCueIfChanged(ItemSlotClickSnapshot before, ItemSlotClickSnapshot after)
    {
        if (before.Equals(after))
        {
            return;
        }

        PlayReplacementCueOncePerFrame();
    }

    private static void PlayReplacementCueOncePerFrame()
    {
        uint currentFrame = Main.GameUpdateCount;
        if (s_lastReplacementCueFrame == currentFrame)
        {
            return;
        }

        s_lastReplacementCueFrame = currentFrame;
        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
    }

    private readonly struct ItemSlotClickSnapshot : IEquatable<ItemSlotClickSnapshot>
    {
        private readonly int _slotType;
        private readonly int _slotStack;
        private readonly int _slotPrefix;
        private readonly bool _slotFavorited;
        private readonly int _mouseType;
        private readonly int _mouseStack;
        private readonly int _mousePrefix;
        private readonly bool _mouseFavorited;

        private ItemSlotClickSnapshot(
            int slotType,
            int slotStack,
            int slotPrefix,
            bool slotFavorited,
            int mouseType,
            int mouseStack,
            int mousePrefix,
            bool mouseFavorited)
        {
            _slotType = slotType;
            _slotStack = slotStack;
            _slotPrefix = slotPrefix;
            _slotFavorited = slotFavorited;
            _mouseType = mouseType;
            _mouseStack = mouseStack;
            _mousePrefix = mousePrefix;
            _mouseFavorited = mouseFavorited;
        }

        public static ItemSlotClickSnapshot Capture(Item[] inv, int slot)
        {
            Item? slotItem = (uint)slot < (uint)inv.Length ? inv[slot] : null;
            return Capture(slotItem);
        }

        public static ItemSlotClickSnapshot Capture(Item? slotItem)
        {
            Item? mouseItem = Main.mouseItem;
            return new ItemSlotClickSnapshot(
                ItemType(slotItem),
                ItemStack(slotItem),
                ItemPrefix(slotItem),
                ItemFavorited(slotItem),
                ItemType(mouseItem),
                ItemStack(mouseItem),
                ItemPrefix(mouseItem),
                ItemFavorited(mouseItem));
        }

        public bool Equals(ItemSlotClickSnapshot other) =>
            _slotType == other._slotType &&
            _slotStack == other._slotStack &&
            _slotPrefix == other._slotPrefix &&
            _slotFavorited == other._slotFavorited &&
            _mouseType == other._mouseType &&
            _mouseStack == other._mouseStack &&
            _mousePrefix == other._mousePrefix &&
            _mouseFavorited == other._mouseFavorited;

        public override bool Equals(object? obj) =>
            obj is ItemSlotClickSnapshot other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_slotType, _slotStack, _slotPrefix, _slotFavorited, _mouseType, _mouseStack, _mousePrefix, _mouseFavorited);

        private static int ItemType(Item? item) =>
            item is null || item.IsAir ? 0 : item.type;

        private static int ItemStack(Item? item) =>
            item is null || item.IsAir ? 0 : item.stack;

        private static int ItemPrefix(Item? item) =>
            item is null || item.IsAir ? 0 : item.prefix;

        private static bool ItemFavorited(Item? item) =>
            item is not null && !item.IsAir && item.favorited;
    }
}
