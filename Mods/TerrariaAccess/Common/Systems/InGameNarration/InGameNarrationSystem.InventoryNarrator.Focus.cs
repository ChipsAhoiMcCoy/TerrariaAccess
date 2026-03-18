#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed partial class InventoryNarrator
    {
        private sealed class FocusTracker
        {
            private const uint MaxFocusAgeFrames = 20;

            private readonly Dictionary<int, FocusCapture> _linkPointFocus = new();
            private SlotFocus? _pendingFocus;
            private uint _pendingFrame;

            public void Capture(in SlotFocus focus)
            {
                _pendingFocus = focus;
                _pendingFrame = Main.GameUpdateCount;

                CacheLinkPointFocus(focus);
                UiAreaNarrationContext.RecordSlotContext(focus.Context);
            }

            public SlotFocus? Consume(bool usingGamepad)
            {
                SlotFocus? focus = ConsumePending();
                if (!focus.HasValue && usingGamepad)
                {
                    // Try cached link point data first
                    focus = ResolveFocusFromLinkPoint();

                    // Fallback: directly compute focus from link point ID
                    // This handles cases where ItemSlot.MouseHover hooks don't fire
                    if (!focus.HasValue)
                    {
                        focus = TryResolveFocusDirectly();
                    }
                }

                return focus;
            }

            public void ClearSpecialLinkPoint(int point)
            {
                if (point >= 0)
                {
                    _linkPointFocus.Remove(point);
                }

                _pendingFocus = null;
                _pendingFrame = 0;
            }

            public void ClearAll()
            {
                _pendingFocus = null;
                _pendingFrame = 0;
                _linkPointFocus.Clear();
            }

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

                SlotFocus focus = capture.Focus;
                if (focus.Items is Item[] items)
                {
                    int index = focus.Slot;
                    if ((uint)index < (uint)items.Length)
                    {
                        item = items[index];
                    }
                }
                else
                {
                    item = focus.SingleItem;
                }

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

                _linkPointFocus[point] = new FocusCapture(focus, Main.GameUpdateCount);
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
                    return null;
                }

                SlotFocus focus = _pendingFocus.Value;
                _pendingFocus = null;
                _pendingFrame = 0;
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
                if (!ShouldCaptureFocusForContext(focus.Context) || !IsFocusValid(focus))
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

                // Link point ranges for inventory slots:
                // 0-9: Hotbar (inventory[0-9])
                // 10-49: Main inventory (inventory[10-49])
                // 50-53: Coins (inventory[50-53])
                // 54-57: Ammo (inventory[54-57])
                // 400-439: Chest slots
                // 500-505: Equipment and other special slots

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
    }
}
