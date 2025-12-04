#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Chat;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
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
    private readonly BiomeAnnouncementEmitter _biomeAnnouncementEmitter = new();
    private readonly LockOnNarrator _lockOnNarrator = new();
    private static readonly bool LogNarratorTimings = false;
    private static readonly double TicksToMilliseconds = 1000d / Stopwatch.Frequency;
    private const float ScreenEdgePaddingPixels = 48f;
    private static readonly Dictionary<int, int> _inventoryStacksByType = new();
    private static bool _inventoryInitialized;
    private bool _wasIngameOptionsOpen;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        RegisterHooks();
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        ResetSharedResources();
        UnregisterHooks();
    }

    public override void OnWorldLoad()
    {
        _worldEventNarrator.InitializeFromWorld();
    }

    public override void OnWorldUnload()
    {
        ResetPerWorldResources();
    }

    public override void PostUpdateWorld()
    {
        _worldEventNarrator.Update();
    }

    public override void PostUpdatePlayers()
    {
        TryUpdateNarrators(requirePaused: false);
    }

    private void RegisterHooks()
    {
        On_ItemSlot.MouseHover_ItemArray_int_int += HandleItemSlotHover;
        On_ItemSlot.MouseHover_refItem_int += HandleItemSlotHoverRef;
        On_Main.DrawNPCChatButtons += CaptureNpcChatButtons;
        On_Main.NewText_string_byte_byte_byte += HandleNewText;
        On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool += CaptureMouseText;
        On_ChestUI.RenameChest += HandleChestRename;
        On_IngameOptions.Draw += HandleIngameOptionsDraw;
        On_IngameOptions.DrawLeftSide += CaptureIngameOptionsLeft;
        On_IngameOptions.DrawRightSide += CaptureIngameOptionsRight;
        On_ChatHelper.BroadcastChatMessage += HandleBroadcastChatMessage;
        On_RemadeChatMonitor.AddNewMessage += HandleChatMonitorMessage;
    }

    private void UnregisterHooks()
    {
        On_ItemSlot.MouseHover_ItemArray_int_int -= HandleItemSlotHover;
        On_ItemSlot.MouseHover_refItem_int -= HandleItemSlotHoverRef;
        On_Main.DrawNPCChatButtons -= CaptureNpcChatButtons;
        On_Main.NewText_string_byte_byte_byte -= HandleNewText;
        On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool -= CaptureMouseText;
        On_ChestUI.RenameChest -= HandleChestRename;
        On_IngameOptions.Draw -= HandleIngameOptionsDraw;
        On_IngameOptions.DrawLeftSide -= CaptureIngameOptionsLeft;
        On_IngameOptions.DrawRightSide -= CaptureIngameOptionsRight;
        On_ChatHelper.BroadcastChatMessage -= HandleBroadcastChatMessage;
        On_RemadeChatMonitor.AddNewMessage -= HandleChatMonitorMessage;
    }

    private void ResetSharedResources()
    {
        ResetPerWorldResources();
        CursorNarrator.DisposeStaticResources();
        TreasureBagBeaconEmitter.DisposeStaticResources();
        FootstepToneProvider.DisposeStaticResources();
        WorldInteractableTracker.DisposeStaticResources();
        HostileStaticAudioEmitter.DisposeStaticResources();
    }

    private void ResetPerWorldResources()
    {
        _worldEventNarrator.Reset();
        _treasureBagBeaconEmitter.Reset();
        _hostileStaticAudioEmitter.Reset();
        _footstepAudioEmitter.Reset();
        _worldInteractableTracker.Reset();
        _biomeAnnouncementEmitter.Reset();
        _inventoryStacksByType.Clear();
        _inventoryInitialized = false;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        TryUpdateNarrators(requirePaused: true);
    }

    private long _timingScratch;

    private void TryUpdateNarrators(bool requirePaused)
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        bool isPaused = Main.gamePaused;
        SynchronizeIngameOptionsState();

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

        DetectInventoryGains(player);

        _timingScratch = StartTiming();
        _hotbarNarrator.Update(player);
        LogDuration("Hotbar", ref _timingScratch);
        _inventoryNarrator.Update(player);
        LogDuration("Inventory", ref _timingScratch);
        _craftingNarrator.Update(player);
        LogDuration("Crafting", ref _timingScratch);
        _smartCursorNarrator.Update();
        LogDuration("SmartCursor", ref _timingScratch);
        _cursorNarrator.Update();
        LogDuration("Cursor", ref _timingScratch);
        _lockOnNarrator.Update();
        LogDuration("LockOn", ref _timingScratch);
        if (!isPaused)
        {
            _treasureBagBeaconEmitter.Update(player);
            LogDuration("TreasureBagBeacon", ref _timingScratch);
            _hostileStaticAudioEmitter.Update(player);
            LogDuration("HostileStaticAudio", ref _timingScratch);
            _footstepAudioEmitter.Update(player);
            LogDuration("FootstepAudio", ref _timingScratch);
            _worldInteractableTracker.Update(player, GuidanceSystem.IsExplorationTrackingEnabled);
            LogDuration("WorldInteractables", ref _timingScratch);
        _biomeAnnouncementEmitter.Update(player);
        LogDuration("BiomeAnnouncement", ref _timingScratch);
        }

        _npcDialogueNarrator.Update(player);
        LogDuration("NpcDialogue", ref _timingScratch);
        _controlsMenuNarrator.Update(isPaused);
        LogDuration("ControlsMenu", ref _timingScratch);
        _modConfigMenuNarrator.TryHandleIngameUi(Main.InGameUI, isPaused);
        LogDuration("ModConfigMenu", ref _timingScratch);
    }

    private void SynchronizeIngameOptionsState()
    {
        bool open = Main.ingameOptionsWindow;
        if (open == _wasIngameOptionsOpen)
        {
            return;
        }

        if (open)
        {
            ScreenReaderService.Interrupt();
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Settings);
            _inventoryNarrator.ForceReset();
            _craftingNarrator.ForceReset();
            _ingameSettingsNarrator.OnMenuOpened();
        }
        else
        {
            UiAreaNarrationContext.Clear();
            _inventoryNarrator.ForceReset();
            _craftingNarrator.ForceReset();
            _ingameSettingsNarrator.OnMenuClosed();
        }

        _wasIngameOptionsOpen = open;
    }

    private static bool IsWorldPositionApproximatelyOnScreen(Vector2 worldPosition, float paddingPixels = ScreenEdgePaddingPixels)
    {
        float zoomX = Math.Abs(Main.GameViewMatrix.Zoom.X) < 0.001f ? 1f : Main.GameViewMatrix.Zoom.X;
        float zoomY = Math.Abs(Main.GameViewMatrix.Zoom.Y) < 0.001f ? zoomX : Main.GameViewMatrix.Zoom.Y;
        float zoom = Math.Max(0.001f, Math.Min(zoomX, zoomY));

        float viewWidth = Main.screenWidth / zoom;
        float viewHeight = Main.screenHeight / zoom;
        Vector2 topLeft = Main.screenPosition;

        float left = topLeft.X - paddingPixels;
        float top = topLeft.Y - paddingPixels;
        float right = left + viewWidth + paddingPixels * 2f;
        float bottom = top + viewHeight + paddingPixels * 2f;

        return worldPosition.X >= left && worldPosition.X <= right &&
               worldPosition.Y >= top && worldPosition.Y <= bottom;
    }

    private static void HandleItemSlotHover(On_ItemSlot.orig_MouseHover_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        orig(inv, context, slot);
        InventoryNarrator.RecordFocus(inv, context, slot);
    }

    private static long StartTiming()
    {
        if (!LogNarratorTimings)
        {
            return 0;
        }

        return Stopwatch.GetTimestamp();
    }

    private static void LogDuration(string name, ref long startTicks)
    {
        if (!LogNarratorTimings)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - startTicks) * TicksToMilliseconds;
        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[NarratorTiming] {name}: {elapsedMs:0.###} ms");
        startTicks = now;
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

    private static void HandleChestRename(On_ChestUI.orig_RenameChest orig)
    {
        bool wasEditing = Main.editChest;
        orig();

        if (wasEditing || !Main.editChest)
        {
            return;
        }

        ScreenReaderService.Announce("Type the new chest name, then press Enter to save or Escape to cancel.", force: true);
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

        internal static class IngameOptionsLabelTracker
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

            public static bool TryGetCurrentOptionLabel(int optionIndex, out string label)
            {
                int category = GetCurrentCategory();
                if (category < 0)
                {
                    label = string.Empty;
                    return false;
                }

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

            public static int GetCurrentCategory()
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

    private static void HandleNewText(On_Main.orig_NewText_string_byte_byte_byte orig, string newText, byte r, byte g, byte b)
    {
        orig(newText, r, g, b);
        TryAnnounceWorldText(newText);
        TryAnnounceHousingQuery(newText, new Color(r, g, b));
    }

    private static void HandleBroadcastChatMessage(On_ChatHelper.orig_BroadcastChatMessage orig, NetworkText text, Color color, int excludedPlayer)
    {
        orig(text, color, excludedPlayer);
        string message = text.ToString();
        TryAnnounceWorldText(message);
        TryAnnounceHousingQuery(message, color);
    }

    private static void HandleChatMonitorMessage(On_RemadeChatMonitor.orig_AddNewMessage orig, RemadeChatMonitor self, string text, Color color, int widthLimitInPixels)
    {
        orig(self, text, color, widthLimitInPixels);
        TryAnnounceChatLine(text);
    }

    private static void TryAnnounceWorldText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (string.IsNullOrWhiteSpace(sanitized) || IsLikelyPlayerChat(sanitized))
        {
            return;
        }

        WorldAnnouncementService.Announce(sanitized, force: true);
    }

    private static void TryAnnounceChatLine(string? rawText)
    {
        string sanitized = TextSanitizer.Clean(rawText);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        ScreenReaderService.Announce(sanitized);
    }

    private static bool IsLikelyPlayerChat(string text)
    {
        return text.Contains(": ", StringComparison.Ordinal);
    }

    private static readonly Lazy<HashSet<string>> HousingQueryPhrases = new(BuildHousingQueryPhraseSet);
    private static readonly Color HousingQueryTextColor = new(255, 240, 20);

    private static HashSet<string> BuildHousingQueryPhraseSet()
    {
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSanitizedIfPresent(phrases, GetLangInterValue(39));
        AddSanitizedIfPresent(phrases, GetLangInterValue(41));
        AddSanitizedIfPresent(phrases, GetLangInterValue(42));

        for (int i = 0; i <= 120; i++)
        {
            string key = $"TownNPCHousingFailureReasons.{i}";
            string value = Language.GetTextValue(key);
            if (!string.Equals(value, key, StringComparison.Ordinal))
            {
                AddSanitizedIfPresent(phrases, value);
            }
        }

        return phrases;
    }

    private static string? GetLangInterValue(int index)
    {
        if (index < 0 || index >= Lang.inter.Length)
        {
            return null;
        }

        return Lang.inter[index].Value;
    }

    private static void AddSanitizedIfPresent(ISet<string> target, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            target.Add(sanitized);
        }
    }

    private static void TryAnnounceHousingQuery(string? text, Color? color = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!Main.playerInventory)
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        bool matchesHousingColor = color.HasValue &&
                                   color.Value.R == HousingQueryTextColor.R &&
                                   color.Value.G == HousingQueryTextColor.G &&
                                   color.Value.B == HousingQueryTextColor.B;

        if (matchesHousingColor ||
            HousingQueryPhrases.Value.Contains(sanitized) ||
            sanitized.Contains("housing", StringComparison.OrdinalIgnoreCase))
        {
            ScreenReaderService.Announce(sanitized, force: true);
        }
    }













    private static void DetectInventoryGains(Player player)
    {
        if (player.inventory is null)
        {
            return;
        }

        var currentTotals = new Dictionary<int, int>(_inventoryStacksByType.Count);
        foreach (Item item in player.inventory)
        {
            if (item is null || item.IsAir || item.type <= 0 || item.stack <= 0)
            {
                continue;
            }

            if (currentTotals.TryGetValue(item.type, out int existing))
            {
                currentTotals[item.type] = existing + item.stack;
            }
            else
            {
                currentTotals[item.type] = item.stack;
            }
        }

        if (!_inventoryInitialized)
        {
            foreach ((int itemType, int stack) in currentTotals)
            {
                _inventoryStacksByType[itemType] = stack;
            }

            _inventoryInitialized = true;
            return;
        }

        foreach ((int itemType, int currentStack) in currentTotals)
        {
            _inventoryStacksByType.TryGetValue(itemType, out int previousStack);
            if (currentStack <= previousStack)
            {
                continue;
            }

            Item? template = FindInventoryItem(player, itemType);
            if (template is null)
            {
                continue;
            }

            Item announcementItem = template.Clone();
            int stackDelta = currentStack - previousStack;
            announcementItem.stack = stackDelta;

            string label = ComposeItemLabel(announcementItem, includeCountWhenSingular: true);
            ScreenReaderService.Announce(
                $"Picked up {label}",
                category: ScreenReaderService.AnnouncementCategory.Pickup);
        }

        _inventoryStacksByType.Clear();
        foreach ((int itemType, int stack) in currentTotals)
        {
            _inventoryStacksByType[itemType] = stack;
        }

        _inventoryInitialized = true;
    }

    private static Item? FindInventoryItem(Player player, int type)
    {
        foreach (Item item in player.inventory)
        {
            if (item is not null && !item.IsAir && item.type == type)
            {
                return item;
            }
        }

        return null;
    }

    internal static string ComposeItemLabel(Item item, bool includeCountWhenSingular = false)
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

        string mainLabel = item.stack > 1 || includeCountWhenSingular ? $"{item.stack} {name}" : name;
        var parts = new List<string> { mainLabel };

        if (item.favorited)
        {
            parts.Add("favorited");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static class TileDescriptor
    {
        private const int LiquidDescriptorBaseTileType = -1000;
        private const int WallDescriptorBaseTileType = -2000;
        private static readonly int[] StyledTileTypes =
        {
            TileID.Banners,
            TileID.Statues,
            TileID.AlphabetStatues,
            TileID.MushroomStatue,
            TileID.BoulderStatue,
            TileID.Painting2X3,
            TileID.Painting3X2,
            TileID.Painting3X3,
            TileID.Painting4X3,
            TileID.Painting6X4,
        };
        private static readonly Dictionary<int, Dictionary<int, int>> TileStyleToItemType = BuildTileStyleMap();
        private static readonly Dictionary<int, int> WallTypeToItemType = BuildWallItemMap();

        public static ScreenReaderService.AnnouncementCategory GetAnnouncementCategory(int tileType)
        {
            if (tileType <= WallDescriptorBaseTileType)
            {
                return ScreenReaderService.AnnouncementCategory.Wall;
            }

            return ScreenReaderService.AnnouncementCategory.Tile;
        }

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

                if (TryDescribeWall(tile, out tileType, out name))
                {
                    return true;
                }

                // Treat open air as a narratable target so cursor navigation isn't silent.
                name = "Empty";
                tileType = -1;
                return true;
            }

            tileType = tile.TileType;

            if (tileType == TileID.Banners && TryDescribeBanner(tile, out name))
            {
                return true;
            }

            if (tileType != TileID.Banners && TryDescribeTileFromItemPlacement(tile, tileType, out name))
            {
                return true;
            }

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

            OverrideChestName(tileX, tileY, tileType, ref name);

            return true;
        }

        private static void OverrideChestName(int tileX, int tileY, int tileType, ref string? name)
        {
            if (!IsChestTile(tileType))
            {
                return;
            }

            int chestIndex = Chest.FindChestByGuessing(tileX, tileY);
            if (chestIndex < 0 || chestIndex >= Main.chest.Length)
            {
                return;
            }

            Chest? chest = Main.chest[chestIndex];
            if (chest is null)
            {
                return;
            }

            string sanitized = TextSanitizer.Clean(chest.name);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Chest";
            }

            name = $"\"{sanitized}\" {name}";
        }

        private static bool IsChestTile(int tileType)
        {
            return tileType == TileID.Containers ||
                   tileType == TileID.Containers2 ||
                   tileType == TileID.Dressers;
        }

        private static bool TryDescribeBanner(Tile tile, out string? name)
        {
            name = null;

            int style = TileObjectData.GetTileStyle(tile);
            if (style < 0)
            {
                return false;
            }

            int itemType = ResolveBannerItemType(style);
            if (itemType > ItemID.None)
            {
                string itemName = Lang.GetItemNameValue(itemType);
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    name = itemName;
                    return true;
                }
            }

            int npcType = Item.BannerToNPC(style);
            if (npcType > NPCID.None)
            {
                string npcName = Lang.GetNPCNameValue(npcType);
                if (!string.IsNullOrWhiteSpace(npcName))
                {
                    name = $"{npcName} banner";
                    return true;
                }
            }

            return false;
        }

        private static bool TryDescribeTileFromItemPlacement(Tile tile, int tileType, out string? name)
        {
            name = null;

            int style = TileObjectData.GetTileStyle(tile);
            if (style < 0)
            {
                return false;
            }

            if (!TryResolveStyleItemType(tileType, style, out int itemType) || itemType <= ItemID.None)
            {
                return false;
            }

            string itemName = Lang.GetItemNameValue(itemType);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return false;
            }

            name = itemName;
            return true;
        }

        private static bool TryResolveStyleItemType(int tileType, int style, out int itemType)
        {
            itemType = ItemID.None;
            if (!TileStyleToItemType.TryGetValue(tileType, out Dictionary<int, int>? map))
            {
                return false;
            }

            return map.TryGetValue(Math.Max(0, style), out itemType);
        }

        private static int ResolveBannerItemType(int style)
        {
            if (TryResolveStyleItemType(TileID.Banners, style, out int itemType))
            {
                return itemType;
            }

            int fallback = Item.BannerToItem(style);
            if (fallback > ItemID.None)
            {
                if (!TileStyleToItemType.TryGetValue(TileID.Banners, out Dictionary<int, int>? map))
                {
                    map = new Dictionary<int, int>();
                    TileStyleToItemType[TileID.Banners] = map;
                }

                map[Math.Max(0, style)] = fallback;
            }

            return fallback;
        }

        private static Dictionary<int, Dictionary<int, int>> BuildTileStyleMap()
        {
            Dictionary<int, Dictionary<int, int>> map = new();
            HashSet<int> explicitTargets = new(StyledTileTypes);
            Item scratch = new();
            for (int type = 1; type < ItemLoader.ItemCount; type++)
            {
                try
                {
                    scratch.SetDefaults(type, true);
                }
                catch
                {
                    continue;
                }

                int tileType = scratch.createTile;
                if (tileType < 0)
                {
                    continue;
                }

                bool shouldTrack = explicitTargets.Contains(tileType);
                if (!shouldTrack && tileType < TileID.Sets.Platforms.Length && TileID.Sets.Platforms[tileType])
                {
                    shouldTrack = true;
                }

                if (!shouldTrack)
                {
                    continue;
                }

                if (!map.TryGetValue(tileType, out Dictionary<int, int>? styleMap))
                {
                    styleMap = new Dictionary<int, int>();
                    map[tileType] = styleMap;
                }

                int style = Math.Max(0, scratch.placeStyle);
                styleMap[style] = type;
            }

            return map;
        }

        private static Dictionary<int, int> BuildWallItemMap()
        {
            Dictionary<int, int> map = new();
            Item scratch = new();
            for (int type = 1; type < ItemLoader.ItemCount; type++)
            {
                try
                {
                    scratch.SetDefaults(type, true);
                }
                catch
                {
                    continue;
                }

                int wallType = scratch.createWall;
                if (wallType <= WallID.None)
                {
                    continue;
                }

                if (!map.ContainsKey(wallType))
                {
                    map[wallType] = type;
                }
            }

            return map;
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

        private static bool TryDescribeWall(Tile tile, out int tileType, out string? name)
        {
            tileType = -1;
            name = null;

            int wallType = tile.WallType;
            if (wallType <= WallID.None)
            {
                return false;
            }

            if (WallTypeToItemType.TryGetValue(wallType, out int itemType) && itemType > ItemID.None)
            {
                string itemName = Lang.GetItemNameValue(itemType);
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    name = itemName;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = WallID.Search.GetName(wallType);
                name = TextSanitizer.Clean(name);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                name = StripUnsafeDescriptor(name);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Wall {wallType}";
            }

            tileType = WallDescriptorBaseTileType - wallType;
            return true;
        }

        private static string StripUnsafeDescriptor(string name)
        {
            string cleaned = name.Trim();

            const string prefix = "Unsafe ";
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..].TrimStart();
            }

            const string suffix = " unsafe";
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length].TrimEnd();
            }

            return string.IsNullOrWhiteSpace(cleaned) ? name : cleaned;
        }
    }

    private static void CaptureMouseText(On_Main.orig_MouseText_string_string_int_byte_int_int_int_int_int_bool orig, Main self, string cursorText, string buffTooltip, int rare, byte diff, int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX, bool noOverride)
    {
        orig(self, cursorText, buffTooltip, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX, noOverride);
        InventoryNarrator.RecordMouseTextSnapshot(string.IsNullOrWhiteSpace(cursorText) ? buffTooltip : cursorText);
    }
}



