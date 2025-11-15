#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Guidance;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems;

public sealed class GuidanceSystem : ModSystem
{
    private const string WaypointListKey = "screenReaderWaypoints";
    private const string SelectedIndexKey = "screenReaderSelectedWaypoint";
    private const string ExplorationModeKey = "screenReaderWaypointExplorationMode";

    internal const float ArrivalTileThreshold = 4f;
    private const int MinPingDelayFrames = 8;
    private const int MaxPingDelayFrames = 54;
    private const float PingDelayScale = 1.35f;
    private const float PitchScale = 320f;
    private const float PanScalePixels = 480f;
    private const float DistanceReferenceTiles = 90f;
    private const float MinVolume = 0.18f;

    private static readonly List<Waypoint> Waypoints = new();
    private enum SelectionMode
    {
        None,
        Exploration,
        Interactable,
        Npc,
        Player,
        Waypoint
    }

    private static SelectionMode _selectionMode = SelectionMode.None;
    private static int _selectedIndex = -1;
    private static int _selectedNpcIndex = -1;
    private static int _selectedPlayerIndex = -1;
    private static int _selectedInteractableIndex = -1;
    private static SelectionMode _categoryAnnouncementMode = SelectionMode.None;
    private static bool _categoryAnnouncementPending;

    private static bool _namingActive;

    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;
    private static SoundEffect? _waypointTone;
    private static readonly List<SoundEffectInstance> ActiveWaypointInstances = new();
    private static UIVirtualKeyboard? _activeKeyboard;
    private static InputSnapshot? _inputSnapshot;

    private struct Waypoint
    {
        public string Name;
        public Vector2 WorldPosition;

        public Waypoint(string name, Vector2 worldPosition)
        {
            Name = name;
            WorldPosition = worldPosition;
        }
    }

