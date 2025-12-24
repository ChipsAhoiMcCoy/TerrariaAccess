#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ScreenReaderMod.Common.Players;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Guidance;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem : ModSystem
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
    private const float ProximityAnnouncementStepTiles = 10f;
    private const float ProximityAnnouncementToleranceTiles = 0.35f;
    private const float ExplorationSelectionMatchToleranceTiles = 6f;
    private const float MinVolume = 0.18f;
    private static readonly SpatialAudioPanner.SpatialAudioProfile GuidanceAudioProfile = new(
        PitchScalePixels: PitchScale,
        PanScalePixels: PanScalePixels,
        DistanceReferenceTiles: DistanceReferenceTiles,
        MinVolume: MinVolume,
        VolumeScale: 0.85f,
        PitchClamp: 0.7f);

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
        ResetTrackingState();
        _namingActive = false;
        DisposeToneResources();
        _activeKeyboard = null;
        _inputSnapshot = null;
    }

    public override void OnWorldUnload()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient && Main.LocalPlayer is not null)
        {
            Main.LocalPlayer.GetModPlayer<GuidancePlayer>().CacheWaypointState();
        }

        ResetTrackingState();
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
        ResetTrackingState();
        LoadWaypointData(tag, "world save", announceSelection: false);
    }

    public override void SaveWorldData(TagCompound tag)
    {
        SaveWaypointData(tag, "world save");
    }

    public override void PostUpdatePlayers()
    {
        if (Main.dedServ || Main.gameMenu || _namingActive)
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            ResetProximityProgress();
        }
        else
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                ResetProximityProgress();
                return;
            }

            if (Main.gamePaused)
            {
                return;
            }

            EnsureTargetsUpToDate(player);

            if (!TryGetCurrentTrackingTarget(player, out Vector2 targetPosition, out string arrivalLabel))
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                ResetProximityProgress();
                LogPing("No tracking target; reset ping state");
                return;
            }

            float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;
            UpdateProximityAnnouncement(player, targetPosition, arrivalLabel, distanceTiles);
            if (distanceTiles <= ArrivalTileThreshold)
            {
                if (!_arrivalAnnounced && !string.IsNullOrWhiteSpace(arrivalLabel) && _selectionMode != SelectionMode.DroppedItem)
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

            bool allowPing = IsPingEnabledForCurrentSelection();
            if (!allowPing)
            {
                _nextPingUpdateFrame = -1;
                return;
            }

            if (_nextPingUpdateFrame < 0)
            {
                _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
                LogPing($"Scheduled initial ping at frame {_nextPingUpdateFrame}");
            }
            else if (Main.GameUpdateCount >= (uint)_nextPingUpdateFrame)
            {
                EmitPing(player, targetPosition);
                _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
                LogPing($"Rescheduled next ping at frame {_nextPingUpdateFrame} after emit");
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
            string resolvedName = TextSanitizer.Clean(string.IsNullOrWhiteSpace(rawInput) ? fallbackName : rawInput.Trim());
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[WaypointNaming:{logContext}] Resolved name: \"{resolvedName}\" (input: \"{rawInput}\")");

            Waypoint waypoint = new(resolvedName, worldPosition);
            Waypoints.Add(waypoint);
            SendWaypointAddedToServer(waypoint);
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

    private static void LogPing(string message)
    {
        if (!LogGuidancePings)
        {
            return;
        }

        global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Info($"[GuidancePing] {message}");
    }

    private static void EnsureTargetsUpToDate(Player player)
    {
        if (player is null || !player.active)
        {
            return;
        }

        if (_lastTargetRefreshFrame == Main.GameUpdateCount && _lastTargetRefreshPlayerIndex == player.whoAmI)
        {
            return;
        }

        RefreshNpcEntries(player);
        RefreshPlayerEntries(player);
        RefreshInteractableEntries(player);
        RefreshExplorationEntries();
        RefreshDroppedItemEntries(player);

        _lastTargetRefreshFrame = Main.GameUpdateCount;
        _lastTargetRefreshPlayerIndex = player.whoAmI;
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

        EnsureTargetsUpToDate(player);

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

        if (GuidanceKeybinds.Teleport?.JustPressed ?? false)
        {
            TeleportToTrackingTarget(player);
            return;
        }

        if (GuidanceKeybinds.Delete?.JustPressed ?? false)
        {
            DeleteSelectedWaypoint(player);
        }
    }

    private static void TeleportToTrackingTarget(Player player)
    {
        if (!TryResolveTeleportTarget(player, out TeleportTarget target))
        {
            ScreenReaderService.Announce("No active guidance target to teleport to.");
            return;
        }

        Vector2 destination = ResolveTeleportDestination(player, target.Anchor);
        player.RemoveAllGrapplingHooks();
        player.Teleport(destination, target.Style);
        player.velocity = Vector2.Zero;
        player.fallStart = (int)(player.position.Y / 16f);

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, player.whoAmI, destination.X, destination.Y, target.Style);
        }

        _arrivalAnnounced = false;
        RescheduleGuidancePing(player);
        EmitCurrentGuidancePing(player);

        string announcement = string.IsNullOrWhiteSpace(target.Label)
            ? "Teleported to guidance target."
            : $"Teleported to {target.Label}.";
        ScreenReaderService.Announce(announcement);
    }

    private static bool TryResolveTeleportTarget(Player player, out TeleportTarget target)
    {
        if (TryGetCurrentTrackingTarget(player, out Vector2 worldPosition, out string label))
        {
            target = new TeleportTarget(worldPosition, label, ResolveTeleportStyleForSelection());
            return true;
        }

        if (_selectionMode == SelectionMode.Exploration && TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget exploration))
        {
            target = new TeleportTarget(exploration.WorldPosition, exploration.Label, TeleportationStyleID.RodOfDiscord);
            return true;
        }

        target = default;
        return false;
    }

    private static Vector2 ResolveTeleportDestination(Player player, Vector2 anchor)
    {
        Vector2 topLeft = anchor - new Vector2(player.width * 0.5f, player.height);

        float minX = 16f;
        float minY = 16f;
        float maxX = (Main.maxTilesX - 2) * 16f - player.width;
        float maxY = (Main.maxTilesY - 2) * 16f - player.height;

        float clampedX = MathHelper.Clamp(topLeft.X, minX, maxX);
        float clampedY = MathHelper.Clamp(topLeft.Y, minY, maxY);

        return new Vector2(clampedX, clampedY);
    }

    private static string BuildDefaultName()
    {
        int nextIndex = Waypoints.Count + 1;
        return BuildDefaultName(nextIndex);
    }

    private static string BuildDefaultName(int index)
    {
        if (index <= 0)
        {
            index = Waypoints.Count + 1;
        }

        return $"Waypoint {index}";
    }

    private static (List<Waypoint> waypoints, SelectionMode selectionMode, int selectedIndex) BuildSerializableWaypointState(string source, bool normalizeRuntime = false)
    {
        List<Waypoint> sanitized = new(Waypoints.Count);
        int mappedSelection = -1;

        for (int i = 0; i < Waypoints.Count; i++)
        {
            Waypoint waypoint = Waypoints[i];
            if (!TryCreateWaypoint(waypoint.Name, waypoint.WorldPosition.X, waypoint.WorldPosition.Y, sanitized.Count, source, out Waypoint sanitizedWaypoint))
            {
                continue;
            }

            if (_selectionMode == SelectionMode.Waypoint && _selectedIndex == i)
            {
                mappedSelection = sanitized.Count;
            }

            sanitized.Add(sanitizedWaypoint);
        }

        SelectionMode selectionMode = _selectionMode;
        int selectedIndex = _selectedIndex;

        if (sanitized.Count == 0)
        {
            selectionMode = SelectionMode.None;
            selectedIndex = -1;
        }
        else if (selectionMode == SelectionMode.Waypoint)
        {
            selectedIndex = mappedSelection >= 0
                ? mappedSelection
                : Math.Clamp(_selectedIndex, 0, sanitized.Count - 1);

            if (selectedIndex < 0 || selectedIndex >= sanitized.Count)
            {
                selectionMode = SelectionMode.None;
                selectedIndex = -1;
            }
        }
        else
        {
            selectedIndex = Math.Clamp(_selectedIndex, -1, sanitized.Count - 1);
        }

        if (normalizeRuntime && (sanitized.Count != Waypoints.Count || selectionMode != _selectionMode || selectedIndex != _selectedIndex))
        {
            Waypoints.Clear();
            Waypoints.AddRange(sanitized);
            _selectionMode = selectionMode;
            _selectedIndex = selectedIndex;
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
        }

        return (sanitized, selectionMode, selectedIndex);
    }

    internal static bool SaveWaypointData(TagCompound tag, string source, bool normalizeRuntime = true)
    {
        (List<Waypoint> waypoints, SelectionMode selectionMode, int selectedIndex) = BuildSerializableWaypointState(source, normalizeRuntime: normalizeRuntime);
        bool hasData = false;

        if (waypoints.Count > 0)
        {
            List<TagCompound> serialized = new(waypoints.Count);
            foreach (Waypoint waypoint in waypoints)
            {
                serialized.Add(new TagCompound
                {
                    ["name"] = waypoint.Name,
                    ["x"] = waypoint.WorldPosition.X,
                    ["y"] = waypoint.WorldPosition.Y,
                });
            }

            tag[WaypointListKey] = serialized;
            hasData = true;
        }
        else
        {
            tag.Remove(WaypointListKey);
        }

        if (selectionMode == SelectionMode.Waypoint && selectedIndex >= 0 && selectedIndex < waypoints.Count)
        {
            tag[SelectedIndexKey] = selectedIndex;
            hasData = true;
        }
        else
        {
            tag.Remove(SelectedIndexKey);
        }

        if (selectionMode == SelectionMode.Exploration)
        {
            tag[ExplorationModeKey] = true;
            hasData = true;
        }
        else
        {
            tag.Remove(ExplorationModeKey);
        }

        return hasData;
    }

    internal static bool LoadWaypointData(TagCompound tag, string source, bool announceSelection)
    {
        ResetWaypointSelectionState();

        if (tag.ContainsKey(WaypointListKey))
        {
            foreach (TagCompound entry in tag.GetList<TagCompound>(WaypointListKey))
            {
                if (!entry.ContainsKey("x") || !entry.ContainsKey("y"))
                {
                    LogWaypointWarning($"Dropped waypoint from {source}: missing coordinates.");
                    continue;
                }

                string name = entry.GetString("name");
                float x = entry.GetFloat("x");
                float y = entry.GetFloat("y");

                if (TryCreateWaypoint(name, x, y, Waypoints.Count, source, out Waypoint waypoint))
                {
                    Waypoints.Add(waypoint);
                }
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
        ResetProximityProgress();

        if (announceSelection && _selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            if (Main.LocalPlayer is { active: true } player)
            {
                RescheduleGuidancePing(player);
            }
        }

        return Waypoints.Count > 0 || explorationMode;
    }

    private static bool TryCreateWaypoint(string? rawName, float x, float y, int fallbackIndex, string source, out Waypoint waypoint)
    {
        waypoint = default;

        Vector2 worldPosition = new(x, y);
        if (!IsValidWaypointPosition(worldPosition))
        {
            LogWaypointWarning($"Dropped waypoint {fallbackIndex + 1} from {source}: invalid position ({x}, {y}).");
            return false;
        }

        string resolvedName = ResolveWaypointName(rawName, fallbackIndex);
        waypoint = new Waypoint(resolvedName, worldPosition);
        return true;
    }

    private static string ResolveWaypointName(string? rawName, int fallbackIndex)
    {
        string cleaned = SanitizeLabel(rawName);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        return BuildDefaultName(fallbackIndex + 1);
    }

    private static bool IsValidWaypointPosition(Vector2 worldPosition)
    {
        if (!float.IsFinite(worldPosition.X) || !float.IsFinite(worldPosition.Y))
        {
            return false;
        }

        float minX = 16f;
        float minY = 16f;
        float maxX = (Main.maxTilesX - 2) * 16f;
        float maxY = (Main.maxTilesY - 2) * 16f;

        return worldPosition.X >= minX && worldPosition.X <= maxX &&
               worldPosition.Y >= minY && worldPosition.Y <= maxY;
    }

    private static void LogWaypointWarning(string message)
    {
        ScreenReaderMod.Instance?.Logger.Warn($"[GuidanceSync] {message}");
    }

    private static void ResetWaypointSelectionState()
    {
        Waypoints.Clear();
        _selectionMode = SelectionMode.None;
        _selectedIndex = -1;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        ClearCategoryAnnouncement();
        ResetProximityProgress();
    }

    private static readonly SelectionMode[] CategoryOrder =
    {
        SelectionMode.None,
        SelectionMode.Exploration,
        SelectionMode.Interactable,
        SelectionMode.Npc,
        SelectionMode.Player,
        SelectionMode.Waypoint,
        SelectionMode.DroppedItem
    };

    private readonly record struct TeleportTarget(Vector2 Anchor, string Label, int Style);

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

        EnsureTargetsUpToDate(player);

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
        EnsureTargetsUpToDate(player);

        switch (category)
        {
            case SelectionMode.None:
                _selectionMode = SelectionMode.None;
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                ExplorationTargetRegistry.SetSelectedTarget(null);
                ClearCategoryAnnouncement();
                RescheduleGuidancePing(player);
                AnnounceDisabledSelection();
                return;
            case SelectionMode.Exploration:
                _selectionMode = SelectionMode.Exploration;
                _selectedExplorationIndex = -1;
                _lastExplorationSelection = null;
                RefreshExplorationEntries();
                ExplorationTargetRegistry.SetSelectedTarget(null);
                _selectedIndex = Math.Min(_selectedIndex, Waypoints.Count - 1);
                ClearCategoryAnnouncement();
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                AnnounceExplorationSelection();
                return;
            case SelectionMode.Interactable:
                _selectionMode = SelectionMode.Interactable;
                ExplorationTargetRegistry.SetSelectedTarget(null);
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
                ExplorationTargetRegistry.SetSelectedTarget(null);
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
                ExplorationTargetRegistry.SetSelectedTarget(null);
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
                ExplorationTargetRegistry.SetSelectedTarget(null);
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
            case SelectionMode.DroppedItem:
                _selectionMode = SelectionMode.DroppedItem;
                ExplorationTargetRegistry.SetSelectedTarget(null);
                RefreshDroppedItemEntries(player);
                if (NearbyDroppedItems.Count == 0)
                {
                    _selectedDroppedItemIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Dropped items", "No dropped items on screen.");
                    return;
                }

                if (_selectedDroppedItemIndex < 0 || _selectedDroppedItemIndex >= NearbyDroppedItems.Count)
                {
                    _selectedDroppedItemIndex = 0;
                }

                BeginCategoryAnnouncement(SelectionMode.DroppedItem);
                RescheduleGuidancePing(player);
                AnnounceDroppedItemSelection(player);
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

        EnsureTargetsUpToDate(player);

        switch (_selectionMode)
        {
            case SelectionMode.Waypoint:
                if (!TryAdvanceSelectionIndex(ref _selectedIndex, Waypoints.Count, direction))
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Waypoints", "No waypoints saved.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnounceWaypointSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Npc:
                RefreshNpcEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedNpcIndex, NearbyNpcs.Count, direction))
                {
                    _selectedNpcIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    int rangeTiles = (int)MathF.Round(DistanceReferenceTiles);
                    AnnounceCategorySelection("NPC guidance", $"No NPCs within {rangeTiles} tiles.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Interactable:
                RefreshInteractableEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedInteractableIndex, NearbyInteractables.Count, direction))
                {
                    _selectedInteractableIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Crafting guidance", "No crafting stations detected nearby.");
                    return;
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
                if (!TryAdvanceSelectionIndex(ref _selectedPlayerIndex, NearbyPlayers.Count, direction))
                {
                    _selectedPlayerIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Player guidance", "No other active players detected.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnouncePlayerSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Exploration:
                RefreshExplorationEntries();
                int totalExploration = NearbyExplorationTargets.Count;
                if (totalExploration == 0)
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Exploration mode", "No exploration targets detected nearby.");
                    return;
                }

                int totalSlots = totalExploration + 1; // include the "All" slot at -1
                int currentSlot = _selectedExplorationIndex + 1;
                int nextSlot = Modulo(currentSlot + direction, totalSlots);
                _selectedExplorationIndex = nextSlot - 1;

                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                AnnounceExplorationEntry(player, totalExploration);
                if (_selectedExplorationIndex < 0)
                {
                    ExplorationTargetRegistry.SetSelectedTarget(null);
                    _lastExplorationSelection = null;
                }
                else if (_selectedExplorationIndex < NearbyExplorationTargets.Count)
                {
                    _lastExplorationSelection = NearbyExplorationTargets[_selectedExplorationIndex];
                    ExplorationTargetRegistry.SetSelectedTarget(_lastExplorationSelection);
                }
                return;
            case SelectionMode.DroppedItem:
                RefreshDroppedItemEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedDroppedItemIndex, NearbyDroppedItems.Count, direction))
                {
                    _selectedDroppedItemIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Dropped items", "No dropped items on screen.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnounceDroppedItemSelection(player);
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
        AnnounceSelectedEntry(SelectionMode.Waypoint, "Waypoints", announcement);
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
        AnnounceSelectedEntry(SelectionMode.Npc, "NPC guidance", announcement);
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
        AnnounceSelectedEntry(SelectionMode.Interactable, "Crafting guidance", announcement);
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
        AnnounceSelectedEntry(SelectionMode.Player, "Player guidance", announcement);
    }

    private static void AnnounceDroppedItemSelection(Player player)
    {
        if (_selectionMode != SelectionMode.DroppedItem)
        {
            return;
        }

        if (!TryGetSelectedDroppedItem(player, out DroppedItemGuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Dropped items", "No dropped items on screen.");
            return;
        }

        int totalEntries = NearbyDroppedItems.Count;
        int position = _selectedDroppedItemIndex + 1;
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.DroppedItem, "Dropped items", announcement);
    }

    private static void AnnounceExplorationEntry(Player player, int totalEntries)
    {
        if (_selectionMode != SelectionMode.Exploration)
        {
            return;
        }

        if (_selectedExplorationIndex < 0 || _selectedExplorationIndex >= NearbyExplorationTargets.Count)
        {
            AnnounceExplorationSelection();
            return;
        }

        int position = _selectedExplorationIndex + 1;
        ExplorationTargetRegistry.ExplorationTarget entry = NearbyExplorationTargets[_selectedExplorationIndex];
        string announcement = ComposeEntityAnnouncement(entry.Label, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Exploration, string.Empty, announcement);
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
        string sanitizedName = SanitizeLabel(displayName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "target";
        }

        string ordinal = FormatEntryOrdinal(position, total);
        string label = string.IsNullOrWhiteSpace(ordinal)
            ? sanitizedName
            : $"{sanitizedName} {ordinal}";

        string relative = DescribeCursorStyleOffset(player, targetPosition);
        return TextSanitizer.JoinWithComma(label, relative);
    }

    private static bool TryAdvanceSelectionIndex(ref int index, int totalCount, int direction)
    {
        if (totalCount <= 0)
        {
            index = -1;
            return false;
        }

        direction = direction == 0 ? 1 : direction;
        if (index < 0 || index >= totalCount)
        {
            index = direction > 0 ? 0 : totalCount - 1;
            return true;
        }

        index = Modulo(index + direction, totalCount);
        return true;
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

        int removedIndex = _selectedIndex;
        Waypoint removed = Waypoints[removedIndex];
        Waypoints.RemoveAt(removedIndex);
        SendWaypointDeletedToServer(removedIndex);

        if (Waypoints.Count == 0)
        {
            _selectedIndex = -1;
            _selectionMode = SelectionMode.None;
            ClearCategoryAnnouncement();
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            ScreenReaderService.Announce($"Deleted waypoint {SanitizeLabel(removed.Name)}.");
            AnnounceDisabledSelection();
            return;
        }

        if (_selectedIndex >= Waypoints.Count)
        {
            _selectedIndex = Waypoints.Count - 1;
        }

        Waypoint nextWaypoint = Waypoints[_selectedIndex];
        string nextAnnouncement = ComposeWaypointAnnouncement(nextWaypoint, player);
        ScreenReaderService.Announce($"Deleted waypoint {SanitizeLabel(removed.Name)}.");
        AnnounceSelectedEntry(SelectionMode.Waypoint, "Waypoints", nextAnnouncement);
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
        AnnounceCategorySelection("Exploration mode", "Tracking all nearby interactables. Use Page Up and Page Down to cycle specific targets.");
        ExplorationTargetRegistry.SetSelectedTarget(null);
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint, Player player)
    {
        int total = Waypoints.Count;
        int position = _selectedIndex + 1;

        string waypointName = SanitizeLabel(waypoint.Name);
        string ordinal = FormatEntryOrdinal(position, total);
        string label = string.IsNullOrWhiteSpace(ordinal)
            ? waypointName
            : $"{waypointName} {ordinal}";

        string relative = DescribeCursorStyleOffset(player, waypoint.WorldPosition);
        return TextSanitizer.JoinWithComma(label, relative);
    }

    private static string ComposeCreationAnnouncement(string waypointName, Player player, Vector2 worldPosition)
    {
        string sanitizedName = SanitizeLabel(waypointName);
        string relative = DescribeRelativeOffset(player.Center, worldPosition);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return $"Created waypoint {sanitizedName}";
        }

        return $"Created waypoint {sanitizedName}, {relative}";
    }

    private static bool TryGetSelectedNpc(Player player, out NPC npc, out NpcGuidanceEntry entry)
    {
        entry = default;
        npc = default!;
        if (_selectionMode != SelectionMode.Npc)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
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

        EnsureTargetsUpToDate(owner);
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

    private static bool TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.Exploration)
        {
            return false;
        }

        RefreshExplorationEntries();
        if (_selectedExplorationIndex < 0 || _selectedExplorationIndex >= NearbyExplorationTargets.Count)
        {
            _selectedExplorationIndex = -1;
            return false;
        }

        entry = NearbyExplorationTargets[_selectedExplorationIndex];
        _lastExplorationSelection = entry;
        return true;
    }

    private static bool TryGetSelectedInteractable(Player player, out InteractableGuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.Interactable)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
        if (_selectedInteractableIndex < 0 || _selectedInteractableIndex >= NearbyInteractables.Count)
        {
            _selectedInteractableIndex = -1;
            return false;
        }

        entry = NearbyInteractables[_selectedInteractableIndex];
        return true;
    }

    private static bool TryGetSelectedDroppedItem(Player player, out DroppedItemGuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.DroppedItem)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
        if (_selectedDroppedItemIndex < 0 || _selectedDroppedItemIndex >= NearbyDroppedItems.Count)
        {
            _selectedDroppedItemIndex = -1;
            return false;
        }

        entry = NearbyDroppedItems[_selectedDroppedItemIndex];

        // Validate the item still exists and is active
        if (entry.ItemIndex < 0 || entry.ItemIndex >= Main.maxItems)
        {
            return false;
        }

        Item item = Main.item[entry.ItemIndex];
        if (!item.active || item.stack <= 0)
        {
            RefreshDroppedItemEntries(player);
            if (_selectedDroppedItemIndex < 0 || _selectedDroppedItemIndex >= NearbyDroppedItems.Count)
            {
                _selectedDroppedItemIndex = -1;
                return false;
            }

            entry = NearbyDroppedItems[_selectedDroppedItemIndex];
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

    private static string SanitizeLabel(string? text)
    {
        return TextSanitizer.Clean(text ?? string.Empty);
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

    private static void AnnounceSelectedEntry(SelectionMode category, string categoryLabel, string detail)
    {
        AnnounceCategoryEntry(category, categoryLabel, detail);
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

    private static string DescribeCursorStyleOffset(Player player, Vector2 targetPosition)
    {
        if (player is null || !player.active)
        {
            return string.Empty;
        }

        Vector2 origin = ResolvePlayerReferencePoint(player);
        int originTileX = (int)(origin.X / 16f);
        int originTileY = (int)(origin.Y / 16f);
        int targetTileX = (int)(targetPosition.X / 16f);
        int targetTileY = (int)(targetPosition.Y / 16f);

        int offsetX = targetTileX - originTileX;
        int offsetY = targetTileY - originTileY;

        if (offsetX == 0 && offsetY == 0)
        {
            return "origin";
        }

        List<string> parts = new(2);
        if (offsetX != 0)
        {
            string direction = offsetX > 0 ? "right" : "left";
            parts.Add($"{Math.Abs(offsetX)} {direction}");
        }

        if (offsetY != 0)
        {
            string direction = offsetY > 0 ? "down" : "up";
            parts.Add($"{Math.Abs(offsetY)} {direction}");
        }

        return string.Join(", ", parts);
    }

    private static Vector2 ResolvePlayerReferencePoint(Player player)
    {
        const float chestFraction = 0.25f;
        float verticalOffset = player.height * chestFraction * player.gravDir;
        return player.Center - new Vector2(0f, verticalOffset);
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
        EnsureTargetsUpToDate(player);

        switch (_selectionMode)
        {
            case SelectionMode.Waypoint when TryGetSelectedWaypoint(out Waypoint waypoint):
                worldPosition = waypoint.WorldPosition;
                label = SanitizeLabel(waypoint.Name);
                return true;
            case SelectionMode.Exploration when TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget exploration):
                worldPosition = exploration.WorldPosition;
                label = SanitizeLabel(exploration.Label);
                return true;
            case SelectionMode.Npc when TryGetSelectedNpc(player, out NPC npc, out NpcGuidanceEntry entry):
                worldPosition = npc.Bottom;
                label = SanitizeLabel(entry.DisplayName);
                return true;
            case SelectionMode.Interactable when TryGetSelectedInteractable(player, out InteractableGuidanceEntry interactable):
                worldPosition = interactable.WorldPosition;
                label = SanitizeLabel(interactable.DisplayName);
                return true;
            case SelectionMode.Player when TryGetSelectedPlayer(player, out Player targetPlayer, out PlayerGuidanceEntry playerEntry):
                worldPosition = targetPlayer.Bottom;
                label = SanitizeLabel(playerEntry.DisplayName);
                return true;
            case SelectionMode.DroppedItem when TryGetSelectedDroppedItem(player, out DroppedItemGuidanceEntry droppedItem):
                worldPosition = droppedItem.WorldPosition;
                label = SanitizeLabel(droppedItem.DisplayName);
                return true;
            default:
                worldPosition = default;
                label = string.Empty;
                return false;
        }
    }

    private static int ResolveTeleportStyleForSelection()
    {
        return _selectionMode == SelectionMode.Player
            ? TeleportationStyleID.TeleportationPotion
            : TeleportationStyleID.RodOfDiscord;
    }

    private static void UpdateProximityAnnouncement(Player player, Vector2 targetPosition, string targetLabel, float distanceTiles)
    {
        ProximityTargetKey key = ResolveProximityTargetKey(player);
        if (!_activeProximityTarget.Equals(key))
        {
            _activeProximityTarget = key;
            _lastProximityStepIndex = int.MaxValue;
        }

        if (distanceTiles <= ArrivalTileThreshold)
        {
            _lastProximityStepIndex = int.MaxValue;
            return;
        }

        float stepPosition = distanceTiles / ProximityAnnouncementStepTiles;
        int stepIndex = (int)MathF.Floor(stepPosition);
        if (_lastProximityStepIndex == int.MaxValue)
        {
            _lastProximityStepIndex = stepIndex;
            return;
        }

        float toleranceSteps = ProximityAnnouncementToleranceTiles / ProximityAnnouncementStepTiles;
        // Re-arm progress when backing out of the current band so new approaches retrigger updates.
        bool movedAway = stepIndex > _lastProximityStepIndex &&
            stepPosition >= (_lastProximityStepIndex + 1) - toleranceSteps;
        if (movedAway)
        {
            _lastProximityStepIndex = stepIndex;
            return;
        }

        bool crossedStep = stepPosition <= _lastProximityStepIndex - toleranceSteps;
        if (!crossedStep)
        {
            return;
        }

        string relative = DescribeRelativeOffset(player.Center, targetPosition);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return;
        }

        // Keep proximity callouts minimal: only report the relative offset, no target name prefix.
        ScreenReaderService.Announce(relative);
        _lastProximityStepIndex = stepIndex;
    }

    private static bool IsPingEnabledForCurrentSelection()
    {
        return _selectionMode switch
        {
            SelectionMode.Exploration => false,
            SelectionMode.None => false,
            _ => true
        };
    }

    private static ProximityTargetKey ResolveProximityTargetKey(Player player)
    {
        return _selectionMode switch
        {
            SelectionMode.Waypoint => new ProximityTargetKey(SelectionMode.Waypoint, _selectedIndex),
            SelectionMode.Npc when TryGetSelectedNpc(player, out _, out NpcGuidanceEntry npcEntry)
                => new ProximityTargetKey(SelectionMode.Npc, npcEntry.NpcIndex),
            SelectionMode.Player when TryGetSelectedPlayer(player, out _, out PlayerGuidanceEntry playerEntry)
                => new ProximityTargetKey(SelectionMode.Player, playerEntry.PlayerIndex),
            SelectionMode.Interactable when TryGetSelectedInteractable(player, out InteractableGuidanceEntry interactableEntry)
                => new ProximityTargetKey(SelectionMode.Interactable, HashCode.Combine(interactableEntry.Anchor.X, interactableEntry.Anchor.Y)),
            SelectionMode.Exploration when TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget explorationEntry)
                => new ProximityTargetKey(
                    SelectionMode.Exploration,
                    HashCode.Combine(explorationEntry.Key.SourceId, explorationEntry.Key.LocalId)),
            SelectionMode.DroppedItem when TryGetSelectedDroppedItem(player, out DroppedItemGuidanceEntry droppedItemEntry)
                => new ProximityTargetKey(SelectionMode.DroppedItem, droppedItemEntry.ItemIndex),
            _ => new ProximityTargetKey(SelectionMode.None, -1)
        };
    }

    private static bool IsExplorationTargetMatch(
        ExplorationTargetRegistry.ExplorationTarget candidate,
        ExplorationTargetRegistry.ExplorationTarget target)
    {
        if (candidate.Key.Equals(target.Key))
        {
            return true;
        }

        float deltaTiles = Vector2.Distance(candidate.WorldPosition, target.WorldPosition) / 16f;
        if (deltaTiles > ExplorationSelectionMatchToleranceTiles)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(target.Label))
        {
            return true;
        }

        string candidateLabel = SanitizeLabel(candidate.Label);
        string targetLabel = SanitizeLabel(target.Label);
        return string.Equals(candidateLabel, targetLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static void ResetProximityProgress()
    {
        _activeProximityTarget = new ProximityTargetKey(SelectionMode.None, -1);
        _lastProximityStepIndex = int.MaxValue;
    }
}
