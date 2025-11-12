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

public sealed partial class InGameNarrationSystem : ModSystem
{
    private readonly HotbarNarrator _hotbarNarrator = new();
    private readonly SmartCursorNarrator _smartCursorNarrator = new();
    private readonly CraftingNarrator _craftingNarrator = new();
    private readonly CursorNarrator _cursorNarrator = new();
    private readonly TreasureBagBeaconEmitter _treasureBagBeaconEmitter = new();
    private readonly HostileStaticAudioEmitter _hostileStaticAudioEmitter = new();
    private readonly WorldInteractableTracker _worldInteractableTracker = new();
    private readonly InventoryNarrator _inventoryNarrator = new();
    private readonly NpcDialogueNarrator _npcDialogueNarrator = new();
    private readonly IngameSettingsNarrator _ingameSettingsNarrator = new();
    private readonly WorldEventNarrator _worldEventNarrator = new();
    private readonly ControlsMenuNarrator _controlsMenuNarrator = new();
    private readonly ModConfigMenuNarrator _modConfigMenuNarrator = new();
    private readonly FootstepAudioEmitter _footstepAudioEmitter = new();
    private readonly NpcFootstepAudioEmitter _npcFootstepAudioEmitter = new();
    private readonly BiomeAnnouncementEmitter _biomeAnnouncementEmitter = new();

    private const int FootstepVariantCount = 5;

    private enum FootstepSide
    {
        Left,
        Right
    }

        public override void Load()
        {
            if (Main.dedServ)
            {
                return;
            }

            On_ItemSlot.MouseHover_ItemArray_int_int += HandleItemSlotHover;
            On_ItemSlot.MouseHover_refItem_int += HandleItemSlotHoverRef;
            On_Main.DrawNPCChatButtons += CaptureNpcChatButtons;
            On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool += CaptureMouseText;
            On_IngameOptions.Draw += HandleIngameOptionsDraw;
            On_IngameOptions.DrawLeftSide += CaptureIngameOptionsLeft;
            On_IngameOptions.DrawRightSide += CaptureIngameOptionsRight;
        }

        public override void Unload()
        {
            if (Main.dedServ)
        {
            return;
        }

            _treasureBagBeaconEmitter.Reset();
            _hostileStaticAudioEmitter.Reset();
            _footstepAudioEmitter.Reset();
            _npcFootstepAudioEmitter.Reset();
            _worldInteractableTracker.Reset();
            _biomeAnnouncementEmitter.Reset();
            CursorNarrator.DisposeStaticResources();
            TreasureBagBeaconEmitter.DisposeStaticResources();
            FootstepToneProvider.DisposeStaticResources();
            WorldInteractableTracker.DisposeStaticResources();
            HostileStaticAudioEmitter.DisposeStaticResources();
            On_ItemSlot.MouseHover_ItemArray_int_int -= HandleItemSlotHover;
            On_ItemSlot.MouseHover_refItem_int -= HandleItemSlotHoverRef;
            On_Main.DrawNPCChatButtons -= CaptureNpcChatButtons;
            On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool -= CaptureMouseText;
            On_IngameOptions.Draw -= HandleIngameOptionsDraw;
            On_IngameOptions.DrawLeftSide -= CaptureIngameOptionsLeft;
            On_IngameOptions.DrawRightSide -= CaptureIngameOptionsRight;
        }

    public override void OnWorldLoad()
    {
        _worldEventNarrator.InitializeFromWorld();
    }

    public override void OnWorldUnload()
    {
        _worldEventNarrator.Reset();
        _treasureBagBeaconEmitter.Reset();
        _hostileStaticAudioEmitter.Reset();
        _footstepAudioEmitter.Reset();
        _npcFootstepAudioEmitter.Reset();
        _worldInteractableTracker.Reset();
        _biomeAnnouncementEmitter.Reset();
    }

    public override void PostUpdateWorld()
    {
        _worldEventNarrator.Update();
    }

    public override void PostUpdatePlayers()
    {
        TryUpdateNarrators(requirePaused: false);
    }

    public override void UpdateUI(GameTime gameTime)
    {
        TryUpdateNarrators(requirePaused: true);
    }

    private void TryUpdateNarrators(bool requirePaused)
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        bool isPaused = Main.gamePaused;
        if (requirePaused)
        {
            if (!isPaused)
            {
                return;
            }
        }
        else if (isPaused)
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
        _craftingNarrator.Update(player);
        _smartCursorNarrator.Update();
        _cursorNarrator.Update();
        if (!isPaused)
        {
            _treasureBagBeaconEmitter.Update(player);
            _hostileStaticAudioEmitter.Update(player);
            _footstepAudioEmitter.Update(player);
            _npcFootstepAudioEmitter.Update(player);
            _worldInteractableTracker.Update(player, WaypointSystem.IsExplorationTrackingEnabled);
            _biomeAnnouncementEmitter.Update(player);
        }

