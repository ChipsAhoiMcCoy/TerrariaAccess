#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Waypoints;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems;

public sealed class WaypointSystem : ModSystem
{
    private const string WaypointListKey = "screenReaderWaypoints";
    private const string SelectedIndexKey = "screenReaderSelectedWaypoint";
    private const string ExplorationModeKey = "screenReaderWaypointExplorationMode";

    private const float ArrivalTileThreshold = 4f;
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
        Npc,
        Waypoint
    }

    private static SelectionMode _selectionMode = SelectionMode.None;
    private static int _selectedIndex = -1;
    private static int _selectedNpcIndex = -1;

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

        WaypointKeybinds.EnsureInitialized(Mod);
    }

    public override void Unload()
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectionMode = SelectionMode.None;

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
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectionMode = SelectionMode.None;
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
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectionMode = SelectionMode.None;
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

        if (WaypointKeybinds.Create?.JustPressed ?? false)
        {
            BeginNaming(player);
            return;
        }

        if (WaypointKeybinds.CategoryNext?.JustPressed ?? false)
        {
            CycleCategory(1, player);
            return;
        }

        if (WaypointKeybinds.CategoryPrevious?.JustPressed ?? false)
        {
            CycleCategory(-1, player);
            return;
        }

        if (WaypointKeybinds.EntryNext?.JustPressed ?? false)
        {
            CycleCategoryEntry(1, player);
            return;
        }

        if (WaypointKeybinds.EntryPrevious?.JustPressed ?? false)
        {
            CycleCategoryEntry(-1, player);
            return;
        }

        if (WaypointKeybinds.Delete?.JustPressed ?? false)
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
        SelectionMode.Npc,
        SelectionMode.Waypoint
    };

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

        int targetIndex = Modulo(currentIndex + direction, CategoryOrder.Length);
        SelectionMode targetCategory = CategoryOrder[targetIndex];
        ApplyCategorySelection(targetCategory, player);
    }

    private static void ApplyCategorySelection(SelectionMode category, Player player)
    {
        switch (category)
        {
            case SelectionMode.None:
                _selectionMode = SelectionMode.None;
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                RescheduleGuidancePing(player);
                AnnounceDisabledSelection();
                return;
            case SelectionMode.Exploration:
                _selectionMode = SelectionMode.Exploration;
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                RescheduleGuidancePing(player);
                AnnounceExplorationSelection();
                return;
            case SelectionMode.Npc:
                _selectionMode = SelectionMode.Npc;
                RefreshNpcEntries(player);
                if (NearbyNpcs.Count == 0)
                {
                    _selectedNpcIndex = -1;
                    RescheduleGuidancePing(player);
                    int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
                    ScreenReaderService.Announce($"NPC guidance selected. No nearby NPCs within {rangeTiles} tiles. {FormatCategoryPositionSuffix(SelectionMode.Npc)}");
                    return;
                }

                if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
                {
                    _selectedNpcIndex = 0;
                }

                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Waypoint:
                _selectionMode = SelectionMode.Waypoint;
                if (Waypoints.Count == 0)
                {
                    _selectedIndex = -1;
                    RescheduleGuidancePing(player);
                    ScreenReaderService.Announce($"Waypoints category selected. No waypoints saved. {FormatCategoryPositionSuffix(SelectionMode.Waypoint)}");
                    return;
                }

                if (_selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
                {
                    _selectedIndex = 0;
                }

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
                    ScreenReaderService.Announce("No waypoints saved.");
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
                    RescheduleGuidancePing(player);
                    int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
                    ScreenReaderService.Announce($"No NPCs within {rangeTiles} tiles.");
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
            default:
                ScreenReaderService.Announce("Select a waypoint or NPC category to browse entries.");
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
        ScreenReaderService.Announce($"{announcement} {FormatCategoryPositionSuffix(SelectionMode.Waypoint)}");
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
            ScreenReaderService.Announce($"NPC guidance selected. No nearby NPCs within {rangeTiles} tiles. {FormatCategoryPositionSuffix(SelectionMode.Npc)}");
            return;
        }

        string announcement = ComposeNpcAnnouncement(entry, player, npc.Center);
        ScreenReaderService.Announce($"{announcement} {FormatCategoryPositionSuffix(SelectionMode.Npc)}");
    }

    private static string ComposeNpcAnnouncement(NpcGuidanceEntry entry, Player player, Vector2 npcPosition)
    {
        int total = NearbyNpcs.Count;
        int position = _selectedNpcIndex + 1;

        string announcement = entry.DisplayName;
        if (total > 0 && position > 0 && position <= total)
        {
            announcement = $"{entry.DisplayName} {position} of {total}";
        }

        string relative = DescribeRelativeOffset(player.Center, npcPosition);
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
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            ScreenReaderService.Announce($"Deleted waypoint {removed.Name}. Guidance disabled. {FormatCategoryPositionSuffix(SelectionMode.None)}");
            return;
        }

        if (_selectedIndex >= Waypoints.Count)
        {
            _selectedIndex = Waypoints.Count - 1;
        }

        Waypoint nextWaypoint = Waypoints[_selectedIndex];
        string nextAnnouncement = ComposeWaypointAnnouncement(nextWaypoint, player);
        ScreenReaderService.Announce($"Deleted waypoint {removed.Name}. {nextAnnouncement} {FormatCategoryPositionSuffix(SelectionMode.Waypoint)}");
        RescheduleGuidancePing(player);
        EmitCurrentGuidancePing(player);
    }

    private static void AnnounceDisabledSelection()
    {
        ScreenReaderService.Announce($"Guidance disabled. {FormatCategoryPositionSuffix(SelectionMode.None)}");
    }

    private static void AnnounceExplorationSelection()
    {
        ScreenReaderService.Announce($"Exploration and gathering mode. Tracking nearby interactables. {FormatCategoryPositionSuffix(SelectionMode.Exploration)}");
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint, Player player)
    {
        int total = Waypoints.Count;
        int position = _selectedIndex + 1;

        string announcement = waypoint.Name;
        if (total > 0 && position > 0 && position <= total)
        {
            announcement = $"{waypoint.Name} {position} of {total}";
        }

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

    private static void RefreshNpcEntries(Player player)
    {
        NearbyNpcs.Clear();
        if (player is null || !player.active)
        {
            _selectedNpcIndex = -1;
            return;
        }

        Vector2 origin = player.Center;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!IsTrackableNpc(npc))
            {
                continue;
            }

            float distanceTiles = Vector2.Distance(origin, npc.Center) / 16f;
            if (distanceTiles > DistanceReferenceTiles)
            {
                continue;
            }

            string displayName = ResolveNpcDisplayName(npc);
            NearbyNpcs.Add(new NpcGuidanceEntry(i, displayName, distanceTiles));
        }

        NearbyNpcs.Sort((left, right) => left.DistanceTiles.CompareTo(right.DistanceTiles));

        if (NearbyNpcs.Count == 0)
        {
            _selectedNpcIndex = -1;
            return;
        }

        if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
        {
            _selectedNpcIndex = 0;
        }
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

    private static bool IsTrackableNpc(NPC npc)
    {
        if (!npc.active || npc.lifeMax <= 0)
        {
            return false;
        }

        if (npc.townNPC || NPCID.Sets.ActsLikeTownNPC[npc.type] || NPCID.Sets.IsTownPet[npc.type] || NPCID.Sets.TownCritter[npc.type])
        {
            return true;
        }

        if (npc.friendly && npc.damage <= 0 && !NPCID.Sets.CountsAsCritter[npc.type])
        {
            return true;
        }

        return false;
    }

    private static string FormatCategoryPositionSuffix(SelectionMode mode)
    {
        int index = Array.IndexOf(CategoryOrder, mode);
        if (index < 0)
        {
            index = 0;
        }

        return $"{index + 1} of {CategoryOrder.Length}";
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
