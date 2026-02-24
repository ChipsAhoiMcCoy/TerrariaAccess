#nullable enable
using System.Collections.Generic;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems.Inventory;

/// <summary>
/// Tracks slot focus across inventory and crafting UI.
/// Captures focus from ItemSlot hooks and resolves focus from gamepad link points.
/// </summary>
internal sealed class SlotFocusTracker
{
    private const uint MaxFocusAgeFrames = 20;

    private readonly Dictionary<int, FocusCapture> _linkPointFocus = new();
    private SlotFocus? _pendingFocus;
    private uint _pendingFrame;
    private int _pendingLinkPoint = -1;

    /// <summary>
    /// Records a slot focus from an ItemSlot hook.
    /// </summary>
    public void Capture(in SlotFocus focus)
    {
        _pendingFocus = focus;
        _pendingFrame = Main.GameUpdateCount;
        _pendingLinkPoint = PlayerInput.UsingGamepadUI ? UILinkPointNavigator.CurrentPoint : -1;

        CacheLinkPointFocus(focus);
        UiAreaNarrationContext.RecordSlotContext(focus.Context);
    }

    /// <summary>
    /// Consumes and returns the current focus, or resolves from link point if using gamepad.
    /// </summary>
    public SlotFocus? Consume(bool usingGamepad)
    {
        SlotFocus? focus = ConsumePending();
        if (!focus.HasValue && usingGamepad)
        {
            // Try cached link point data first
            focus = ResolveFocusFromLinkPoint();

            // Fallback: directly compute focus from link point ID
            if (!focus.HasValue)
            {
                focus = TryResolveFocusDirectly();
            }
        }

        return focus;
    }

    /// <summary>
    /// Clears cached focus for a special link point (buttons, etc.).
    /// </summary>
    public void ClearSpecialLinkPoint(int point)
    {
        if (point >= 0)
        {
            _linkPointFocus.Remove(point);
        }

        _pendingFocus = null;
        _pendingFrame = 0;
        _pendingLinkPoint = -1;
    }

    /// <summary>
    /// Clears all tracked focus state.
    /// </summary>
    public void ClearAll()
    {
        _pendingFocus = null;
        _pendingFrame = 0;
        _pendingLinkPoint = -1;
        _linkPointFocus.Clear();
    }

    /// <summary>
    /// Gets the cached context for a link point.
    /// </summary>
    public bool TryGetContextForLinkPoint(int point, out int context)
    {
        context = -1;
        if (point < 0)
        {
            return false;
        }

        if (!_linkPointFocus.TryGetValue(point, out FocusCapture capture))
        {
            return false;
        }

        if (!IsCaptureFresh(capture))
        {
            _linkPointFocus.Remove(point);
            return false;
        }

        context = capture.Focus.Context;
        return true;
    }

    /// <summary>
    /// Gets the cached item and context for a link point.
    /// </summary>
    public bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
    {
        item = null;
        context = -1;

        if (point < 0)
        {
            return false;
        }

        if (!_linkPointFocus.TryGetValue(point, out FocusCapture capture))
        {
            return false;
        }

        if (!IsCaptureFresh(capture))
        {
            _linkPointFocus.Remove(point);
            return false;
        }

        context = capture.Focus.Context;
        item = capture.Focus.GetItem();

        if (item is null || item.IsAir)
        {
            item = null;
            return false;
        }

        return true;
    }

    private void CacheLinkPointFocus(SlotFocus focus)
    {
        if (!PlayerInput.UsingGamepadUI)
        {
            return;
        }

        int point = UILinkPointNavigator.CurrentPoint;
        if (point < 0)
        {
            return;
        }

        // Validate the focus slot matches the link point to prevent cross-contamination.
        // When navigation moves to a new slot, the ItemSlot hook can still fire for the
        // old slot position. Without this check, stale data gets cached under the new
        // link point, causing the narrator to report the wrong slot.
        if (!IsFocusPlausibleForLinkPoint(focus, point))
        {
            return;
        }

        _linkPointFocus[point] = new FocusCapture(focus, Main.GameUpdateCount);
    }

    /// <summary>
    /// Checks whether a focus's slot index plausibly corresponds to the given link point.
    /// Returns true for unknown ranges (where we can't validate).
    /// </summary>
    private static bool IsFocusPlausibleForLinkPoint(SlotFocus focus, int point)
    {
        int slot = focus.Slot;

        // Inventory/hotbar 0-49: link point = slot index
        if (point >= 0 && point < 50)
        {
            return slot == point;
        }

        // Coins 50-53: link point = inventory index
        if (point >= 50 && point < 54)
        {
            return slot == point;
        }

        // Ammo 54-57: link point = inventory index
        if (point >= 54 && point < 58)
        {
            return slot == point;
        }

        // Chest/storage 400-439: link point = 400 + slot
        if (point >= 400 && point < 440)
        {
            return slot == point - 400;
        }

        // Shop 2700-2739: link point = 2700 + slot
        if (point >= 2700 && point < 2740)
        {
            return slot == point - 2700;
        }

        // For other ranges (armor, dye, special buttons, etc.) we can't easily validate
        return true;
    }