        _npcDialogueNarrator.Update(player);
        _controlsMenuNarrator.Update(isPaused);
        _modConfigMenuNarrator.TryHandleIngameUi(Main.InGameUI, isPaused);
    }

    private static void HandleItemSlotHover(On_ItemSlot.orig_MouseHover_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        orig(inv, context, slot);
        InventoryNarrator.RecordFocus(inv, context, slot);
    }

    private static void CaptureNpcChatButtons(On_Main.orig_DrawNPCChatButtons orig, int superColor, Color chatColor, int numLines, string focusText, string focusText3)
    {
        string? primary = focusText;
        string? closeLabel = Lang.inter[52].Value;
        string? secondary = string.IsNullOrWhiteSpace(focusText3) ? null : focusText3;

        string? happiness = null;
        if (!Main.remixWorld)
        {
            int playerIndex = Main.myPlayer;
            if (playerIndex >= 0 && playerIndex < Main.maxPlayers)
            {
                Player? localPlayer = Main.player[playerIndex];
                if (localPlayer is not null && !string.IsNullOrWhiteSpace(localPlayer.currentShoppingSettings.HappinessReport))
                {
                    happiness = Language.GetTextValue("UI.NPCCheckHappiness");
                }
            }
        }

        NpcDialogueNarrator.UpdateButtonLabels(primary, closeLabel, secondary, happiness);
        orig(superColor, chatColor, numLines, focusText, focusText3);
    }

        private void HandleIngameOptionsDraw(On_IngameOptions.orig_Draw orig, Main self, SpriteBatch spriteBatch)
        {
            _ingameSettingsNarrator.PrimeReflection();
            IngameOptionsLabelTracker.BeginFrame();
            orig(self, spriteBatch);
            IngameOptionsLabelTracker.EndFrame();

            if (!Main.gameMenu)
            {
                _ingameSettingsNarrator.Update();
            }
        }

        private static bool CaptureIngameOptionsLeft(On_IngameOptions.orig_DrawLeftSide orig, SpriteBatch spriteBatch, string label, int index, Vector2 anchorPosition, Vector2 offset, float[] scaleArray, float minScale, float maxScale, float scaleSpeed)
        {
            bool result = orig(spriteBatch, label, index, anchorPosition, offset, scaleArray, minScale, maxScale, scaleSpeed);
            IngameOptionsLabelTracker.RecordLeft(index, label);
            return result;
        }

        private static bool CaptureIngameOptionsRight(On_IngameOptions.orig_DrawRightSide orig, SpriteBatch spriteBatch, string label, int index, Vector2 anchorPosition, Vector2 offset, float scale, float lockedScale, Color color)
        {
            bool result = orig(spriteBatch, label, index, anchorPosition, offset, scale, lockedScale, color);
            IngameOptionsLabelTracker.RecordRight(index, label);
            return result;
        }

        private static class IngameOptionsLabelTracker
        {
            private static readonly Dictionary<int, string> LeftLabels = new();
            private static readonly Dictionary<int, int> LeftIndexToCategory = new();
            private static readonly Dictionary<int, string> CategoryLabels = new();
            private static readonly Dictionary<(int category, int optionIndex), string> OptionLabels = new();
            private static bool[] _skipRightSlotSnapshot = Array.Empty<bool>();

            private static FieldInfo? _leftSideCategoryMappingField;
            private static FieldInfo? _skipRightSlotField;
            private static FieldInfo? _categoryField;

            private static readonly Dictionary<int, int> EmptyMapping = new();

            public static void Configure(FieldInfo? leftMapping, FieldInfo? skipField, FieldInfo? categoryField)
            {
                if (leftMapping is not null)
                {
                    _leftSideCategoryMappingField = leftMapping;
                }

                if (skipField is not null)
                {
                    _skipRightSlotField = skipField;
                }

                if (categoryField is not null)
                {
                    _categoryField = categoryField;
                }
            }

            public static void BeginFrame()
            {
                LeftLabels.Clear();
                LeftIndexToCategory.Clear();
                CategoryLabels.Clear();
                OptionLabels.Clear();

                UpdateSkipSnapshot();
            }

            public static void EndFrame()
            {
                UpdateSkipSnapshot();
            }

            public static void RecordLeft(int index, string? label)
            {
                string sanitized = TextSanitizer.Clean(label ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    LeftLabels[index] = sanitized;
                }

                foreach (KeyValuePair<int, int> kvp in GetLeftSideCategoryMapping())
                {
                    if (kvp.Key != index)
                    {
                        continue;
                    }

                    LeftIndexToCategory[index] = kvp.Value;
                    if (!string.IsNullOrWhiteSpace(sanitized))
                    {
                        CategoryLabels[kvp.Value] = sanitized;
                    }
                    break;
                }
            }

        public static void RecordRight(int index, string? label)
        {
            UpdateSkipSnapshot();

            int category = GetCurrentCategory();
            if (category < 0)
            {
                return;
            }

            if ((uint)index < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[index])
            {
                return;
            }

            string sanitized = TextSanitizer.Clean(label ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                OptionLabels[(category, index)] = sanitized;
            }
        }

            public static bool TryGetLeftLabel(int index, out string label)
            {
                return LeftLabels.TryGetValue(index, out label!);
            }

            public static bool TryGetCategoryLabel(int category, out string label)
            {
                return CategoryLabels.TryGetValue(category, out label!);
            }

            public static bool TryMapLeftToCategory(int leftIndex, out int category)
            {
                return LeftIndexToCategory.TryGetValue(leftIndex, out category);
            }

            public static bool TryGetOptionLabel(int category, int optionIndex, out string label)
            {
                return OptionLabels.TryGetValue((category, optionIndex), out label!);
            }

            public static bool IsOptionSkipped(int optionIndex)
            {
                return (uint)optionIndex < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[optionIndex];
            }

            public static IReadOnlyDictionary<int, int> GetLeftMappingSnapshot()
            {
                return LeftIndexToCategory.Count == 0 ? EmptyMapping : new Dictionary<int, int>(LeftIndexToCategory);
            }

            private static void UpdateSkipSnapshot()
            {
                if (_skipRightSlotField?.GetValue(null) is bool[] array)
                {
                    _skipRightSlotSnapshot = array.Length == 0 ? Array.Empty<bool>() : (bool[])array.Clone();
                }
                else
                {
                    _skipRightSlotSnapshot = Array.Empty<bool>();
                }
            }

            private static Dictionary<int, int> GetLeftSideCategoryMapping()
            {
                if (_leftSideCategoryMappingField?.GetValue(null) is Dictionary<int, int> mapping)
                {
                    return mapping;
                }

                return EmptyMapping;
            }

            private static int GetCurrentCategory()
            {
                if (_categoryField?.GetValue(null) is int value)
                {
                    return value;
                }

                return -1;
            }
        }







    private static void HandleItemSlotHoverRef(On_ItemSlot.orig_MouseHover_refItem_int orig, ref Item item, int context)
    {
        orig(ref item, context);
        InventoryNarrator.RecordFocus(item, context);
    }













    // Emits a continuous tone for boss treasure bags until collected.




    private static string ComposeItemLabel(Item item)
    {
        string name = TextSanitizer.Clean(item.AffixName());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(item.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(Lang.GetItemNameValue(item.type));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Item {item.type}";
        }

        var parts = new List<string> { name };

        if (item.stack > 1)
        {
            parts.Add($"stack {item.stack}");
        }

        if (item.favorited)
        {
            parts.Add("favorited");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static class TileDescriptor
    {
        private const int LiquidDescriptorBaseTileType = -1000;

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
                if (tile.LiquidAmount > 0 && TryDescribeLiquid(tile, out tileType, out name))
                {
                    return true;
                }

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

        private static bool TryDescribeLiquid(Tile tile, out int tileType, out string? name)
        {
            tileType = -1;
            name = null;

            int liquidType = tile.LiquidType;
            string? key = liquidType switch
            {
                LiquidID.Water => "Mods.ScreenReaderMod.CursorLiquids.Water",
                LiquidID.Lava => "Mods.ScreenReaderMod.CursorLiquids.Lava",
                LiquidID.Honey => "Mods.ScreenReaderMod.CursorLiquids.Honey",
                _ => null,
            };

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string fallback = liquidType switch
            {
                LiquidID.Water => "Water",
                LiquidID.Lava => "Lava",
                LiquidID.Honey => "Honey",
                _ => string.Empty,
            };

            string localizedName = Language.GetTextValue(key);
            if (string.IsNullOrWhiteSpace(localizedName) || string.Equals(localizedName, key, StringComparison.Ordinal))
            {
                localizedName = fallback;
            }

            if (string.IsNullOrWhiteSpace(localizedName))
            {
                return false;
            }

            tileType = LiquidDescriptorBaseTileType - liquidType;
            name = localizedName;
            return true;
        }
    }

    private static void CaptureMouseText(On_Main.orig_MouseText_string_string_int_byte_int_int_int_int_int_bool orig, Main self, string cursorText, string buffTooltip, int rare, byte diff, int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX, bool noOverride)
    {
        orig(self, cursorText, buffTooltip, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX, noOverride);
        InventoryNarrator.RecordMouseTextSnapshot(string.IsNullOrWhiteSpace(cursorText) ? buffTooltip : cursorText);
    }
}
