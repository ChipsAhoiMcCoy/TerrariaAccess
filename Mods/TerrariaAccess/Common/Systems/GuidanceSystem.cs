#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Players;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Guidance;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem : ModSystem
{
    private const string WaypointListKey = "screenReaderWaypoints";
    private const string CustomTargetListKey = "screenReaderCustomTargets";
    private const string SelectedIndexKey = "screenReaderSelectedWaypoint";
    private const string SelectedCustomIndexKey = "screenReaderSelectedCustomTarget";
    private const string PersistentSelectionModeKey = "screenReaderPersistentGuidanceSelectionMode";
    private const string ExplorationModeKey = "screenReaderWaypointExplorationMode";
    private const string MultiplayerWaypointListKey = "screenReaderMultiplayerWaypoints";
    private const string MultiplayerCustomTargetListKey = "screenReaderMultiplayerCustomTargets";
    private const string MultiplayerSelectedIndexKey = "screenReaderMultiplayerSelectedWaypoint";
    private const string MultiplayerSelectedCustomIndexKey = "screenReaderMultiplayerSelectedCustomTarget";
    private const string MultiplayerPersistentSelectionModeKey = "screenReaderMultiplayerPersistentGuidanceSelectionMode";
    private const string MultiplayerExplorationModeKey = "screenReaderMultiplayerWaypointExplorationMode";

    internal const float ArrivalTileThreshold = 4f;
    private const int MaxPingDelayFrames = 54;
    private const float ScanRangeTiles = 90f;
    private const float ProximityAnnouncementStepTiles = 10f;
    private const float ProximityAnnouncementToleranceTiles = 0.35f;
    private const float ExplorationSelectionMatchToleranceTiles = 6f;

    private readonly record struct WaypointDataKeys(
        string WaypointList,
        string CustomTargetList,
        string SelectedWaypoint,
        string SelectedCustomTarget,
        string PersistentSelectionMode,
        string ExplorationMode);

    private static readonly WaypointDataKeys LocalWorldWaypointDataKeys = new(
        WaypointListKey,
        CustomTargetListKey,
        SelectedIndexKey,
        SelectedCustomIndexKey,
        PersistentSelectionModeKey,
        ExplorationModeKey);

    private static readonly WaypointDataKeys MultiplayerWorldWaypointDataKeys = new(
        MultiplayerWaypointListKey,
        MultiplayerCustomTargetListKey,
        MultiplayerSelectedIndexKey,
        MultiplayerSelectedCustomIndexKey,
        MultiplayerPersistentSelectionModeKey,
        MultiplayerExplorationModeKey);

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
        NamingDialog.Close(LogWaypoint);
        CustomTargetDialog.Close(LogWaypoint);
        DisposeToneResources();
    }

    public override void OnWorldUnload()
    {
        LogWaypoint($"OnWorldUnload: NetMode={Main.netMode}, WaypointCount={Waypoints.Count}, " +
                    $"SelectionMode={_selectionMode}, SelectedIndex={_selectedIndex}, NamingActive={NamingDialog.IsActive}");

        if (Main.netMode == NetmodeID.MultiplayerClient && Main.LocalPlayer is not null)
        {
            Main.LocalPlayer.GetModPlayer<GuidancePlayer>().CacheWaypointState();
        }

        ResetTrackingState();
        CloseNaming();
        CloseCustomTargetInput();
        DisposeToneResources();
        LogWaypoint("OnWorldUnload: Cleanup complete.");
    }

    public override void LoadWorldData(TagCompound tag)
    {
        LogWaypoint($"LoadWorldData: NetMode={Main.netMode}, WorldID={Main.worldID}, WorldName=\"{Main.worldName}\"");

        // Multiplayer clients use per-player cache via GuidancePlayer.OnEnterWorld
        // instead of world data (which won't exist since mod is client-side only)
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            LogWaypoint("LoadWorldData: Skipped (multiplayer client uses player cache instead).");
            return;
        }

        ResetTrackingState();
        bool loaded = LoadWaypointData(tag, "world save", announceSelection: false, ResolveWorldWaypointDataKeys());
        LogWaypoint($"LoadWorldData: Complete. HasData={loaded}, WaypointCount={Waypoints.Count}");
    }

    public override void SaveWorldData(TagCompound tag)
    {
        LogWaypoint($"SaveWorldData: NetMode={Main.netMode}, WaypointCount={Waypoints.Count}");
        SaveWaypointData(tag, "world save", normalizeRuntime: true, keys: ResolveWorldWaypointDataKeys());
    }

    public override void PostUpdateInput()
    {
        // PlayerInput.UpdateInput() resets WritingText = false every frame (line 742 in PlayerInput.cs).
        // This causes HandleIME() in the draw phase to disable the IME service, which stops
        // OS text input events from being delivered. Re-assert WritingText here so the IME
        // stays enabled and GetInputText() receives keystrokes (including Enter/Escape).
        if (NamingDialog.IsActive || CustomTargetDialog.IsActive)
        {
            PlayerInput.WritingText = true;
            Main.instance.HandleIME();
        }

        UpdateNaming();
        UpdateCustomTargetInput();
    }

    public override void PostUpdatePlayers()
    {
        if (Main.dedServ || Main.gameMenu || Main.inFancyUI || NamingDialog.IsActive || CustomTargetDialog.IsActive)
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

            // Check sweep mode FIRST for "All" selections (index = -1)
            // This must run before TryGetCurrentTrackingTarget, which returns false for "All" modes
            if (IsSweepModeActive())
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
                UpdateSweepPings(player);
                return;
            }

            // Not in sweep mode - reset cycle so next sweep starts fresh
            SweepScheduler.Reset();

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
                    // Check suppression (e.g., after teleporting, arrival is redundant)
                    if (!ScreenReaderService.CheckAndClearSuppression(SuppressionKeyArrival))
                    {
                        string arrivalMessage = $"Arrived at {arrivalLabel}";

                        // If category just changed, prefix arrival with category so user knows context
                        if (_includeCategoryInNextAnnouncement)
                        {
                            string categoryLabel = ResolveCategoryLabel(_selectionMode);
                            if (!string.IsNullOrWhiteSpace(categoryLabel))
                            {
                                arrivalMessage = $"{categoryLabel}. {arrivalMessage}";
                            }
                            _includeCategoryInNextAnnouncement = false;
                        }

                        ScreenReaderService.Announce(arrivalMessage);
                    }
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
                // Sweep mode already handled at start of function; just disable pinging here
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
                // Use hostile ping for hostile mob tracking, waypoint ping for everything else
                if (_selectionMode == SelectionMode.HostileMob)
                {
                    EmitHostilePing(player, targetPosition);
                }
                else
                {
                    EmitPing(player, targetPosition);
                }
                _nextPingUpdateFrame = ComputeNextPingFrame(player, targetPosition);
                LogPing($"Rescheduled next ping at frame {_nextPingUpdateFrame} after emit");
            }
        }

    }

    private static bool IsSweepModeActive()
    {
        return _selectionMode switch
        {
            SelectionMode.Custom => ShouldUseCustomSweepMode(),
            SelectionMode.DroppedItem when _selectedDroppedItemIndex < 0 => NearbyDroppedItems.Count > 0,
            SelectionMode.Critter when _selectedCritterIndex < 0 => NearbyCritters.Count > 0,
            SelectionMode.Plantlife when _selectedPlantlifeIndex < 0 => NearbyPlantlife.Count > 0,
            _ => false
        };
    }

    private static void UpdateSweepPings(Player player)
    {
        // Snapshot the sweep order once per cycle so player movement doesn't
        // cause jitter or restart the sweep mid-cycle.
        if (!SweepScheduler.IsCycleActive)
        {
            RefreshSweepOrder(player);
            if (!SweepScheduler.EnsureCycleStarted(SweepOrder.Count))
            {
                return;
            }
        }

        if (SweepOrder.Count == 0)
        {
            SweepScheduler.Reset();
            return;
        }

        if (SweepScheduler.IsWaiting(Main.GameUpdateCount))
        {
            return;
        }

        // Cycle complete - pause briefly then start a fresh snapshot
        if (SweepScheduler.HasCompletedCycle(SweepOrder.Count))
        {
            SweepScheduler.PauseUntilNextCycle(Main.GameUpdateCount);
            return;
        }

        SweepTarget target = SweepOrder[SweepScheduler.Cursor];

        // Use hostile ping for hostile mob sweep, waypoint ping for everything else
        if (_selectionMode == SelectionMode.HostileMob)
        {
            EmitHostilePing(player, target.WorldPosition);
        }
        else
        {
            EmitPing(player, target.WorldPosition);
        }

        SweepScheduler.Advance(Main.GameUpdateCount, SweepOrder.Count);
    }

    private static void RefreshSweepOrder(Player player)
    {
        SweepOrder.Clear();
        Vector2 origin = player.Center;

        switch (_selectionMode)
        {
            case SelectionMode.Custom:
                foreach (CustomGuidanceMatch match in NearbyCustomMatches)
                {
                    if (_selectedCustomIndex >= 0 && match.FilterIndex != _selectedCustomIndex)
                    {
                        continue;
                    }

                    SweepOrder.Add(new SweepTarget(match.Entry.WorldPosition, match.Entry.DistanceTiles));
                }
                break;
            case SelectionMode.DroppedItem when _selectedDroppedItemIndex < 0:
                foreach (GuidanceEntry entry in NearbyDroppedItems)
                {
                    SweepOrder.Add(new SweepTarget(entry.WorldPosition, entry.DistanceTiles));
                }
                break;
            case SelectionMode.Critter when _selectedCritterIndex < 0:
                foreach (GuidanceEntry entry in NearbyCritters)
                {
                    SweepOrder.Add(new SweepTarget(entry.WorldPosition, entry.DistanceTiles));
                }
                break;
            case SelectionMode.Plantlife when _selectedPlantlifeIndex < 0:
                foreach (GuidanceEntry entry in NearbyPlantlife)
                {
                    SweepOrder.Add(new SweepTarget(entry.WorldPosition, entry.DistanceTiles));
                }
                break;
        }

        if ((TerrariaAccessConfig.Instance?.GuidanceAllMode ?? GuidanceAllMode.Sweep) == GuidanceAllMode.NearestOnly
            && SweepOrder.Count > 1)
        {
            int nearestIndex = 0;
            for (int i = 1; i < SweepOrder.Count; i++)
            {
                if (SweepOrder[i].DistanceTiles < SweepOrder[nearestIndex].DistanceTiles)
                {
                    nearestIndex = i;
                }
            }

            SweepTarget nearest = SweepOrder[nearestIndex];
            SweepOrder.Clear();
            SweepOrder.Add(nearest);
            return;
        }

        // Sort by X position (left to right), then by Y, then by distance
        SweepOrder.Sort(static (left, right) =>
        {
            int compareX = left.WorldPosition.X.CompareTo(right.WorldPosition.X);
            if (compareX != 0)
            {
                return compareX;
            }

            int compareY = left.WorldPosition.Y.CompareTo(right.WorldPosition.Y);
            if (compareY != 0)
            {
                return compareY;
            }

            return left.DistanceTiles.CompareTo(right.DistanceTiles);
        });
    }

    private static bool ShouldUseCustomSweepMode()
    {
        if (_selectionMode != SelectionMode.Custom || CustomTargets.Count == 0)
        {
            return false;
        }

        return CountCustomMatchesForSelection(_selectedCustomIndex) > (_selectedCustomIndex < 0 ? 0 : 1);
    }

    private static void BeginNaming(Player player)
    {
        _nextPingUpdateFrame = -1;
        NamingDialog.Begin(player, BuildDefaultName(), Waypoints.Count, LogWaypoint);
    }

    private static void CloseNaming()
    {
        NamingDialog.Close(LogWaypoint);
    }

    private static void BeginCustomTargetInput(Player player)
    {
        _nextPingUpdateFrame = -1;
        CustomTargetDialog.Begin(player, LogWaypoint);
    }

    private static void CloseCustomTargetInput()
    {
        CustomTargetDialog.Close(LogWaypoint);
    }

    private static void UpdateNaming()
    {
        GuidanceNamingUpdateResult result = NamingDialog.Update(LogWaypoint);
        if (result.Kind == GuidanceNamingUpdateKind.None)
        {
            return;
        }

        if (result.Kind == GuidanceNamingUpdateKind.Confirmed)
        {
            LogWaypoint($"UpdateNaming: WaypointCountBefore={Waypoints.Count}");
            Waypoint waypoint = new(result.ResolvedName, result.WorldPosition);
            Waypoints.Add(waypoint);
            SendWaypointAddedToServer(waypoint);
            _selectedIndex = Waypoints.Count - 1;
            _selectionMode = SelectionMode.Waypoint;

            LogWaypoint($"UpdateNaming: Waypoint added. SelectedIndex={_selectedIndex}, " +
                        $"SelectionMode={_selectionMode}, TotalWaypoints={Waypoints.Count}, " +
                        $"NetMode={Main.netMode}");

            Player? owner = ResolveNamingPlayer(result.PlayerIndex);
            if (owner is not null)
            {
                RescheduleGuidancePing(owner);
                string announcement = ComposeCreationAnnouncement(result.ResolvedName, owner, result.WorldPosition);
                ScreenReaderService.Announce(announcement);
                EmitPing(owner, result.WorldPosition);
                LogWaypoint($"UpdateNaming: Announced creation: \"{announcement}\", Ping emitted for player {owner.whoAmI}.");
            }
            else
            {
                ScreenReaderService.Announce($"Created waypoint {result.ResolvedName}");
                LogWaypoint($"UpdateNaming: Owner player could not be resolved (index={result.PlayerIndex}). " +
                            "Announced without ping.");
            }

            ScreenReaderService.SuppressNext(SuppressionKeyArrival);
            return;
        }

        if (result.Kind == GuidanceNamingUpdateKind.Canceled)
        {
            Player? owner = ResolveNamingPlayer(result.PlayerIndex);
            if (owner is not null && _selectionMode == SelectionMode.Waypoint
                && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                RescheduleGuidancePing(owner);
            }
        }
    }

    private static void UpdateCustomTargetInput()
    {
        GuidanceCustomTargetInputUpdateResult result = CustomTargetDialog.Update(LogWaypoint);
        if (result.Kind == GuidanceCustomTargetInputUpdateKind.None)
        {
            return;
        }

        if (result.Kind == GuidanceCustomTargetInputUpdateKind.Confirmed)
        {
            Player? owner = ResolveNamingPlayer(result.PlayerIndex);
            if (owner is null)
            {
                ScreenReaderService.Announce("Custom tracker target could not be saved because the player was unavailable.");
                LogWaypoint($"UpdateCustomTargetInput: Owner player could not be resolved (index={result.PlayerIndex}).");
                return;
            }

            if (!TryResolveCustomTargetInput(result.RawInput, owner, out CustomGuidanceFilter filter, out string failure))
            {
                string announcement = string.IsNullOrWhiteSpace(failure)
                    ? "Custom tracker target not recognized."
                    : failure;
                ScreenReaderService.Announce(announcement, force: true);
                LogWaypoint($"UpdateCustomTargetInput: Failed to resolve \"{result.RawInput}\". Reason=\"{announcement}\"");
                return;
            }

            AddCustomTarget(owner, filter);
            return;
        }

        if (result.Kind == GuidanceCustomTargetInputUpdateKind.Canceled)
        {
            Player? owner = ResolveNamingPlayer(result.PlayerIndex);
            if (owner is not null && _selectionMode == SelectionMode.Custom)
            {
                RescheduleGuidancePing(owner);
            }
        }
    }

    private static Player? ResolveNamingPlayer(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
        {
            return null;
        }

        Player candidate = Main.player[playerIndex];
        return candidate?.active == true ? candidate : null;
    }

    private static void AddCustomTarget(Player player, CustomGuidanceFilter filter)
    {
        int existingIndex = FindExistingCustomTargetIndex(filter);
        if (existingIndex >= 0)
        {
            _selectionMode = SelectionMode.Custom;
            _selectedCustomIndex = existingIndex;
            RefreshCustomEntries(player);
            RescheduleGuidancePing(player);
            ScreenReaderService.Announce($"Custom tracker {SanitizeLabel(CustomTargets[existingIndex].Label)} is already saved.");
            AnnounceCustomTargetSelection(player);
            EmitCurrentGuidancePing(player);
            return;
        }

        CustomTargets.Add(filter);
        SendCustomTargetAddedToServer(filter);
        _selectionMode = SelectionMode.Custom;
        _selectedCustomIndex = CustomTargets.Count - 1;
        RefreshCustomEntries(player);
        RescheduleGuidancePing(player);

        bool hasTrackingPosition = TryGetCurrentTrackingTarget(player, out Vector2 trackingPosition, out _);
        string announcement = hasTrackingPosition
            ? ComposeCustomCreationAnnouncement(filter.Label, player, trackingPosition)
            : ComposeCustomCreationAnnouncement(filter.Label, player, null);
        ScreenReaderService.Announce(announcement);
        if (hasTrackingPosition)
        {
            EmitPing(player, trackingPosition);
        }
        ScreenReaderService.SuppressNext(SuppressionKeyArrival);
    }

    private static int FindExistingCustomTargetIndex(CustomGuidanceFilter target)
    {
        for (int i = 0; i < CustomTargets.Count; i++)
        {
            CustomGuidanceFilter existing = CustomTargets[i];
            if (existing.Kind == target.Kind &&
                existing.TypeId == target.TypeId &&
                existing.StyleId == target.StyleId &&
                existing.RequireLabelMatch == target.RequireLabelMatch &&
                string.Equals(SanitizeLabel(existing.Label), SanitizeLabel(target.Label), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private enum CustomTargetInputKind
    {
        Any,
        Tile,
        Object,
        Item,
        Npc,
        Enemy,
        Critter,
        Projectile,
        Player
    }

    private static bool TryResolveCustomTargetInput(
        string input,
        Player player,
        out CustomGuidanceFilter filter,
        out string failure)
    {
        filter = default;
        failure = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            failure = "Type a custom tracker target before pressing Enter.";
            return false;
        }

        ParseCustomTargetInput(input, out CustomTargetInputKind kind, out string selector);
        if (string.IsNullOrWhiteSpace(selector))
        {
            failure = "Type an ID or name after the custom tracker category.";
            return false;
        }

        bool resolved = kind switch
        {
            CustomTargetInputKind.Tile => TryResolveTileCustomTarget(selector, out filter),
            CustomTargetInputKind.Object => TryResolveObjectCustomTarget(selector, out filter),
            CustomTargetInputKind.Item => TryResolveDroppedItemCustomTarget(selector, out filter),
            CustomTargetInputKind.Npc => TryResolveNpcCustomTarget(selector, NpcInputCategory.Any, out filter),
            CustomTargetInputKind.Enemy => TryResolveNpcCustomTarget(selector, NpcInputCategory.Enemy, out filter),
            CustomTargetInputKind.Critter => TryResolveNpcCustomTarget(selector, NpcInputCategory.Critter, out filter),
            CustomTargetInputKind.Projectile => TryResolveProjectileCustomTarget(selector, out filter),
            CustomTargetInputKind.Player => TryResolvePlayerCustomTarget(selector, player, out filter),
            _ => TryResolveObjectCustomTarget(selector, out filter) ||
                 TryResolveDroppedItemCustomTarget(selector, out filter) ||
                 TryResolveNpcCustomTarget(selector, NpcInputCategory.Any, out filter) ||
                 TryResolveProjectileCustomTarget(selector, out filter) ||
                 TryResolvePlayerCustomTarget(selector, player, out filter)
        };

        if (resolved)
        {
            return true;
        }

        failure = kind switch
        {
            CustomTargetInputKind.Tile => $"Tile target {selector} was not recognized.",
            CustomTargetInputKind.Object => $"Object target {selector} was not recognized.",
            CustomTargetInputKind.Item => $"Item target {selector} was not recognized.",
            CustomTargetInputKind.Npc => $"NPC target {selector} was not recognized.",
            CustomTargetInputKind.Enemy => $"Enemy target {selector} was not recognized.",
            CustomTargetInputKind.Critter => $"Critter target {selector} was not recognized.",
            CustomTargetInputKind.Projectile => $"Projectile target {selector} was not recognized.",
            CustomTargetInputKind.Player => $"Player target {selector} was not recognized.",
            _ => $"Custom tracker target {selector} was not recognized. Try a category like tile, object, item, NPC, enemy, critter, or projectile before the ID or name."
        };
        return false;
    }

    private static void ParseCustomTargetInput(string input, out CustomTargetInputKind kind, out string selector)
    {
        string trimmed = input.Trim();
        int separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex > 0)
        {
            string prefix = trimmed[..separatorIndex].Trim();
            if (TryParseCustomTargetPrefix(prefix, out kind))
            {
                selector = trimmed[(separatorIndex + 1)..].Trim();
                return;
            }
        }

        int whitespaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
        if (whitespaceIndex > 0)
        {
            string prefix = trimmed[..whitespaceIndex].Trim();
            if (TryParseCustomTargetPrefix(prefix, out kind))
            {
                selector = trimmed[(whitespaceIndex + 1)..].Trim();
                return;
            }
        }

        kind = CustomTargetInputKind.Any;
        selector = trimmed;
    }

    private static bool TryParseCustomTargetPrefix(string prefix, out CustomTargetInputKind kind)
    {
        switch (NormalizeSelector(prefix))
        {
            case "tile":
            case "tiles":
                kind = CustomTargetInputKind.Tile;
                return true;
            case "object":
            case "objects":
            case "obj":
                kind = CustomTargetInputKind.Object;
                return true;
            case "item":
            case "items":
            case "drop":
            case "droppeditem":
            case "droppeditems":
                kind = CustomTargetInputKind.Item;
                return true;
            case "npc":
            case "npcs":
                kind = CustomTargetInputKind.Npc;
                return true;
            case "enemy":
            case "enemies":
            case "hostile":
            case "mob":
            case "mobs":
                kind = CustomTargetInputKind.Enemy;
                return true;
            case "critter":
            case "critters":
                kind = CustomTargetInputKind.Critter;
                return true;
            case "projectile":
            case "projectiles":
            case "proj":
                kind = CustomTargetInputKind.Projectile;
                return true;
            case "player":
            case "players":
                kind = CustomTargetInputKind.Player;
                return true;
            default:
                kind = CustomTargetInputKind.Any;
                return false;
        }
    }

    private enum NpcInputCategory
    {
        Any,
        Enemy,
        Critter
    }

    private static bool TryResolveObjectCustomTarget(string selector, out CustomGuidanceFilter filter)
    {
        filter = default;
        if (TryResolveItemType(selector, out int itemType) &&
            ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item) &&
            item.createTile >= 0 &&
            TryCreateTileFilter(
                item.createTile,
                ResolveCustomFilterLabel(Lang.GetItemNameValue(itemType), CustomTargets.Count),
                requireLabelMatch: false,
                item.placeStyle,
                out filter))
        {
            return true;
        }

        return TryResolveTileCustomTarget(selector, out filter);
    }

    private static bool TryResolveTileCustomTarget(string selector, out CustomGuidanceFilter filter)
    {
        filter = default;
        if (!TryResolveTileType(selector, out int tileType))
        {
            return false;
        }

        string label = ResolveTileTypeDisplayName(tileType);
        return TryCreateTileFilter(tileType, label, requireLabelMatch: false, styleId: -1, out filter);
    }

    private static bool TryCreateTileFilter(
        int tileType,
        string label,
        bool requireLabelMatch,
        int styleId,
        out CustomGuidanceFilter filter)
    {
        filter = default;
        if (tileType < 0 || tileType >= TileLoader.TileCount)
        {
            return false;
        }

        styleId = Math.Max(-1, styleId);
        filter = new CustomGuidanceFilter(
            CustomFilterKind.Tile,
            tileType,
            ResolveCustomFilterLabel(label, CustomTargets.Count),
            requireLabelMatch,
            styleId);
        return true;
    }

    private static bool TryResolveDroppedItemCustomTarget(string selector, out CustomGuidanceFilter filter)
    {
        filter = default;
        if (!TryResolveItemType(selector, out int itemType))
        {
            return false;
        }

        string label = ResolveCustomFilterLabel(Lang.GetItemNameValue(itemType), CustomTargets.Count);
        filter = new CustomGuidanceFilter(CustomFilterKind.DroppedItem, itemType, label);
        return true;
    }

    private static bool TryResolveNpcCustomTarget(string selector, NpcInputCategory category, out CustomGuidanceFilter filter)
    {
        filter = default;
        if (!TryResolveNpcType(selector, out int npcType) || npcType < 0 || npcType >= NPCLoader.NPCCount)
        {
            return false;
        }

        CustomFilterKind filterKind;
        string label;
        if (category == NpcInputCategory.Critter || NPCID.Sets.CountsAsCritter[npcType])
        {
            filterKind = CustomFilterKind.Critter;
            label = ResolveCustomFilterLabel(Lang.GetNPCNameValue(npcType), CustomTargets.Count);
        }
        else if (category == NpcInputCategory.Enemy || IsHostileNpcType(npcType))
        {
            filterKind = CustomFilterKind.HostileMob;
            label = ResolveCustomFilterLabel(Lang.GetNPCNameValue(npcType), CustomTargets.Count);
        }
        else
        {
            filterKind = CustomFilterKind.Npc;
            label = ResolveCustomFilterLabel(Lang.GetNPCNameValue(npcType), CustomTargets.Count);
        }

        filter = new CustomGuidanceFilter(filterKind, npcType, label);
        return true;
    }

    private static bool IsHostileNpcType(int npcType)
    {
        if (!ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC? npc))
        {
            return false;
        }

        return npc.lifeMax > 5 &&
               npc.damage > 0 &&
               !npc.townNPC &&
               !npc.friendly &&
               !NPCID.Sets.CountsAsCritter[npcType];
    }

    private static bool TryResolveProjectileCustomTarget(string selector, out CustomGuidanceFilter filter)
    {
        filter = default;
        if (!TryResolveProjectileType(selector, out int projectileType) ||
            projectileType < 0 ||
            projectileType >= ProjectileLoader.ProjectileCount)
        {
            return false;
        }

        string label = ResolveCustomFilterLabel(Lang.GetProjectileName(projectileType).Value, CustomTargets.Count);
        filter = new CustomGuidanceFilter(CustomFilterKind.Projectile, projectileType, label);
        return true;
    }

    private static bool TryResolvePlayerCustomTarget(string selector, Player owner, out CustomGuidanceFilter filter)
    {
        filter = default;
        string normalizedSelector = NormalizeSelector(selector);
        if (string.IsNullOrWhiteSpace(normalizedSelector))
        {
            return false;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            Player candidate = Main.player[i];
            if (candidate is null || !candidate.active || candidate == owner)
            {
                continue;
            }

            if (NormalizeSelector(candidate.name) == normalizedSelector)
            {
                filter = new CustomGuidanceFilter(CustomFilterKind.Player, 0, ResolvePlayerDisplayName(candidate));
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveItemType(string selector, out int itemType)
    {
        if (TryParseBoundedId(selector, 1, ItemLoader.ItemCount - 1, out itemType))
        {
            return true;
        }

        if (ItemID.Search.TryGetId(selector, out int searchedType) &&
            searchedType > ItemID.None &&
            searchedType < ItemLoader.ItemCount)
        {
            itemType = searchedType;
            return true;
        }

        string normalizedSelector = NormalizeSelector(selector);
        foreach (KeyValuePair<int, Item> entry in ContentSamples.ItemsByType)
        {
            if (entry.Key <= ItemID.None || entry.Key >= ItemLoader.ItemCount || entry.Value.IsAir)
            {
                continue;
            }

            if (SelectorMatches(normalizedSelector, Lang.GetItemNameValue(entry.Key)) ||
                SelectorMatches(normalizedSelector, entry.Value.Name) ||
                SelectorMatches(normalizedSelector, TryGetSearchName(ItemID.Search, entry.Key)))
            {
                itemType = entry.Key;
                return true;
            }
        }

        itemType = ItemID.None;
        return false;
    }

    private static bool TryResolveTileType(string selector, out int tileType)
    {
        if (TryParseBoundedId(selector, 0, TileLoader.TileCount - 1, out tileType))
        {
            return true;
        }

        if (TileID.Search.TryGetId(selector, out int searchedType) &&
            searchedType >= 0 &&
            searchedType < TileLoader.TileCount)
        {
            tileType = searchedType;
            return true;
        }

        string normalizedSelector = NormalizeSelector(selector);
        for (int type = 0; type < TileLoader.TileCount; type++)
        {
            if (SelectorMatches(normalizedSelector, TryGetSearchName(TileID.Search, type)) ||
                SelectorMatches(normalizedSelector, ResolveTileTypeDisplayName(type)))
            {
                tileType = type;
                return true;
            }
        }

        tileType = -1;
        return false;
    }

    private static bool TryResolveNpcType(string selector, out int npcType)
    {
        if (TryParseBoundedId(selector, 0, NPCLoader.NPCCount - 1, out npcType))
        {
            return true;
        }

        if (NPCID.Search.TryGetId(selector, out int searchedType) &&
            searchedType >= 0 &&
            searchedType < NPCLoader.NPCCount)
        {
            npcType = searchedType;
            return true;
        }

        string normalizedSelector = NormalizeSelector(selector);
        foreach (KeyValuePair<int, NPC> entry in ContentSamples.NpcsByNetId)
        {
            if (entry.Key < 0 || entry.Key >= NPCLoader.NPCCount)
            {
                continue;
            }

            if (SelectorMatches(normalizedSelector, Lang.GetNPCNameValue(entry.Key)) ||
                SelectorMatches(normalizedSelector, entry.Value.FullName) ||
                SelectorMatches(normalizedSelector, TryGetSearchName(NPCID.Search, entry.Key)))
            {
                npcType = entry.Key;
                return true;
            }
        }

        npcType = NPCID.None;
        return false;
    }

    private static bool TryResolveProjectileType(string selector, out int projectileType)
    {
        if (TryParseBoundedId(selector, 0, ProjectileLoader.ProjectileCount - 1, out projectileType))
        {
            return true;
        }

        if (ProjectileID.Search.TryGetId(selector, out int searchedType) &&
            searchedType >= 0 &&
            searchedType < ProjectileLoader.ProjectileCount)
        {
            projectileType = searchedType;
            return true;
        }

        string normalizedSelector = NormalizeSelector(selector);
        foreach (KeyValuePair<int, Projectile> entry in ContentSamples.ProjectilesByType)
        {
            if (entry.Key < 0 || entry.Key >= ProjectileLoader.ProjectileCount)
            {
                continue;
            }

            if (SelectorMatches(normalizedSelector, Lang.GetProjectileName(entry.Key).Value) ||
                SelectorMatches(normalizedSelector, TryGetSearchName(ProjectileID.Search, entry.Key)))
            {
                projectileType = entry.Key;
                return true;
            }
        }

        projectileType = -1;
        return false;
    }

    private static bool TryParseBoundedId(string selector, int min, int max, out int id)
    {
        if (int.TryParse(selector.Trim(), out id) && id >= min && id <= max)
        {
            return true;
        }

        id = min - 1;
        return false;
    }

    private static string ResolveTileTypeDisplayName(int tileType)
    {
        if (tileType < 0 || tileType >= TileLoader.TileCount)
        {
            return "Tile";
        }

        try
        {
            string mapName = Lang.GetMapObjectName(MapHelper.TileToLookup(tileType, 0));
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                return mapName;
            }
        }
        catch
        {
            // Some tile IDs do not have a base map entry.
        }

        string searchName = TryGetSearchName(TileID.Search, tileType);
        return !string.IsNullOrWhiteSpace(searchName) ? searchName : $"Tile {tileType}";
    }

    private static bool SelectorMatches(string normalizedSelector, string? candidate)
    {
        return !string.IsNullOrWhiteSpace(normalizedSelector) &&
               NormalizeSelector(candidate) == normalizedSelector;
    }

    private static string NormalizeSelector(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }

    private static string TryGetSearchName(ReLogic.Reflection.IdDictionary search, int id)
    {
        try
        {
            return search.GetName(id) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryResolveFocusedCustomTarget(Player player, out CustomGuidanceFilter filter, out Vector2 previewPosition)
    {
        filter = default;
        previewPosition = default;
        if (player is null || !player.active)
        {
            return false;
        }

        if (TryResolveSmartInteractCustomTarget(player, out filter, out previewPosition))
        {
            return true;
        }

        if (GamepadEmulationSystem.GetEffectiveSmartCursorState())
        {
            return TryCreateCustomTargetFromTile(Main.SmartCursorX, Main.SmartCursorY, out filter, out previewPosition);
        }

        Vector2 cursorWorld = Main.MouseWorld;
        if (TryResolveHoveredCustomEntityTarget(player, cursorWorld, out filter, out previewPosition))
        {
            return true;
        }

        int tileX = (int)(cursorWorld.X / 16f);
        int tileY = (int)(cursorWorld.Y / 16f);
        return TryCreateCustomTargetFromTile(tileX, tileY, out filter, out previewPosition);
    }

    private static bool TryResolveSmartInteractCustomTarget(Player player, out CustomGuidanceFilter filter, out Vector2 previewPosition)
    {
        filter = default;
        previewPosition = default;
        if (!Main.HasSmartInteractTarget)
        {
            return false;
        }

        int npcIndex = Main.SmartInteractNPC;
        if (npcIndex >= 0 && npcIndex < Main.maxNPCs)
        {
            NPC npc = Main.npc[npcIndex];
            if (npc.active)
            {
                return TryCreateCustomTargetFromNpc(npc, player, out filter, out previewPosition);
            }
        }

        int projectileIndex = Main.SmartInteractProj;
        if (projectileIndex >= 0 && projectileIndex < Main.maxProjectiles)
        {
            Projectile projectile = Main.projectile[projectileIndex];
            if (projectile.active)
            {
                filter = new CustomGuidanceFilter(CustomFilterKind.Projectile, projectile.type, ResolveProjectileDisplayName(projectile));
                previewPosition = projectile.Center;
                return true;
            }
        }

        return TryCreateCustomTargetFromTile(Main.SmartInteractX, Main.SmartInteractY, out filter, out previewPosition);
    }

    private static bool TryResolveHoveredCustomEntityTarget(Player player, Vector2 cursorWorld, out CustomGuidanceFilter filter, out Vector2 previewPosition)
    {
        filter = default;
        previewPosition = default;

        int hoveredOtherPlayerIndex = GetHoveredOtherPlayerIndex(player, cursorWorld);
        if (hoveredOtherPlayerIndex >= 0)
        {
            Player otherPlayer = Main.player[hoveredOtherPlayerIndex];
            filter = new CustomGuidanceFilter(CustomFilterKind.Player, 0, ResolvePlayerDisplayName(otherPlayer));
            previewPosition = otherPlayer.Center;
            return true;
        }

        int hoveredItemIndex = GetHoveredDroppedItemIndex(cursorWorld);
        if (hoveredItemIndex >= 0)
        {
            Item item = Main.item[hoveredItemIndex];
            filter = new CustomGuidanceFilter(CustomFilterKind.DroppedItem, item.type, ResolveDroppedItemBaseName(item));
            previewPosition = item.Center;
            return true;
        }

        int hoveredNpcIndex = GetHoveredNpcIndex(cursorWorld);
        if (hoveredNpcIndex >= 0)
        {
            NPC npc = Main.npc[hoveredNpcIndex];
            return TryCreateCustomTargetFromNpc(npc, player, out filter, out previewPosition);
        }

        return false;
    }

    private static bool TryCreateCustomTargetFromTile(int tileX, int tileY, out CustomGuidanceFilter filter, out Vector2 previewPosition)
    {
        filter = default;
        previewPosition = default;
        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return false;
        }

        if (!InGameNarrationSystem.CursorDescriptors.TryDescribe(tileX, tileY, out CursorDescriptorService.CursorDescriptor descriptor) ||
            descriptor.IsAir)
        {
            return false;
        }

        previewPosition = ResolveCustomTileWorldPosition(tileX, tileY);
        int styleId = -1;
        Tile tile = Main.tile[tileX, tileY];
        if (tile.HasTile)
        {
            CursorDescriptorService.TryResolveTileStyle(tile, descriptor.TileType, out styleId);
        }

        filter = new CustomGuidanceFilter(
            CustomFilterKind.Tile,
            descriptor.TileType,
            ResolveCustomFilterLabel(descriptor.Name, CustomTargets.Count),
            styleId: styleId);
        return true;
    }

    private static bool TryCreateCustomTargetFromNpc(NPC npc, Player player, out CustomGuidanceFilter filter, out Vector2 previewPosition)
    {
        filter = default;
        previewPosition = npc.Center;

        if (NPCID.Sets.CountsAsCritter[npc.type])
        {
            filter = new CustomGuidanceFilter(CustomFilterKind.Critter, npc.type, ResolveCritterDisplayName(npc));
            return true;
        }

        if (IsEligibleHostileMob(npc, player))
        {
            filter = new CustomGuidanceFilter(CustomFilterKind.HostileMob, npc.type, ResolveHostileMobDisplayName(npc));
            return true;
        }

        if (IsTrackableNpc(npc))
        {
            filter = new CustomGuidanceFilter(CustomFilterKind.Npc, npc.type, ResolveNpcDisplayName(npc));
            return true;
        }

        return false;
    }

    private static string ResolveCustomFilterLabel(string? rawName, int fallbackIndex)
    {
        string cleaned = SanitizeLabel(rawName);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        int index = fallbackIndex + 1;
        return $"Custom target {index}";
    }

    private static Vector2 ResolveCustomTileWorldPosition(int tileX, int tileY)
    {
        Vector2 tileCenter = new(tileX * 16f + 8f, tileY * 16f + 8f);
        if (!WorldGen.InWorld(tileX, tileY, 1))
        {
            return tileCenter;
        }

        Tile tile = Main.tile[tileX, tileY];
        if (!tile.HasTile)
        {
            return tileCenter;
        }

        if (IsTrackableTreeTile(tile.TileType))
        {
            return ResolveTreeTrackingWorldPosition(tileX, tileY);
        }

        TileObjectData? tileData = TileObjectData.GetTileData(tile.TileType, 0);
        if (tileData is null || tileData.Width <= 0 || tileData.Height <= 0 ||
            tileData.CoordinateHeights is null || tileData.CoordinateHeights.Length == 0)
        {
            return tileCenter;
        }

        int tileWidth = tileData.CoordinateWidth + tileData.CoordinatePadding;
        int subX = tileWidth > 0 ? (tile.TileFrameX % Math.Max(tileWidth * tileData.Width, 1)) / tileWidth : 0;
        int subY = ResolveTileFrameRow(tileData, tile.TileFrameY);
        int originX = tileX - subX;
        int originY = tileY - subY;
        Vector2 objectCenter = new(
            (originX + tileData.Width * 0.5f) * 16f,
            (originY + tileData.Height * 0.5f) * 16f);

        return IsValidWaypointPosition(objectCenter) ? objectCenter : tileCenter;
    }

    private static int ResolveTileFrameRow(TileObjectData tileData, int frameY)
    {
        if (tileData.Height <= 1)
        {
            return 0;
        }

        int cycleHeight = 0;
        for (int row = 0; row < tileData.Height; row++)
        {
            int rowHeight = tileData.CoordinateHeights[Math.Min(row, tileData.CoordinateHeights.Length - 1)] + tileData.CoordinatePadding;
            cycleHeight += Math.Max(1, rowHeight);
        }

        if (cycleHeight <= 0)
        {
            return 0;
        }

        int remaining = frameY % cycleHeight;
        for (int row = 0; row < tileData.Height; row++)
        {
            int rowHeight = tileData.CoordinateHeights[Math.Min(row, tileData.CoordinateHeights.Length - 1)] + tileData.CoordinatePadding;
            int step = Math.Max(1, rowHeight);
            if (remaining < step)
            {
                return row;
            }

            remaining -= step;
        }

        return 0;
    }

    private static bool IsTrackableTreeTile(int tileType)
    {
        return tileType == TileID.PalmTree ||
               tileType == TileID.MushroomTrees ||
               (tileType >= 0 && tileType < TileID.Sets.IsATreeTrunk.Length && TileID.Sets.IsATreeTrunk[tileType]);
    }

    private static Vector2 ResolveTreeTrackingWorldPosition(int tileX, int tileY)
    {
        int x = tileX;
        int y = tileY;
        Tile startTile = Framing.GetTileSafely(x, y);
        int tileType = startTile.TileType;

        if (tileType != TileID.PalmTree)
        {
            int frameCol = startTile.TileFrameX / 22;
            int frameRow = startTile.TileFrameY / 22;

            if (frameCol == 3 && frameRow <= 2)
            {
                x++;
            }
            else if (frameCol == 4 && frameRow >= 3 && frameRow <= 5)
            {
                x--;
            }
            else if (frameCol == 1 && frameRow >= 6 && frameRow <= 8)
            {
                x--;
            }
            else if (frameCol == 2 && frameRow >= 6 && frameRow <= 8)
            {
                x++;
            }
            else if (frameCol == 2 && frameRow >= 9)
            {
                x++;
            }
            else if (frameCol == 3 && frameRow >= 9)
            {
                x--;
            }
        }

        while (y < Main.maxTilesY - 2)
        {
            Tile candidate = Framing.GetTileSafely(x, y);
            if (!candidate.HasTile)
            {
                y++;
                continue;
            }

            if (candidate.TileType == TileID.PalmTree ||
                candidate.TileType == TileID.MushroomTrees ||
                (candidate.TileType >= 0 && candidate.TileType < TileID.Sets.IsATreeTrunk.Length && TileID.Sets.IsATreeTrunk[candidate.TileType]))
            {
                y++;
                continue;
            }

            break;
        }

        int trunkTileY = Math.Max(0, y - 1);
        return new Vector2(x * 16f + 8f, trunkTileY * 16f + 8f);
    }

    private static int GetHoveredNpcIndex(Vector2 cursorWorld)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active)
            {
                continue;
            }

            Rectangle bounds = npc.getRect();
            bounds.Inflate(4, 4);
            if (bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetHoveredOtherPlayerIndex(Player localPlayer, Vector2 cursorWorld)
    {
        if (Main.netMode == NetmodeID.SinglePlayer)
        {
            return -1;
        }

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            Player other = Main.player[i];
            if (!other.active || other.dead || other.ghost || other == localPlayer)
            {
                continue;
            }

            Rectangle bounds = other.getRect();
            bounds.Inflate(4, 4);
            if (bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetHoveredDroppedItemIndex(Vector2 cursorWorld)
    {
        for (int i = 0; i < Main.maxItems; i++)
        {
            Item item = Main.item[i];
            if (!item.active || item.stack <= 0)
            {
                continue;
            }

            Rectangle bounds = new((int)item.position.X, (int)item.position.Y, Math.Max(1, item.width), Math.Max(1, item.height));
            bounds.Inflate(4, 4);
            if (bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ResolvePlayerDisplayName(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.name) ? player.name : "Player";
    }

    private static string ResolveProjectileDisplayName(Projectile projectile)
    {
        string name = Lang.GetProjectileName(projectile.type).Value;
        return !string.IsNullOrWhiteSpace(name) ? name : $"Projectile {projectile.type}";
    }

    private static void LogPing(string message)
    {
        if (!LogGuidancePings)
        {
            return;
        }

        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info($"[GuidancePing] {message}");
    }

    private static void LogWaypoint(string message)
    {
        TerrariaAccess.Instance?.Logger.Info($"[Waypoint] {message}");
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
        RefreshCritterEntries(player);
        RefreshPlantlifeEntries(player);
        RefreshHostileMobEntries(player);
        RefreshCustomEntries(player);

        _lastTargetRefreshFrame = Main.GameUpdateCount;
        _lastTargetRefreshPlayerIndex = player.whoAmI;
    }

    internal static void HandleKeybinds(Player player)
    {
        if (NamingDialog.IsActive || CustomTargetDialog.IsActive)
        {
            return;
        }

        if (Main.dedServ || Main.gameMenu || Main.inFancyUI)
        {
            return;
        }

        if (player is null || !player.active || player.whoAmI != Main.myPlayer)
        {
            return;
        }

        EnsureTargetsUpToDate(player);

        if (GuidanceKeybinds.Create?.JustPressed ?? false)
        {
            LogWaypoint($"Create keybind pressed. Player={player.whoAmI}, Position=({player.Center.X:F1}, {player.Center.Y:F1}), " +
                        $"NamingActive={NamingDialog.IsActive}, GameMenu={Main.gameMenu}, InFancyUI={Main.inFancyUI}, " +
                        $"BlockInput={Main.blockInput}, WritingText={PlayerInput.WritingText}, " +
                        $"DrawingPlayerChat={Main.drawingPlayerChat}, EditSign={Main.editSign}, EditChest={Main.editChest}");
            if (_selectionMode == SelectionMode.Custom)
            {
                BeginCustomTargetInput(player);
            }
            else
            {
                BeginNaming(player);
            }
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
            // Contextual delete: if the user just cycled onto an individual buff via the status check,
            // cancel that buff instead of deleting a waypoint. Only fall through to waypoint deletion
            // when no buff is currently focused.
            if (StatusCheckSystem.TryCancelFocusedBuff(player))
            {
                return;
            }

            DeleteSelectedGuidanceTarget(player);
        }
    }

    private static void TeleportToTrackingTarget(Player player)
    {
        if (!TryResolveTeleportTarget(player, out TeleportTarget target))
        {
            ScreenReaderService.Announce("No active guidance target to teleport to.");
            return;
        }

        Vector2 destination = target.Destination;
        player.RemoveAllGrapplingHooks();

        if (target.UsePlayerTeleportPacket && Main.netMode == NetmodeID.MultiplayerClient)
        {
            // Match vanilla wormhole/player-map teleportation so the server treats this
            // as a player-to-player teleport instead of a generic position warp.
            player.UnityTeleport(destination);
        }
        else
        {
            player.Teleport(destination, target.Style);
            player.velocity = Vector2.Zero;
            player.fallStart = (int)(destination.Y / 16f);

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, player.whoAmI, destination.X, destination.Y, target.Style);
            }
        }

        _arrivalAnnounced = false;
        RescheduleGuidancePing(player);
        EmitCurrentGuidancePing(player);

        string announcement = string.IsNullOrWhiteSpace(target.Label)
            ? "Teleported to guidance target."
            : $"Teleported to {target.Label}.";
        ScreenReaderService.Announce(announcement);

        // Suppress redundant "Arrived at X" since we just announced teleport destination
        ScreenReaderService.SuppressNext(SuppressionKeyArrival);
    }

    private static bool TryResolveTeleportTarget(Player player, out TeleportTarget target)
    {
        if (_selectionMode == SelectionMode.Player && TryGetSelectedPlayer(player, out Player targetPlayer, out GuidanceEntry playerEntry))
        {
            Vector2 destination = ResolvePlayerTeleportDestination(player, targetPlayer);
            target = new TeleportTarget(
                destination,
                SanitizeLabel(playerEntry.DisplayName),
                TeleportationStyleID.RecallPotion,
                UsePlayerTeleportPacket: true);
            return true;
        }

        if (TryGetCurrentTrackingTarget(player, out Vector2 worldPosition, out string label))
        {
            target = new TeleportTarget(
                ResolveTeleportDestination(player, worldPosition),
                label,
                ResolveTeleportStyleForSelection(),
                UsePlayerTeleportPacket: false);
            return true;
        }

        if (_selectionMode == SelectionMode.Exploration && TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget exploration))
        {
            target = new TeleportTarget(
                ResolveTeleportDestination(player, exploration.WorldPosition),
                exploration.Label,
                TeleportationStyleID.RodOfDiscord,
                UsePlayerTeleportPacket: false);
            return true;
        }

        target = default;
        return false;
    }

    private static Vector2 ResolveTeleportDestination(Player player, Vector2 anchor)
    {
        Vector2 topLeft = GuidanceTeleportMath.AlignTopLeftToAnchorBottom(anchor, player.width, player.height);
        return GuidanceTeleportMath.ClampTopLeftToWorld(topLeft, player.width, player.height, Main.maxTilesX, Main.maxTilesY);
    }

    private static Vector2 ResolvePlayerTeleportDestination(Player player, Player targetPlayer)
    {
        return GuidanceTeleportMath.AlignTopLeftByBottomDelta(player.position, player.Bottom, targetPlayer.Bottom);
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

    private static (List<Waypoint> waypoints,
        List<CustomGuidanceFilter> customTargets,
        SelectionMode selectionMode,
        int selectedWaypointIndex,
        int selectedCustomIndex) BuildSerializableWaypointState(string source, bool normalizeRuntime = false)
    {
        List<Waypoint> sanitizedWaypoints = SanitizePersistentTargets(Waypoints, source, out int mappedWaypointSelection, _selectedIndex, _selectionMode == SelectionMode.Waypoint);
        List<CustomGuidanceFilter> sanitizedCustomTargets = SanitizeCustomFilters(CustomTargets, out int mappedCustomSelection, _selectedCustomIndex, _selectionMode == SelectionMode.Custom);

        SelectionMode selectionMode = _selectionMode;
        int selectedWaypointIndex = ClampPersistentSelection(_selectedIndex, sanitizedWaypoints.Count, mappedWaypointSelection, _selectionMode == SelectionMode.Waypoint);
        int selectedCustomIndex = ClampCustomSelectionIndex(_selectedCustomIndex, sanitizedCustomTargets.Count, mappedCustomSelection, _selectionMode == SelectionMode.Custom);

        if (selectionMode == SelectionMode.Waypoint && selectedWaypointIndex < 0)
        {
            selectionMode = SelectionMode.None;
        }
        else if (selectionMode == SelectionMode.Custom && sanitizedCustomTargets.Count == 0)
        {
            selectionMode = SelectionMode.None;
        }

        if (normalizeRuntime &&
            (sanitizedWaypoints.Count != Waypoints.Count ||
             sanitizedCustomTargets.Count != CustomTargets.Count ||
             selectedWaypointIndex != _selectedIndex ||
             selectedCustomIndex != _selectedCustomIndex ||
             selectionMode != _selectionMode))
        {
            Waypoints.Clear();
            Waypoints.AddRange(sanitizedWaypoints);
            CustomTargets.Clear();
            CustomTargets.AddRange(sanitizedCustomTargets);
            _selectedIndex = selectedWaypointIndex;
            _selectedCustomIndex = selectedCustomIndex;
            _selectionMode = selectionMode;
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
        }

        return (sanitizedWaypoints, sanitizedCustomTargets, selectionMode, selectedWaypointIndex, selectedCustomIndex);
    }

    private static WaypointDataKeys ResolveWorldWaypointDataKeys()
    {
        return Main.netMode == NetmodeID.Server
            ? MultiplayerWorldWaypointDataKeys
            : LocalWorldWaypointDataKeys;
    }

    internal static bool SaveWaypointData(TagCompound tag, string source, bool normalizeRuntime = true)
    {
        return SaveWaypointData(tag, source, normalizeRuntime, LocalWorldWaypointDataKeys);
    }

    private static bool SaveWaypointData(TagCompound tag, string source, bool normalizeRuntime, WaypointDataKeys keys)
    {
        LogWaypoint($"SaveWaypointData: source=\"{source}\", WaypointCount={Waypoints.Count}, CustomTargetCount={CustomTargets.Count}, " +
                    $"SelectionMode={_selectionMode}, SelectedIndex={_selectedIndex}, SelectedCustomIndex={_selectedCustomIndex}, " +
                    $"NormalizeRuntime={normalizeRuntime}");
        (List<Waypoint> waypoints,
            List<CustomGuidanceFilter> customTargets,
            SelectionMode selectionMode,
            int selectedWaypointIndex,
            int selectedCustomIndex) = BuildSerializableWaypointState(source, normalizeRuntime: normalizeRuntime);
        LogWaypoint($"SaveWaypointData: After sanitize: {waypoints.Count} waypoints, {customTargets.Count} custom targets, " +
                    $"SelectionMode={selectionMode}, SelectedWaypointIndex={selectedWaypointIndex}, SelectedCustomIndex={selectedCustomIndex}");

        bool hasData = false;
        hasData |= SerializePersistentTargets(tag, keys.WaypointList, waypoints);
        hasData |= SerializeCustomFilters(tag, keys.CustomTargetList, customTargets);

        SelectionMode persistentSelectionMode = selectionMode switch
        {
            SelectionMode.Exploration or SelectionMode.Waypoint or SelectionMode.Custom => selectionMode,
            _ => SelectionMode.None
        };

        if (persistentSelectionMode == SelectionMode.Waypoint && selectedWaypointIndex >= 0 && selectedWaypointIndex < waypoints.Count)
        {
            tag[keys.SelectedWaypoint] = selectedWaypointIndex;
            hasData = true;
        }
        else
        {
            tag.Remove(keys.SelectedWaypoint);
        }

        if (persistentSelectionMode == SelectionMode.Custom && selectedCustomIndex >= 0 && selectedCustomIndex < customTargets.Count)
        {
            tag[keys.SelectedCustomTarget] = selectedCustomIndex;
            hasData = true;
        }
        else
        {
            tag.Remove(keys.SelectedCustomTarget);
        }

        if (persistentSelectionMode == SelectionMode.Exploration)
        {
            tag[keys.ExplorationMode] = true;
            hasData = true;
        }
        else
        {
            tag.Remove(keys.ExplorationMode);
        }

        if (persistentSelectionMode != SelectionMode.None)
        {
            tag[keys.PersistentSelectionMode] = (int)persistentSelectionMode;
            hasData = true;
        }
        else
        {
            tag.Remove(keys.PersistentSelectionMode);
        }

        return hasData;
    }

    internal static bool LoadWaypointData(TagCompound tag, string source, bool announceSelection)
    {
        return LoadWaypointData(tag, source, announceSelection, LocalWorldWaypointDataKeys);
    }

    private static bool LoadWaypointData(TagCompound tag, string source, bool announceSelection, WaypointDataKeys keys)
    {
        LogWaypoint($"LoadWaypointData: source=\"{source}\", AnnounceSelection={announceSelection}, " +
                    $"HasWaypointList={tag.ContainsKey(keys.WaypointList)}, HasCustomTargetList={tag.ContainsKey(keys.CustomTargetList)}, " +
                    $"HasSelectedIndex={tag.ContainsKey(keys.SelectedWaypoint)}, HasSelectedCustomIndex={tag.ContainsKey(keys.SelectedCustomTarget)}, " +
                    $"HasSelectionMode={tag.ContainsKey(keys.PersistentSelectionMode)}, HasExplorationMode={tag.ContainsKey(keys.ExplorationMode)}");

        ResetWaypointSelectionState();

        LoadPersistentTargets(tag, keys.WaypointList, Waypoints, source, "waypoint");
        LoadCustomFilters(tag, keys.CustomTargetList, CustomTargets, source);

        if (tag.ContainsKey(keys.SelectedWaypoint))
        {
            int rawIndex = tag.GetInt(keys.SelectedWaypoint);
            _selectedIndex = Waypoints.Count > 0
                ? Math.Clamp(rawIndex, 0, Waypoints.Count - 1)
                : -1;
            LogWaypoint($"LoadWaypointData: SelectedWaypointIndex raw={rawIndex}, clamped={_selectedIndex}");
        }

        if (tag.ContainsKey(keys.SelectedCustomTarget))
        {
            int rawIndex = tag.GetInt(keys.SelectedCustomTarget);
            _selectedCustomIndex = CustomTargets.Count > 0
                ? Math.Clamp(rawIndex, 0, CustomTargets.Count - 1)
                : -1;
            LogWaypoint($"LoadWaypointData: SelectedCustomIndex raw={rawIndex}, clamped={_selectedCustomIndex}");
        }

        bool hasExplicitSelectionMode = tag.ContainsKey(keys.PersistentSelectionMode);
        SelectionMode persistentSelectionMode = hasExplicitSelectionMode
            ? (SelectionMode)tag.GetInt(keys.PersistentSelectionMode)
            : SelectionMode.None;
        bool explorationMode = tag.ContainsKey(keys.ExplorationMode) && tag.GetBool(keys.ExplorationMode);

        if (!hasExplicitSelectionMode)
        {
            if (explorationMode)
            {
                persistentSelectionMode = SelectionMode.Exploration;
            }
            else if (_selectedCustomIndex >= 0 && _selectedCustomIndex < CustomTargets.Count)
            {
                persistentSelectionMode = SelectionMode.Custom;
            }
            else if (_selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                persistentSelectionMode = SelectionMode.Waypoint;
            }
        }

        _selectionMode = persistentSelectionMode switch
        {
            SelectionMode.Exploration => SelectionMode.Exploration,
            SelectionMode.Custom when CustomTargets.Count > 0 => SelectionMode.Custom,
            SelectionMode.Waypoint when _selectedIndex >= 0 && _selectedIndex < Waypoints.Count => SelectionMode.Waypoint,
            _ => SelectionMode.None
        };

        if (_selectionMode != SelectionMode.Waypoint)
        {
            _selectedIndex = Math.Clamp(_selectedIndex, -1, Waypoints.Count - 1);
        }

        if (_selectionMode != SelectionMode.Custom)
        {
            _selectedCustomIndex = Math.Clamp(_selectedCustomIndex, -1, CustomTargets.Count - 1);
        }

        LogWaypoint($"LoadWaypointData: Final state: SelectionMode={_selectionMode}, SelectedWaypointIndex={_selectedIndex}, " +
                    $"SelectedCustomIndex={_selectedCustomIndex}, TotalWaypoints={Waypoints.Count}, " +
                    $"TotalCustomTargets={CustomTargets.Count}, ExplorationMode={explorationMode}");

        ClearCategoryAnnouncement();
        ResetProximityProgress();

        if (announceSelection && Main.LocalPlayer is { active: true } player)
        {
            if (_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                LogWaypoint($"LoadWaypointData: Rescheduling ping for waypoint \"{Waypoints[_selectedIndex].Name}\".");
                RescheduleGuidancePing(player);
            }
            else if (_selectionMode == SelectionMode.Custom && CustomTargets.Count > 0)
            {
                LogWaypoint($"LoadWaypointData: Rescheduling ping for custom target selection \"{(_selectedCustomIndex >= 0 && _selectedCustomIndex < CustomTargets.Count ? CustomTargets[_selectedCustomIndex].Label : "All")}\".");
                RescheduleGuidancePing(player);
            }
        }

        return Waypoints.Count > 0 || CustomTargets.Count > 0 || _selectionMode == SelectionMode.Exploration;
    }

    private static List<Waypoint> SanitizePersistentTargets(List<Waypoint> sourceTargets, string source, out int mappedSelection, int currentSelectionIndex, bool selectionBelongsToList)
    {
        List<Waypoint> sanitized = new(sourceTargets.Count);
        mappedSelection = -1;

        for (int i = 0; i < sourceTargets.Count; i++)
        {
            Waypoint target = sourceTargets[i];
            if (!TryCreateWaypoint(target.Name, target.WorldPosition.X, target.WorldPosition.Y, sanitized.Count, source, out Waypoint sanitizedTarget))
            {
                continue;
            }

            if (selectionBelongsToList && currentSelectionIndex == i)
            {
                mappedSelection = sanitized.Count;
            }

            sanitized.Add(sanitizedTarget);
        }

        return sanitized;
    }

    private static List<CustomGuidanceFilter> SanitizeCustomFilters(List<CustomGuidanceFilter> sourceFilters, out int mappedSelection, int currentSelectionIndex, bool selectionBelongsToList)
    {
        List<CustomGuidanceFilter> sanitized = new(sourceFilters.Count);
        mappedSelection = -1;

        for (int i = 0; i < sourceFilters.Count; i++)
        {
            CustomGuidanceFilter filter = sourceFilters[i];
            string label = ResolveCustomFilterLabel(filter.Label, sanitized.Count);
            CustomGuidanceFilter sanitizedFilter = new(filter.Kind, filter.TypeId, label, filter.RequireLabelMatch, filter.StyleId);

            if (selectionBelongsToList && currentSelectionIndex == i)
            {
                mappedSelection = sanitized.Count;
            }

            sanitized.Add(sanitizedFilter);
        }

        return sanitized;
    }

    private static int ClampPersistentSelection(int currentSelectionIndex, int totalCount, int mappedSelection, bool selectionBelongsToList)
    {
        if (totalCount == 0)
        {
            return -1;
        }

        if (selectionBelongsToList)
        {
            if (mappedSelection >= 0)
            {
                return mappedSelection;
            }

            return Math.Clamp(currentSelectionIndex, 0, totalCount - 1);
        }

        return Math.Clamp(currentSelectionIndex, -1, totalCount - 1);
    }

    private static int ClampCustomSelectionIndex(int currentSelectionIndex, int totalCount, int mappedSelection, bool selectionBelongsToList)
    {
        if (totalCount == 0)
        {
            return -1;
        }

        if (!selectionBelongsToList)
        {
            return Math.Clamp(currentSelectionIndex, -1, totalCount - 1);
        }

        if (currentSelectionIndex < 0)
        {
            return -1;
        }

        if (mappedSelection >= 0)
        {
            return mappedSelection;
        }

        return Math.Clamp(currentSelectionIndex, 0, totalCount - 1);
    }

    private static bool SerializePersistentTargets(TagCompound tag, string listKey, List<Waypoint> targets)
    {
        if (targets.Count == 0)
        {
            tag.Remove(listKey);
            return false;
        }

        List<TagCompound> serialized = new(targets.Count);
        foreach (Waypoint target in targets)
        {
            serialized.Add(new TagCompound
            {
                ["name"] = target.Name,
                ["x"] = target.WorldPosition.X,
                ["y"] = target.WorldPosition.Y,
            });
        }

        tag[listKey] = serialized;
        return true;
    }

    private static bool SerializeCustomFilters(TagCompound tag, string listKey, List<CustomGuidanceFilter> filters)
    {
        if (filters.Count == 0)
        {
            tag.Remove(listKey);
            return false;
        }

        List<TagCompound> serialized = new(filters.Count);
        foreach (CustomGuidanceFilter filter in filters)
        {
            serialized.Add(new TagCompound
            {
                ["kind"] = (int)filter.Kind,
                ["typeId"] = filter.TypeId,
                ["styleId"] = filter.StyleId,
                ["label"] = filter.Label,
                ["requireLabelMatch"] = filter.RequireLabelMatch,
            });
        }

        tag[listKey] = serialized;
        return true;
    }

    private static void LoadPersistentTargets(TagCompound tag, string listKey, List<Waypoint> destination, string source, string targetLabel)
    {
        if (!tag.ContainsKey(listKey))
        {
            LogWaypoint($"LoadWaypointData: No {targetLabel} list found in tag data.");
            return;
        }

        int loadedCount = 0;
        int droppedCount = 0;
        foreach (TagCompound entry in tag.GetList<TagCompound>(listKey))
        {
            if (!entry.ContainsKey("x") || !entry.ContainsKey("y"))
            {
                LogWaypointWarning($"Dropped {targetLabel} from {source}: missing coordinates.");
                droppedCount++;
                continue;
            }

            string name = entry.GetString("name");
            float x = entry.GetFloat("x");
            float y = entry.GetFloat("y");

            if (TryCreateWaypoint(name, x, y, destination.Count, source, out Waypoint target))
            {
                destination.Add(target);
                loadedCount++;
                LogWaypoint($"LoadWaypointData: Loaded {targetLabel} \"{target.Name}\" at ({x:F1}, {y:F1})");
            }
            else
            {
                droppedCount++;
            }
        }

        LogWaypoint($"LoadWaypointData: Loaded {loadedCount} {targetLabel}s, dropped {droppedCount}.");
    }

    private static void LoadCustomFilters(TagCompound tag, string listKey, List<CustomGuidanceFilter> destination, string source)
    {
        if (!tag.ContainsKey(listKey))
        {
            LogWaypoint("LoadWaypointData: No custom target list found in tag data.");
            return;
        }

        int loadedCount = 0;
        int droppedCount = 0;
        foreach (TagCompound entry in tag.GetList<TagCompound>(listKey))
        {
            if ((!entry.ContainsKey("kind") || !entry.ContainsKey("label")) &&
                entry.ContainsKey("x") && entry.ContainsKey("y"))
            {
                int tileX = (int)(entry.GetFloat("x") / 16f);
                int tileY = (int)(entry.GetFloat("y") / 16f);
                if (WorldGen.InWorld(tileX, tileY, 1) &&
                    InGameNarrationSystem.CursorDescriptors.TryDescribe(tileX, tileY, out CursorDescriptorService.CursorDescriptor descriptor) &&
                    !descriptor.IsAir)
                {
                    string legacyLabel = entry.ContainsKey("name") ? entry.GetString("name") : descriptor.Name;
                    string resolvedLabel = ResolveCustomFilterLabel(legacyLabel, destination.Count);
                    int legacyStyleId = -1;
                    Tile legacyTile = Main.tile[tileX, tileY];
                    if (legacyTile.HasTile)
                    {
                        CursorDescriptorService.TryResolveTileStyle(legacyTile, descriptor.TileType, out legacyStyleId);
                    }

                    destination.Add(new CustomGuidanceFilter(
                        CustomFilterKind.Tile,
                        descriptor.TileType,
                        resolvedLabel,
                        styleId: legacyStyleId));
                    loadedCount++;
                    LogWaypoint($"LoadWaypointData: Converted legacy custom target \"{resolvedLabel}\" to tile tracker.");
                    continue;
                }
            }

            if (!entry.ContainsKey("kind") || !entry.ContainsKey("label"))
            {
                LogWaypointWarning($"Dropped custom target from {source}: missing kind or label.");
                droppedCount++;
                continue;
            }

            CustomFilterKind kind = (CustomFilterKind)entry.GetInt("kind");
            int typeId = entry.ContainsKey("typeId") ? entry.GetInt("typeId") : 0;
            string label = ResolveCustomFilterLabel(entry.GetString("label"), destination.Count);
            bool requireLabelMatch = !entry.ContainsKey("requireLabelMatch") || entry.GetBool("requireLabelMatch");
            int styleId = entry.ContainsKey("styleId")
                ? entry.GetInt("styleId")
                : ResolveLegacyCustomFilterStyle(kind, typeId, label, requireLabelMatch);
            destination.Add(new CustomGuidanceFilter(kind, typeId, label, requireLabelMatch, styleId));
            loadedCount++;
            LogWaypoint($"LoadWaypointData: Loaded custom target \"{label}\" of kind {kind}.");
        }

        LogWaypoint($"LoadWaypointData: Loaded {loadedCount} custom targets, dropped {droppedCount}.");
    }

    private static int ResolveLegacyCustomFilterStyle(
        CustomFilterKind kind,
        int typeId,
        string label,
        bool requireLabelMatch)
    {
        if (kind != CustomFilterKind.Tile || requireLabelMatch)
        {
            return -1;
        }

        if (!TryResolveItemType(label, out int itemType) ||
            !ContentSamples.ItemsByType.TryGetValue(itemType, out Item? item) ||
            item.createTile != typeId)
        {
            return -1;
        }

        return Math.Max(-1, item.placeStyle);
    }

    private static bool TryCreateWaypoint(string? rawName, float x, float y, int fallbackIndex, string source, out Waypoint waypoint)
    {
        waypoint = default;

        Vector2 worldPosition = new(x, y);
        if (!IsValidWaypointPosition(worldPosition))
        {
            LogWaypointWarning($"Dropped waypoint {fallbackIndex + 1} from {source}: invalid position ({x}, {y}).");
            LogWaypoint($"TryCreateWaypoint FAILED: RawName=\"{rawName}\", Pos=({x}, {y}), " +
                        $"Source=\"{source}\", Reason=InvalidPosition, " +
                        $"WorldBounds=(16..{(Main.maxTilesX - 2) * 16f}, 16..{(Main.maxTilesY - 2) * 16f})");
            return false;
        }

        string resolvedName = ResolveWaypointName(rawName, fallbackIndex);
        waypoint = new Waypoint(resolvedName, worldPosition);
        LogWaypoint($"TryCreateWaypoint OK: RawName=\"{rawName}\", ResolvedName=\"{resolvedName}\", " +
                    $"Pos=({x:F1}, {y:F1}), Source=\"{source}\"");
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
        TerrariaAccess.Instance?.Logger.Warn($"[GuidanceSync] {message}");
    }

    private static void ResetWaypointSelectionState()
    {
        Waypoints.Clear();
        CustomTargets.Clear();
        NearbyCustomMatches.Clear();
        _selectionMode = SelectionMode.None;
        _selectedIndex = -1;
        _selectedCustomIndex = -1;
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
        SelectionMode.Custom,
        SelectionMode.DroppedItem,
        SelectionMode.Critter,
        SelectionMode.Plantlife,
        SelectionMode.HostileMob
    };

    private readonly record struct TeleportTarget(Vector2 Destination, string Label, int Style, bool UsePlayerTeleportPacket);

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
                _selectedCustomIndex = Math.Min(_selectedCustomIndex, CustomTargets.Count - 1);
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
                _selectedCustomIndex = Math.Min(_selectedCustomIndex, CustomTargets.Count - 1);
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
                    AnnounceCategorySelection("Crafting", "No crafting stations detected nearby.");
                    return;
                }

                _selectedInteractableIndex = 0;

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
                    AnnounceCategorySelection("NPCs", "No NPCs detected nearby.");
                    return;
                }

                _selectedNpcIndex = 0;

                BeginCategoryAnnouncement(SelectionMode.Npc);
                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Player:
                if (Main.netMode == NetmodeID.SinglePlayer)
                {
                    ScreenReaderService.Announce("Players is available only in multiplayer.");
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
                    AnnounceCategorySelection("Players", "No other active players detected.");
                    return;
                }

                _selectedPlayerIndex = 0;

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

                // Waypoints don't have an "All" mode - start at first waypoint
                _selectedIndex = 0;

                BeginCategoryAnnouncement(SelectionMode.Waypoint);
                RescheduleGuidancePing(player);
                AnnounceWaypointSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            case SelectionMode.Custom:
                _selectionMode = SelectionMode.Custom;
                ExplorationTargetRegistry.SetSelectedTarget(null);
                if (CustomTargets.Count == 0)
                {
                    _selectedCustomIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Custom", "No custom trackers saved. Press the create waypoint key to type a tracker target.");
                    return;
                }

                _selectedCustomIndex = -1;

                BeginCategoryAnnouncement(SelectionMode.Custom);
                RescheduleGuidancePing(player);
                AnnounceCustomTargetSelection(player);
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
                    AnnounceCategorySelection("Items", "No dropped items on screen.");
                    return;
                }

                _selectedDroppedItemIndex = -1;

                BeginCategoryAnnouncement(SelectionMode.DroppedItem);
                RescheduleGuidancePing(player);
                AnnounceDroppedItemEntry(player, NearbyDroppedItems.Count);
                return;
            case SelectionMode.Critter:
                _selectionMode = SelectionMode.Critter;
                ExplorationTargetRegistry.SetSelectedTarget(null);
                RefreshCritterEntries(player);
                if (NearbyCritters.Count == 0)
                {
                    _selectedCritterIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Critters", "No critters detected nearby.");
                    return;
                }

                _selectedCritterIndex = -1;

                BeginCategoryAnnouncement(SelectionMode.Critter);
                RescheduleGuidancePing(player);
                AnnounceCritterEntry(player, NearbyCritters.Count);
                return;
            case SelectionMode.Plantlife:
                _selectionMode = SelectionMode.Plantlife;
                ExplorationTargetRegistry.SetSelectedTarget(null);
                RefreshPlantlifeEntries(player);
                if (NearbyPlantlife.Count == 0)
                {
                    _selectedPlantlifeIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Plants", "No harvestable plants nearby.");
                    return;
                }

                _selectedPlantlifeIndex = -1;

                BeginCategoryAnnouncement(SelectionMode.Plantlife);
                RescheduleGuidancePing(player);
                AnnouncePlantlifeEntry(player, NearbyPlantlife.Count);
                return;
            case SelectionMode.HostileMob:
                _selectionMode = SelectionMode.HostileMob;
                ExplorationTargetRegistry.SetSelectedTarget(null);
                RefreshHostileMobEntries(player);
                if (NearbyHostileMobs.Count == 0)
                {
                    _selectedHostileMobIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Enemies", "No hostile enemies on screen.");
                    return;
                }

                // No "All" mode for hostile mobs - start at first enemy
                _selectedHostileMobIndex = 0;

                BeginCategoryAnnouncement(SelectionMode.HostileMob);
                RescheduleGuidancePing(player);
                AnnounceHostileMobSelection(player);
                EmitHostileMobSelectionPing(player);
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
            {
                int totalWaypoints = Waypoints.Count;
                if (totalWaypoints == 0)
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Waypoints", "No waypoints saved.");
                    return;
                }

                // Waypoints don't have an "All" mode - wrap directly between first and last
                if (_selectedIndex < 0)
                {
                    _selectedIndex = 0;
                }
                else
                {
                    _selectedIndex += direction;
                    if (_selectedIndex < 0)
                    {
                        // Wrap to last waypoint
                        _selectedIndex = totalWaypoints - 1;
                    }
                    else if (_selectedIndex >= totalWaypoints)
                    {
                        // Wrap to first waypoint
                        _selectedIndex = 0;
                    }
                }

                RescheduleGuidancePing(player);
                AnnounceWaypointSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            }
            case SelectionMode.Custom:
            {
                int totalCustomTargets = CustomTargets.Count;
                if (totalCustomTargets == 0)
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Custom", "No custom trackers saved. Press the create waypoint key to type a tracker target.");
                    return;
                }

                int totalSlots = totalCustomTargets + 1;
                int currentSlot = _selectedCustomIndex + 1;
                int nextSlot = Modulo(currentSlot + direction, totalSlots);
                _selectedCustomIndex = nextSlot - 1;

                RescheduleGuidancePing(player);
                AnnounceCustomTargetSelection(player);
                if (!IsSweepModeActive())
                {
                    EmitCurrentGuidancePing(player);
                }
                return;
            }
            case SelectionMode.Npc:
            {
                RefreshNpcEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedNpcIndex, NearbyNpcs.Count, direction))
                {
                    _selectedNpcIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("NPCs", "No NPCs detected nearby.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnounceNpcSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            }
            case SelectionMode.Interactable:
            {
                RefreshInteractableEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedInteractableIndex, NearbyInteractables.Count, direction))
                {
                    _selectedInteractableIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Crafting", "No crafting stations detected nearby.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnounceInteractableSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            }
            case SelectionMode.Player:
            {
                if (Main.netMode == NetmodeID.SinglePlayer)
                {
                    ScreenReaderService.Announce("Players is available only in multiplayer.");
                    return;
                }

                RefreshPlayerEntries(player);
                if (!TryAdvanceSelectionIndex(ref _selectedPlayerIndex, NearbyPlayers.Count, direction))
                {
                    _selectedPlayerIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Players", "No other active players detected.");
                    return;
                }

                RescheduleGuidancePing(player);
                AnnouncePlayerSelection(player);
                EmitCurrentGuidancePing(player);
                return;
            }
            case SelectionMode.Exploration:
            {
                RefreshExplorationEntries();
                int totalExploration = NearbyExplorationTargets.Count;
                if (totalExploration == 0)
                {
                    ClearCategoryAnnouncement();
                    AnnounceCategorySelection("Explore", "No exploration targets detected nearby.");
                    return;
                }

                int totalSlots = totalExploration + 1;
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
            }
            case SelectionMode.DroppedItem:
            {
                RefreshDroppedItemEntries(player);
                int totalItems = NearbyDroppedItems.Count;
                if (totalItems == 0)
                {
                    _selectedDroppedItemIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Items", "No dropped items on screen.");
                    return;
                }

                {
                    int totalSlots = totalItems + 1;
                    int currentSlot = _selectedDroppedItemIndex + 1;
                    int nextSlot = Modulo(currentSlot + direction, totalSlots);
                    _selectedDroppedItemIndex = nextSlot - 1;
                }

                RescheduleGuidancePing(player);
                AnnounceDroppedItemEntry(player, totalItems);
                if (_selectedDroppedItemIndex >= 0)
                {
                    EmitCurrentGuidancePing(player);
                }
                return;
            }
            case SelectionMode.Critter:
            {
                RefreshCritterEntries(player);
                int totalCritters = NearbyCritters.Count;
                if (totalCritters == 0)
                {
                    _selectedCritterIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Critters", "No critters detected nearby.");
                    return;
                }

                {
                    int totalSlots = totalCritters + 1;
                    int currentSlot = _selectedCritterIndex + 1;
                    int nextSlot = Modulo(currentSlot + direction, totalSlots);
                    _selectedCritterIndex = nextSlot - 1;
                }

                RescheduleGuidancePing(player);
                AnnounceCritterEntry(player, totalCritters);
                if (_selectedCritterIndex >= 0)
                {
                    EmitCurrentGuidancePing(player);
                }
                return;
            }
            case SelectionMode.Plantlife:
            {
                RefreshPlantlifeEntries(player);
                int totalPlants = NearbyPlantlife.Count;
                if (totalPlants == 0)
                {
                    _selectedPlantlifeIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Plants", "No harvestable plants nearby.");
                    return;
                }

                {
                    int totalSlots = totalPlants + 1;
                    int currentSlot = _selectedPlantlifeIndex + 1;
                    int nextSlot = Modulo(currentSlot + direction, totalSlots);
                    _selectedPlantlifeIndex = nextSlot - 1;
                }

                RescheduleGuidancePing(player);
                AnnouncePlantlifeEntry(player, totalPlants);
                if (_selectedPlantlifeIndex >= 0)
                {
                    EmitCurrentGuidancePing(player);
                }
                return;
            }
            case SelectionMode.HostileMob:
            {
                RefreshHostileMobEntries(player);
                int totalHostiles = NearbyHostileMobs.Count;
                if (totalHostiles == 0)
                {
                    _selectedHostileMobIndex = -1;
                    ClearCategoryAnnouncement();
                    RescheduleGuidancePing(player);
                    AnnounceCategorySelection("Enemies", "No hostile enemies on screen.");
                    return;
                }

                // No "All" mode - wrap directly between first and last
                if (_selectedHostileMobIndex < 0)
                {
                    _selectedHostileMobIndex = 0;
                }
                else
                {
                    _selectedHostileMobIndex += direction;
                    if (_selectedHostileMobIndex < 0)
                    {
                        // Wrap to last enemy
                        _selectedHostileMobIndex = totalHostiles - 1;
                    }
                    else if (_selectedHostileMobIndex >= totalHostiles)
                    {
                        // Wrap to first enemy
                        _selectedHostileMobIndex = 0;
                    }
                }

                RescheduleGuidancePing(player);
                AnnounceHostileMobSelection(player);
                EmitHostileMobSelectionPing(player);
                return;
            }
            default:
                ScreenReaderService.Announce("Select a custom, waypoint, player, NPC, or crafting category to browse entries.");
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

    private static void AnnounceCustomTargetSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Custom)
        {
            return;
        }

        string announcement = ComposeCustomTargetAnnouncement(player);
        AnnounceSelectedEntry(SelectionMode.Custom, "Custom", announcement);
    }

    private static void AnnounceNpcSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Npc)
        {
            return;
        }

        if (!TryGetSelectedNpc(player, out NPC npc, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("NPCs", "No NPCs detected nearby.");
            return;
        }

        int totalEntries = NearbyNpcs.Count;
        int position = _selectedNpcIndex + 1;
        string announcement = ComposeNpcAnnouncement(entry, player, npc.Center, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Npc, "NPCs", announcement);
    }

    private static void AnnounceInteractableSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Interactable)
        {
            return;
        }

        if (!TryGetSelectedInteractable(player, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Crafting", "No crafting stations detected nearby.");
            return;
        }

        int totalEntries = NearbyInteractables.Count;
        int position = _selectedInteractableIndex + 1;
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Interactable, "Crafting", announcement);
    }

    private static void AnnouncePlayerSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Player)
        {
            return;
        }

        if (!TryGetSelectedPlayer(player, out Player targetPlayer, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Players", "No other active players detected.");
            return;
        }

        int totalEntries = NearbyPlayers.Count;
        int position = _selectedPlayerIndex + 1;
        string announcement = ComposePlayerAnnouncement(entry, player, targetPlayer.Center, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Player, "Players", announcement);
    }

    private static void AnnounceDroppedItemSelection(Player player)
    {
        if (_selectionMode != SelectionMode.DroppedItem)
        {
            return;
        }

        if (_selectedDroppedItemIndex < 0)
        {
            int total = NearbyDroppedItems.Count + 1; // +1 for "All" option
            AnnounceCategorySelection("Items", $"All, 1 of {total}");
            return;
        }

        if (!TryGetSelectedDroppedItem(player, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Items", "No dropped items on screen.");
            return;
        }

        int totalEntries = NearbyDroppedItems.Count + 1; // +1 for "All" option
        int position = _selectedDroppedItemIndex + 2; // +2 because "All" is position 1
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.DroppedItem, "Items", announcement);
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

    private static void AnnounceDroppedItemEntry(Player player, int totalEntries)
    {
        if (_selectionMode != SelectionMode.DroppedItem)
        {
            return;
        }

        if (_selectedDroppedItemIndex < 0)
        {
            int total = totalEntries + 1; // +1 for "All" option
            AnnounceSelectedEntry(SelectionMode.DroppedItem, "Items", $"All, 1 of {total}");
            return;
        }

        AnnounceDroppedItemSelection(player);
    }

    private static void AnnounceCritterSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Critter)
        {
            return;
        }

        if (_selectedCritterIndex < 0)
        {
            int total = NearbyCritters.Count + 1; // +1 for "All" option
            AnnounceCategorySelection("Critters", $"All, 1 of {total}");
            return;
        }

        if (!TryGetSelectedCritter(player, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Critters", "No critters detected nearby.");
            return;
        }

        int totalEntries = NearbyCritters.Count + 1; // +1 for "All" option
        int position = _selectedCritterIndex + 2; // +2 because "All" is position 1
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Critter, "Critters", announcement);
    }

    private static void AnnounceCritterEntry(Player player, int totalEntries)
    {
        if (_selectionMode != SelectionMode.Critter)
        {
            return;
        }

        if (_selectedCritterIndex < 0)
        {
            int total = totalEntries + 1; // +1 for "All" option
            AnnounceSelectedEntry(SelectionMode.Critter, "Critters", $"All, 1 of {total}");
            return;
        }

        AnnounceCritterSelection(player);
    }

    private static void AnnouncePlantlifeSelection(Player player)
    {
        if (_selectionMode != SelectionMode.Plantlife)
        {
            return;
        }

        if (_selectedPlantlifeIndex < 0)
        {
            int total = NearbyPlantlife.Count + 1; // +1 for "All" option
            AnnounceCategorySelection("Plants", $"All, 1 of {total}");
            return;
        }

        if (!TryGetSelectedPlantlife(player, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Plants", "No harvestable plants nearby.");
            return;
        }

        int totalEntries = NearbyPlantlife.Count + 1; // +1 for "All" option
        int position = _selectedPlantlifeIndex + 2; // +2 because "All" is position 1
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.Plantlife, "Plants", announcement);
    }

    private static void AnnouncePlantlifeEntry(Player player, int totalEntries)
    {
        if (_selectionMode != SelectionMode.Plantlife)
        {
            return;
        }

        if (_selectedPlantlifeIndex < 0)
        {
            int total = totalEntries + 1; // +1 for "All" option
            AnnounceSelectedEntry(SelectionMode.Plantlife, "Plants", $"All, 1 of {total}");
            return;
        }

        AnnouncePlantlifeSelection(player);
    }

    private static void AnnounceHostileMobSelection(Player player)
    {
        if (_selectionMode != SelectionMode.HostileMob)
        {
            return;
        }

        if (!TryGetSelectedHostileMob(player, out GuidanceEntry entry))
        {
            ClearCategoryAnnouncement();
            AnnounceCategorySelection("Enemies", "No hostile enemies on screen.");
            return;
        }

        int totalEntries = NearbyHostileMobs.Count;
        int position = _selectedHostileMobIndex + 1;
        string announcement = ComposeEntityAnnouncement(entry.DisplayName, player, entry.WorldPosition, position, totalEntries);
        AnnounceSelectedEntry(SelectionMode.HostileMob, "Enemies", announcement);
    }

    private static bool TryGetSelectedCritter(Player player, out GuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.Critter)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
        if (_selectedCritterIndex < 0 || _selectedCritterIndex >= NearbyCritters.Count)
        {
            _selectedCritterIndex = -1;
            return false;
        }

        entry = NearbyCritters[_selectedCritterIndex];
        return true;
    }

    private static bool TryGetSelectedPlantlife(Player player, out GuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.Plantlife)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
        if (_selectedPlantlifeIndex < 0 || _selectedPlantlifeIndex >= NearbyPlantlife.Count)
        {
            _selectedPlantlifeIndex = -1;
            return false;
        }

        entry = NearbyPlantlife[_selectedPlantlifeIndex];
        return true;
    }

    private static bool TryGetSelectedHostileMob(Player player, out GuidanceEntry entry)
    {
        entry = default;
        if (_selectionMode != SelectionMode.HostileMob)
        {
            return false;
        }

        EnsureTargetsUpToDate(player);
        if (_selectedHostileMobIndex < 0 || _selectedHostileMobIndex >= NearbyHostileMobs.Count)
        {
            _selectedHostileMobIndex = -1;
            return false;
        }

        entry = NearbyHostileMobs[_selectedHostileMobIndex];

        // Validate the NPC still exists and is active
        if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
        {
            return false;
        }

        NPC npc = Main.npc[entry.Index];
        if (!npc.active || npc.friendly || npc.townNPC)
        {
            RefreshHostileMobEntries(player);
            if (_selectedHostileMobIndex < 0 || _selectedHostileMobIndex >= NearbyHostileMobs.Count)
            {
                _selectedHostileMobIndex = -1;
                return false;
            }

            entry = NearbyHostileMobs[_selectedHostileMobIndex];
        }

        return true;
    }

    private static string ComposeNpcAnnouncement(GuidanceEntry entry, Player player, Vector2 npcPosition, int position, int total)
    {
        return ComposeEntityAnnouncement(entry.DisplayName, player, npcPosition, position, total);
    }

    private static string ComposePlayerAnnouncement(GuidanceEntry entry, Player player, Vector2 targetPlayerPosition, int position, int total)
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

    private static void DeleteSelectedGuidanceTarget(Player player)
    {
        if (_selectionMode == SelectionMode.Custom)
        {
            DeleteSelectedCustomTarget(player);
            return;
        }

        DeleteSelectedWaypoint(player);
    }

    private static void DeleteSelectedWaypoint(Player player)
    {
        if (Waypoints.Count == 0)
        {
            LogWaypoint("DeleteSelectedWaypoint: No waypoints exist.");
            ScreenReaderService.Announce("No waypoints saved.");
            return;
        }

        if (_selectionMode != SelectionMode.Waypoint || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            LogWaypoint($"DeleteSelectedWaypoint: No waypoint selected. SelectionMode={_selectionMode}, " +
                        $"SelectedIndex={_selectedIndex}, WaypointCount={Waypoints.Count}");
            ScreenReaderService.Announce("No waypoint selected.");
            return;
        }

        int removedIndex = _selectedIndex;
        Waypoint removed = Waypoints[removedIndex];
        LogWaypoint($"DeleteSelectedWaypoint: Removing waypoint at index {removedIndex}. " +
                    $"Name=\"{removed.Name}\", Position=({removed.WorldPosition.X:F1}, {removed.WorldPosition.Y:F1}), " +
                    $"TotalBefore={Waypoints.Count}, NetMode={Main.netMode}");
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

    private static void DeleteSelectedCustomTarget(Player player)
    {
        if (CustomTargets.Count == 0)
        {
            LogWaypoint("DeleteSelectedCustomTarget: No custom targets exist.");
            ScreenReaderService.Announce("No custom trackers saved.");
            return;
        }

        if (_selectionMode != SelectionMode.Custom)
        {
            ScreenReaderService.Announce("No custom target selected.");
            return;
        }

        if (_selectedCustomIndex < 0)
        {
            ScreenReaderService.Announce("Select a custom tracker before deleting.");
            return;
        }

        if (_selectedCustomIndex >= CustomTargets.Count)
        {
            LogWaypoint($"DeleteSelectedCustomTarget: No custom target selected. SelectionMode={_selectionMode}, " +
                        $"SelectedCustomIndex={_selectedCustomIndex}, CustomTargetCount={CustomTargets.Count}");
            ScreenReaderService.Announce("No custom target selected.");
            return;
        }

        int removedIndex = _selectedCustomIndex;
        CustomGuidanceFilter removed = CustomTargets[removedIndex];
        LogWaypoint($"DeleteSelectedCustomTarget: Removing custom target at index {removedIndex}. " +
                    $"Name=\"{removed.Label}\", Kind={removed.Kind}, TotalBefore={CustomTargets.Count}, NetMode={Main.netMode}");
        CustomTargets.RemoveAt(removedIndex);
        SendCustomTargetDeletedToServer(removedIndex);
        RefreshCustomEntries(player);

        if (CustomTargets.Count == 0)
        {
            _selectedCustomIndex = -1;
            _selectionMode = SelectionMode.Custom;
            ClearCategoryAnnouncement();
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            ScreenReaderService.Announce($"Deleted custom tracker {SanitizeLabel(removed.Label)}.");
            AnnounceCategorySelection("Custom", "No custom trackers saved. Press the create waypoint key to type a tracker target.");
            return;
        }

        if (_selectedCustomIndex >= CustomTargets.Count)
        {
            _selectedCustomIndex = CustomTargets.Count - 1;
        }

        string nextAnnouncement = ComposeCustomTargetAnnouncement(player);
        ScreenReaderService.Announce($"Deleted custom tracker {SanitizeLabel(removed.Label)}.");
        AnnounceSelectedEntry(SelectionMode.Custom, "Custom", nextAnnouncement);
        RescheduleGuidancePing(player);
        if (!IsSweepModeActive())
        {
            EmitCurrentGuidancePing(player);
        }
    }

    private static void AnnounceDisabledSelection()
    {
        ClearCategoryAnnouncement();
        AnnounceCategorySelection("Off", string.Empty);
    }

    private static void AnnounceExplorationSelection()
    {
        ClearCategoryAnnouncement();
        AnnounceCategorySelection("Explore", "All nearby interactables");
        ExplorationTargetRegistry.SetSelectedTarget(null);
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint, Player player)
    {
        return ComposePersistentTargetAnnouncement(waypoint, player, _selectedIndex + 1, Waypoints.Count);
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

    private static string ComposeCustomTargetAnnouncement(Player player)
    {
        if (_selectedCustomIndex < 0)
        {
            int allTotalSlots = CustomTargets.Count + 1;
            int totalMatches = NearbyCustomMatches.Count;
            string allDetail = totalMatches > 0
                ? $"{totalMatches} matches nearby"
                : "No tracked matches nearby";
            return $"All, 1 of {allTotalSlots}, {allDetail}";
        }

        if (_selectedCustomIndex >= CustomTargets.Count)
        {
            return "Custom target unavailable";
        }

        CustomGuidanceFilter customTarget = CustomTargets[_selectedCustomIndex];
        int totalSlots = CustomTargets.Count + 1;
        int position = _selectedCustomIndex + 2;
        int matchCount = CountCustomMatchesForSelection(_selectedCustomIndex);
        string ordinal = FormatEntryOrdinal(position, totalSlots);
        string label = string.IsNullOrWhiteSpace(ordinal)
            ? customTarget.Label
            : $"{customTarget.Label} {ordinal}";
        string detail = matchCount > 0
            ? $"{matchCount} matches nearby"
            : "No matches nearby";

        if (matchCount == 1 && TryGetCurrentTrackingTarget(player, out Vector2 worldPosition, out _))
        {
            string relative = DescribeCursorStyleOffset(player, worldPosition);
            return TextSanitizer.JoinWithComma(label, detail, relative);
        }

        return TextSanitizer.JoinWithComma(label, detail);
    }

    private static string ComposeCustomCreationAnnouncement(string targetName, Player player, Vector2? worldPosition)
    {
        string sanitizedName = SanitizeLabel(targetName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "target";
        }

        if (worldPosition is null)
        {
            return $"Added custom tracker {sanitizedName}. No matches nearby.";
        }

        string relative = DescribeRelativeOffset(player.Center, worldPosition.Value);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return $"Added custom tracker {sanitizedName}";
        }

        return $"Added custom tracker {sanitizedName}, {relative}";
    }

    private static string ComposePersistentTargetAnnouncement(Waypoint target, Player player, int position, int total)
    {
        string targetName = SanitizeLabel(target.Name);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            targetName = "target";
        }

        string ordinal = FormatEntryOrdinal(position, total);
        string label = string.IsNullOrWhiteSpace(ordinal)
            ? targetName
            : $"{targetName} {ordinal}";

        string relative = DescribeCursorStyleOffset(player, target.WorldPosition);
        return TextSanitizer.JoinWithComma(label, relative);
    }

    private static bool TryGetSelectedNpc(Player player, out NPC npc, out GuidanceEntry entry)
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
        if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
        {
            return false;
        }

        npc = Main.npc[entry.Index];
        if (!IsTrackableNpc(npc))
        {
            RefreshNpcEntries(player);
            if (_selectedNpcIndex < 0 || _selectedNpcIndex >= NearbyNpcs.Count)
            {
                _selectedNpcIndex = -1;
                return false;
            }

            entry = NearbyNpcs[_selectedNpcIndex];
            if (entry.Index < 0 || entry.Index >= Main.maxNPCs)
            {
                return false;
            }

            npc = Main.npc[entry.Index];
            if (!IsTrackableNpc(npc))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetSelectedPlayer(Player owner, out Player target, out GuidanceEntry entry)
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
        if (entry.Index < 0 || entry.Index >= Main.maxPlayers)
        {
            return false;
        }

        target = Main.player[entry.Index];
        if (!IsTrackablePlayer(target, owner))
        {
            RefreshPlayerEntries(owner);
            if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= NearbyPlayers.Count)
            {
                _selectedPlayerIndex = -1;
                return false;
            }

            entry = NearbyPlayers[_selectedPlayerIndex];
            if (entry.Index < 0 || entry.Index >= Main.maxPlayers)
            {
                return false;
            }

            target = Main.player[entry.Index];
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

    private static bool TryGetSelectedInteractable(Player player, out GuidanceEntry entry)
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

    private static bool TryGetSelectedDroppedItem(Player player, out GuidanceEntry entry)
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
        if (entry.Index < 0 || entry.Index >= Main.maxItems)
        {
            return false;
        }

        Item item = Main.item[entry.Index];
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

        // Category is being announced, so don't include it again in subsequent announcements
        _includeCategoryInNextAnnouncement = false;

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

        // Use centralized speech queue: enqueue category as prefix if switching categories
        bool includeCategory = _lastAnnouncedCategory != category;
        _lastAnnouncedCategory = category;

        if (includeCategory && !string.IsNullOrWhiteSpace(categoryLabel))
        {
            ScreenReaderService.EnqueuePrefix(categoryLabel);
            // Category is being announced, so don't include it again in arrival
            _includeCategoryInNextAnnouncement = false;
        }

        // The enqueued prefix (if any) will be automatically prepended by SpeechController
        ScreenReaderService.Announce(detail);
    }

    private static void AnnounceSelectedEntry(SelectionMode category, string categoryLabel, string detail)
    {
        AnnounceCategoryEntry(category, categoryLabel, detail);
    }

    /// <summary>
    /// Marks the start of a category selection, forcing the next announcement to include the category label.
    /// Uses the centralized speech queue system via EnqueuePrefix.
    /// </summary>
    private static void BeginCategoryAnnouncement(SelectionMode category)
    {
        // Clear any pending prefixes from previous context
        ScreenReaderService.ClearAllPrefixes();
        // Force category to be announced by resetting tracking
        _lastAnnouncedCategory = SelectionMode.None;
        // Ensure any immediate announcements (like arrival) also include the category
        _includeCategoryInNextAnnouncement = true;
        // Suppress immediate arrival announcement - we're about to announce the selection,
        // so saying "Arrived at X" right after would be redundant
        ScreenReaderService.SuppressNext(SuppressionKeyArrival);
    }

    /// <summary>
    /// Clears category announcement state and any pending speech prefixes.
    /// </summary>
    private static void ClearCategoryAnnouncement()
    {
        ScreenReaderService.ClearAllPrefixes();
        _lastAnnouncedCategory = SelectionMode.None;
        _includeCategoryInNextAnnouncement = false;
    }

    /// <summary>
    /// Resolves the display label for a category, used for announcements.
    /// </summary>
    private static string ResolveCategoryLabel(SelectionMode mode)
    {
        return mode switch
        {
            SelectionMode.Exploration => "Explore",
            SelectionMode.Npc => "NPCs",
            SelectionMode.Player => "Players",
            SelectionMode.Interactable => "Crafting",
            SelectionMode.Waypoint => "Waypoints",
            SelectionMode.Custom => "Custom",
            SelectionMode.DroppedItem => "Items",
            SelectionMode.Critter => "Critters",
            SelectionMode.Plantlife => "Plants",
            SelectionMode.HostileMob => "Enemies",
            SelectionMode.None => "Off",
            _ => string.Empty
        };
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

    private static bool TryGetSelectedCustomTarget(out CustomGuidanceFilter target)
    {
        if (_selectionMode == SelectionMode.Custom && _selectedCustomIndex >= 0 && _selectedCustomIndex < CustomTargets.Count)
        {
            target = CustomTargets[_selectedCustomIndex];
            return true;
        }

        target = default;
        return false;
    }

    private static int CountCustomMatchesForSelection(int filterIndex)
    {
        if (filterIndex < 0)
        {
            return NearbyCustomMatches.Count;
        }

        int count = 0;
        foreach (CustomGuidanceMatch match in NearbyCustomMatches)
        {
            if (match.FilterIndex == filterIndex)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetCurrentCustomTrackingTarget(out GuidanceEntry entry)
    {
        foreach (CustomGuidanceMatch match in NearbyCustomMatches)
        {
            if (_selectedCustomIndex < 0 || match.FilterIndex == _selectedCustomIndex)
            {
                entry = match.Entry;
                return true;
            }
        }

        entry = default;
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
            case SelectionMode.Custom when TryGetCurrentCustomTrackingTarget(out GuidanceEntry customTarget):
                worldPosition = customTarget.WorldPosition;
                label = SanitizeLabel(customTarget.DisplayName);
                return true;
            case SelectionMode.Exploration when TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget exploration):
                worldPosition = exploration.WorldPosition;
                label = SanitizeLabel(exploration.Label);
                return true;
            case SelectionMode.Npc when TryGetSelectedNpc(player, out NPC npc, out GuidanceEntry entry):
                worldPosition = npc.Bottom;
                label = SanitizeLabel(entry.DisplayName);
                return true;
            case SelectionMode.Interactable when TryGetSelectedInteractable(player, out GuidanceEntry interactable):
                worldPosition = interactable.WorldPosition;
                label = SanitizeLabel(interactable.DisplayName);
                return true;
            case SelectionMode.Player when TryGetSelectedPlayer(player, out Player targetPlayer, out GuidanceEntry playerEntry):
                worldPosition = targetPlayer.Bottom;
                label = SanitizeLabel(playerEntry.DisplayName);
                return true;
            case SelectionMode.DroppedItem when TryGetSelectedDroppedItem(player, out GuidanceEntry droppedItem):
                worldPosition = droppedItem.WorldPosition;
                label = SanitizeLabel(droppedItem.DisplayName);
                return true;
            case SelectionMode.Critter when TryGetSelectedCritter(player, out GuidanceEntry critter):
                worldPosition = critter.WorldPosition;
                label = SanitizeLabel(critter.DisplayName);
                return true;
            case SelectionMode.Plantlife when TryGetSelectedPlantlife(player, out GuidanceEntry plantlife):
                worldPosition = plantlife.WorldPosition;
                label = SanitizeLabel(plantlife.DisplayName);
                return true;
            case SelectionMode.HostileMob when TryGetSelectedHostileMob(player, out GuidanceEntry hostileMob):
                worldPosition = hostileMob.WorldPosition;
                label = SanitizeLabel(hostileMob.DisplayName);
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
            SelectionMode.Waypoint when _selectedIndex < 0 => false,
            SelectionMode.Custom => CountCustomMatchesForSelection(_selectedCustomIndex) > 0,
            SelectionMode.DroppedItem when _selectedDroppedItemIndex < 0 => false,
            SelectionMode.Critter when _selectedCritterIndex < 0 => false,
            SelectionMode.Plantlife when _selectedPlantlifeIndex < 0 => false,
            _ => true
        };
    }

    private static ProximityTargetKey ResolveProximityTargetKey(Player player)
    {
        return _selectionMode switch
        {
            SelectionMode.Waypoint => new ProximityTargetKey(SelectionMode.Waypoint, _selectedIndex),
            SelectionMode.Custom => new ProximityTargetKey(SelectionMode.Custom, _selectedCustomIndex),
            SelectionMode.Npc when TryGetSelectedNpc(player, out _, out GuidanceEntry npcEntry)
                => new ProximityTargetKey(SelectionMode.Npc, npcEntry.Index),
            SelectionMode.Player when TryGetSelectedPlayer(player, out _, out GuidanceEntry playerEntry)
                => new ProximityTargetKey(SelectionMode.Player, playerEntry.Index),
            SelectionMode.Interactable when TryGetSelectedInteractable(player, out GuidanceEntry interactableEntry)
                => new ProximityTargetKey(SelectionMode.Interactable, HashCode.Combine(interactableEntry.Anchor.X, interactableEntry.Anchor.Y)),
            SelectionMode.Exploration when TryGetSelectedExploration(out ExplorationTargetRegistry.ExplorationTarget explorationEntry)
                => new ProximityTargetKey(
                    SelectionMode.Exploration,
                    HashCode.Combine(explorationEntry.Key.SourceId, explorationEntry.Key.LocalId)),
            SelectionMode.DroppedItem when TryGetSelectedDroppedItem(player, out GuidanceEntry droppedItemEntry)
                => new ProximityTargetKey(SelectionMode.DroppedItem, droppedItemEntry.Index),
            SelectionMode.Critter when TryGetSelectedCritter(player, out GuidanceEntry critterEntry)
                => new ProximityTargetKey(SelectionMode.Critter, critterEntry.Index),
            SelectionMode.Plantlife when TryGetSelectedPlantlife(player, out GuidanceEntry plantlifeEntry)
                => new ProximityTargetKey(SelectionMode.Plantlife, HashCode.Combine(plantlifeEntry.Anchor.X, plantlifeEntry.Anchor.Y)),
            SelectionMode.HostileMob when TryGetSelectedHostileMob(player, out GuidanceEntry hostileMobEntry)
                => new ProximityTargetKey(SelectionMode.HostileMob, hostileMobEntry.Index),
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
