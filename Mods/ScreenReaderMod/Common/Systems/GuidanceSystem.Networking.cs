#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class GuidanceSystem
{
    private const int MaxWaypointSyncCount = 2048;

    internal static bool CanUseNetworkSync()
    {
        return global::ScreenReaderMod.ScreenReaderMod.Instance is { Side: ModSide.Both or ModSide.Server };
    }

    private enum GuidancePacketType : byte
    {
        SyncWaypoints,
        WaypointAdded,
        WaypointDeleted
    }

    public override void NetSend(BinaryWriter writer)
    {
        if (!CanUseNetworkSync())
        {
            return;
        }

        WriteWaypointState(writer);
    }

    public override void NetReceive(BinaryReader reader)
    {
        if (!CanUseNetworkSync())
        {
            return;
        }

        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            return;
        }

        ReadWaypointState(reader, announceSelection: false);
        RescheduleLocalPingAfterSync();
    }

    internal static void HandlePacket(BinaryReader reader, int sender)
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
                    ReadWaypointState(reader, announceSelection: true);
                    RescheduleLocalPingAfterSync();
                }

                break;
            case GuidancePacketType.WaypointAdded:
                ReceiveWaypointAdded(reader, sender);
                break;
            case GuidancePacketType.WaypointDeleted:
                ReceiveWaypointDeleted(reader, sender);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packetType), packetType, "Unknown guidance packet type.");
        }
    }

    private static void ReceiveWaypointAdded(BinaryReader reader, int sender)
    {
        if (!TryReadWaypoint(reader, Waypoints.Count, "waypoint add packet", out Waypoint waypoint))
        {
            return;
        }

        Waypoints.Add(waypoint);
        ClampSelectedWaypointIndex();

        if (Main.netMode == NetmodeID.Server)
        {
            BroadcastWaypointSync(ignoreClient: sender);
        }
        else
        {
            RescheduleLocalPingAfterSync();
        }
    }

    private static void ReceiveWaypointDeleted(BinaryReader reader, int sender)
    {
        int removedIndex = reader.ReadInt32();
        if (removedIndex < 0 || removedIndex >= Waypoints.Count)
        {
            return;
        }

        Waypoints.RemoveAt(removedIndex);
        ClampSelectedWaypointIndex();

        if (Main.netMode == NetmodeID.Server)
        {
            BroadcastWaypointSync(ignoreClient: sender);
        }
        else
        {
            RescheduleLocalPingAfterSync();
        }
    }

    private static void WriteWaypointState(BinaryWriter writer)
    {
        (List<Waypoint> waypoints, SelectionMode selectionMode, int selectedIndex) = BuildSerializableWaypointState("network sync", normalizeRuntime: true);

        writer.Write(waypoints.Count);
        foreach (Waypoint waypoint in waypoints)
        {
            writer.Write(waypoint.Name);
            writer.Write(waypoint.WorldPosition.X);
            writer.Write(waypoint.WorldPosition.Y);
        }

        writer.Write((byte)selectionMode);
        writer.Write(selectedIndex);
    }

    private static void ReadWaypointState(BinaryReader reader, bool announceSelection)
    {
        ResetWaypointSelectionState();

        if (!TryReadWaypointCount(reader, out int waypointCount))
        {
            return;
        }

        for (int i = 0; i < waypointCount; i++)
        {
            if (!TryReadWaypoint(reader, i, "network sync", out Waypoint waypoint))
            {
                ResetWaypointSelectionState();
                return;
            }

            Waypoints.Add(waypoint);
        }

        if (!TryReadWaypointSelection(reader, out SelectionMode selectionMode, out int selectedIndex))
        {
            return;
        }

        _selectionMode = selectionMode;
        _selectedIndex = selectedIndex;
        ClampSelectedWaypointIndex();

        ClearCategoryAnnouncement();
        ResetProximityProgress();

        if (!announceSelection || _selectionMode != SelectionMode.Waypoint || _selectedIndex < 0 || _selectedIndex >= Waypoints.Count)
        {
            return;
        }

        if (Main.LocalPlayer is null || !Main.LocalPlayer.active)
        {
            return;
        }

        RescheduleGuidancePing(Main.LocalPlayer);
    }

    private static void BroadcastWaypointSync(int toClient = -1, int ignoreClient = -1)
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
        WriteWaypointState(packet);
        packet.Send(toClient, ignoreClient);
    }

    private static void SendWaypointAddedToServer(Waypoint waypoint)
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

        string name = ResolveWaypointName(waypoint.Name, Waypoints.Count);

        packet.Write((byte)GuidancePacketType.WaypointAdded);
        packet.Write(name);
        packet.Write(waypoint.WorldPosition.X);
        packet.Write(waypoint.WorldPosition.Y);
        packet.Send();
    }

    private static void SendWaypointDeletedToServer(int index)
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

    private static void ClampSelectedWaypointIndex()
    {
        if (_selectionMode != SelectionMode.Waypoint)
        {
            _selectedIndex = Math.Clamp(_selectedIndex, -1, Waypoints.Count - 1);
            return;
        }

        if (Waypoints.Count == 0)
        {
            _selectionMode = SelectionMode.None;
            _selectedIndex = -1;
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Waypoints.Count - 1);
    }

    private static void RescheduleLocalPingAfterSync()
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
        {
            return;
        }

        if (_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count)
        {
            RescheduleGuidancePing(player);
        }
        else
        {
            _nextPingUpdateFrame = -1;
        }
    }

    private static bool TryReadWaypointCount(BinaryReader reader, out int waypointCount)
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

    private static bool TryReadWaypoint(BinaryReader reader, int fallbackIndex, string source, out Waypoint waypoint)
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

    private static bool TryReadWaypointSelection(BinaryReader reader, out SelectionMode selectionMode, out int selectedIndex)
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
}
