#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed class InGameNarrationSystem : ModSystem
{
    private readonly HotbarNarrator _hotbarNarrator = new();
    private readonly SmartCursorNarrator _smartCursorNarrator = new();
    private readonly CursorNarrator _cursorNarrator = new();
    private readonly InventoryNarrator _inventoryNarrator = new();

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_ItemSlot.MouseHover_ItemArray_int_int += HandleItemSlotHover;
        On_ItemSlot.MouseHover_refItem_int += HandleItemSlotHoverRef;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        CursorNarrator.DisposeStaticResources();
        On_ItemSlot.MouseHover_ItemArray_int_int -= HandleItemSlotHover;
        On_ItemSlot.MouseHover_refItem_int -= HandleItemSlotHoverRef;
    }

    public override void PostUpdatePlayers()
    {
        if (Main.dedServ)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        _hotbarNarrator.Update(player);
        _inventoryNarrator.Update(player);
        _smartCursorNarrator.Update();
        _cursorNarrator.Update();
    }

    private static void HandleItemSlotHover(On_ItemSlot.orig_MouseHover_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        orig(inv, context, slot);
        InventoryNarrator.RecordFocus(inv, context, slot);
    }

    private static void HandleItemSlotHoverRef(On_ItemSlot.orig_MouseHover_refItem_int orig, ref Item item, int context)
    {
        orig(ref item, context);
        InventoryNarrator.RecordFocus(item, context);
    }

    private sealed class HotbarNarrator
    {
        private int _lastSelectedSlot = -1;
        private int _lastItemType = -1;
        private int _lastPrefix = -1;
        private int _lastStack = -1;

        public void Update(Player player)
        {
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
                ScreenReaderService.Announce(description);
            }
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
    }

    private sealed class InventoryNarrator
    {
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

        private static readonly Dictionary<string, string> GlyphTokenMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "A button",
            ["2"] = "B button",
            ["3"] = "X button",
            ["4"] = "Y button",
            ["5"] = "Right bumper",
            ["6"] = "Left bumper",
            ["7"] = "Left trigger",
            ["8"] = "Right trigger",
            ["9"] = "View button",
            ["10"] = "Menu button",
            ["11"] = "Left stick",
            ["12"] = "Right stick",
            ["13"] = "D-pad up",
            ["14"] = "D-pad down",
            ["15"] = "D-pad left",
            ["16"] = "D-pad right",
            ["17"] = "Left stick click",
            ["18"] = "Right stick click",
            ["lb"] = "Left bumper",
            ["rb"] = "Right bumper",
            ["lt"] = "Left trigger",
            ["rt"] = "Right trigger",
            ["ls"] = "Left stick",
            ["rs"] = "Right stick",
            ["back"] = "View button",
            ["select"] = "View button",
            ["menu"] = "Menu button",
            ["start"] = "Menu button",
            ["up"] = "D-pad up",
            ["down"] = "D-pad down",
            ["left"] = "D-pad left",
            ["right"] = "D-pad right",
            ["mouseleft"] = "Left mouse button",
            ["mouseright"] = "Right mouse button",
            ["mousemiddle"] = "Middle mouse button",
            ["mousewheelup"] = "Mouse wheel up",
            ["mousewheeldown"] = "Mouse wheel down",
            ["mousexbutton1"] = "Mouse button four",
            ["mousexbutton2"] = "Mouse button five",
        };

        private static Type? _glyphSnippetType;

        private static readonly string[] GlyphSnippetMemberCandidates =
        {
            "Glyph",
            "_glyph",
            "glyph",
            "GlyphId",
            "_glyphId",
            "glyphId",
            "GlyphIndex",
            "_glyphIndex",
            "glyphIndex",
            "Id",
            "_id",
            "id",
        };

        private static readonly HashSet<string> ReportedGlyphSnippetTypes = new(StringComparer.Ordinal);

        private static readonly TextInfo InvariantTextInfo = CultureInfo.InvariantCulture.TextInfo;

        private static readonly Lazy<PropertyInfo?> LinkPointsProperty = new(() =>
            typeof(UILinkPointNavigator).GetProperty("Points", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

        private static readonly Lazy<FieldInfo?> LinkPointsField = new(() =>
            typeof(UILinkPointNavigator).GetField("Points", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

        private static readonly Dictionary<int, int> OriginalLinkPointRight = new();

        private static int _lastLinkPoint = -1;
        private static bool _lastHopFromBottomInventory;

        private static int _lastLoggedLinkPoint = int.MinValue;

        private static readonly Lazy<FieldInfo?> MouseTextCacheField = new(() =>
            typeof(Main).GetField("_mouseTextCache", BindingFlags.Instance | BindingFlags.NonPublic));

        private static FieldInfo? _mouseTextCursorField;
        private static FieldInfo? _mouseTextIsValidField;

        public static void RecordFocus(Item[] inventory, int context, int slot)
        {
            _pendingFocus = new SlotFocus(inventory, null, context, slot);
            LogLinkPointState(context, slot);
        }

        public static void RecordFocus(Item item, int context)
        {
            _pendingFocus = new SlotFocus(null, item, context, -1);
            LogLinkPointState(context, -1);
        }

        public void Update(Player player)
        {
            if (!IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            if (_pendingFocus.HasValue)
            {
                _currentFocus = _pendingFocus;
                _pendingFocus = null;
            }

            HandleMouseItem();
            HandleHoverItem(player);
        }

        private static bool IsInventoryUiOpen(Player player)
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
            Item hover = Main.HoverItem;
            ItemIdentity identity = ItemIdentity.From(hover);
            string rawTooltip = Main.hoverItemName ?? string.Empty;
            string normalizedTooltip = NormalizeGlyphTags(rawTooltip);

            SlotFocus? focus = _currentFocus;
            string location = DescribeLocation(player, identity, focus);

            if (TryAnnounceSpecialSelection(identity.IsAir, location))
            {
                return;
            }

            if (!identity.IsAir)
            {
                string label = ComposeItemLabel(hover);
                string message = string.IsNullOrEmpty(location) ? label : $"{label}, {location}";
                string? details = BuildTooltipDetails(hover, rawTooltip);
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
                string trimmedMouseText = NormalizeGlyphTags(mouseText.Trim());
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

            if (_mouseTextIsValidField is not null && _mouseTextIsValidField.GetValue(cache) is bool isValid && !isValid)
            {
                return null;
            }

            if (_mouseTextCursorField?.GetValue(cache) is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            return null;
        }

        private static string? BuildTooltipDetails(Item item, string hoverName)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            HashSet<string> nameCandidates = BuildItemNameCandidates(item, hoverName);
            List<string>? lines = null;
            string? raw = TryGetMouseText();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                lines = ExtractTooltipLines(raw, nameCandidates);
            }

            if (lines is null || lines.Count == 0)
            {
                lines = ExtractTooltipLinesFromItem(item, nameCandidates);
            }

            if (lines.Count == 0)
            {
                return null;
            }

            string formatted = FormatTooltipLines(lines);
            return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
        }

        private static List<string> ExtractTooltipLines(string raw, HashSet<string> nameCandidates)
        {
            string sanitized = SanitizeTooltipText(raw);
            List<string> lines = new();

            string[] segments = sanitized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (trimmed.Length > 0 && !IsItemNameLine(trimmed, nameCandidates))
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        }

        private static List<string> ExtractTooltipLinesFromItem(Item item, HashSet<string> nameCandidates)
        {
            List<string> lines = new();

            try
            {
                Item clone = item.Clone();
                const int MaxLines = 60;
                string[] toolTipLine = new string[MaxLines];
                bool[] preFixLine = new bool[MaxLines];
                bool[] badPreFixLine = new bool[MaxLines];
                string[] toolTipNames = new string[MaxLines];
                int yoyoLogo = -1;
                int researchLine = -1;
                float originalKnockBack = clone.knockBack;
                int numLines = 1;

                Main.MouseText_DrawItemTooltip_GetLinesInfo(
                    clone,
                    ref yoyoLogo,
                    ref researchLine,
                    originalKnockBack,
                    ref numLines,
                    toolTipLine,
                    preFixLine,
                    badPreFixLine,
                    toolTipNames,
                    out _);

                for (int i = 0; i < numLines && i < toolTipLine.Length; i++)
                {
                    string? line = toolTipLine[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string? entryName = toolTipNames[i];
                    if (!string.IsNullOrWhiteSpace(entryName) &&
                        string.Equals(entryName, "ItemName", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string trimmed = line.Trim();
                    if (IsItemNameLine(trimmed, nameCandidates))
                    {
                        continue;
                    }

                    lines.Add(trimmed);
                }
            }
            catch
            {
                // Swallow exceptions and return whatever we have.
            }

            return lines;
        }

        private static bool IsItemNameLine(string? line, HashSet<string> nameCandidates)
        {
            if (string.IsNullOrWhiteSpace(line) || nameCandidates is null || nameCandidates.Count == 0)
            {
                return false;
            }

            string normalizedLine = NormalizeGlyphTags(line).Trim();
            return nameCandidates.Contains(normalizedLine);
        }

        private static HashSet<string> BuildItemNameCandidates(Item item, string hoverName)
        {
            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidates, hoverName);
            AddCandidate(candidates, item.Name);
            AddCandidate(candidates, item.AffixName());
            AddCandidate(candidates, Lang.GetItemNameValue(item.type));
            return candidates;
        }

        private static void AddCandidate(HashSet<string> candidates, string? value)
        {
            if (candidates is null)
            {
                return;
            }

            string normalized = NormalizeNameCandidate(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        private static string NormalizeNameCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string sanitized = SanitizeTooltipText(value);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = value;
            }

            return NormalizeGlyphTags(sanitized).Trim();
        }

        private static string SanitizeTooltipText(string raw)
        {
            try
            {
                List<TextSnippet>? snippets = ChatManager.ParseMessage(raw, Color.White);
                if (snippets is null || snippets.Count == 0)
                {
                    return raw;
                }

                StringBuilder builder = new();
                foreach (TextSnippet snippet in snippets)
                {
                    if (snippet is null)
                    {
                        continue;
                    }

                    if (TryAppendGlyphSnippet(builder, snippet))
                    {
                        continue;
                    }

                    builder.Append(snippet.Text);
                }

                string sanitized = builder.ToString();
                if (raw.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sanitized.IndexOf(" to open", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogGlyphDebug(raw, sanitized, snippets);
                }

                return NormalizeGlyphTags(sanitized);
            }
            catch
            {
                return NormalizeGlyphTags(raw);
            }
        }

        private static bool TryAppendGlyphSnippet(StringBuilder builder, TextSnippet snippet)
        {
            if (snippet is null)
            {
                return false;
            }

            Type snippetType = snippet.GetType();
            if (!IsGlyphSnippetType(snippetType))
            {
                ReportGlyphSnippetType(snippetType, "UnrecognizedSnippetType");
                return false;
            }

            string? glyphToken = ExtractGlyphToken(snippetType, snippet);
            if (string.IsNullOrWhiteSpace(glyphToken))
            {
                ReportGlyphSnippetType(snippetType, "MissingGlyphToken");
                return false;
            }

            glyphToken = glyphToken.Trim();

            if (!TryTranslateGlyphToken(glyphToken, out string replacement) &&
                !TryTranslateGlyphToken($"g{glyphToken}", out replacement))
            {
                string humanized = HumanizeToken(glyphToken);
                if (string.IsNullOrWhiteSpace(humanized))
                {
                    ReportGlyphSnippetType(snippetType, $"UnmappedToken:{glyphToken}");
                    return false;
                }

                replacement = humanized;
            }

            if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
            {
                builder.Append(' ');
            }

            builder.Append(replacement);
            return true;
        }

        private static bool IsGlyphSnippetType(Type snippetType)
        {
            if (snippetType is null)
            {
                return false;
            }

            if (_glyphSnippetType is not null)
            {
                return snippetType == _glyphSnippetType || snippetType.IsSubclassOf(_glyphSnippetType);
            }

            string? fullName = snippetType.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return false;
            }

            if (fullName.Contains("GlyphTagHandler", StringComparison.Ordinal) ||
                fullName.Contains("GlyphSnippet", StringComparison.Ordinal))
            {
                _glyphSnippetType = snippetType;
                return true;
            }

            ReportGlyphSnippetType(snippetType, "UnrecognizedSnippetType");
            return false;
        }

        private static void ReportGlyphSnippetType(Type snippetType, string note)
        {
            if (snippetType is null)
            {
                return;
            }

            string name = snippetType.FullName ?? snippetType.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string key = $"{name}:{note}";
            lock (ReportedGlyphSnippetTypes)
            {
                if (!ReportedGlyphSnippetTypes.Add(key))
                {
                    return;
                }
            }

            ScreenReaderMod.Instance?.Logger.Info($"[GlyphSnippet] {note} -> {name}");
        }

        private static void LogGlyphDebug(string raw, string sanitized, IEnumerable<TextSnippet> snippets)
        {
            try
            {
                ScreenReaderMod.Instance?.Logger.Info($"[GlyphDebug] raw: {raw}");
                ScreenReaderMod.Instance?.Logger.Info($"[GlyphDebug] sanitized: {sanitized}");

                foreach (TextSnippet snippet in snippets)
                {
                    if (snippet is null)
                    {
                        continue;
                    }

                    Type type = snippet.GetType();
                    string typeName = type.FullName ?? type.Name;
                    ScreenReaderMod.Instance?.Logger.Info($"[GlyphDebug] snippet: {typeName} -> \"{snippet.Text}\"");
                }
            }
            catch
            {
                // ignore logging failures
            }
        }

        private static void LogLinkPointState(int context, int slot)
        {
            try
            {
                if (!PlayerInput.UsingGamepad)
                {
                    return;
                }

                int currentPoint = UILinkPointNavigator.CurrentPoint;
                if (currentPoint == _lastLoggedLinkPoint)
                {
                    return;
                }

                _lastLoggedLinkPoint = currentPoint;
                if (!TryGetLinkPoint(currentPoint, out UILinkPoint? linkPoint) || linkPoint is null)
                {
                    ScreenReaderMod.Instance?.Logger.Debug($"[InventoryNav] point={currentPoint} missing (context={context}, slot={slot})");
                    return;
                }

                ScreenReaderMod.Instance?.Logger.Debug(
                    $"[InventoryNav] point={currentPoint} up={linkPoint.Up} down={linkPoint.Down} left={linkPoint.Left} right={linkPoint.Right} page={linkPoint.Page} ctx={context} slot={slot}");

                if (slot >= 30 && slot <= 40)
                {
                    ScreenReaderMod.Instance?.Logger.Debug(
                        $"[InventoryNavDetail] point={currentPoint} upPoint={DescribeLinkPoint(linkPoint.Up)} downPoint={DescribeLinkPoint(linkPoint.Down)} leftPoint={DescribeLinkPoint(linkPoint.Left)} rightPoint={DescribeLinkPoint(linkPoint.Right)}");
                }

                HandleInventoryBottomRowHop(currentPoint, context);
                _lastLinkPoint = currentPoint;
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[InventoryNav] Failed to log link point: {ex.Message}");
            }
        }

        private static bool TryGetLinkPoint(int id, out UILinkPoint? linkPoint)
        {
            linkPoint = null;

            object? collection = GetLinkPointCollection();

            if (collection is UILinkPoint[] array)
            {
                if (id >= 0 && id < array.Length)
                {
                    UILinkPoint? candidate = array[id];
                    if (candidate is not null)
                    {
                        linkPoint = candidate;
                        return true;
                    }
                }

                return false;
            }

            if (collection is Dictionary<int, UILinkPoint> dict)
            {
                if (dict.TryGetValue(id, out UILinkPoint? value) && value is not null)
                {
                    linkPoint = value;
                    return true;
                }

                return false;
            }

            if (collection is IList list)
            {
                if (id >= 0 && id < list.Count && list[id] is UILinkPoint element && element is not null)
                {
                    linkPoint = element;
                    return true;
                }

                return false;
            }

            return false;
        }

        private static string DescribeLinkPoint(int id)
        {
            if (id < 0)
            {
                return id.ToString(CultureInfo.InvariantCulture);
            }

            return TryGetLinkPoint(id, out UILinkPoint? point) && point is not null
                ? $"{id}(L={point.Left},R={point.Right},U={point.Up},D={point.Down})"
                : $"{id}(missing)";
        }

        private static void HandleInventoryBottomRowHop(int currentPoint, int context)
        {
            bool inInventory = context == ItemSlot.Context.InventoryItem;

            if (_lastLinkPoint == 40 && currentPoint == 311 && inInventory)
            {
                _lastHopFromBottomInventory = true;
            }
            else if (currentPoint != 311)
            {
                _lastHopFromBottomInventory = false;
            }

            if (currentPoint == 311)
            {
                EnsureQuickAccessFromQuickStack(inventoryHop: inInventory && _lastHopFromBottomInventory);
            }
            else if (currentPoint == 40)
            {
                EnsureQuickAccessFromQuickStack(inventoryHop: false);
            }
        }

        private static void EnsureQuickAccessFromQuickStack(bool inventoryHop)
        {
            if (!TryGetLinkPoint(311, out UILinkPoint? quickStack) || quickStack is null)
            {
                return;
            }

            if (!OriginalLinkPointRight.ContainsKey(311))
            {
                OriginalLinkPointRight[311] = quickStack.Right;
            }

            if (inventoryHop)
            {
                if (TryGetLinkPoint(41, out UILinkPoint? neighbor) && neighbor is not null)
                {
                    quickStack.Right = 41;
                }

                return;
            }

            if (OriginalLinkPointRight.TryGetValue(311, out int originalRight))
            {
                quickStack.Right = originalRight;
            }
        }

        private static object? GetLinkPointCollection()
        {
            PropertyInfo? property = LinkPointsProperty.Value;
            if (property is not null)
            {
                try
                {
                    return property.GetValue(null);
                }
                catch
                {
                    // ignore
                }
            }

            FieldInfo? field = LinkPointsField.Value;
            if (field is not null)
            {
                try
                {
                    return field.GetValue(null);
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static string? ExtractGlyphToken(Type snippetType, TextSnippet snippet)
        {
            foreach (string memberName in GlyphSnippetMemberCandidates)
            {
                object? value = TryGetMemberValue(snippetType, snippet, memberName);
                if (value is null)
                {
                    continue;
                }

                string? token = ConvertGlyphMemberToString(value);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }

            return null;
        }

        private static object? TryGetMemberValue(Type type, object instance, string memberName)
        {
            if (type is null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance);
                }
                catch
                {
                    // Ignore accessor failures.
                }
            }

            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                    // Ignore accessor failures.
                }
            }

            return null;
        }

        private static string? ConvertGlyphMemberToString(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string text:
                    return text;
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case Enum enumValue:
                    return Convert.ToInt32(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                default:
                    string? result = value.ToString();
                    return string.IsNullOrWhiteSpace(result) ? null : result;
            }
        }

        private static string CombineItemAnnouncement(string message, string? details)
        {
            string normalizedMessage = NormalizeGlyphTags(message.Trim());
            if (string.IsNullOrWhiteSpace(details))
            {
                return normalizedMessage;
            }

            string normalizedDetails = NormalizeGlyphTags(details.Trim());
            if (!HasTerminalPunctuation(normalizedMessage))
            {
                normalizedMessage += ".";
            }

            string combined = $"{normalizedMessage} {normalizedDetails}";
            return combined;
        }

        private static string FormatTooltipLines(List<string> lines)
        {
            StringBuilder builder = new();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    builder.Append(line);
                    if (i + 1 < lines.Count)
                    {
                        string next = lines[++i];
                        if (!string.IsNullOrWhiteSpace(next))
                        {
                            builder.Append(' ');
                            builder.Append(next);
                            if (!HasTerminalPunctuation(next))
                            {
                                builder.Append('.');
                            }
                        }
                    }

                    continue;
                }

                builder.Append(line);
                if (!HasTerminalPunctuation(line))
                {
                    builder.Append('.');
                }
            }

            string result = builder.ToString();
            return NormalizeGlyphTags(result);
        }

        private static bool HasTerminalPunctuation(string text)
        {
            text = text.TrimEnd();
            if (text.Length == 0)
            {
                return false;
            }

            char last = text[^1];
            return last == '.' || last == '!' || last == '?' || last == ':' || last == ')';
        }

        private static string NormalizeGlyphTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            StringBuilder builder = new(text.Length);
            bool replaced = false;
            int index = 0;

            while (index < text.Length)
            {
                if (text[index] == '[' && index + 2 < text.Length && (text[index + 1] == 'g' || text[index + 1] == 'G'))
                {
                    int end = index + 2;
                    while (end < text.Length && text[end] != ']')
                    {
                        end++;
                    }

                    if (end < text.Length)
                    {
                        string token = text.Substring(index + 1, end - index - 1);
                        if (TryTranslateGlyphToken(token, out string replacement))
                        {
                            builder.Append(replacement);
                            index = end + 1;
                            replaced = true;
                            continue;
                        }
                    }
                }

                if ((text[index] == 'g' || text[index] == 'G') &&
                    (index == 0 || !char.IsLetterOrDigit(text[index - 1])))
                {
                    int end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    {
                        end++;
                    }

                    if (end > index + 1)
                    {
                        string token = text.Substring(index, end - index);
                        if (TryTranslateGlyphToken(token, out string replacement))
                        {
                            builder.Append(replacement);
                            index = end;
                            replaced = true;
                            continue;
                        }
                    }
                }

                builder.Append(text[index]);
                index++;
            }

            string normalized = replaced ? builder.ToString() : text;
            return ReplaceFallbackGlyphNumbers(normalized);
        }

        private static bool TryTranslateGlyphToken(string token, out string replacement)
        {
            replacement = string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string trimmed = token.Trim().Trim('[', ']');
            if (trimmed.Length == 0)
            {
                return false;
            }

            string raw = trimmed;
            if (trimmed.Length > 0 && (trimmed[0] == 'g' || trimmed[0] == 'G'))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.Length == 0)
            {
                return false;
            }

            string normalized = trimmed.TrimStart('_');

            if (GlyphTokenMap.TryGetValue(normalized, out string? mapped) ||
                GlyphTokenMap.TryGetValue(raw, out mapped))
            {
                replacement = mapped!;
                return true;
            }

            string humanized = HumanizeToken(normalized);
            if (!string.IsNullOrWhiteSpace(humanized))
            {
                replacement = humanized;
                return true;
            }

            return false;
        }

        private static string ReplaceFallbackGlyphNumbers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            StringBuilder builder = new(text.Length + 8);
            int index = 0;

            while (index < text.Length)
            {
                char current = text[index];
                if (char.IsDigit(current))
                {
                    int start = index;
                    int end = index;
                    while (end < text.Length && char.IsDigit(text[end]))
                    {
                        end++;
                    }

                    string token = text.Substring(start, end - start);
                    if (TryTranslateGlyphToken(token, out string replacement) &&
                        ShouldTreatAsGlyphNumber(text, start, end))
                    {
                        builder.Append(replacement);
                    }
                    else
                    {
                        builder.Append(token);
                    }

                    index = end;
                    continue;
                }

                builder.Append(current);
                index++;
            }

            return builder.ToString();
        }

        private static bool ShouldTreatAsGlyphNumber(string text, int start, int end)
        {
            static bool MatchesAny(ReadOnlySpan<char> span, params string[] candidates)
            {
                foreach (string candidate in candidates)
                {
                    if (span.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            ReadOnlySpan<char> before = text.AsSpan(0, start);
            ReadOnlySpan<char> after = text.AsSpan(end);

            if (MatchesAny(after, " to ", " to_", " to-", " to.", " to,", " to!"))
            {
                return true;
            }

            if (MatchesAny(after, " button", " trigger", " shoulder", " stick"))
            {
                return true;
            }

            const int PressLength = 6; // "Press "
            if (before.Length >= PressLength)
            {
                ReadOnlySpan<char> prefix = before.Slice(before.Length - PressLength);
                if (prefix.Equals("Press ", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (before.Length >= 2)
            {
                ReadOnlySpan<char> suffix = before.Slice(before.Length - 2);
                if (suffix.Equals(": ", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string HumanizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            StringBuilder builder = new(token.Length + 4);
            for (int i = 0; i < token.Length; i++)
            {
                char current = token[i];
                if (i > 0)
                {
                    char previous = token[i - 1];
                    bool split = char.IsDigit(current) != char.IsDigit(previous) ||
                                 (char.IsUpper(current) && !char.IsUpper(previous)) ||
                                 current == '_';
                    if (split)
                    {
                        builder.Append(' ');
                    }
                }

                if (current != '_')
                {
                    builder.Append(char.ToLowerInvariant(current));
                }
            }

            string spaced = builder.ToString().Trim();
            if (spaced.Length == 0)
            {
                return string.Empty;
            }

            return InvariantTextInfo.ToTitleCase(spaced);
        }

        private bool TryAnnounceSpecialSelection(bool hoverIsAir, string? location)
        {
            int currentPoint = UILinkPointNavigator.CurrentPoint;
            string? label = GetSpecialSelectionLabel(currentPoint, hoverIsAir, location);
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            if (string.Equals(_lastAnnouncedMessage, label, StringComparison.Ordinal))
            {
                return true;
            }

            _lastHover = ItemIdentity.Empty;
            _lastHoverTooltip = null;
            _lastHoverDetails = null;
            _lastHoverLocation = null;
            _lastEmptyMessage = null;
            _lastAnnouncedMessage = label;
            ScreenReaderService.Announce(label, force: true);
            return true;
        }

        private static string? GetSpecialSelectionLabel(int point, bool hoverIsAir, string? location)
        {
            static string? Button(string? text)
            {
                return string.IsNullOrWhiteSpace(text) ? null : $"{text} button";
            }

            string? result = point switch
            {
                301 => Button(Language.GetTextValue("GameUI.QuickStackToNearby")),
                302 => Button(Language.GetTextValue("GameUI.SortInventory")),
                304 => Button(Lang.inter[19].Value),
                305 => Button(Lang.inter[79].Value),
                306 => Button(Lang.inter[80].Value),
                307 => Button(Main.CaptureModeDisabled ? Lang.inter[115].Value : Lang.inter[81].Value),
                308 => Button(Lang.inter[62].Value),
                309 => Button(Language.GetTextValue("GameUI.Emote")),
                310 => Button(Language.GetTextValue("GameUI.Bestiary")),
                int loadout when loadout >= 312 && loadout <= 320 => Button(GetLoadoutLabel(loadout)),
                _ => null,
            };

            static string? GetLoadoutLabel(int point)
            {
                int index = point - 311;
                if (index < 1 || index > 9)
                {
                    return null;
                }

                return Language.GetTextValue($"UI.Loadout{index}");
            }

            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            if (Main.ingameOptionsWindow)
            {
                return DescribeIngameOptionsFocus(hoverIsAir, location);
            }

            return null;
        }

        private static string? DescribeIngameOptionsFocus(bool hoverIsAir, string? location)
        {
            int feature = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;
            string? label = feature switch
            {
                1 => $"Background parallax {Main.bgScroll} percent",
                2 => $"Music volume {Math.Round(Main.musicVolume * 100f)} percent",
                3 => $"Sound volume {Math.Round(Main.soundVolume * 100f)} percent",
                4 => $"Ambient volume {Math.Round(Main.ambientVolume * 100f)} percent",
                5 => "Cursor color hue slider",
                6 => "Cursor color saturation slider",
                7 => "Cursor color brightness slider",
                8 => "Cursor color opacity slider",
                9 => "Hair style selector",
                _ => null,
            };

            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            LogIngameOptionsState(feature, hoverIsAir, location);
            return null;
        }

        private static int _lastOptionsStateHash = int.MinValue;

        private static void LogIngameOptionsState(int feature, bool hoverIsAir, string? location)
        {
            int leftHover = GetStaticFieldValue(IngameOptionsLeftHoverField);
            int category = GetStaticFieldValue(IngameOptionsCategoryField);
            int rightHover = IngameOptions.rightHover;
            int rightLock = IngameOptions.rightLock;
            int currentPoint = UILinkPointNavigator.CurrentPoint;
            int hash = HashCode.Combine(feature, leftHover, category, rightHover, rightLock, currentPoint, hoverIsAir ? 1 : 0, location ?? string.Empty);

            if (hash == _lastOptionsStateHash)
            {
                return;
            }

            _lastOptionsStateHash = hash;
            ScreenReaderMod.Instance?.Logger.Debug($"[IngameOptionsNarration] point={currentPoint} feature={feature} cat={category} left={leftHover} rightHover={rightHover} rightLock={rightLock} hoverIsAir={hoverIsAir} location='{location}'");
        }

        private static readonly FieldInfo? IngameOptionsLeftHoverField = typeof(IngameOptions).GetField("leftHover", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? IngameOptionsCategoryField = typeof(IngameOptions).GetField("category", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static int GetStaticFieldValue(FieldInfo? field)
        {
            if (field is null)
            {
                return -1;
            }

            try
            {
                object? value = field.GetValue(null);
                if (value is int intValue)
                {
                    return intValue;
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameOptionsNarration] Unable to read {field.Name}: {ex.Message}");
            }

            return -1;
        }

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
    }

    private sealed class SmartCursorNarrator
    {
        private string? _lastAnnouncement;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private int _lastNpc = -1;
        private int _lastProj = -1;
        private int _lastInteractTileType = -1;
        private int _lastCursorTileType = -1;

        public void Update()
        {
            bool hasInteract = Main.HasSmartInteractTarget;
            bool hasSmartCursor = Main.SmartCursorIsUsed || Main.SmartCursorWanted;

            if (!hasInteract && !hasSmartCursor)
            {
                Reset();
                return;
            }

            string? message = hasInteract ? DescribeSmartInteract() : DescribeSmartCursor();
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (string.Equals(message, _lastAnnouncement, StringComparison.Ordinal))
            {
                return;
            }

            _lastAnnouncement = message;
            ScreenReaderService.Announce(message);
        }

        private void Reset()
        {
            _lastAnnouncement = null;
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
            _lastNpc = -1;
            _lastProj = -1;
            _lastInteractTileType = -1;
            _lastCursorTileType = -1;
        }

        private string? DescribeSmartInteract()
        {
            int npcIndex = Main.SmartInteractNPC;
            if (npcIndex >= 0 && npcIndex < Main.npc.Length)
            {
                string npcName = Main.npc[npcIndex].GivenOrTypeName;
                if (npcIndex == _lastNpc && string.Equals(npcName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                _lastNpc = npcIndex;
                _lastProj = -1;
                _lastTileX = int.MinValue;
                _lastTileY = int.MinValue;
                _lastInteractTileType = -1;

                return npcName;
            }

            int projIndex = Main.SmartInteractProj;
            if (projIndex >= 0 && projIndex < Main.projectile.Length)
            {
                string projName = Lang.GetProjectileName(Main.projectile[projIndex].type).Value;
                if (string.IsNullOrWhiteSpace(projName))
                {
                    projName = $"Projectile {Main.projectile[projIndex].type}";
                }

                if (projIndex == _lastProj && string.Equals(projName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                _lastProj = projIndex;
                _lastNpc = -1;
                _lastTileX = int.MinValue;
                _lastTileY = int.MinValue;
                _lastInteractTileType = -1;

                return projName;
            }

            int tileX = Main.SmartInteractX;
            int tileY = Main.SmartInteractY;
            if (tileX >= 0 && tileY >= 0)
            {
                if (!TileDescriptor.TryDescribe(tileX, tileY, out int tileType, out string? tileName))
                {
                    return null;
                }

                if (tileX == _lastTileX && tileY == _lastTileY && string.Equals(tileName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                if (tileType == _lastInteractTileType)
                {
                    return null;
                }

                _lastTileX = tileX;
                _lastTileY = tileY;
                _lastNpc = -1;
                _lastProj = -1;
                _lastInteractTileType = tileType;

                if (!string.IsNullOrWhiteSpace(tileName))
                {
                    return tileName;
                }
            }

            return null;
        }

        private string? DescribeSmartCursor()
        {
            int tileX = Main.SmartCursorX;
            int tileY = Main.SmartCursorY;
            if (!TileDescriptor.TryDescribe(tileX, tileY, out int tileType, out string? tileName))
            {
                return null;
            }

            if (tileX == _lastTileX && tileY == _lastTileY && string.Equals(tileName, _lastAnnouncement, StringComparison.Ordinal))
            {
                return null;
            }

            if (tileType == _lastCursorTileType)
            {
                return null;
            }

            _lastTileX = tileX;
            _lastTileY = tileY;
            _lastNpc = -1;
            _lastProj = -1;
            _lastCursorTileType = tileType;

            if (string.IsNullOrWhiteSpace(tileName))
            {
                return null;
            }

            return tileName;
        }

    }

    private sealed class CursorNarrator
    {
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private static SoundEffect? _cursorTone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();

        public void Update()
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                ResetAll();
                return;
            }

            bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool cursorIsFree = !smartCursorActive && !hasSmartInteract;

            if (_lastSmartCursorActive && cursorIsFree)
            {
                CenterCursorOnPlayer(player);
            }

            _lastSmartCursorActive = smartCursorActive || hasSmartInteract;

            if (!cursorIsFree)
            {
                ResetCursorFeedback();
                return;
            }

            Vector2 world = Main.MouseWorld;
            int tileX = (int)(world.X / 16f);
            int tileY = (int)(world.Y / 16f);

            bool tileChanged = tileX != _lastTileX || tileY != _lastTileY;
            if (!tileChanged)
            {
                return;
            }

            Vector2 tileCenter = new(tileX * 16f + 8f, tileY * 16f + 8f);
            PlayCursorCue(player, tileCenter);

            _lastTileX = tileX;
            _lastTileY = tileY;

            if (!PlayerInput.UsingGamepad)
            {
                return;
            }

            if (!TileDescriptor.TryDescribe(tileX, tileY, out int tileType, out string? name))
            {
                ScreenReaderService.Announce("Empty", force: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                ScreenReaderService.Announce(name, force: true);
            }
        }

        private void ResetAll()
        {
            ResetCursorFeedback();
            _lastSmartCursorActive = false;
        }

        private void ResetCursorFeedback()
        {
            ResetTileTracking();
        }

        private void ResetTileTracking()
        {
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
        }

        private static void CenterCursorOnPlayer(Player player)
        {
            Vector2 screenSpace = player.Center - Main.screenPosition;
            int centeredX = (int)MathHelper.Clamp(screenSpace.X, 0f, Main.screenWidth - 1);
            int centeredY = (int)MathHelper.Clamp(screenSpace.Y, 0f, Main.screenHeight - 1);

            Main.mouseX = centeredX;
            Main.mouseY = centeredY;
            PlayerInput.MouseX = centeredX;
            PlayerInput.MouseY = centeredY;
        }

        private static void PlayCursorCue(Player player, Vector2 tileCenterWorld)
        {
            CleanupFinishedInstances();

            Vector2 offset = tileCenterWorld - player.Center;
            SoundEffect tone = EnsureCursorTone();

            float pan = MathHelper.Clamp(offset.X / 480f, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.6f, 0.6f);
            float volume = MathHelper.Clamp(0.6f + Math.Abs(pitch) * 0.3f, 0f, 1f);

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = volume * Main.soundVolume;
            instance.Pitch = pitch;
            instance.Pan = pan;
            instance.Play();
            ActiveInstances.Add(instance);
        }

        public static void DisposeStaticResources()
        {
            foreach (SoundEffectInstance instance in ActiveInstances)
            {
                try
                {
                    instance.Stop();
                }
                catch
                {
                    // ignore
                }

                instance.Dispose();
            }

            ActiveInstances.Clear();

            if (_cursorTone is not null)
            {
                _cursorTone.Dispose();
                _cursorTone = null;
            }
        }

        private static SoundEffect EnsureCursorTone()
        {
            if (_cursorTone is null || _cursorTone.IsDisposed)
            {
                _cursorTone?.Dispose();
                _cursorTone = CreateCursorTone();
            }

            return _cursorTone;
        }

        private static SoundEffect CreateCursorTone()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.09f;
            const float frequency = 880f;
            int sampleCount = Math.Max(1, (int)(sampleRate * durationSeconds));
            byte[] buffer = new byte[sampleCount * sizeof(short)];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float window = (float)(0.5 - 0.5 * Math.Cos((2 * Math.PI * i) / Math.Max(1, sampleCount - 1)));
                float sample = MathF.Sin(MathHelper.TwoPi * frequency * t) * window;
                short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(value & 0xFF);
                buffer[index + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
        }

        private static void CleanupFinishedInstances()
        {
            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = ActiveInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    ActiveInstances.RemoveAt(i);
                }
            }
        }
    }

    private static string ComposeItemLabel(Item item)
    {
        string name = item.AffixName();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = item.Name;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Lang.GetItemNameValue(item.type);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Item {item.type}";
        }

        if (item.stack > 1)
        {
            name = $"{name}, stack {item.stack}";
        }

        if (item.favorited)
        {
            name = $"{name}, favorited";
        }

        return name;
    }

    private static class TileDescriptor
    {
        public static bool TryDescribe(int tileX, int tileY, out int tileType, out string? name)
        {
            tileType = -1;
            name = string.Empty;

            if (!WorldGen.InWorld(tileX, tileY))
            {
                return false;
            }

            Tile tile = Main.tile[tileX, tileY];
            if (!tile.HasTile)
            {
                return false;
            }

            tileType = tile.TileType;

            try
            {
                int lookup = MapHelper.TileToLookup(tileType, 0);
                name = Lang.GetMapObjectName(lookup);
            }
            catch
            {
                name = null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = TileID.Search.GetName(tileType);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"tile {tileType}";
            }

            return true;
        }
    }
}
