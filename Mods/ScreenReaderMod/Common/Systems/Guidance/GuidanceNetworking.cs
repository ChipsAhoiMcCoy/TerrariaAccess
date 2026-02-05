#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems.Guidance;

/// <summary>
/// Handles network synchronization for the guidance system's waypoint state.
/// </summary>
internal sealed class GuidanceNetworking
{
    private const int MaxWaypointSyncCount = 2048;

    private readonly GuidanceStateManager _state;

    public GuidanceNetworking(GuidanceStateManager state)
    {
        _state = state;
    }

    internal enum GuidancePacketType : byte
    {
        SyncWaypoints,
        WaypointAdded,
        WaypointDeleted
    }

    public static bool CanUseNetworkSync()
    {
        return global::ScreenReaderMod.ScreenReaderMod.Instance is { Side: ModSide.Both or ModSide.Server };
    }

    public void WriteWaypointState(BinaryWriter writer, Func<string, (System.Collections.Generic.List<GuidanceStateManager.Waypoint> waypoints, SelectionMode selectionMode, int selectedIndex)> buildSerializableState)
    {
        var state = buildSerializableState("network sync");

        writer.Write(state.waypoints.Count);
        foreach (GuidanceStateManager.Waypoint waypoint in state.waypoints)
        {
            writer.Write(waypoint.Name);
            writer.Write(waypoint.WorldPosition.X);
            writer.Write(waypoint.WorldPosition.Y);
        }

        writer.Write((byte)state.selectionMode);
        writer.Write(state.selectedIndex);
    }

    public void ReadWaypointState(BinaryReader reader, bool announceSelection, Action<Player>? rescheduleGuidancePing)
    {
        _state.ResetWaypointSelectionState();

        if (!TryReadWaypointCount(reader, out int waypointCount))
        {
            return;
        }

        for (int i = 0; i < waypointCount; i++)
        {
            if (!TryReadWaypoint(reader, i, "network sync", out GuidanceStateManager.Waypoint waypoint))
            {
                _state.ResetWaypointSelectionState();
                return;
            }

            _state.Waypoints.Add(waypoint);
        }

        if (!TryReadWaypointSelection(reader, out SelectionMode selectionMode, out int selectedIndex))
        {
            return;
        }

        _state.CurrentMode = selectionMode;
        _state.SelectedIndex = selectedIndex;
        _state.ClampSelectedWaypointIndex();

        _state.ClearCategoryAnnouncement();
        _state.ResetProximityProgress();

        if (!announceSelection || _state.CurrentMode != SelectionMode.Waypoint || _state.SelectedIndex < 0 || _state.SelectedIndex >= _state.Waypoints.Count)
        {
            return;
        }

        if (Main.LocalPlayer is null || !Main.LocalPlayer.active)
        {
            return;
        }

        rescheduleGuidancePing?.Invoke(Main.LocalPlayer);
    }

