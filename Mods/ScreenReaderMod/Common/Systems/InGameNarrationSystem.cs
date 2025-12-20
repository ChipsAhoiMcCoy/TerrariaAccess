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
    private readonly HotbarNarrator _hotbarNarrator;
    private readonly SmartCursorNarrator _smartCursorNarrator;
    private readonly CraftingNarrator _craftingNarrator;
    private readonly CursorNarrator _cursorNarrator;
    private readonly TreasureBagBeaconEmitter _treasureBagBeaconEmitter;
    private readonly HostileStaticAudioEmitter _hostileStaticAudioEmitter;
    private readonly WorldInteractableTracker _worldInteractableTracker;
    private readonly InventoryNarrator _inventoryNarrator;
    private readonly NpcDialogueNarrator _npcDialogueNarrator;
    private readonly IngameSettingsNarrator _ingameSettingsNarrator;
    private readonly ControlsMenuNarrator _controlsMenuNarrator;
    private readonly ModConfigMenuNarrator _modConfigMenuNarrator;
    private readonly FootstepAudioEmitter _footstepAudioEmitter;
    private readonly ClimbAudioEmitter _climbAudioEmitter;
    private readonly BiomeAnnouncementEmitter _biomeAnnouncementEmitter;
    private readonly WorldPositionalAudioService _worldPositionalAudioService;
    private readonly LockOnNarrator _lockOnNarrator;
    private readonly ChatInputNarrator _chatInputNarrator;
    private readonly CursorDescriptorService _cursorDescriptorService;
    private static CursorDescriptorService? _sharedCursorDescriptorService;
    private readonly INarrationScheduler _narrationScheduler;
    private readonly INarrationService _hotbarNarrationService;
    private readonly INarrationService _inventoryNarrationService;
    private readonly INarrationService _craftingGuideReforgeNarrationService;
    private readonly INarrationService _cursorNarrationService;
    private readonly INarrationService _npcDialogueNarrationService;
    private readonly INarrationService _settingsControlsNarrationService;
    private readonly INarrationService _lockOnNarrationService;
    private readonly INarrationService _worldAudioNarrationService;
    private readonly INarrationService _interactableTrackerNarrationService;
    private readonly INarrationService _chatInputNarrationService;
    private static readonly bool SchedulerTraceOnly = NarrationSchedulerSettings.IsTraceOnlyEnabled();
    private const float ScreenEdgePaddingPixels = 48f;
    private static readonly TimeSpan ChatRepeatWindow = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PickupRepeatWindow = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan LowLightAnnouncementCooldown = TimeSpan.FromSeconds(8);
    private const float LowLightEnterBrightness = 0.22f;
    private const float LowLightExitBrightness = 0.28f;
    private static readonly string[] BlockedStatusPhrasesWhileInWorld =
    {
        "receiving tile data",
        "saving map data",
        "saving world data",
        "saving modded world data",
        "validating world save",
        "backing up player",
        "backing up world",
    };
    private static readonly string[] BlockedStatusPhrases =
    {
        "please start a new instance of terraria to join",
        "please start a new instance of terraria to host",
    };
    private static string? _lastChatAnnouncement;
    private static DateTime _lastChatAnnouncedAt;
    private static string? _lastPopupAnnouncement;
    private static DateTime _lastPopupAnnouncedAt;
    private static string? _lastChatMonitorAnnouncement;
    private static bool[] _popupActiveSnapshot = Array.Empty<bool>();
    private static string?[] _popupTextSnapshot = Array.Empty<string?>();
    private static FieldInfo? _remadeChatMessagesField;
    private static FieldInfo? _legacyChatLinesField;
    private readonly Dictionary<int, int> _inventoryStacksByType = new();
    private bool _inventoryInitialized;
    private bool _wasIngameOptionsOpen;
    private string? _lastStatusAnnouncement;
    private NarrationInstrumentation? _instrumentation;
    private readonly Dictionary<int, DateTime> _lastPickupAnnouncedAt = new();
    private DateTime _lastLowLightAnnouncementAt = DateTime.MinValue;
    private bool _inLowLight;

    internal static CursorDescriptorService CursorDescriptors => _sharedCursorDescriptorService ??= new CursorDescriptorService();

    public InGameNarrationSystem()
    {
        _hotbarNarrator = new HotbarNarrator();
        _cursorDescriptorService = new CursorDescriptorService();
        _smartCursorNarrator = new SmartCursorNarrator(_cursorDescriptorService);
        _craftingNarrator = new CraftingNarrator();
        _cursorNarrator = new CursorNarrator(_cursorDescriptorService);
        _treasureBagBeaconEmitter = new TreasureBagBeaconEmitter();
        _hostileStaticAudioEmitter = new HostileStaticAudioEmitter();
        _worldInteractableTracker = new WorldInteractableTracker();
        _inventoryNarrator = new InventoryNarrator();
        _npcDialogueNarrator = new NpcDialogueNarrator();
        _ingameSettingsNarrator = new IngameSettingsNarrator();
        _controlsMenuNarrator = new ControlsMenuNarrator();
        _modConfigMenuNarrator = new ModConfigMenuNarrator();
        _footstepAudioEmitter = new FootstepAudioEmitter();
        _climbAudioEmitter = new ClimbAudioEmitter();
        _biomeAnnouncementEmitter = new BiomeAnnouncementEmitter();
        _worldPositionalAudioService = new WorldPositionalAudioService(
            _treasureBagBeaconEmitter,
            _hostileStaticAudioEmitter,
            _footstepAudioEmitter,
            _climbAudioEmitter,
            _biomeAnnouncementEmitter);
        _lockOnNarrator = new LockOnNarrator();
        _chatInputNarrator = new ChatInputNarrator();
        _narrationScheduler = new NarrationScheduler();
        _sharedCursorDescriptorService = _cursorDescriptorService;

        _hotbarNarrationService = new DelegatedNarrationService(
            "Hotbar",
            ctx =>
            {
                // Suppress hotbar callouts when an in-game UI (like settings) is open to avoid random item chatter.
                if (ctx.Runtime.InGameUiOpen)
                {
                    return;
                }

                _hotbarNarrator.Update(ctx.Player);
            });
        _inventoryNarrationService = new DelegatedNarrationService("Inventory", ctx => _inventoryNarrator.Update(ctx.Player));
        _craftingGuideReforgeNarrationService = new DelegatedNarrationService("CraftingGuideReforge", ctx => _craftingNarrator.Update(ctx.Player));
        _cursorNarrationService = new DelegatedNarrationService(
            "CursorAndSmartCursor",
            ctx =>
            {
                _smartCursorNarrator.Update();
                _cursorNarrator.Update();
            });
        _npcDialogueNarrationService = new DelegatedNarrationService("NpcDialogue", ctx => _npcDialogueNarrator.Update(ctx));
        _settingsControlsNarrationService = new DelegatedNarrationService("SettingsAndControls", ctx => _controlsMenuNarrator.Update(ctx.IsPaused));
        _lockOnNarrationService = new DelegatedNarrationService("LockOn", _ => _lockOnNarrator.Update());
        _worldAudioNarrationService = new DelegatedNarrationService(
            "WorldAudio",
            ctx =>
            {
                _worldPositionalAudioService.Update(ctx);
            },
            "biome/footstep/hostile static/treasure beacon");
        _interactableTrackerNarrationService = new DelegatedNarrationService(
            "InteractableTracker",
            ctx => _worldInteractableTracker.Update(ctx.Player, GuidanceSystem.IsExplorationTrackingEnabled));
        _chatInputNarrationService = new DelegatedNarrationService(
            "ChatInput",
            ctx => _chatInputNarrator.Update(ctx));
    }

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        RegisterHooks();
        ConfigureNarrationScheduler();
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        _narrationScheduler.Clear();
        ResetSharedResources();
        UnregisterHooks();
    }

    public override void OnWorldLoad()
    {
    }

    public override void OnWorldUnload()
    {
        ResetPerWorldResources();
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
        On_Main.NewText_object_Nullable1 += HandleNewTextObject;
        On_Main.NewTextMultiline += HandleNewTextMultiline;
        On_PopupText.NewText_AdvancedPopupRequest_Vector2 += HandlePopupTextAdvanced;
        On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool += CaptureMouseText;
        On_ChestUI.RenameChest += HandleChestRename;
        On_IngameOptions.Draw += HandleIngameOptionsDraw;
        On_IngameOptions.DrawLeftSide += CaptureIngameOptionsLeft;
        On_IngameOptions.DrawRightSide += CaptureIngameOptionsRight;
    }

    private void ConfigureNarrationScheduler()
    {
        _narrationScheduler.Clear();
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _hotbarNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.Default,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _inventoryNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.Default,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _craftingGuideReforgeNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.Default,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _cursorNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.Tile,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _npcDialogueNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.World,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _settingsControlsNarrationService,
            new NarrationServiceGating
            {
                Category = ScreenReaderService.AnnouncementCategory.Default,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _lockOnNarrationService,
            new NarrationServiceGating
            {
                SkipWhenPaused = true,
                Category = ScreenReaderService.AnnouncementCategory.World,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _worldAudioNarrationService,
            new NarrationServiceGating
            {
                SkipWhenPaused = true,
                Category = ScreenReaderService.AnnouncementCategory.World,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _interactableTrackerNarrationService,
            new NarrationServiceGating
            {
                SkipWhenPaused = true,
                Category = ScreenReaderService.AnnouncementCategory.World,
            }));
        _narrationScheduler.Register(new NarrationServiceRegistration(
            _chatInputNarrationService,
            new NarrationServiceGating
            {
                SkipWhenPaused = true,
                Category = ScreenReaderService.AnnouncementCategory.Default,
            }));
    }

    private void UnregisterHooks()
    {
        On_ItemSlot.MouseHover_ItemArray_int_int -= HandleItemSlotHover;
        On_ItemSlot.MouseHover_refItem_int -= HandleItemSlotHoverRef;
        On_Main.DrawNPCChatButtons -= CaptureNpcChatButtons;
        On_Main.NewText_string_byte_byte_byte -= HandleNewText;
        On_Main.NewText_object_Nullable1 -= HandleNewTextObject;
        On_Main.NewTextMultiline -= HandleNewTextMultiline;
        On_PopupText.NewText_AdvancedPopupRequest_Vector2 -= HandlePopupTextAdvanced;
        On_Main.MouseText_string_string_int_byte_int_int_int_int_int_bool -= CaptureMouseText;
        On_ChestUI.RenameChest -= HandleChestRename;
        On_IngameOptions.Draw -= HandleIngameOptionsDraw;
        On_IngameOptions.DrawLeftSide -= CaptureIngameOptionsLeft;
        On_IngameOptions.DrawRightSide -= CaptureIngameOptionsRight;
    }

    private void ResetSharedResources()
    {
        ResetPerWorldResources();
        CursorNarrator.DisposeStaticResources();
        _worldPositionalAudioService.ResetStaticResources();
        WorldInteractableTracker.DisposeStaticResources();
    }

    private void ResetPerWorldResources()
    {
        _worldPositionalAudioService.Reset();
        _worldInteractableTracker.Reset();
        _chatInputNarrator.Reset();
        ChatHistoryService.Reset();
        _lastChatAnnouncement = null;
        _lastChatAnnouncedAt = DateTime.MinValue;
        _lastPopupAnnouncement = null;
        _lastPopupAnnouncedAt = DateTime.MinValue;
        _lastChatMonitorAnnouncement = null;
        _popupActiveSnapshot = Array.Empty<bool>();
        _popupTextSnapshot = Array.Empty<string?>();
        _inventoryStacksByType.Clear();
        _lastPickupAnnouncedAt.Clear();
        _inventoryInitialized = false;
        _inLowLight = false;
        _lastLowLightAnnouncementAt = DateTime.MinValue;
        InventoryNarrator.ResetStaticCaches();
    }

    public override void UpdateUI(GameTime gameTime)
    {
        AnnounceStatusTextIfNeeded();
        TryAnnounceChatMonitorFallback();
        TryAnnouncePopupTextInstances();
        TryUpdateNarrators(requirePaused: true);
    }

    private void TryUpdateNarrators(bool requirePaused)
    {
        RuntimeContextSnapshot runtime = RuntimeContext.GetSnapshot();
        if (runtime.IsServer || runtime.InMenu || !runtime.HasActivePlayer)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        SynchronizeIngameOptionsState();
        bool isPaused = runtime.IsPaused;

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

        if (!requirePaused)
        {
            DetectLowLight(player);
        }

        DetectInventoryGains(player);
        RunNarrationScheduler(runtime, player, isPaused, requirePaused);
        _modConfigMenuNarrator.TryHandleIngameUi(Main.InGameUI, isPaused);
    }

    private void DetectLowLight(Player player)
    {
        Vector2 center = player.Center;
        int tileX = (int)(center.X / 16f);
        int tileY = (int)(center.Y / 16f);
        float brightness = Lighting.Brightness(tileX, tileY);

        if (_inLowLight)
        {
            if (brightness >= LowLightExitBrightness)
            {
                _inLowLight = false;
            }
            return;
        }

        if (brightness >= LowLightEnterBrightness)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastLowLightAnnouncementAt < LowLightAnnouncementCooldown)
        {
            return;
        }

        _inLowLight = true;
        _lastLowLightAnnouncementAt = now;
        ScreenReaderService.Announce("It is dark");
    }

    private void AnnounceStatusTextIfNeeded()
    {
        RuntimeContextSnapshot runtime = RuntimeContext.GetSnapshot();
        string raw = Main.statusText ?? string.Empty;
        string sanitized = TextSanitizer.Clean(raw);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            _lastStatusAnnouncement = null;
            return;
        }

        string lower = sanitized.ToLowerInvariant();
        if (runtime.WorldActive)
        {
            foreach (string phrase in BlockedStatusPhrasesWhileInWorld)
            {
                if (lower.Contains(phrase))
                {
                    return;
                }
            }
        }

        foreach (string phrase in BlockedStatusPhrases)
        {
            if (lower.Contains(phrase))
            {
                _lastStatusAnnouncement = sanitized;
                return;
            }
        }

        if (string.Equals(_lastStatusAnnouncement, sanitized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastStatusAnnouncement = sanitized;
        ScreenReaderService.Announce(sanitized, force: true);
    }

    private void RunNarrationScheduler(RuntimeContextSnapshot runtime, Player player, bool isPaused, bool requirePaused)
    {
        NarrationSchedulerContext context = new(
            runtime,
            player,
            isPaused,
            requirePaused,
            ScreenReaderDiagnostics.IsTraceEnabled(),
            SchedulerTraceOnly,
            GetOrCreateInstrumentation());
        _narrationScheduler.Update(context);
    }

    private NarrationInstrumentation GetOrCreateInstrumentation()
    {
        _instrumentation ??= new NarrationInstrumentation();
        return _instrumentation;
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
            string lower = sanitized.ToLowerInvariant();
            if (Main.gameMenu && (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("tmodloader")))
            {
                return;
            }

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

    private static void HandleNewTextObject(On_Main.orig_NewText_object_Nullable1 orig, object newText, Color? color)
    {
        orig(newText, color);

        if (newText is string)
        {
            // The string overload already announces; avoid duplicates if it routes through this method.
            return;
        }

        string message = newText?.ToString() ?? string.Empty;
        Color resolvedColor = color ?? new Color(255, 255, 255);
        TryAnnounceWorldText(message);
        TryAnnounceHousingQuery(message, resolvedColor);
    }

    private static void HandleNewTextMultiline(On_Main.orig_NewTextMultiline orig, string text, bool force, Color c, int WidthLimit)
    {
        orig(text, force, c, WidthLimit);
        if (TryAnnounceChatMultiline(text, c))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        string historyEntry = FormatChatHistoryEntry(sanitized);
        ChatHistoryService.Record(historyEntry);
        AnnounceChatLine(sanitized);
        TryAnnounceHousingQuery(sanitized, c);
    }

    private static int HandlePopupTextAdvanced(On_PopupText.orig_NewText_AdvancedPopupRequest_Vector2 orig, AdvancedPopupRequest request, Vector2 position)
    {
        return orig(request, position);
    }

    private static void TryAnnounceWorldText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(text);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        string historyEntry = FormatChatHistoryEntry(sanitized);
        ChatHistoryService.Record(historyEntry);
        AnnounceChatLine(sanitized);
    }

    private static void TryAnnounceChatMonitorFallback()
    {
        string? raw = TryGetLatestChatMonitorMessage();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string sanitized;
        if (ChatLineParser.TryParseLeadingNameTagChat(raw, out string playerName, out string message))
        {
            sanitized = ChatLineParser.FormatNameMessage(playerName, message);
        }
        else
        {
            sanitized = TextSanitizer.Clean(raw);
        }
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastChatMonitorAnnouncement) &&
            string.Equals(_lastChatMonitorAnnouncement, sanitized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastChatAnnouncement) &&
            string.Equals(_lastChatAnnouncement, sanitized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastPopupAnnouncement) &&
            string.Equals(_lastPopupAnnouncement, sanitized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastChatMonitorAnnouncement = sanitized;
        ChatHistoryService.Record(sanitized);
        WorldAnnouncementService.Announce(sanitized);
    }

    private static void TryAnnouncePopupTextInstances()
    {
        PopupText[] popups = Main.popupText;
        if (popups is null || popups.Length == 0)
        {
            return;
        }

        if (_popupActiveSnapshot.Length != popups.Length)
        {
            _popupActiveSnapshot = new bool[popups.Length];
            _popupTextSnapshot = new string?[popups.Length];
        }

        for (int i = 0; i < popups.Length; i++)
        {
            PopupText? popup = popups[i];
            if (popup is null)
            {
                _popupActiveSnapshot[i] = false;
                _popupTextSnapshot[i] = null;
                continue;
            }

            if (!popup.active)
            {
                _popupActiveSnapshot[i] = false;
                _popupTextSnapshot[i] = null;
                continue;
            }

            if (popup.context == PopupTextContext.RegularItemPickup ||
                popup.context == PopupTextContext.ItemPickupToVoidContainer)
            {
                _popupActiveSnapshot[i] = true;
                _popupTextSnapshot[i] = null;
                continue;
            }

            string announcement = BuildPopupAnnouncement(popup);
            if (string.IsNullOrWhiteSpace(announcement))
            {
                continue;
            }

            string sanitized = TextSanitizer.Clean(announcement);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            bool wasActive = _popupActiveSnapshot[i];
            string? previousText = _popupTextSnapshot[i];
            if (!wasActive || !string.Equals(previousText, sanitized, StringComparison.OrdinalIgnoreCase))
            {
                _lastPopupAnnouncement = sanitized;
                _lastPopupAnnouncedAt = DateTime.UtcNow;
                WorldAnnouncementService.Announce(sanitized);
            }

            _popupActiveSnapshot[i] = true;
            _popupTextSnapshot[i] = sanitized;
        }
    }

    private static string BuildPopupAnnouncement(PopupText popup)
    {
        if (string.IsNullOrWhiteSpace(popup.name))
        {
            return string.Empty;
        }

        string text = popup.name;
        if (popup.stack > 1)
        {
            text += $" ({popup.stack})";
        }

        return text;
    }

    private static string? TryGetLatestChatMonitorMessage()
    {
        if (Main.chatMonitor is RemadeChatMonitor remade)
        {
            _remadeChatMessagesField ??= typeof(RemadeChatMonitor).GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_remadeChatMessagesField?.GetValue(remade) is List<ChatMessageContainer> messages && messages.Count > 0)
            {
                return messages[0]?.OriginalText;
            }

            return null;
        }

        if (Main.chatMonitor is LegacyChatMonitor legacy)
        {
            _legacyChatLinesField ??= typeof(LegacyChatMonitor).GetField("chatLine", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_legacyChatLinesField?.GetValue(legacy) is ChatLine[] lines && lines.Length > 0)
            {
                ChatLine line = lines[0];
                string text = line.originalText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) || text.StartsWith("this is a hack", StringComparison.OrdinalIgnoreCase))
                {
                    text = FlattenSnippets(line.parsedText);
                }

                return text;
            }
        }

        return null;
    }

    private static string FlattenSnippets(TextSnippet[] snippets)
    {
        if (snippets == null || snippets.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (TextSnippet snippet in snippets)
        {
            if (snippet == null)
            {
                continue;
            }

            builder.Append(snippet.Text);
        }

        return builder.ToString();
    }

    private static bool TryAnnounceChatMultiline(string? rawText, Color color)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        if (ChatLineParser.TryParseLeadingNameTagChat(rawText, out string playerName, out string message))
        {
            string entry = ChatLineParser.FormatNameMessage(playerName, message);
            ChatHistoryService.Record(entry);
            TryAnnounceChatCore(entry, message, null, playerName, "NewTextMultilineNameTag", color);
            return true;
        }

        string sanitized = TextSanitizer.Clean(rawText);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        if (CursorDescriptorService.IsLikelyPlayerChat(sanitized))
        {
            ChatHistoryService.Record(sanitized);
            TryAnnounceChatCore(sanitized, sanitized, null, null, "NewTextMultilineLikelyChat", color);
            return true;
        }

        return false;
    }

    private static string FormatChatHistoryEntry(string sanitized, string? resolvedPlayerName = null)
    {
        if (!string.IsNullOrWhiteSpace(resolvedPlayerName))
        {
            return $"{resolvedPlayerName}: {sanitized}";
        }

        return sanitized;
    }

    private static void TryAnnounceChatCore(string announcement, string raw, byte? author, string? resolvedName, string stage, Color color)
    {
        LogChatDebug(stage, author ?? byte.MaxValue, NetworkText.FromLiteral(raw), announcement, color, resolvedName);
        AnnounceChatLine(announcement);
    }

    private static void LogChatDebug(string stage, byte author, NetworkText text, object? extra, Color color, string? resolvedName = null)
    {
        var logger = ScreenReaderMod.Instance?.Logger;
        if (logger is null || !ScreenReaderDiagnostics.IsTraceEnabled())
        {
            return;
        }

        string raw = text?.ToString() ?? "<null>";
        string message = $"[ChatDebug] stage={stage} author={author} name={resolvedName ?? "<null>"} color=({color.R},{color.G},{color.B}) raw=\"{raw}\" extra={extra ?? "<null>"}";
        logger.Info(message);
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

    private static void AnnounceChatLine(string announcement)
    {
        DateTime now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(_lastChatAnnouncement) &&
            string.Equals(_lastChatAnnouncement, announcement, StringComparison.OrdinalIgnoreCase) &&
            now - _lastChatAnnouncedAt < ChatRepeatWindow)
        {
            return;
        }

        _lastChatAnnouncement = announcement;
        _lastChatAnnouncedAt = now;
        WorldAnnouncementService.Announce(announcement);
    }












    private void DetectInventoryGains(Player player)
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

            DateTime now = DateTime.UtcNow;
            if (_lastPickupAnnouncedAt.TryGetValue(itemType, out DateTime lastAnnounced) &&
                now - lastAnnounced < PickupRepeatWindow)
            {
                _lastPickupAnnouncedAt[itemType] = now;
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

            string label = NarrationTextFormatter.ComposeItemLabel(announcementItem, includeCountWhenSingular: true);
            ScreenReaderService.Announce(
                $"Picked up {label}",
                category: ScreenReaderService.AnnouncementCategory.Pickup);
            _lastPickupAnnouncedAt[itemType] = now;
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

    private static void CaptureMouseText(On_Main.orig_MouseText_string_string_int_byte_int_int_int_int_int_bool orig, Main self, string cursorText, string buffTooltip, int rare, byte diff, int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX, bool noOverride)
    {
        orig(self, cursorText, buffTooltip, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX, noOverride);
        InventoryNarrator.RecordMouseTextSnapshot(string.IsNullOrWhiteSpace(cursorText) ? buffTooltip : cursorText);
    }
}

