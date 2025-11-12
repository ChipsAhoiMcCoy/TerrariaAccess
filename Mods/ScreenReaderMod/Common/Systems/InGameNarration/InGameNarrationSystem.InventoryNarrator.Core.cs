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
    private sealed partial class InventoryNarrator
    {
        private readonly MenuUiSelectionTracker _inGameUiTracker = new();
        private ItemIdentity _lastHover;
        private string? _lastHoverLocation;
        private string? _lastHoverTooltip;
        private string? _lastHoverDetails;
        private ItemIdentity _lastMouse;
        private string? _lastMouseMessage;
        private string? _lastEmptyMessage;
        private string? _lastAnnouncedMessage;
        private SlotFocus? _currentFocus;

        private static SlotFocus? _pendingFocus;
        private static readonly Dictionary<int, SlotFocus> LinkPointFocusCache = new();

        private static readonly Lazy<FieldInfo?> MouseTextCacheField = new(() =>
            typeof(Main).GetField("_mouseTextCache", BindingFlags.Instance | BindingFlags.NonPublic));

        private static FieldInfo? _mouseTextCursorField;
        private static FieldInfo? _mouseTextIsValidField;
        private static string? _capturedMouseText;
        private static uint _capturedMouseTextFrame;

        public static void RecordFocus(Item[] inventory, int context, int slot)
        {
            SlotFocus focus = new(inventory, null, context, slot);
            CacheLinkPointFocus(focus);

            if (ShouldCaptureFocusForContext(context))
            {
                StorePendingFocus(focus);
            }
        }

        public static void RecordFocus(Item item, int context)
        {
            SlotFocus focus = new(null, item, context, -1);
            CacheLinkPointFocus(focus);

            if (ShouldCaptureFocusForContext(context))
            {
                StorePendingFocus(focus);
            }
        }

        private static bool ShouldCaptureFocusForContext(int context)
        {
            int normalized = Math.Abs(context);
            return normalized != ItemSlot.Context.CraftingMaterial;
        }

        private static void StorePendingFocus(SlotFocus focus)
        {
            _pendingFocus = focus;
        }

        internal static void RecordMouseTextSnapshot(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _capturedMouseText = null;
                _capturedMouseTextFrame = 0;
                return;
            }

            _capturedMouseText = text.Trim();
            _capturedMouseTextFrame = Main.GameUpdateCount;
        }

        public void Update(Player player)
        {
            if (!IsInventoryUiOpen(player))
            {
                Reset();
                _inGameUiTracker.Reset();
                return;
            }

            bool usingGamepad = PlayerInput.UsingGamepadUI;

            SlotFocus? nextFocus = null;
            if (_pendingFocus.HasValue)
            {
                nextFocus = _pendingFocus;
                _pendingFocus = null;
            }
            else if (usingGamepad)
            {
                nextFocus = ResolveFocusFromLinkPoint();
            }

            if (nextFocus.HasValue && IsFocusValid(nextFocus.Value))
            {
                _currentFocus = nextFocus;
            }
            else
            {
                _currentFocus = null;
            }

            HandleMouseItem();
            HandleHoverItem(player);
        }

        internal static bool IsInventoryUiOpen(Player player)
        {
            return Main.playerInventory ||
                   player.chest != -1 ||
                   Main.npcShop != 0 ||
                   Main.InGuideCraftMenu ||
                   Main.InReforgeMenu ||
                   Main.ingameOptionsWindow;
        }

        private void HandleMouseItem()
        {
            Item mouse = Main.mouseItem;
            ItemIdentity identity = ItemIdentity.From(mouse);
            if (identity.IsAir)
            {
                _lastMouse = ItemIdentity.Empty;
                _lastMouseMessage = null;
                return;
            }

            string message = $"Holding {ComposeItemLabel(mouse)}";
            if (identity.Equals(_lastMouse) && string.Equals(message, _lastMouseMessage, StringComparison.Ordinal))
            {
                return;
            }

            _lastMouse = identity;
            _lastMouseMessage = message;
            ScreenReaderService.Announce(message);
        }

        private void HandleHoverItem(Player player)
        {
            SlotFocus? focus = _currentFocus;
            Item? focusedItem = GetItemFromFocus(focus);
            bool usingGamepadFocus = PlayerInput.UsingGamepadUI && focusedItem is not null;

            Item hover = usingGamepadFocus ? focusedItem! : Main.HoverItem;
            ItemIdentity identity = ItemIdentity.From(hover);

            string rawTooltip;
            if (usingGamepadFocus)
            {
                rawTooltip = GetHoverNameForItem(hover);
            }
            else
            {
                rawTooltip = Main.hoverItemName ?? string.Empty;
            }
            string normalizedTooltip = GlyphTagFormatter.Normalize(rawTooltip);

            string location = DescribeLocation(player, identity, focus);

            if (TryAnnounceSpecialSelection(identity.IsAir, location))
            {
                return;
            }

            if (!identity.IsAir)
            {
                string label = ComposeItemLabel(hover);
                string message = string.IsNullOrEmpty(location) ? label : $"{label}, {location}";
                string? details = BuildTooltipDetails(hover, rawTooltip, allowMouseText: !usingGamepadFocus);
                string? requirementDetails = CraftingNarrator.TryGetRequirementTooltipDetails(hover, string.IsNullOrWhiteSpace(location));
                if (!string.IsNullOrWhiteSpace(requirementDetails))
                {
                    details = string.IsNullOrWhiteSpace(details)
                        ? requirementDetails
                        : $"{details}. {requirementDetails}";
                }
                string combined = CombineItemAnnouncement(message, details);

                if (identity.Equals(_lastHover) &&
                    string.Equals(combined, _lastAnnouncedMessage, StringComparison.Ordinal) &&
                    string.Equals(normalizedTooltip, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    return;
                }

                _lastHover = identity;
                _lastHoverLocation = location;
                _lastHoverTooltip = normalizedTooltip;
                _lastHoverDetails = details;
                _lastEmptyMessage = null;
                _lastAnnouncedMessage = combined;

                ScreenReaderService.Announce(combined);
                return;
            }

            _lastHover = ItemIdentity.Empty;
            _lastHoverDetails = null;

            if (!string.IsNullOrEmpty(location))
            {
                string message = $"Empty, {location}";

                if (!string.Equals(message, _lastEmptyMessage, StringComparison.Ordinal))
                {
                    _lastEmptyMessage = message;
                    _lastHoverLocation = location;
                    _lastHoverTooltip = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = message;
                    ScreenReaderService.Announce(message);
                }

                return;
            }

            string? mouseText = TryGetMouseText();
            if (!string.IsNullOrWhiteSpace(mouseText))
            {
                string trimmedMouseText = GlyphTagFormatter.Normalize(mouseText.Trim());
                if (!string.Equals(trimmedMouseText, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    _lastHoverTooltip = trimmedMouseText;
                    _lastHoverLocation = null;
                    _lastEmptyMessage = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = trimmedMouseText;
                    ScreenReaderService.Announce(trimmedMouseText);
                }

                return;
            }

            if (TryAnnounceInGameUiHover())
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(rawTooltip))
            {
                if (!string.Equals(normalizedTooltip, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    _lastHoverTooltip = normalizedTooltip;
                    _lastHoverLocation = null;
                    _lastEmptyMessage = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = normalizedTooltip;
                    ScreenReaderService.Announce(normalizedTooltip);
                }
            }
        }

        private static void CacheLinkPointFocus(SlotFocus focus)
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

            LinkPointFocusCache[point] = focus;
        }

        private static SlotFocus? ResolveFocusFromLinkPoint()
        {
            int point = UILinkPointNavigator.CurrentPoint;
            if (point < 0)
            {
                return null;
            }

            if (!LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                return null;
            }

            if (!ShouldCaptureFocusForContext(focus.Context))
            {
                return null;
            }

            if (IsFocusValid(focus))
            {
                return focus;
            }

            LinkPointFocusCache.Remove(point);
            return null;
        }

        public static bool TryGetContextForLinkPoint(int point, out int context)
        {
            if (point >= 0 && LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                context = focus.Context;
                return true;
            }

            context = -1;
            return false;
        }

        public static bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
        {
            item = null;
            context = -1;

            if (point < 0)
            {
                return false;
            }

            if (!LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                return false;
            }

            context = focus.Context;

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

        private static bool IsFocusValid(SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                int index = focus.Slot;
                return (uint)index < (uint)items.Length;
            }

            return focus.SingleItem is not null;
        }

        private static Item? GetItemFromFocus(SlotFocus? focus)
        {
            if (!focus.HasValue)
            {
                return null;
            }

            SlotFocus value = focus.Value;

            if (value.Items is Item[] items)
            {
                int index = value.Slot;
                if ((uint)index < (uint)items.Length)
                {
                    return items[index];
                }

                return null;
            }

            return value.SingleItem;
        }

        private static string GetHoverNameForItem(Item item)
        {
            string name = item.AffixName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                return item.Name;
            }

            string fallback = Lang.GetItemNameValue(item.type);
            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;
        }

        private void Reset()
        {
            _lastHover = ItemIdentity.Empty;
            _lastHoverLocation = null;
            _lastHoverTooltip = null;
            _lastHoverDetails = null;
            _lastMouse = ItemIdentity.Empty;
            _lastMouseMessage = null;
            _lastEmptyMessage = null;
            _lastAnnouncedMessage = null;
            _currentFocus = null;
            _pendingFocus = null;
            LinkPointFocusCache.Clear();
            _inGameUiTracker.Reset();
        }

        private static string DescribeLocation(Player player, ItemIdentity identity, SlotFocus? focus)
        {
            if (focus.HasValue)
            {
                string focused = DescribeFocusedSlot(player, focus.Value);
                if (!string.IsNullOrWhiteSpace(focused))
                {
                    return focused;
                }
            }

            if (TryMatch(player.inventory, identity, out int inventoryIndex))
            {
                if (inventoryIndex < 10)
                {
                    return $"Hotbar slot {inventoryIndex + 1}";
                }

                if (inventoryIndex < 50)
                {
                    return $"Inventory slot {inventoryIndex - 9}";
                }

                if (inventoryIndex < 54)
                {
                    return $"Coin slot {inventoryIndex - 49}";
                }

                if (inventoryIndex < 58)
                {
                    return $"Ammo slot {inventoryIndex - 53}";
                }
            }

            if (Matches(player.trashItem, identity))
            {
                return "Trash slot";
            }

            if (TryMatch(player.armor, identity, out int armorIndex))
            {
                return DescribeArmorSlot(armorIndex);
            }

            if (TryMatch(player.dye, identity, out int dyeIndex))
            {
                return $"Dye slot {dyeIndex + 1}";
            }

            if (TryMatch(player.miscEquips, identity, out int miscIndex))
            {
                return $"Misc equipment slot {miscIndex + 1}";
            }

            if (TryMatch(player.miscDyes, identity, out int miscDyeIndex))
            {
                return $"Misc dye slot {miscDyeIndex + 1}";
            }

            int chestIndex = player.chest;
            if (chestIndex != -1)
            {
                string container = DescribeContainer(chestIndex);
                Item[]? containerItems = GetContainerItems(player, chestIndex);
                if (containerItems is not null && TryMatch(containerItems, identity, out int containerSlot))
                {
                    return $"{container} slot {containerSlot + 1}";
                }

                return container;
            }

            if (Main.npcShop > 0)
            {
                Chest[]? shops = Main.instance?.shop;
                if (shops is not null && Main.npcShop < shops.Length)
                {
                    Item[]? shopItems = shops[Main.npcShop]?.item;
                    if (shopItems is not null && TryMatch(shopItems, identity, out int shopSlot))
                    {
                        return $"Shop slot {shopSlot + 1}";
                    }
                }
            }

            return string.Empty;
        }

        private static string DescribeFocusedSlot(Player player, SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                if (ReferenceEquals(items, player.inventory))
                {
                    return DescribeInventorySlot(focus.Slot);
                }

                if (ReferenceEquals(items, player.bank.item))
                {
                    return $"Piggy bank slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank2.item))
                {
                    return $"Safe slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank3.item))
                {
                    return $"Defender's forge slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank4.item))
                {
                    return $"Void vault slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.armor))
                {
                    return DescribeArmorSlot(focus.Slot);
                }

                if (ReferenceEquals(items, player.dye))
                {
                    return $"Dye slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.miscEquips))
                {
                    return $"Misc equipment slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.miscDyes))
                {
                    return $"Misc dye slot {focus.Slot + 1}";
                }

                if (Main.chest is not null)
                {
                    for (int i = 0; i < Main.chest.Length; i++)
                    {
                        if (ReferenceEquals(Main.chest[i]?.item, items))
                        {
                            string container = DescribeContainer(i);
                            return focus.Slot >= 0 ? $"{container} slot {focus.Slot + 1}" : container;
                        }
                    }
                }

                Chest[]? shops = Main.instance?.shop;
                if (shops is not null)
                {
                    for (int i = 0; i < shops.Length; i++)
                    {
                        if (ReferenceEquals(shops[i]?.item, items))
                        {
                            return focus.Slot >= 0 ? $"Shop slot {focus.Slot + 1}" : "Shop slot";
                        }
                    }
                }
            }

            int context = Math.Abs(focus.Context);

            if (context == ItemSlot.Context.TrashItem)
            {
                return "Trash slot";
            }

            return string.Empty;
        }

        private static string DescribeInventorySlot(int slot)
        {
            if (slot < 0)
            {
                return string.Empty;
            }

            if (slot < 10)
            {
                return $"Hotbar slot {slot + 1}";
            }

            if (slot < 50)
            {
                return $"Inventory slot {slot - 9}";
            }

            if (slot < 54)
            {
                return $"Coin slot {slot - 49}";
            }

            if (slot < 58)
            {
                return $"Ammo slot {slot - 53}";
            }

            return string.Empty;
        }

        private static Item[]? GetContainerItems(Player player, int chestIndex)
        {
            if (chestIndex >= 0 && chestIndex < Main.chest.Length)
            {
                return Main.chest[chestIndex]?.item;
            }

            return chestIndex switch
            {
                -2 => player.bank.item,
                -3 => player.bank2.item,
                -4 => player.bank3.item,
                -5 => player.bank4.item,
                _ => null,
            };
        }

        private static bool TryMatch(Item[] items, ItemIdentity identity, out int index)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (Matches(items[i], identity))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static bool Matches(Item item, ItemIdentity identity)
        {
            if (item is null || item.IsAir)
            {
                return false;
            }

            return ItemIdentity.From(item).Equals(identity);
        }

        private static string DescribeContainer(int chestIndex)
        {
            return chestIndex switch
            {
                >= 0 => "Chest",
                -2 => "Piggy bank",
                -3 => "Safe",
                -4 => "Defender's forge",
                -5 => "Void vault",
                _ => "Chest",
            };
        }

        private static string DescribeArmorSlot(int index)
        {
            return index switch
            {
                0 => "Helmet slot",
                1 => "Chestplate slot",
                2 => "Leggings slot",
                >= 3 and < 10 => $"Accessory slot {index - 2}",
                >= 10 and < 13 => $"Vanity armor slot {index - 9}",
                >= 13 and < 20 => $"Vanity accessory slot {index - 12}",
                _ => $"Armor slot {index + 1}",
            };
        }

        private static string? TryGetMouseText()
        {
            string? captured = TryGetCapturedMouseText();
            if (!string.IsNullOrWhiteSpace(captured))
            {
                return captured;
            }

            Main? main = Main.instance;
            if (main is null)
            {
                return null;
            }

            FieldInfo? cacheField = MouseTextCacheField.Value;
            if (cacheField is null)
            {
                return null;
            }

            object? cache = cacheField.GetValue(main);
            if (cache is null)
            {
                return null;
            }

            Type cacheType = cache.GetType();
            _mouseTextCursorField ??= cacheType.GetField("cursorText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _mouseTextIsValidField ??= cacheType.GetField("isValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_mouseTextCursorField?.GetValue(cache) is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            return null;
        }

        private static string? TryGetCapturedMouseText()
        {
            if (string.IsNullOrWhiteSpace(_capturedMouseText) || _capturedMouseTextFrame == 0)
            {
                return null;
            }

            uint current = Main.GameUpdateCount;
            uint frame = _capturedMouseTextFrame;
            uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
            if (age <= 2)
            {
                return _capturedMouseText;
            }

            _capturedMouseText = null;
            _capturedMouseTextFrame = 0;
            return null;
        }

        private bool TryAnnounceInGameUiHover()
        {
            if (!_inGameUiTracker.TryGetHoverLabel(Main.InGameUI, out MenuUiLabel hover))
            {
                return false;
            }

            string cleaned = TextSanitizer.Clean(hover.Text);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return false;
            }

            if (!hover.IsNew && string.Equals(cleaned, _lastHoverTooltip, StringComparison.Ordinal))
            {
                return true;
            }

            _lastHover = ItemIdentity.Empty;
            _lastHoverLocation = null;
            _lastHoverTooltip = cleaned;
            _lastHoverDetails = null;
            _lastEmptyMessage = null;
            _lastAnnouncedMessage = cleaned;
            ScreenReaderService.Announce(cleaned);
            return true;
        }

    }
}
