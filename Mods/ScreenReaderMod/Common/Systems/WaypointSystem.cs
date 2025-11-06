#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.Waypoints;
using Terraria;
using Terraria.Audio;
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

    private const float ArrivalTileThreshold = 4f;
    private const int MinPingDelayFrames = 12;
    private const int MaxPingDelayFrames = 70;
    private const float PitchScale = 320f;

    private static readonly List<Waypoint> Waypoints = new();
    private static int _selectedIndex = -1;

    private static bool _namingActive;

    private static int _nextPingUpdateFrame = -1;
    private static bool _arrivalAnnounced;

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
        _selectedIndex = -1;

        _namingActive = false;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
    }

    public override void OnWorldUnload()
    {
        Waypoints.Clear();
        _selectedIndex = -1;
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        CloseNamingUi();
    }

    public override void LoadWorldData(TagCompound tag)
    {
        Waypoints.Clear();
        _selectedIndex = -1;
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

        if (_selectedIndex >= Waypoints.Count)
        {
            _selectedIndex = Waypoints.Count - 1;
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

        if (_selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            tag[SelectedIndexKey] = _selectedIndex;
        }
    }

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
            if (player is null || !player.active || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
            {
                _nextPingUpdateFrame = -1;
                _arrivalAnnounced = false;
            }
            else if (Main.gamePaused)
            {
                // hold current schedule while paused
            }
            else
            {
                Waypoint waypoint = Waypoints[_selectedIndex];
                Vector2 targetPosition = waypoint.WorldPosition;
                float distanceTiles = Vector2.Distance(player.Center, targetPosition) / 16f;

                if (distanceTiles <= ArrivalTileThreshold)
                {
                    if (!_arrivalAnnounced)
                    {
                        ScreenReaderService.Announce($"Arrived at {waypoint.Name}");
                        _arrivalAnnounced = true;
                    }

                    _nextPingUpdateFrame = -1;
                }
                else
                {
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

            Player? owner = ResolvePlayer();
            if (owner is not null)
            {
                RescheduleWaypointPing(owner);
                ScreenReaderService.Announce($"Created waypoint {resolvedName}");
                EmitPing(owner, worldPosition);
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

            if (owner is not null && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
            {
                RescheduleWaypointPing(owner);
            }

            CloseNamingUi();
        }

        UIVirtualKeyboard keyboard = new("Create Waypoint", string.Empty, Submit, Cancel, 0, true);
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
        if (Main.InGameUI?.CurrentState is UIVirtualKeyboard)
        {
            IngameFancyUI.Close();
        }

        PlayerInput.WritingText = false;
        Main.blockInput = false;
        Main.playerInventory = false;
        Main.editSign = false;
        Main.editChest = false;
        Main.drawingPlayerChat = false;
        Main.inFancyUI = false;
        Main.gameMenu = false;
        Main.clrInput();
        Main.chatText = string.Empty;
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

        if (WaypointKeybinds.Next?.JustPressed ?? false)
        {
            CycleSelection(1, player);
            return;
        }

        if (WaypointKeybinds.Previous?.JustPressed ?? false)
        {
            CycleSelection(-1, player);
        }
    }

    private static string BuildDefaultName()
    {
        int nextIndex = Waypoints.Count + 1;
        return $"Waypoint {nextIndex}";
    }

    private static void CycleSelection(int direction, Player player)
    {
        if (Waypoints.Count == 0)
        {
            ScreenReaderService.Announce("No waypoints available");
            return;
        }

        if (_selectedIndex < 0)
        {
            _selectedIndex = direction > 0 ? 0 : Waypoints.Count - 1;
        }
        else
        {
            _selectedIndex += direction;
            if (_selectedIndex >= Waypoints.Count)
            {
                _selectedIndex = 0;
            }
            else if (_selectedIndex < 0)
            {
                _selectedIndex = Waypoints.Count - 1;
            }
        }

        Waypoint waypoint = Waypoints[_selectedIndex];
        EmitPing(player, waypoint.WorldPosition);
        RescheduleWaypointPing(player);
        ScreenReaderService.Announce(ComposeWaypointAnnouncement(waypoint));
    }

    private static void RescheduleWaypointPing(Player player)
    {
        if (_selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            _nextPingUpdateFrame = -1;
            _arrivalAnnounced = false;
            return;
        }

        _arrivalAnnounced = false;
        _nextPingUpdateFrame = ComputeNextPingFrame(player, Waypoints[_selectedIndex].WorldPosition);
    }

    private static string ComposeWaypointAnnouncement(Waypoint waypoint)
    {
        int total = Waypoints.Count;
        int position = _selectedIndex + 1;

        if (total <= 0 || position <= 0 || position > total)
        {
            return waypoint.Name;
        }

        return $"{waypoint.Name} {position} of {total}";
    }

    private static void EmitPing(Player player, Vector2 worldPosition)
    {
        if (Main.dedServ)
        {
            return;
        }

        Vector2 offset = worldPosition - player.Center;
        float pitch = MathHelper.Clamp(-offset.Y / PitchScale, -0.6f, 0.6f);
        float volume = MathHelper.Clamp(0.35f + Math.Abs(pitch) * 0.2f, 0f, 0.75f);

        SoundStyle style = SoundID.MenuTick
            .WithVolumeScale(volume)
            .WithPitchOffset(pitch);

        SoundEngine.PlaySound(style, worldPosition);
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

        float frames = MathHelper.Clamp(distanceTiles * 2f, MinPingDelayFrames, MaxPingDelayFrames);
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

