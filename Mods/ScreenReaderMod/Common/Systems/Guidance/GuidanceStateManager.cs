#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace ScreenReaderMod.Common.Systems.Guidance;

/// <summary>
/// Manages the state for the guidance system including waypoints, selection indices,
/// and tracking mode.
/// </summary>
internal sealed class GuidanceStateManager
{
    internal readonly record struct ProximityTargetKey(SelectionMode Mode, int Index);

    internal readonly List<Waypoint> Waypoints = new();
    internal readonly List<GuidanceEntry> NearbyNpcs = new();
    internal readonly List<GuidanceEntry> NearbyPlayers = new();
    internal readonly List<GuidanceEntry> NearbyInteractables = new();
    internal readonly List<ExplorationTargetRegistry.ExplorationTarget> NearbyExplorationTargets = new();
    internal readonly List<GuidanceEntry> NearbyDroppedItems = new();
    internal readonly List<GuidanceEntry> NearbyCritters = new();
    internal readonly List<GuidanceEntry> NearbyPlantlife = new();
    internal readonly List<GuidanceEntry> NearbyHostileMobs = new();

    // Sweep state for "All" mode pinging
    internal readonly List<SweepTarget> SweepOrder = new();
    internal int SweepCursor;
    internal int NextSweepFrame;

    internal SelectionMode CurrentMode = SelectionMode.None;
    internal int SelectedIndex = -1;
    internal int SelectedNpcIndex = -1;
    internal int SelectedPlayerIndex = -1;
    internal int SelectedInteractableIndex = -1;
    internal int SelectedExplorationIndex = -1;
    internal int SelectedDroppedItemIndex = -1;
    internal int SelectedCritterIndex = -1;
    internal int SelectedPlantlifeIndex = -1;
    internal int SelectedHostileMobIndex = -1;
    internal ExplorationTargetRegistry.ExplorationTarget? LastExplorationSelection;

    // Speech queue integration
    internal SelectionMode LastAnnouncedCategory = SelectionMode.None;
    internal bool IncludeCategoryInNextAnnouncement;

    internal ProximityTargetKey ActiveProximityTarget = new(SelectionMode.None, -1);
    internal int LastProximityStepIndex = int.MaxValue;

    internal int NextPingUpdateFrame = -1;
    internal bool ArrivalAnnounced;
    internal SoundEffect? WaypointTone;
    internal readonly List<SoundEffectInstance> ActiveWaypointInstances = new();
    internal uint LastTargetRefreshFrame;
    internal int LastTargetRefreshPlayerIndex = -1;

    internal bool HasWaypointState => Waypoints.Count > 0 || CurrentMode != SelectionMode.None;

    internal bool IsExplorationTrackingEnabled => CurrentMode == SelectionMode.Exploration;

    internal void ResetTrackingState()
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        NearbyExplorationTargets.Clear();
        NearbyDroppedItems.Clear();
        NearbyCritters.Clear();
        NearbyPlantlife.Clear();
        NearbyHostileMobs.Clear();
        SelectedIndex = -1;
        SelectedNpcIndex = -1;
        SelectedPlayerIndex = -1;
        SelectedInteractableIndex = -1;
        SelectedExplorationIndex = -1;
        SelectedDroppedItemIndex = -1;
        SelectedCritterIndex = -1;
        SelectedPlantlifeIndex = -1;
        SelectedHostileMobIndex = -1;
        LastExplorationSelection = null;
        CurrentMode = SelectionMode.None;
        SweepOrder.Clear();
        SweepCursor = 0;
        NextSweepFrame = 0;
        ResetProximityProgress();
        ClearCategoryAnnouncement();
        LastAnnouncedCategory = SelectionMode.None;
        IncludeCategoryInNextAnnouncement = false;
        NextPingUpdateFrame = -1;
        ArrivalAnnounced = false;
        LastTargetRefreshFrame = 0;
        LastTargetRefreshPlayerIndex = -1;
    }

    internal void ResetProximityProgress()
    {
        ActiveProximityTarget = new ProximityTargetKey(SelectionMode.None, -1);
        LastProximityStepIndex = int.MaxValue;
    }

    internal void ClearCategoryAnnouncement()
    {
        LastAnnouncedCategory = SelectionMode.None;
        IncludeCategoryInNextAnnouncement = false;
    }

    internal void ResetWaypointSelectionState()
    {
        Waypoints.Clear();
        CurrentMode = SelectionMode.None;
        SelectedIndex = -1;
        NextPingUpdateFrame = -1;
        ArrivalAnnounced = false;
        ClearCategoryAnnouncement();
        ResetProximityProgress();
    }

    internal void ClampSelectedWaypointIndex()
    {
        if (CurrentMode != SelectionMode.Waypoint)
        {
            SelectedIndex = Math.Clamp(SelectedIndex, -1, Waypoints.Count - 1);
            return;
        }

        if (Waypoints.Count == 0)
        {
            CurrentMode = SelectionMode.None;
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, Waypoints.Count - 1);
    }

    internal readonly struct SweepTarget
    {
        public readonly Vector2 WorldPosition;
        public readonly float DistanceTiles;

        public SweepTarget(Vector2 worldPosition, float distanceTiles)
        {
            WorldPosition = worldPosition;
            DistanceTiles = distanceTiles;
        }
    }

    internal struct Waypoint
    {
        public string Name;
        public Vector2 WorldPosition;

        public Waypoint(string name, Vector2 worldPosition)
        {
            Name = name;
            WorldPosition = worldPosition;
        }
    }
}

/// <summary>
/// Selection mode for guidance categories.
/// </summary>
internal enum SelectionMode
{
    None,
    Exploration,
    Interactable,
    Npc,
    Player,
    Waypoint,
    DroppedItem,
    Critter,
    Plantlife,
    HostileMob
}