    public void HandlePacket(BinaryReader reader, int sender, Action<Player>? rescheduleGuidancePing, Action<int>? broadcastWaypointSync)
    {
        if (!CanUseNetworkSync())
        {
            return;
        }

        GuidancePacketType packetType = (GuidancePacketType)reader.ReadByte();
        switch (packetType)
        {
            case GuidancePacketType.SyncWaypoints:
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    ReadWaypointState(reader, announceSelection: true, rescheduleGuidancePing);
                    RescheduleLocalPingAfterSync(rescheduleGuidancePing);
                }

                break;
            case GuidancePacketType.WaypointAdded:
                ReceiveWaypointAdded(reader, sender, rescheduleGuidancePing, broadcastWaypointSync);
                break;
            case GuidancePacketType.WaypointDeleted:
                ReceiveWaypointDeleted(reader, sender, rescheduleGuidancePing, broadcastWaypointSync);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packetType), packetType, "Unknown guidance packet type.");
        }
    }

    private void ReceiveWaypointAdded(BinaryReader reader, int sender, Action<Player>? rescheduleGuidancePing, Action<int>? broadcastWaypointSync)
    {
        if (!TryReadWaypoint(reader, _state.Waypoints.Count, "waypoint add packet", out GuidanceStateManager.Waypoint waypoint))
        {
            return;
        }

        _state.Waypoints.Add(waypoint);
        _state.ClampSelectedWaypointIndex();

        if (Main.netMode == NetmodeID.Server)
        {
            broadcastWaypointSync?.Invoke(sender);
        }
        else
        {
            RescheduleLocalPingAfterSync(rescheduleGuidancePing);
        }
    }

    private void ReceiveWaypointDeleted(BinaryReader reader, int sender, Action<Player>? rescheduleGuidancePing, Action<int>? broadcastWaypointSync)
    {
        int removedIndex = reader.ReadInt32();
        if (removedIndex < 0 || removedIndex >= _state.Waypoints.Count)
        {
            return;
        }

        _state.Waypoints.RemoveAt(removedIndex);
        _state.ClampSelectedWaypointIndex();

        if (Main.netMode == NetmodeID.Server)
        {
            broadcastWaypointSync?.Invoke(sender);
        }
        else
        {
            RescheduleLocalPingAfterSync(rescheduleGuidancePing);
        }
    }

    public void BroadcastWaypointSync(int toClient, int ignoreClient, Func<string, (System.Collections.Generic.List<GuidanceStateManager.Waypoint> waypoints, SelectionMode selectionMode, int selectedIndex)> buildSerializableState)
    {
        if (Main.netMode != NetmodeID.Server || !CanUseNetworkSync())
        {
            return;
        }

        ModPacket? packet = ScreenReaderMod.Instance?.GetPacket();
        if (packet is null)
        {
            return;
        }

        packet.Write((byte)GuidancePacketType.SyncWaypoints);
        WriteWaypointState(packet, buildSerializableState);
        packet.Send(toClient, ignoreClient);
    }

    public void SendWaypointAddedToServer(GuidanceStateManager.Waypoint waypoint, Func<string?, int, string> resolveWaypointName)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !CanUseNetworkSync())
        {
            return;
        }

        ModPacket? packet = ScreenReaderMod.Instance?.GetPacket();
        if (packet is null)
        {
            return;
        }

        string name = resolveWaypointName(waypoint.Name, _state.Waypoints.Count);

        packet.Write((byte)GuidancePacketType.WaypointAdded);
        packet.Write(name);
        packet.Write(waypoint.WorldPosition.X);
        packet.Write(waypoint.WorldPosition.Y);
        packet.Send();
    }

    public void SendWaypointDeletedToServer(int index)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !CanUseNetworkSync())
        {
            return;
        }

        ModPacket? packet = ScreenReaderMod.Instance?.GetPacket();
        if (packet is null)
        {
            return;
        }

        packet.Write((byte)GuidancePacketType.WaypointDeleted);
        packet.Write(index);
        packet.Send();
    }

    private void RescheduleLocalPingAfterSync(Action<Player>? rescheduleGuidancePing)
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
        {
            return;
        }

        if (_state.CurrentMode == SelectionMode.Waypoint && _state.SelectedIndex >= 0 && _state.SelectedIndex < _state.Waypoints.Count)
        {
            rescheduleGuidancePing?.Invoke(player);
        }
        else
        {
            _state.NextPingUpdateFrame = -1;
        }
    }

    private bool TryReadWaypointCount(BinaryReader reader, out int waypointCount)
    {
        waypointCount = 0;
        if (!HasRemainingBytes(reader, sizeof(int)))
        {
            LogWaypointWarning("Waypoint sync payload missing count.");
            return false;
        }

        waypointCount = reader.ReadInt32();
        if (waypointCount < 0 || waypointCount > MaxWaypointSyncCount)
        {
            LogWaypointWarning($"Waypoint sync count {waypointCount} is invalid; discarding payload.");
            return false;
        }

        return true;
    }

    private bool TryReadWaypoint(BinaryReader reader, int fallbackIndex, string source, out GuidanceStateManager.Waypoint waypoint)
    {
        waypoint = default;

        if (!TryReadStringSafe(reader, out string name))
        {
            return false;
        }

        if (!TryReadSingleSafe(reader, out float x) || !TryReadSingleSafe(reader, out float y))
        {
            return false;
        }

        return TryCreateWaypoint(name, x, y, fallbackIndex, source, out waypoint);
    }

    private bool TryReadWaypointSelection(BinaryReader reader, out SelectionMode selectionMode, out int selectedIndex)
    {
        selectionMode = SelectionMode.None;
        selectedIndex = -1;

        if (!HasRemainingBytes(reader, sizeof(byte) + sizeof(int)))
        {
            LogWaypointWarning("Waypoint sync payload missing selection data.");
            return false;
        }

        selectionMode = (SelectionMode)reader.ReadByte();
        selectedIndex = reader.ReadInt32();
        return true;
    }

    private static bool TryReadStringSafe(BinaryReader reader, out string value)
    {
        value = string.Empty;

        try
        {
            value = reader.ReadString();
            return true;
        }
        catch (EndOfStreamException ex)
        {
            LogWaypointWarning($"Waypoint payload ended early while reading a name: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            LogWaypointWarning($"Failed to read waypoint name: {ex.Message}");
            return false;
        }
    }

    private static bool TryReadSingleSafe(BinaryReader reader, out float value)
    {
        value = 0f;
        if (!HasRemainingBytes(reader, sizeof(float)))
        {
            LogWaypointWarning("Waypoint payload ended early while reading coordinates.");
            return false;
        }

        try
        {
            value = reader.ReadSingle();
        }
        catch (EndOfStreamException ex)
        {
            LogWaypointWarning($"Waypoint payload ended early while reading coordinates: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            LogWaypointWarning($"Failed to read waypoint coordinates: {ex.Message}");
            return false;
        }

        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            LogWaypointWarning("Discarded waypoint coordinate because it was not finite.");
            return false;
        }

        return true;
    }

    private static bool HasRemainingBytes(BinaryReader reader, int bytesNeeded)
    {
        Stream? stream = reader.BaseStream;
        if (stream is null || !stream.CanSeek)
        {
            return true;
        }

        return stream.Position + bytesNeeded <= stream.Length;
    }

    private static bool TryCreateWaypoint(string? rawName, float x, float y, int fallbackIndex, string source, out GuidanceStateManager.Waypoint waypoint)
    {
        waypoint = default;

        Vector2 worldPosition = new(x, y);
        if (!IsValidWaypointPosition(worldPosition))
        {
            LogWaypointWarning($"Dropped waypoint {fallbackIndex + 1} from {source}: invalid position ({x}, {y}).");
            return false;
        }

        string resolvedName = ResolveWaypointName(rawName, fallbackIndex);
        waypoint = new GuidanceStateManager.Waypoint(resolvedName, worldPosition);
        return true;
    }

    private static string ResolveWaypointName(string? rawName, int fallbackIndex)
    {
        string cleaned = TextSanitizer.Clean(rawName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        return $"Waypoint {fallbackIndex + 1}";
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
}
