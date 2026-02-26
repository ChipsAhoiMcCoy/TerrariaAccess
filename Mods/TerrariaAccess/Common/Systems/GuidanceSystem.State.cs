#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem
{
    private readonly record struct ProximityTargetKey(SelectionMode Mode, int Index);

    private static readonly List<Waypoint> Waypoints = new();

    internal static bool HasWaypointState => Waypoints.Count > 0 || _selectionMode != SelectionMode.None;
    internal static bool IsNamingActive => _namingActive;

    private enum SelectionMode
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

    private static SelectionMode _selectionMode = SelectionMode.None;
    private static int _selectedIndex = -1;
    private static int _selectedNpcIndex = -1;
    private static int _selectedPlayerIndex = -1;
    private static int _selectedInteractableIndex = -1;
    private static int _selectedExplorationIndex = -1;
    private static int _selectedDroppedItemIndex = -1;
    private static int _selectedCritterIndex = -1;
    private static int _selectedPlantlifeIndex = -1;
    private static int _selectedHostileMobIndex = -1;
    private static ExplorationTargetRegistry.ExplorationTarget? _lastExplorationSelection;

    // Sweep state for "All" mode pinging
    private static readonly List<SweepTarget> SweepOrder = new();
    private static int _sweepCursor;
    private static int _nextSweepFrame;
    private static bool _sweepCycleActive;
    private const int TargetSweepDurationFrames = 60;  // ~1 second at 60 FPS
    private const int MinSweepIntervalFrames = 3;      // ~50ms floor so tones stay distinct
    private const int SweepCycleGapFrames = 15;        // ~250ms pause between cycles

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

    private static bool _namingActive;

    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;
    private static SoundEffect? _waypointTone;
    private static readonly List<SoundEffectInstance> ActiveWaypointInstances = new();
    private static InputSnapshot? _inputSnapshot;

    // Direct text-input naming state
    private static string _namingText = string.Empty;
    private static string _namingPreviousText = string.Empty;
    private static Vector2 _namingWorldPosition;
    private static string _namingFallbackName = string.Empty;
    private static int _namingPlayerIndex = -1;
    private static readonly bool LogGuidancePings = false;
    private static uint _lastTargetRefreshFrame;
    private static int _lastTargetRefreshPlayerIndex = -1;

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
    }

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
        _selectedDroppedItemIndex = -1;
        _selectedCritterIndex = -1;
        _selectedPlantlifeIndex = -1;
        _selectedHostileMobIndex = -1;
        _lastExplorationSelection = null;
        _selectionMode = SelectionMode.None;
        SweepOrder.Clear();
        _sweepCursor = 0;
        _nextSweepFrame = 0;
        _sweepCycleActive = false;
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
