#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Systems.Guidance;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem
{
    private readonly record struct ProximityTargetKey(SelectionMode Mode, int Index);

    private enum CustomFilterKind : byte
    {
        Tile,
        Npc,
        Player,
        Projectile,
        DroppedItem,
        Critter,
        Plantlife,
        HostileMob
    }

    private readonly struct CustomGuidanceFilter
    {
        public readonly CustomFilterKind Kind;
        public readonly int TypeId;
        public readonly int StyleId;
        public readonly string Label;
        public readonly bool RequireLabelMatch;

        public CustomGuidanceFilter(
            CustomFilterKind kind,
            int typeId,
            string label,
            bool requireLabelMatch = true,
            int styleId = -1)
        {
            Kind = kind;
            TypeId = typeId;
            StyleId = styleId;
            Label = label;
            RequireLabelMatch = requireLabelMatch;
        }
    }

    private readonly struct CustomGuidanceMatch
    {
        public readonly int FilterIndex;
        public readonly GuidanceEntry Entry;

        public CustomGuidanceMatch(int filterIndex, GuidanceEntry entry)
        {
            FilterIndex = filterIndex;
            Entry = entry;
        }
    }

    private static readonly List<Waypoint> Waypoints = new();
    private static readonly List<CustomGuidanceFilter> CustomTargets = new();
    private static readonly List<CustomGuidanceMatch> NearbyCustomMatches = new();

    internal static bool HasWaypointState => Waypoints.Count > 0 || CustomTargets.Count > 0 || _selectionMode != SelectionMode.None;
    internal static bool IsNamingActive => NamingDialog.IsActive || CustomTargetDialog.IsActive;

    private enum SelectionMode
    {
        None,
        Exploration,
        Interactable,
        Npc,
        Player,
        Waypoint,
        Custom,
        DroppedItem,
        Critter,
        Plantlife,
        HostileMob
    }

    private static SelectionMode _selectionMode = SelectionMode.None;
    private static int _selectedIndex = -1;
    private static int _selectedNpcIndex = -1;
    private static int _selectedPlayerIndex = -1;
    private static int _selectedInteractableIndex = -1;
    private static int _selectedExplorationIndex = -1;
    private static int _selectedCustomIndex = -1;
    private static int _selectedDroppedItemIndex = -1;
    private static int _selectedCritterIndex = -1;
    private static int _selectedPlantlifeIndex = -1;
    private static int _selectedHostileMobIndex = -1;
    private static ExplorationTargetRegistry.ExplorationTarget? _lastExplorationSelection;

    private static readonly List<SweepTarget> SweepOrder = new();
    private static readonly AudioSweepScheduler SweepScheduler = new();

    private readonly struct SweepTarget
    {
        public readonly Vector2 WorldPosition;
        public readonly float DistanceTiles;

        public SweepTarget(Vector2 worldPosition, float distanceTiles)
        {
            WorldPosition = worldPosition;
            DistanceTiles = distanceTiles;
        }
    }

    // Speech queue integration - uses centralized SpeechController queue system
    private const string SuppressionKeyArrival = "guidance:arrival";
    private static SelectionMode _lastAnnouncedCategory = SelectionMode.None;
    private static bool _includeCategoryInNextAnnouncement;

    private static ProximityTargetKey _activeProximityTarget = new(SelectionMode.None, -1);
    private static int _lastProximityStepIndex = int.MaxValue;

    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;
    private static SoundEffect? _waypointTone;
    private static readonly List<SoundEffectInstance> ActiveWaypointInstances = new();
    private static readonly GuidanceNamingDialogController NamingDialog = new();
    private static readonly GuidanceCustomTargetDialogController CustomTargetDialog = new();
    private static readonly bool LogGuidancePings = false;
    private static uint _lastTargetRefreshFrame;
    private static int _lastTargetRefreshPlayerIndex = -1;

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

    internal static void ResetTrackingState()
    {
        Waypoints.Clear();
        CustomTargets.Clear();
        NearbyCustomMatches.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        NearbyExplorationTargets.Clear();
        NearbyDroppedItems.Clear();
        NearbyCritters.Clear();
        NearbyPlantlife.Clear();
        NearbyHostileMobs.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectedExplorationIndex = -1;
        _selectedCustomIndex = -1;
        _selectedDroppedItemIndex = -1;
        _selectedCritterIndex = -1;
        _selectedPlantlifeIndex = -1;
        _selectedHostileMobIndex = -1;
        _lastExplorationSelection = null;
        _selectionMode = SelectionMode.None;
        SweepOrder.Clear();
        SweepScheduler.Reset();
        ResetProximityProgress();
        ClearCategoryAnnouncement();
        _lastAnnouncedCategory = SelectionMode.None;
        _includeCategoryInNextAnnouncement = false;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        _lastTargetRefreshFrame = 0;
        _lastTargetRefreshPlayerIndex = -1;
    }
}
