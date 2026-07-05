#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
using Terraria;
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

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    internal sealed class HotbarNarrator
    {
        private static readonly bool HotbarDebugEnabled =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_HOTBAR")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

        private int _lastSelectedSlot = -1;
        private int _lastItemType = -1;
        private int _lastPrefix = -1;
        private int _lastStack = -1;
        private static string? _lastAnnouncedDescription;
        private static string? _pendingAnnouncement;
        private static string? _pendingAnnouncementKey;
        private static uint _lastAnnouncementFrame;
        private static bool _externalSuppressed;
        private static int _suppressedSelectedSlot = -1;
        private static int _suppressedItemType = -1;
        private static int _suppressedPrefix = -1;
        private static int _suppressedStack = -1;
        private static string? _lastDebugState;

        public void Update(Player player)
        {
            string? suppressionReason = GetSuppressionReason(player);
            if (suppressionReason is not null)
            {
                LogHotbarDebug("suppressed", player, extra: suppressionReason);
                Reset();
                return;
            }

            int selectedSlot = player.selectedItem;
            Item held = player.HeldItem ?? new Item();

            if (MatchesSuppressedSnapshot(selectedSlot, held))
            {
                _lastSelectedSlot = selectedSlot;
                _lastItemType = held.type;
                _lastPrefix = held.prefix;
                _lastStack = held.stack;
                _lastAnnouncedDescription = DescribeHeldItem(selectedSlot, held);
                ClearSuppressedSnapshot();
                LogHotbarDebug("suppressed-snapshot-match", player, selectedSlot, held, _lastAnnouncedDescription);
                return;
            }

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

            // Skip if this is the exact same announcement as last time (prevents duplicates)
            if (string.Equals(description, _lastAnnouncedDescription, StringComparison.Ordinal))
            {
                LogHotbarDebug("duplicate-description-suppressed", player, selectedSlot, held, description);
                return;
            }

            string key = BuildHotbarKey(selectedSlot, held);
            if (!string.IsNullOrWhiteSpace(description))
            {
                _lastAnnouncedDescription = description;
                ClearPendingAnnouncement();
                _lastAnnouncementFrame = Main.GameUpdateCount;
                NarrationInstrumentationContext.SetPendingKey(key);
                LogHotbarDebug("announce", player, selectedSlot, held, description, extra: key, dedupe: false);
                ScreenReaderService.Announce(description);
            }
        }

        private void Reset()
        {
            _lastSelectedSlot = -1;
            _lastItemType = -1;
            _lastPrefix = -1;
            _lastStack = -1;
            // Note: Don't clear _lastAnnouncedDescription here to prevent duplicate
            // announcements when transitioning from inventory back to hotbar
            ClearPendingAnnouncement();
        }

        private static bool ShouldSuppressHotbarNarration(Player player)
        {
            return GetSuppressionReason(player) is not null;
        }

        private static string DescribeHeldItem(int slot, Item item)
        {
            if (item.IsAir)
            {
                return $"Empty, slot {slot + 1}";
            }

            string label = NarrationTextFormatter.ComposeItemLabel(item);
            return $"{label}, slot {slot + 1}";
        }

        private static void QueuePendingAnnouncement(string description, string key)
        {
            _pendingAnnouncement = description;
            _pendingAnnouncementKey = key;
        }

        private static bool MatchesSuppressedSnapshot(int selectedSlot, Item held)
        {
            return selectedSlot == _suppressedSelectedSlot &&
                held.type == _suppressedItemType &&
                held.prefix == _suppressedPrefix &&
                held.stack == _suppressedStack;
        }

        private static void CaptureSuppressedSnapshot(int selectedSlot, Item held)
        {
            _suppressedSelectedSlot = selectedSlot;
            _suppressedItemType = held.type;
            _suppressedPrefix = held.prefix;
            _suppressedStack = held.stack;
        }

        private static void ClearSuppressedSnapshot()
        {
            _suppressedSelectedSlot = -1;
            _suppressedItemType = -1;
            _suppressedPrefix = -1;
            _suppressedStack = -1;
        }

        private static void ClearPendingAnnouncement()
        {
            _pendingAnnouncement = null;
            _pendingAnnouncementKey = null;
        }

        internal static bool TryDequeuePendingAnnouncement(out string announcement, out string? key)
        {
            if (_pendingAnnouncement is null)
            {
                announcement = string.Empty;
                key = null;
                return false;
            }

            announcement = _pendingAnnouncement;
            key = _pendingAnnouncementKey;
            ClearPendingAnnouncement();
            return true;
        }

        internal static bool WasAnnouncementIssuedRecently(uint maxAgeFrames = 1)
        {
            if (_lastAnnouncementFrame == 0 || Main.GameUpdateCount < _lastAnnouncementFrame)
            {
                return false;
            }

            return Main.GameUpdateCount - _lastAnnouncementFrame <= maxAgeFrames;
        }

        internal static void SetExternalSuppression(bool suppressed)
        {
            _externalSuppressed = suppressed;
            if (suppressed)
            {
                ClearPendingAnnouncement();
            }
        }

        internal static void SubscribeToInventoryEvents()
        {
            InventoryNarrator.InventoryOpened += OnInventoryOpened;
            InventoryNarrator.InventoryClosed += OnInventoryClosed;
        }

        internal static void UnsubscribeFromInventoryEvents()
        {
            InventoryNarrator.InventoryOpened -= OnInventoryOpened;
            InventoryNarrator.InventoryClosed -= OnInventoryClosed;
        }

        private static void OnInventoryOpened()
        {
            ClearSuppressedSnapshot();
            ClearPendingAnnouncement();
            LogHotbarDebug("inventory-opened");
        }

        private static void OnInventoryClosed()
        {
            Player player = Main.LocalPlayer;
            if (player is null)
            {
                return;
            }

            int selectedSlot = player.selectedItem;
            Item held = player.HeldItem ?? new Item();
            CaptureSuppressedSnapshot(selectedSlot, held);
            _lastAnnouncedDescription = DescribeHeldItem(selectedSlot, held);
            ClearPendingAnnouncement();
            LogHotbarDebug("inventory-closed", player, selectedSlot, held, _lastAnnouncedDescription);
        }

        private static string BuildHotbarKey(int slot, Item item)
        {
            return $"hotbar:{slot + 1}:{item.type}:{item.prefix}:{item.stack}:{(item.favorited ? 1 : 0)}";
        }

        private static string? GetSuppressionReason(Player player)
        {
            if (_externalSuppressed)
            {
                return "external";
            }

            int selectedSlot = player.selectedItem;
            if (selectedSlot < 0 || selectedSlot > 9)
            {
                return $"selected-slot-out-of-range:{selectedSlot}";
            }

            if (InventoryNarrator.IsInventoryUiOpen(player))
            {
                return "inventory-open";
            }

            return null;
        }

        private static void LogHotbarDebug(
            string action,
            Player? player = null,
            int? selectedSlot = null,
            Item? held = null,
            string? description = null,
            string? extra = null,
            bool dedupe = true)
        {
            if (!HotbarDebugEnabled)
            {
                return;
            }

            Player? effectivePlayer = player ?? Main.LocalPlayer;
            int slot = selectedSlot ?? effectivePlayer?.selectedItem ?? -1;
            Item effectiveHeld = held ?? effectivePlayer?.HeldItem ?? new Item();
            string itemName = effectiveHeld.IsAir
                ? "Empty"
                : NarrationTextFormatter.ComposeItemName(effectiveHeld);
            string state = $"{action}|slot={slot}|type={effectiveHeld.type}|prefix={effectiveHeld.prefix}|stack={effectiveHeld.stack}|desc={description}|extra={extra}";
            if (dedupe && string.Equals(state, _lastDebugState, StringComparison.Ordinal))
            {
                return;
            }

            _lastDebugState = state;
            TerrariaAccess.Instance?.Logger.Info(
                $"[HotbarDebug] action={action} frame={Main.GameUpdateCount} selectedSlot={slot} " +
                $"item='{itemName}' type={effectiveHeld.type} prefix={effectiveHeld.prefix} stack={effectiveHeld.stack} " +
                $"inventoryOpen={(effectivePlayer is not null && InventoryNarrator.IsInventoryUiOpen(effectivePlayer))} " +
                $"usingGamepad={PlayerInput.UsingGamepadUI} inputMode={PlayerInput.CurrentInputMode} " +
                $"description='{description ?? string.Empty}' extra='{extra ?? string.Empty}'");
        }
    }
}
