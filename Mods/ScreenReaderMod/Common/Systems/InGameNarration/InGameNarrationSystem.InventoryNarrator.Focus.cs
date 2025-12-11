#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Common.Systems;

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
                    focus = ResolveFocusFromLinkPoint();
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
