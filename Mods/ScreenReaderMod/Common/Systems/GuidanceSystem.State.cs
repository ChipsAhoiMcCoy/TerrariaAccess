#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria.UI;
using Terraria.GameContent.UI.States;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
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
    private static int _selectedExplorationIndex = -1;
    private static SelectionMode _categoryAnnouncementMode = SelectionMode.None;
    private static bool _categoryAnnouncementPending;

    private static bool _namingActive;

    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;
    private static SoundEffect? _waypointTone;
    private static readonly List<SoundEffectInstance> ActiveWaypointInstances = new();
    private static UIVirtualKeyboard? _activeKeyboard;
    private static InputSnapshot? _inputSnapshot;
    private static readonly bool LogGuidancePings = false;

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

    private static void ResetTrackingState()
    {
        Waypoints.Clear();
        NearbyNpcs.Clear();
        NearbyPlayers.Clear();
        NearbyInteractables.Clear();
        NearbyExplorationTargets.Clear();
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectedExplorationIndex = -1;
        _selectionMode = SelectionMode.None;
        ClearCategoryAnnouncement();
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
    }
}
