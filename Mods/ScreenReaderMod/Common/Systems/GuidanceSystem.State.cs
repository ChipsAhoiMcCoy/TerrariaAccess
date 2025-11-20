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
    private static SelectionMode _categoryAnnouncementMode = SelectionMode.None;
    private static bool _categoryAnnouncementPending;

    private static bool _namingActive;

    private static bool _autoPathActive;
    private static bool _autoPathArrivedAnnounced;
    private static string _autoPathLabel = string.Empty;
    private static float _autoPathLastDistanceTiles;
    private static ulong _autoPathLastProgressFrame;
    private static readonly List<Vector2> _autoPathNodes = new();
    private static int _autoPathNodeIndex;
    private static ulong _autoPathNextSearchFrame;
    private static int _autoPathPlatformDropHold;

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
        _selectedIndex = -1;
        _selectedNpcIndex = -1;
        _selectedPlayerIndex = -1;
        _selectedInteractableIndex = -1;
        _selectionMode = SelectionMode.None;
        ClearCategoryAnnouncement();
        _autoPathActive = false;
        _autoPathLabel = string.Empty;
        _autoPathArrivedAnnounced = false;
        _autoPathLastDistanceTiles = 0f;
        _autoPathLastProgressFrame = 0;
        _autoPathNodes.Clear();
        _autoPathNodeIndex = 0;
        _autoPathNextSearchFrame = 0;
        _autoPathPlatformDropHold = 0;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
    }
}
