#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class HotbarNarrator
    {
        private int _lastSelectedSlot = -1;
        private int _lastItemType = -1;
        private int _lastPrefix = -1;
        private int _lastStack = -1;
        private static string? _pendingAnnouncement;
        private static int _pendingAnnouncementTicks;
        private const int PendingAnnouncementTimeoutTicks = 1;

        public void Update(Player player)
        {
            TickPendingAnnouncement();

            if (ShouldSuppressHotbarNarration(player))
            {
                Reset();
                return;
            }

            int selectedSlot = player.selectedItem;
            Item held = player.HeldItem ?? new Item();

            if (selectedSlot == _lastSelectedSlot &&
                held.type == _lastItemType &&
                held.prefix == _lastPrefix &&
                held.stack == _lastStack)
            {
                return;
            }

            _lastSelectedSlot = selectedSlot;
            _lastItemType = held.type;
            _lastPrefix = held.prefix;
            _lastStack = held.stack;

            string description = DescribeHeldItem(selectedSlot, held);
            if (!string.IsNullOrWhiteSpace(description))
            {
                bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
                if (smartCursorActive)
                {
                    QueuePendingAnnouncement(description);
                }
                else
                {
                    ScreenReaderService.Announce(description);
                }
            }
        }

        private void Reset()
        {
            _lastSelectedSlot = -1;
            _lastItemType = -1;
            _lastPrefix = -1;
            _lastStack = -1;
            ClearPendingAnnouncement();
        }

        private static bool ShouldSuppressHotbarNarration(Player player)
        {
            int selectedSlot = player.selectedItem;
            if (selectedSlot < 0 || selectedSlot > 9)
            {
                return true;
            }

            bool usingGamepadUi = PlayerInput.UsingGamepadUI;
            if (!usingGamepadUi)
            {
                return false;
            }

            return InventoryNarrator.IsInventoryUiOpen(player);
        }

        private static string DescribeHeldItem(int slot, Item item)
        {
            if (item.IsAir)
            {
                return $"Empty, slot {slot + 1}";
            }

            string label = ComposeItemLabel(item);
            return $"{label}, slot {slot + 1}";
        }

        private static void TickPendingAnnouncement()
        {
            if (_pendingAnnouncementTicks <= 0)
            {
                return;
            }

            _pendingAnnouncementTicks--;

            if (_pendingAnnouncementTicks == 0 && _pendingAnnouncement is not null)
            {
                ScreenReaderService.Announce(_pendingAnnouncement, force: true);
                _pendingAnnouncement = null;
            }
        }

        private static void QueuePendingAnnouncement(string description)
        {
            _pendingAnnouncement = description;
            _pendingAnnouncementTicks = PendingAnnouncementTimeoutTicks;
        }

        private static void ClearPendingAnnouncement()
        {
            _pendingAnnouncement = null;
            _pendingAnnouncementTicks = 0;
        }

        internal static bool TryDequeuePendingAnnouncement(out string announcement)
        {
            if (_pendingAnnouncement is null)
            {
                announcement = string.Empty;
                return false;
            }

            announcement = _pendingAnnouncement;
            _pendingAnnouncement = null;
            _pendingAnnouncementTicks = 0;
            return true;
        }
    }
}