    internal static bool IsExplorationTrackingEnabled => _selectionMode == SelectionMode.Exploration;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        GuidanceKeybinds.EnsureInitialized(Mod);
    }

    public override void Unload()
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectionMode = SelectionMode.None;
        ClearCategoryAnnouncement();

        _namingActive = false;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        DisposeToneResources();
        _activeKeyboard = null;
        _inputSnapshot = null;
    }

    public override void OnWorldUnload()
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectionMode = SelectionMode.None;
        ClearCategoryAnnouncement();
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        CloseNamingUi();
        DisposeToneResources();
        _inputSnapshot = null;
        _activeKeyboard = null;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (!_namingActive)
        {
            return;
        }

        UIState? currentState = Main.InGameUI?.CurrentState;
        if (currentState is UIVirtualKeyboard keyboard && ReferenceEquals(keyboard, _activeKeyboard))
        {
            return;
        }

        // The naming UI was dismissed externally (e.g. Escape on keyboard or controller B),
        // so restore the captured input snapshot to avoid leaving the game in a blocked state.
        CloseNamingUi();
    }

    public override void LoadWorldData(TagCompound tag)
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectionMode = SelectionMode.None;
        ClearCategoryAnnouncement();
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;

        if (tag.ContainsKey(WaypointListKey))
        {
            foreach (TagCompound entry in tag.GetList<TagCompound>(WaypointListKey))
            {
                string name = entry.GetString("name");
                float x = entry.GetFloat("x");
                float y = entry.GetFloat("y");
                Waypoints.Add(new Waypoint(name, new Vector2(x, y)));
            }
        }

        if (tag.ContainsKey(SelectedIndexKey))
        {
            _selectedIndex = Math.Clamp(tag.GetInt(SelectedIndexKey), -1, Waypoints.Count - 1);
        }

        bool explorationMode = tag.ContainsKey(ExplorationModeKey) && tag.GetBool(ExplorationModeKey);
        if (explorationMode)
        {
            _selectionMode = SelectionMode.Exploration;
        }
        else if (_selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            _selectionMode = SelectionMode.Waypoint;
        }
        else
        {
            _selectionMode = SelectionMode.None;
            _selectedIndex = -1;
        }

        ClearCategoryAnnouncement();
    }

    public override void SaveWorldData(TagCompound tag)
    {
        if (Waypoints.Count > 0)
        {
            List<TagCompound> serialized = new(Waypoints.Count);
            foreach (Waypoint waypoint in Waypoints)
            {
                serialized.Add(new TagCompound
                {
                    ["name"] = waypoint.Name,
                    ["x"] = waypoint.WorldPosition.X,
                    ["y"] = waypoint.WorldPosition.Y,
                });
            }

            tag[WaypointListKey] = serialized;
        }

        if (_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            tag[SelectedIndexKey] = _selectedIndex;
        }

        if (_selectionMode == SelectionMode.Exploration)
        {
            tag[ExplorationModeKey] = true;
        }
    }

    private readonly struct NpcGuidanceEntry
    {
        public readonly int NpcIndex;
        public readonly string DisplayName;
        public readonly float DistanceTiles;

        public NpcGuidanceEntry(int npcIndex, string displayName, float distanceTiles)
        {
            NpcIndex = npcIndex;
            DisplayName = displayName;
            DistanceTiles = distanceTiles;
        }
    }

    private static readonly List<NpcGuidanceEntry> NearbyNpcs = new();
    private readonly struct InteractableGuidanceEntry
    {
        public readonly Point Anchor;
        public readonly string DisplayName;
        public readonly Vector2 WorldPosition;
        public readonly float DistanceTiles;

        public InteractableGuidanceEntry(Point anchor, string displayName, Vector2 worldPosition, float distanceTiles)
        {
            Anchor = anchor;
            DisplayName = displayName;
            WorldPosition = worldPosition;
            DistanceTiles = distanceTiles;
        }
    }

    private readonly struct InteractableDefinition
    {
        public readonly int TileType;
        public readonly int WidthTiles;
        public readonly int HeightTiles;
        public readonly int FrameWidth;
        public readonly int FrameHeight;
        public readonly string DisplayName;

        public InteractableDefinition(int tileType, int widthTiles, int heightTiles, string displayName)
        {
            TileType = tileType;
            WidthTiles = Math.Max(1, widthTiles);
            HeightTiles = Math.Max(1, heightTiles);
            FrameWidth = WidthTiles * 18;
            FrameHeight = HeightTiles * 18;
            DisplayName = displayName;
        }
    }

    private static readonly List<InteractableGuidanceEntry> NearbyInteractables = new();
    private static readonly HashSet<Point> InteractableAnchorScratch = new();
    private static readonly InteractableDefinition[] InteractableDefinitions =
    {
        new(TileID.WorkBenches, 2, 1, "Work bench"),
        new(TileID.HeavyWorkBench, 3, 2, "Heavy work bench"),
        new(TileID.Anvils, 2, 1, "Anvil"),
        new(TileID.MythrilAnvil, 3, 2, "Mythril or Orichalcum anvil"),
        new(TileID.Furnaces, 3, 2, "Furnace"),
        new(TileID.Hellforge, 3, 2, "Hellforge"),
        new(TileID.AdamantiteForge, 3, 2, "Adamantite or Titanium forge"),
        new(TileID.TinkerersWorkbench, 3, 2, "Tinkerer's workbench"),
        new(TileID.ImbuingStation, 3, 3, "Imbuing station"),
        new(TileID.AlchemyTable, 3, 2, "Alchemy table"),
        new(TileID.Loom, 2, 3, "Loom"),
        new(TileID.Sawmill, 3, 3, "Sawmill"),
        new(TileID.Bottles, 1, 1, "Placed bottle"),
        new(TileID.Tables, 3, 2, "Table"),
        new(TileID.BewitchingTable, 3, 3, "Bewitching table"),
        new(TileID.CrystalBall, 2, 3, "Crystal ball"),
        new(TileID.DemonAltar, 3, 2, "Demon or crimson altar")
    };

    private static readonly Dictionary<int, List<InteractableDefinition>> InteractableDefinitionsByTileType = BuildInteractableDefinitionMap();

    private static Dictionary<int, List<InteractableDefinition>> BuildInteractableDefinitionMap()
    {
        Dictionary<int, List<InteractableDefinition>> map = new();
        foreach (InteractableDefinition definition in InteractableDefinitions)
        {
            if (!map.TryGetValue(definition.TileType, out List<InteractableDefinition>? list))
            {
                list = new List<InteractableDefinition>();
                map[definition.TileType] = list;
            }

            list.Add(definition);
        }

        return map;
    }
    private readonly struct PlayerGuidanceEntry
    {
        public readonly int PlayerIndex;
        public readonly string DisplayName;
        public readonly float DistanceTiles;

        public PlayerGuidanceEntry(int playerIndex, string displayName, float distanceTiles)
        {
            PlayerIndex = playerIndex;
            DisplayName = displayName;
            DistanceTiles = distanceTiles;
        }
    }

    private static readonly List<PlayerGuidanceEntry> NearbyPlayers = new();

    public override void PostUpdatePlayers()
    {
        if (Main.dedServ || Main.gameMenu || _namingActive)
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
        }
        else
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                return;
            }

            if (Main.gamePaused)
            {
                return;
            }

            if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out string arrivalLabel))
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                return;
            }

            float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;
            if (distanceTiles <= ArrivalTileThreshold)
            {
                if (!_arrivalAnnounced && !string.IsNullOrWhiteSpace(arrivalLabel))
                {
                    ScreenReaderService.Announce($"Arrived at {arrivalLabel}");
                }

                _arrivalAnnounced = true;
                _nextPingUpdateFrame = -1;
                return;
            }

            if (_arrivalAnnounced)
            {
                _arrivalAnnounced = false;
            }

            if (_nextPingUpdateFrame < 0)
            {
                _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
            }
            else if (Main.GameUpdateCount >= (uint)_nextPingUpdateFrame)
            {
                EmitPing(player, targetPosition);
                _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
            }
        }

    }

    private static void BeginNaming(Player player)
    {
        if (_namingActive)
        {
            return;
        }

        Vector2 worldPosition = player.Center;
        string fallbackName = BuildDefaultName();
        _nextPingUpdateFrame = -1;

        int playerIndex = player.whoAmI;
        _namingActive = true;

        _inputSnapshot = new InputSnapshot
        {
            BlockInput = Main.blockInput,
            WritingText = PlayerInput.WritingText,
            PlayerInventory = Main.playerInventory,
            EditSign = Main.editSign,
            EditChest = Main.editChest,
            DrawingPlayerChat = Main.drawingPlayerChat,
            InFancyUI = Main.inFancyUI,
            GameMenu = Main.gameMenu,
            ChatText = Main.chatText ?? string.Empty,
            PreviousUiState = Main.InGameUI?.CurrentState
        };

        Main.blockInput = true;
        PlayerInput.WritingText = true;
        Main.clrInput();

        Player? ResolvePlayer()
        {
            if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
            {
                return null;
            }

            Player candidate = Main.player[playerIndex];
            return candidate?.active == true ? candidate : null;
        }

        void FinalizeCreation(string rawInput, string logContext)
        {
            string resolvedName = string.IsNullOrWhiteSpace(rawInput) ? fallbackName : rawInput.Trim();
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[WaypointNaming:{logContext}] Resolved name: \"{resolvedName}\" (input: \"{rawInput}\")");

            Waypoints.Add(new Waypoint(resolvedName, worldPosition));
            _selectedIndex = Waypoints.Count - 1;
            _selectionMode = SelectionMode.Waypoint;

            Player? owner = ResolvePlayer();
            if (owner is not null)
            {
                RescheduleGuidancePing(owner);
                string creationAnnouncement = ComposeCreationAnnouncement(resolvedName, owner, worldPosition);
                ScreenReaderService.Announce(creationAnnouncement);
                EmitPing(owner, worldPosition);
            }
            else
            {
                ScreenReaderService.Announce($"Created waypoint {resolvedName}");
            }

            CloseNamingUi();
        }

        void Submit(string input) => FinalizeCreation(input, "Submit");

        void Cancel()
        {
            Player? owner = ResolvePlayer();
            ScreenReaderService.Announce("Waypoint creation cancelled");
            string discarded = Main.chatText?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(discarded))
            {
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[WaypointNaming:Cancel] Discarded input: \"{discarded}\"");
            }

            if (owner is not null && _selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                RescheduleGuidancePing(owner);
            }

            CloseNamingUi();
        }

        UIVirtualKeyboard keyboard = new("Create Waypoint", string.Empty, Submit, Cancel, 0, true);
        _activeKeyboard = keyboard;
        IngameFancyUI.OpenUIState(keyboard);

        Main.NewText("Waypoint naming: type a name, press Enter to save, or Escape to cancel.", Color.LightSkyBlue);
        ScreenReaderService.Announce("Type the waypoint name, then press Enter to save or Escape to cancel.");
    }

    private static void CloseNamingUi()
    {
        if (!_namingActive)
        {
            return;
        }

        _namingActive = false;
        if (Main.InGameUI is not null)
        {
            if (_activeKeyboard is not null && ReferenceEquals(Main.InGameUI.CurrentState, _activeKeyboard))
            {
                IngameFancyUI.Close();
            }
            else if (Main.InGameUI.CurrentState is UIVirtualKeyboard)
            {
                IngameFancyUI.Close();
            }
        }

        Main.clrInput();

        if (_inputSnapshot is InputSnapshot snapshot)
        {
            PlayerInput.WritingText = snapshot.WritingText;
            Main.blockInput = snapshot.BlockInput;
            Main.playerInventory = snapshot.PlayerInventory;
            Main.editSign = snapshot.EditSign;
            Main.editChest = snapshot.EditChest;
            Main.drawingPlayerChat = snapshot.DrawingPlayerChat;
            Main.inFancyUI = snapshot.InFancyUI;
            Main.gameMenu = snapshot.GameMenu;
            Main.chatText = snapshot.ChatText;

            if (Main.InGameUI is not null)
            {
                if (snapshot.PreviousUiState is null)
                {
                    Main.InGameUI.SetState(null);
                }
                else if (!ReferenceEquals(Main.InGameUI.CurrentState, snapshot.PreviousUiState))
                {
                    Main.InGameUI.SetState(snapshot.PreviousUiState);
                }
            }
        }
        else
        {
            PlayerInput.WritingText = false;
            Main.blockInput = false;
            Main.playerInventory = false;
            Main.editSign = false;
            Main.editChest = false;
            Main.drawingPlayerChat = false;
            Main.inFancyUI = false;
            Main.gameMenu = false;
            Main.chatText = string.Empty;
        }

        _inputSnapshot = null;
        _activeKeyboard = null;
    }

    internal static void HandleKeybinds(Player player)
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        if (player is null || !player.active || player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (_namingActive || Main.InGameUI?.CurrentState is UIVirtualKeyboard)
        {
            return;
        }

        if (GuidanceKeybinds.Create?.JustPressed ?? false)
        {
            BeginNaming(player);
            return;
        }

        if (GuidanceKeybinds.CategoryNext?.JustPressed ?? false)
        {
            CycleCategory(1, player);
            return;
        }

        if (GuidanceKeybinds.CategoryPrevious?.JustPressed ?? false)
        {
            CycleCategory(-1, player);
            return;
        }

        if (GuidanceKeybinds.EntryNext?.JustPressed ?? false)
        {
            CycleCategoryEntry(1, player);
            return;
        }

        if (GuidanceKeybinds.EntryPrevious?.JustPressed ?? false)
        {
            CycleCategoryEntry(-1, player);
            return;
        }

        if (GuidanceKeybinds.Delete?.JustPressed ?? false)
        {
            DeleteSelectedWaypoint(player);
        }
    }

    private static string BuildDefaultName()
    {
        int nextIndex = Waypoints.Count + 1;
        return $"Waypoint {nextIndex}";
    }

    private static readonly SelectionMode[] CategoryOrder =
    {
        SelectionMode.None,
        SelectionMode.Exploration,
        SelectionMode.Interactable,
        SelectionMode.Npc,
        SelectionMode.Player,
        SelectionMode.Waypoint
    };

    private static bool IsCategoryAvailable(SelectionMode category, Player player)
    {
        return category switch
        {
            SelectionMode.Player => Main.netMode != NetmodeID.SinglePlayer && player is not null && player.active,
            _ => true
        };
    }

    private static void CycleCategory(int direction, Player player)
    {
        if (direction == 0)
        {
            direction = 1;
        }

        int currentIndex = Array.IndexOf(CategoryOrder, _selectionMode);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int targetIndex = currentIndex;
        SelectionMode targetCategory = _selectionMode;
        int attempts = 0;
        do
        {
            targetIndex = Modulo(targetIndex + direction, CategoryOrder.Length);
            targetCategory = CategoryOrder[targetIndex];
            attempts++;
        }
        while (!IsCategoryAvailable(targetCategory, player) && attempts <= CategoryOrder.Length);

        if (!IsCategoryAvailable(targetCategory, player))
        {
            return;
        }

        ApplyCategorySelection(targetCategory, player);
    }

    private static void ApplyCategorySelection(SelectionMode category, Player player)
    {
        switch (category)
        {
            case SelectionMode.None:
                _selectionMode = SelectionMode.None;
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                ClearCategoryAnnouncement();
                RescheduleGuidancePing(player);
                AnnounceDisabledSelection();
                return;
            case SelectionMode.Exploration:
                _selectionMode = SelectionMode.Exploration;
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                ClearCategoryAnnouncement();
                RescheduleGuidancePing(player);
                AnnounceExplorationSelection();
                return;
            case SelectionMode.Interactable:
                _selectionMode = SelectionMode.Interactable;
                RefreshInteractableEntries(player);
                if (NearbyInteractables.Count == 0)
                {
                    _selectedInteractableIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Crafting guidance", "No crafting stations detected nearby.");
                    return;
                }

                if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
                {
                    _selectedInteractableIndex = 0;
                }

                BeginCategoryAnnouncement(SelectionMode.Interactable);
                RescheduleGuidancePing(player);
                AnnounceInteractableSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Npc:
                _selectionMode = SelectionMode.Npc;
                RefreshNpcEntries(player);
                if (NearbyNpcs.Count == 0)
                {
                    _selectedNpcIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
                    AnnounceCategorySelection("NPC guidance", $"No nearby NPCs within {rangeTiles} tiles.");
                    return;
                }

                if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
                {
                    _selectedNpcIndex = 0;
                }

                BeginCategoryAnnouncement(SelectionMode.Npc);
                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Player:
                if (Main.netMode == NetmodeID.SinglePlayer)
                {
                    ScreenReaderService.Announce("Player guidance is available only in multiplayer.");
                    return;
                }

                _selectionMode = SelectionMode.Player;
                RefreshPlayerEntries(player);
                if (NearbyPlayers.Count == 0)
                {
                    _selectedPlayerIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Player guidance", "No other active players detected.");
                    return;
                }

                if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
                {
                    _selectedPlayerIndex = 0;
                }

                BeginCategoryAnnouncement(SelectionMode.Player);
                RescheduleGuidancePing(player);
                AnnouncePlayerSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Waypoint:
                _selectionMode = SelectionMode.Waypoint;
                if (Waypoints.Count == 0)
                {
                    _selectedIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Waypoints", "No waypoints saved.");
                    return;
                }

                if (_selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
                {
                    _selectedIndex = 0;
                }

                BeginCategoryAnnouncement(SelectionMode.Waypoint);
                RescheduleGuidancePing(player);
                AnnounceWaypointSelection(player);
                EmitCurrentGuidancePing(player);
                return;
        }
    }

    private static void CycleCategoryEntry(int direction, Player player)
    {
        if (direction == 0)
        {
            direction = 1;
        }

        switch (_selectionMode)
        {
            case SelectionMode.Waypoint:
                if (Waypoints.Count == 0)
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Waypoints", "No waypoints saved.");
                    return;
                }

                if (_selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
                {
                    _selectedIndex = direction > 0 ? 0 : Waypoints.Count - 1;
                }
                else
                {
                    _selectedIndex = Modulo(_selectedIndex + direction, Waypoints.Count);
                }

                RescheduleGuidancePing(player);
                AnnounceWaypointSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Npc:
                RefreshNpcEntries(player);
                if (NearbyNpcs.Count == 0)
                {
                    _selectedNpcIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
                    AnnounceCategorySelection("NPC guidance", $"No NPCs within {rangeTiles} tiles.");
                    return;
                }

                if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
                {
                    _selectedNpcIndex = direction > 0 ? 0 : NearbyNpcs.Count - 1;
                }
                else
                {
                    _selectedNpcIndex = Modulo(_selectedNpcIndex + direction, NearbyNpcs.Count);
                }

                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Interactable:
                RefreshInteractableEntries(player);
                if (NearbyInteractables.Count == 0)
                {
                    _selectedInteractableIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Crafting guidance", "No crafting stations detected nearby.");
                    return;
                }

                if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
                {
                    _selectedInteractableIndex = direction > 0 ? 0 : NearbyInteractables.Count - 1;
                }
                else
                {
                    _selectedInteractableIndex = Modulo(_selectedInteractableIndex + direction, NearbyInteractables.Count);
                }

                RescheduleGuidancePing(player);
                AnnounceInteractableSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Player:
                if (Main.netMode == NetmodeID.SinglePlayer)
                {
                    ScreenReaderService.Announce("Player guidance is available only in multiplayer.");
                    return;
                }

                RefreshPlayerEntries(player);
                if (NearbyPlayers.Count == 0)
                {
                    _selectedPlayerIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Player guidance", "No other active players detected.");
                    return;
                }

                if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
                {
                    _selectedPlayerIndex = direction > 0 ? 0 : NearbyPlayers.Count - 1;
                }
                else
                {
                    _selectedPlayerIndex = Modulo(_selectedPlayerIndex + direction, NearbyPlayers.Count);
                }

                RescheduleGuidancePing(player);
                AnnouncePlayerSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            default:
                ScreenReaderService.Announce("Select a waypoint, player, NPC, or crafting category to browse entries.");
                return;
        }
    }

    private static void AnnounceWaypointSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Waypoint || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            return;
        }

        Waypoint waypoint = Waypoints[_selectedIndex];
        string announcement = ComposeWaypointAnnouncement(waypoint, player);
        AnnounceCategoryEntry(SelectionMode.Waypoint, "Waypoints", announcement);
    }

    private static void AnnounceNpcSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Npc)
        {
            return;
        }

        if (!TryGetSelectedNpc(player, out NPC npc, out NpcGuidanceEntry entry))
        {
            int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("NPC guidance", $"No nearby NPCs within {rangeTiles} tiles.");
            return;
        }

        int totalEntries = NearbyNpcs.Count;
        int position = _selectedNpcIndex + 1;
        string announcement = ComposeNpcAnnouncement(entry, player, npc.Center, position, totalEntries);
        AnnounceCategoryEntry(SelectionMode.Npc, "NPC guidance", announcement);
    }

    private static void AnnounceInteractableSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Interactable)
        {
            return;
        }

        if (!TryGetSelectedInteractable(player, out InteractableGuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Crafting guidance", "No crafting stations detected nearby.");
            return;
        }

        int totalEntries = NearbyInteractables.Count;
        int position = _selectedInteractableIndex + 1;
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceCategoryEntry(SelectionMode.Interactable, "Crafting guidance", announcement);
    }

    private static void AnnouncePlayerSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Player)
        {
            return;
        }

        if (!TryGetSelectedPlayer(player, out Player targetPlayer, out PlayerGuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Player guidance", "No other active players detected.");
            return;
        }

        int totalEntries = NearbyPlayers.Count;
        int position = _selectedPlayerIndex + 1;
        string announcement = ComposePlayerAnnouncement(entry, player, targetPlayer.Center, position, totalEntries);
        AnnounceCategoryEntry(SelectionMode.Player, "Player guidance", announcement);
    }

    private static string ComposeNpcAnnouncement(NpcGuidanceEntry entry, Player player, Vector2 npcPosition, int position, int total)
    {
        return ComposeEntityAnnouncement(entry.DisplayName, player, npcPosition, position, total);
    }

    private static string ComposePlayerAnnouncement(PlayerGuidanceEntry entry, Player player, Vector2 targetPlayerPosition, int position, int total)
    {
        return ComposeEntityAnnouncement(entry.DisplayName, player, targetPlayerPosition, position, total);
    }

    private static string ComposeEntityAnnouncement(string displayName, Player player, Vector2 targetPosition, int position, int total)
    {
        string ordinal = FormatEntryOrdinal(position, total);
        string announcement = string.IsNullOrWhiteSpace(ordinal)
            ? displayName
            : $"{displayName} {ordinal}";

        string relative = DescribeRelativeOffset(player.Center, targetPosition);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return announcement;
        }

        return $"{announcement}, {relative}";
    }

    private static int Modulo(int value, int modulus)
    {
        if (modulus == 0)
        {
            return 0;
        }

        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }


    private static void DeleteSelectedWaypoint(Player player)
    {
        if (Waypoints.Count == 0)
        {
            ScreenReaderService.Announce("No waypoints saved.");
            return;
        }

        if (_selectionMode != SelectionMode.Waypoint || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            ScreenReaderService.Announce("No waypoint selected.");
            return;
        }

        Waypoint removed = Waypoints[_selectedIndex];
        Waypoints.RemoveAt(_selectedIndex);

        if (Waypoints.Count == 0)
        {
            _selectedIndex = -1;
            _selectionMode = SelectionMode.None;
            ClearCategoryAnnouncement();
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            ScreenReaderService.Announce($"Deleted waypoint {removed.Name}.");
            AnnounceDisabledSelection();
            return;
        }

        if (_selectedIndex >= Waypoints.Count)
        {
            _selectedIndex = Waypoints.Count - 1;
        }

        Waypoint nextWaypoint = Waypoints[_selectedIndex];
        string nextAnnouncement = ComposeWaypointAnnouncement(nextWaypoint, player);
        ScreenReaderService.Announce($"Deleted waypoint {removed.Name}.");
        AnnounceCategoryEntry(SelectionMode.Waypoint, "Waypoints", nextAnnouncement);
        RescheduleGuidancePing(player);
        EmitCurrentGuidancePing(player);
    }

    private static void AnnounceDisabledSelection()
    {
        ClearCategoryAnnouncement();
        AnnounceCategorySelection("Guidance disabled", string.Empty);
    }

    private static void AnnounceExplorationSelection()
    {
        ClearCategoryAnnouncement();
        AnnounceCategorySelection("Exploration mode", "Tracking nearby interactables.");
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint, Player player)
    {
        int total = Waypoints.Count;
        int position = _selectedIndex + 1;

        string ordinal = FormatEntryOrdinal(position, total);
        string announcement = string.IsNullOrWhiteSpace(ordinal)
            ? waypoint.Name
            : $"{waypoint.Name} {ordinal}";

        string relative = DescribeRelativeOffset(player.Center, waypoint.WorldPosition);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return announcement;
        }

        return $"{announcement}, {relative}";
    }

    private static string ComposeCreationAnnouncement(string waypointName, Player player, Vector2 worldPosition)
    {
        string relative = DescribeRelativeOffset(player.Center, worldPosition);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return $"Created waypoint {waypointName}";
        }

        return $"Created waypoint {waypointName}, {relative}";
    }

    private static bool TryGetSelectedNpc(Player player, out NPC npc, out NpcGuidanceEntry entry)
    {
        entry = default;
        npc = default!;
        if (_selectionMode != SelectionMode.Npc)
        {
            return false;
        }

        RefreshNpcEntries(player);
        if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
        {
            _selectedNpcIndex = -1;
            return false;
        }

        entry = NearbyNpcs[_selectedNpcIndex];
        if (entry.NpcIndex < 0 || entry.NpcIndex >= Main.maxNPCs)
        {
            return false;
        }

        npc = Main.npc[entry.NpcIndex];
        if (!IsTrackableNpc(npc))
        {
            RefreshNpcEntries(player);
            if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
            {
                _selectedNpcIndex = -1;
                return false;
            }

            entry = NearbyNpcs[_selectedNpcIndex];
            if (entry.NpcIndex < 0 || entry.NpcIndex >= Main.maxNPCs)
            {
                return false;
            }

            npc = Main.npc[entry.NpcIndex];
            if (!IsTrackableNpc(npc))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetSelectedPlayer(Player owner, out Player target, out PlayerGuidanceEntry entry)
    {
        entry = default;
        target = default!;
        if (_selectionMode != SelectionMode.Player)
        {
            return false;
        }

        if (Main.netMode == NetmodeID.SinglePlayer)
        {
            _selectedPlayerIndex = -1;
            return false;
        }

        RefreshPlayerEntries(owner);
        if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
        {
            _selectedPlayerIndex = -1;
            return false;
        }

        entry = NearbyPlayers[_selectedPlayerIndex];
        if (entry.PlayerIndex < 0 || entry.PlayerIndex >= Main.maxPlayers)
        {
            return false;
        }

        target = Main.player[entry.PlayerIndex];
        if (!IsTrackablePlayer(target, owner))
        {
            RefreshPlayerEntries(owner);
            if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
            {
                _selectedPlayerIndex = -1;
                return false;
            }

            entry = NearbyPlayers[_selectedPlayerIndex];
            if (entry.PlayerIndex < 0 || entry.PlayerIndex >= Main.maxPlayers)
            {
                return false;
            }

            target = Main.player[entry.PlayerIndex];
            if (!IsTrackablePlayer(target, owner))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetSelectedInteractable(Player player, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.Interactable)
        {
            return false;
        }

        RefreshInteractableEntries(player);
        if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
        {
            _selectedInteractableIndex = -1;
            return false;
        }

        entry = NearbyInteractables[_selectedInteractableIndex];
        return true;
    }

    private static void RefreshNpcEntries(Player player)
    {
        int preservedNpcIndex = -1;
        if (_selectedNpcIndex >= 0 && _selectedNpcIndex < NearbyNpcs.Count)
        {
            preservedNpcIndex = NearbyNpcs[_selectedNpcIndex].NpcIndex;
        }

        NearbyNpcs.Clear();
        if (player is null || !player.active)
        {
            _selectedNpcIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (TryCreateNpcEntry(i, origin, includeOutOfRange: false, out NpcGuidanceEntry entry))
            {
                NearbyNpcs.Add(entry);
            }
        }

        if (preservedNpcIndex >= 0 && !NearbyNpcs.Exists(entry => entry.NpcIndex == preservedNpcIndex))
        {
            if (TryCreateNpcEntry(preservedNpcIndex, origin, includeOutOfRange: true, out NpcGuidanceEntry preservedEntry))
            {
                NearbyNpcs.Add(preservedEntry);
            }
            else
            {
                preservedNpcIndex = -1;
            }
        }

        NearbyNpcs.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (NearbyNpcs.Count == 0)
        {
            _selectedNpcIndex = -1;
            return;
        }

        if (preservedNpcIndex >= 0)
        {
            int restoredIndex = NearbyNpcs.FindIndex(entry => entry.NpcIndex == preservedNpcIndex);
            if (restoredIndex >= 0)
            {
                _selectedNpcIndex = restoredIndex;
                return;
            }
        }

        if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
        {
            _selectedNpcIndex = 0;
        }
    }

    private static void RefreshPlayerEntries(Player player)
    {
        int preservedPlayerIndex = -1;
        if (_selectedPlayerIndex >= 0 && _selectedPlayerIndex < NearbyPlayers.Count)
        {
            preservedPlayerIndex = NearbyPlayers[_selectedPlayerIndex].PlayerIndex;
        }

        NearbyPlayers.Clear();
        if (player is null || !player.active || Main.netMode == NetmodeID.SinglePlayer)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            if (TryCreatePlayerEntry(i, player, out PlayerGuidanceEntry entry))
            {
                NearbyPlayers.Add(entry);
            }
        }

        if (NearbyPlayers.Count == 0)
        {
            _selectedPlayerIndex = -1;
            return;
        }

        NearbyPlayers.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (preservedPlayerIndex >= 0)
        {
            int restoredIndex = NearbyPlayers.FindIndex(entry => entry.PlayerIndex == preservedPlayerIndex);
            if (restoredIndex >= 0)
            {
                _selectedPlayerIndex = restoredIndex;
                return;
            }
        }

        if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
        {
            _selectedPlayerIndex = 0;
        }
    }

    private static void RefreshInteractableEntries(Player player)
    {
        bool hasPreservedAnchor = false;
        Point preservedAnchor = Point.Zero;
        if (_selectedInteractableIndex >= 0 && _selectedInteractableIndex < NearbyInteractables.Count)
        {
            preservedAnchor = NearbyInteractables[_selectedInteractableIndex].Anchor;
            hasPreservedAnchor = true;
        }

        NearbyInteractables.Clear();
        if (player is null || !player.active)
        {
            _selectedInteractableIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        int scanRadius = (int)Math.Clamp(DistanceReferenceTiles + 8f, 4f, 240f);
        int playerTileX = (int)(origin.X / 16f);
        int playerTileY = (int)(origin.Y / 16f);
        int minX = Math.Max(0, playerTileX - scanRadius);
        int maxX = Math.Min(Main.maxTilesX - 1, playerTileX + scanRadius);
        int minY = Math.Max(0, playerTileY - scanRadius);
        int maxY = Math.Min(Main.maxTilesY - 1, playerTileY + scanRadius);

        InteractableAnchorScratch.Clear();
        bool preservedIncluded = false;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile)
                {
                    continue;
                }

                if (!InteractableDefinitionsByTileType.TryGetValue(tile.TileType, out List<InteractableDefinition>? definitions))
                {
                    continue;
                }

                foreach (InteractableDefinition definition in definitions)
                {
                    if (!IsInteractableAnchor(tile, definition))
                    {
                        continue;
                    }

                    Point anchor = new(x, y);
                    bool isPreservedAnchor = hasPreservedAnchor && anchor == preservedAnchor;
                    if (!InteractableAnchorScratch.Add(anchor) && !isPreservedAnchor)
                    {
                        continue;
                    }

                    if (TryCreateInteractableEntry(definition, anchor, origin, isPreservedAnchor, out InteractableGuidanceEntry entry))
                    {
                        NearbyInteractables.Add(entry);
                        if (isPreservedAnchor)
                        {
                            preservedIncluded = true;
                        }
                    }
                }
            }
        }

        if (hasPreservedAnchor && !preservedIncluded && TryCreateInteractableEntryForAnchor(preservedAnchor, origin, includeOutOfRange: true, out InteractableGuidanceEntry preservedEntry))
        {
            NearbyInteractables.Add(preservedEntry);
        }

        NearbyInteractables.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (NearbyInteractables.Count == 0)
        {
            _selectedInteractableIndex = -1;
            return;
        }

        if (hasPreservedAnchor)
        {
            int restoredIndex = NearbyInteractables.FindIndex(entry => entry.Anchor == preservedAnchor);
            if (restoredIndex >= 0)
            {
                _selectedInteractableIndex = restoredIndex;
                return;
            }
        }

        if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
        {
            _selectedInteractableIndex = 0;
        }
    }

    private static bool TryCreateNpcEntry(int npcIndex, Vector2 origin, bool includeOutOfRange, out NpcGuidanceEntry entry)
    {
        entry = default;
        if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
        {
            return false;
        }

        NPC npc = Main.npc[npcIndex];
        if (!IsTrackableNpc(npc))
        {
            return false;
        }

        float distanceTiles = Vector2.Distance(origin, npc.Center) / 16f;
        if (!includeOutOfRange && distanceTiles > DistanceReferenceTiles)
        {
            return false;
        }

        string displayName = ResolveNpcDisplayName(npc);
        entry = new NpcGuidanceEntry(npcIndex, displayName, distanceTiles);
        return true;
    }

    private static bool TryCreatePlayerEntry(int playerIndex, Player owner, out PlayerGuidanceEntry entry)
    {
        entry = default;
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
        {
            return false;
        }

        Player candidate = Main.player[playerIndex];
        if (!IsTrackablePlayer(candidate, owner))
        {
            return false;
        }

        float distanceTiles = Vector2.Distance(owner.Center, candidate.Center) / 16f;
        string displayName = ResolvePlayerDisplayName(candidate, playerIndex);
        entry = new PlayerGuidanceEntry(playerIndex, displayName, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntry(InteractableDefinition definition, Point anchor, Vector2 origin, bool includeOutOfRange, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (!IsWithinWorld(anchor))
        {
            return false;
        }

        Tile tile = Main.tile[anchor.X, anchor.Y];
        if (!tile.HasTile || tile.TileType != definition.TileType)
        {
            return false;
        }

        Vector2 worldPosition = ResolveInteractableWorldPosition(anchor, definition);
        float distanceTiles = Vector2.Distance(worldPosition, origin) / 16f;
        if (!includeOutOfRange && distanceTiles > DistanceReferenceTiles)
        {
            return false;
        }

        entry = new InteractableGuidanceEntry(anchor, definition.DisplayName, worldPosition, distanceTiles);
        return true;
    }

    private static bool TryCreateInteractableEntryForAnchor(Point anchor, Vector2 origin, bool includeOutOfRange, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (!IsWithinWorld(anchor))
        {
            return false;
        }

        Tile tile = Main.tile[anchor.X, anchor.Y];
        if (!tile.HasTile)
        {
            return false;
        }

        if (!InteractableDefinitionsByTileType.TryGetValue(tile.TileType, out List<InteractableDefinition>? definitions))
        {
            return false;
        }

        foreach (InteractableDefinition definition in definitions)
        {
            if (!IsInteractableAnchor(tile, definition))
            {
                continue;
            }

            if (TryCreateInteractableEntry(definition, anchor, origin, includeOutOfRange, out entry))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveNpcDisplayName(NPC npc)
    {
        if (!string.IsNullOrWhiteSpace(npc.FullName))
        {
            return npc.FullName;
        }

        if (!string.IsNullOrWhiteSpace(npc.GivenName))
        {
            return npc.GivenName;
        }

        string localized = Lang.GetNPCNameValue(npc.type);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        return "NPC";
    }

    private static string ResolvePlayerDisplayName(Player player, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(player.name))
        {
            return player.name;
        }

        return $"Player {fallbackIndex + 1}";
    }

    private static bool IsWithinWorld(Point point)
    {
        return point.X >= 0 && point.X < Main.maxTilesX && point.Y >= 0 && point.Y < Main.maxTilesY;
    }

    private static bool IsInteractableAnchor(Tile tile, InteractableDefinition definition)
    {
        if (definition.FrameWidth <= 0 || definition.FrameHeight <= 0)
        {
            return true;
        }

        int frameX = tile.TileFrameX;
        int frameY = tile.TileFrameY;
        if (frameX < 0 || frameY < 0)
        {
            return false;
        }

        return frameX % definition.FrameWidth == 0 && frameY % definition.FrameHeight == 0;
    }

    private static Vector2 ResolveInteractableWorldPosition(Point anchor, InteractableDefinition definition)
    {
        float centerX = anchor.X + (definition.WidthTiles * 0.5f);
        float centerY = anchor.Y + (definition.HeightTiles * 0.5f);
        return new Vector2(centerX * 16f, centerY * 16f);
    }

    private static bool IsTrackableNpc(NPC npc)
    {
        if (!npc.active || npc.lifeMax <= 0)
        {
            return false;
        }

        if (npc.townNPC || NPCID.Sets.ActsLikeTownNPC[npc.type] || NPCID.Sets.IsTownPet[npc.type])
        {
            return true;
        }

        return false;
    }

    private static bool IsTrackablePlayer(Player candidate, Player owner)
    {
        if (candidate is null || !candidate.active || owner is null || !owner.active)
        {
            return false;
        }

        if (candidate.whoAmI == owner.whoAmI || candidate.dead || candidate.ghost)
        {
            return false;
        }

        return true;
    }

    private static string FormatEntryOrdinal(int position, int total)
    {
        if (position <= 0 || total <= 0 || position > total)
        {
            return string.Empty;
        }

        return $"{position} of {total}";
    }

    private static void AnnounceCategorySelection(string categoryLabel, string detail)
    {
        if (string.IsNullOrWhiteSpace(categoryLabel))
        {
            categoryLabel = "Guidance";
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            ScreenReaderService.Announce(categoryLabel);
            return;
        }

        ScreenReaderService.Announce($"{categoryLabel}. {detail}");
    }

    private static void AnnounceCategoryEntry(SelectionMode category, string categoryLabel, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            AnnounceCategorySelection(categoryLabel, detail);
            return;
        }

        bool includeCategory = _categoryAnnouncementPending || _categoryAnnouncementMode != category;
        _categoryAnnouncementMode = category;
        _categoryAnnouncementPending = false;

        if (includeCategory && !string.IsNullOrWhiteSpace(categoryLabel))
        {
            ScreenReaderService.Announce($"{categoryLabel}. {detail}");
        }
        else
        {
            ScreenReaderService.Announce(detail);
        }
    }

    private static void BeginCategoryAnnouncement(SelectionMode category)
    {
        _categoryAnnouncementMode = category;
        _categoryAnnouncementPending = true;
    }

    private static void ClearCategoryAnnouncement()
    {
        _categoryAnnouncementMode = SelectionMode.None;
        _categoryAnnouncementPending = false;
    }

    private static string DescribeRelativeOffset(Vector2 origin, Vector2 target)
    {
        Vector2 offset = target - origin;
        int tilesX = (int)MathF.Round(offset.X / 16f);
        int tilesY = (int)MathF.Round(offset.Y / 16f);

        if (tilesX == 0 && tilesY == 0)
        {
            return "at your position";
        }

        List<string> parts = new(3);
        if (tilesX != 0)
        {
            string direction = tilesX > 0 ? "right" : "left";
            parts.Add($"{Math.Abs(tilesX)} {direction}");
        }

        if (tilesY != 0)
        {
            string direction = tilesY > 0 ? "down" : "up";
            parts.Add($"{Math.Abs(tilesY)} {direction}");
        }

        return string.Join(", ", parts);
    }

    private static bool TryGetSelectedWaypoint(out Waypoint waypoint)
    {
        if (_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            waypoint = Waypoints[_selectedIndex];
            return true;
        }

        waypoint = default;
        return false;
    }

    private static bool TryGetCurrentTrackingTarget(Player player, out Vector2 worldPosition, out string label)
    {
        switch (_selectionMode)
        {
            case SelectionMode.Waypoint when TryGetSelectedWaypoint(out Waypoint waypoint):
                worldPosition = waypoint.WorldPosition;
                label = waypoint.Name;
                return true;
            case SelectionMode.Npc when TryGetSelectedNpc(player, out NPC npc, out NpcGuidanceEntry entry):
                worldPosition = npc.Center;
                label = entry.DisplayName;
                return true;
            case SelectionMode.Interactable when TryGetSelectedInteractable(player, out InteractableGuidanceEntry interactable):
                worldPosition = interactable.WorldPosition;
                label = interactable.DisplayName;
                return true;
            case SelectionMode.Player when TryGetSelectedPlayer(player, out Player targetPlayer, out PlayerGuidanceEntry playerEntry):
                worldPosition = targetPlayer.Center;
                label = playerEntry.DisplayName;
                return true;
            default:
                worldPosition = default;
                label = string.Empty;
                return false;
        }
    }

    private static void EmitCurrentGuidancePing(Player player)
    {
        if (TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out _))
        {
            EmitPing(player, targetPosition);
        }
    }

    private static void RescheduleGuidancePing(Player player)
    {
        if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out _))
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            return;
        }

        _arrivalAnnounced = false;
        _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
    }

    private static void EmitPing(Player player, Vector2 worldPosition)
    {
        if (Main.dedServ || Main.soundVolume <= 0f)
        {
            return;
        }

        try
        {
            CleanupFinishedWaypointInstances();

            Vector2 offset = worldPosition - player.Center;
            float pitch = MathHelper.Clamp(-offset.Y / PitchScale, -0.7f, 0.7f);
            float pan = MathHelper.Clamp(offset.X / PanScalePixels, -1f, 1f);

            float distanceTiles = offset.Length() / 16f;
            float distanceFactor = 1f / (1f + (distanceTiles / Math.Max(1f, DistanceReferenceTiles)));
            float volume = MathHelper.Clamp(MinVolume + distanceFactor * 0.85f, 0f, 1f) * Main.soundVolume;

            SoundEffect tone = EnsureWaypointTone();
            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Pan = pan;
            instance.Pitch = pitch;
            instance.Volume = MathHelper.Clamp(volume, 0f, 1f);

            try
            {
                instance.Play();
                ActiveWaypointInstances.Add(instance);
            }
            catch (Exception inner)
            {
                instance.Dispose();
                global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Debug($"[WaypointPing] Play failed: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn($"[WaypointPing] Tone setup failed: {ex.Message}");
        }
    }

    private static SoundEffect EnsureWaypointTone()
    {
        if (_waypointTone is { IsDisposed: false })
        {
            return _waypointTone;
        }

        _waypointTone?.Dispose();
        _waypointTone = CreateWaypointTone();
        return _waypointTone;
    }

    private static SoundEffect CreateWaypointTone()
    {
        return SynthesizedSoundFactory.CreateSineTone(
            frequency: 720f,
            durationSeconds: 0.13f,
            envelope: SynthesizedSoundFactory.ToneEnvelopes.WaypointPulse,
            gain: 0.75f);
    }

    private sealed class InputSnapshot
    {
        public bool BlockInput;
        public bool WritingText;
        public bool PlayerInventory;
        public bool EditSign;
        public bool EditChest;
        public bool DrawingPlayerChat;
        public bool InFancyUI;
        public bool GameMenu;
        public string ChatText = string.Empty;
        public UIState? PreviousUiState;
    }

    private static void CleanupFinishedWaypointInstances()
    {
        for (int i = ActiveWaypointInstances.Count - 1; i >= 0; i--)
        {
            SoundEffectInstance instance = ActiveWaypointInstances[i];
            if (instance.IsDisposed || instance.State == SoundState.Stopped)
            {
                instance.Dispose();
                ActiveWaypointInstances.RemoveAt(i);
            }
        }
    }

    private static void DisposeToneResources()
    {
        foreach (SoundEffectInstance instance in ActiveWaypointInstances)
        {
            try
            {
                if (!instance.IsDisposed)
                {
                    instance.Stop();
                }
            }
            catch
            {
            }

            instance.Dispose();
        }

        ActiveWaypointInstances.Clear();

        if (_waypointTone is not null)
        {
            if (!_waypointTone.IsDisposed)
            {
                _waypointTone.Dispose();
            }

            _waypointTone = null;
        }
    }

    private static int ComputeNextPingFrame(Player player, Vector2 waypointPosition)
    {
        int delay = DeterminePingDelayFrames(player, waypointPosition);
        if (delay <= 0)
        {
            return -1;
        }

        return ComputeNextPingFrameFromDelay(delay);
    }

    private static int DeterminePingDelayFrames(Player player, Vector2 waypointPosition)
    {
        float distanceTiles = Vector2.Distance(player.Center, waypointPosition) / 16f;
        if (distanceTiles <= ArrivalTileThreshold)
        {
            return -1;
        }

        float frames = MathHelper.Clamp(distanceTiles * PingDelayScale, MinPingDelayFrames, MaxPingDelayFrames);
        return (int)MathF.Round(frames);
    }

    private static int ComputeNextPingFrameFromDelay(int delayFrames)
    {
        int safeDelay = Math.Max(1, delayFrames);
        ulong current = Main.GameUpdateCount;
        ulong target = current + (ulong)safeDelay;
        if (target > int.MaxValue)
        {
            target = (ulong)int.MaxValue;
        }

        return (int)target;
    }
}