    private SlotFocus? ConsumePending()
    {
        if (!_pendingFocus.HasValue)
        {
            return null;
        }

        if (!IsFresh(_pendingFrame))
        {
            _pendingFocus = null;
            _pendingFrame = 0;
            _pendingLinkPoint = -1;
            return null;
        }

        // If the link point changed since capture, the pending focus is stale
        // (e.g., ItemSlot hook fired for old slot after navigation moved to new slot).
        // Discard it so TryResolveFocusDirectly can provide correct data.
        if (_pendingLinkPoint >= 0 && PlayerInput.UsingGamepadUI &&
            UILinkPointNavigator.CurrentPoint != _pendingLinkPoint)
        {
            _pendingFocus = null;
            _pendingFrame = 0;
            _pendingLinkPoint = -1;
            return null;
        }

        SlotFocus focus = _pendingFocus.Value;
        _pendingFocus = null;
        _pendingFrame = 0;
        _pendingLinkPoint = -1;
        return focus;
    }

    private SlotFocus? ResolveFocusFromLinkPoint()
    {
        int point = UILinkPointNavigator.CurrentPoint;
        if (point < 0 || !_linkPointFocus.TryGetValue(point, out FocusCapture capture))
        {
            return null;
        }

        if (!IsCaptureFresh(capture))
        {
            _linkPointFocus.Remove(point);
            return null;
        }

        SlotFocus focus = capture.Focus;
        if (!focus.IsValid)
        {
            _linkPointFocus.Remove(point);
            return null;
        }

        return focus;
    }

    /// <summary>
    /// Directly computes focus from the current link point ID without relying on cached data.
    /// This is a fallback for when ItemSlot.MouseHover hooks don't fire.
    /// </summary>
    private static SlotFocus? TryResolveFocusDirectly()
    {
        int point = UILinkPointNavigator.CurrentPoint;
        if (point < 0)
        {
            return null;
        }

        Player? player = Main.LocalPlayer;
        if (player is null)
        {
            return null;
        }

        Item[]? items = null;
        int slot = -1;
        int context = -1;

        if (point >= 0 && point < 10)
        {
            // Hotbar: 0-9
            items = player.inventory;
            slot = point;
            context = ItemSlot.Context.InventoryItem;
        }
        else if (point >= 10 && point < 50)
        {
            // Main inventory: 10-49
            items = player.inventory;
            slot = point;
            context = ItemSlot.Context.InventoryItem;
        }
        else if (point >= 50 && point < 54)
        {
            // Coins: 50-53
            items = player.inventory;
            slot = point;
            context = ItemSlot.Context.InventoryCoin;
        }
        else if (point >= 54 && point < 58)
        {
            // Ammo: 54-57
            items = player.inventory;
            slot = point;
            context = ItemSlot.Context.InventoryAmmo;
        }
        else if (point >= 400 && point < 440)
        {
            // Chest slots: 400-439
            slot = point - 400;
            context = ItemSlot.Context.ChestItem;

            if (player.chest >= 0 && player.chest < Main.chest.Length)
            {
                items = Main.chest[player.chest]?.item;
            }
            else if (player.chest == -2)
            {
                items = player.bank.item;
            }
            else if (player.chest == -3)
            {
                items = player.bank2.item;
            }
            else if (player.chest == -4)
            {
                items = player.bank3.item;
            }
            else if (player.chest == -5)
            {
                items = player.bank4.item;
            }
        }

        if (items is null || slot < 0 || (uint)slot >= (uint)items.Length)
        {
            return null;
        }

        return new SlotFocus(items, null, context, slot);
    }

    private static bool IsCaptureFresh(FocusCapture capture)
    {
        return IsFresh(capture.Frame);
    }

    private static bool IsFresh(uint capturedFrame)
    {
        if (capturedFrame == 0)
        {
            return false;
        }

        uint current = Main.GameUpdateCount;
        uint age = current >= capturedFrame
            ? current - capturedFrame
            : uint.MaxValue - capturedFrame + current + 1;

        return age <= MaxFocusAgeFrames;
    }

    private readonly record struct FocusCapture(SlotFocus Focus, uint Frame);
}
