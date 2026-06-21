#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

public sealed partial class GuidanceSystem
{
    private const int MaxWaypointSyncCount = 2048;

    internal static bool CanUseNetworkSync()
    {
        return global::TerrariaAccess.TerrariaAccess.Instance?.IsNetSynced == true;
    }

    private enum GuidancePacketType : byte
    {
        SyncWaypoints,
        WaypointAdded,
        WaypointDeleted,
        CustomTargetAdded,
        CustomTargetDeleted
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
            case GuidancePacketType.CustomTargetAdded:
                ReceiveCustomTargetAdded(reader, sender);
                break;
            case GuidancePacketType.CustomTargetDeleted:
                ReceiveCustomTargetDeleted(reader, sender);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packetType), packetType, "Unknown guidance packet type.");
        }
    }

    private static void ReceiveWaypointAdded(BinaryReader reader, int sender)
    {
        LogWaypoint($"ReceiveWaypointAdded: Sender={sender}, NetMode={Main.netMode}, " +
                    $"CurrentWaypointCount={Waypoints.Count}");

        if (!TryReadWaypoint(reader, Waypoints.Count, "waypoint add packet", out Waypoint waypoint))
        {
            LogWaypoint("ReceiveWaypointAdded: Failed to read waypoint from packet.");
            return;
        }

        Waypoints.Add(waypoint);
        ClampSelectedWaypointIndex();
        LogWaypoint($"ReceiveWaypointAdded: Added \"{waypoint.Name}\" at ({waypoint.WorldPosition.X:F1}, {waypoint.WorldPosition.Y:F1}). " +
                    $"TotalWaypoints={Waypoints.Count}");

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
        LogWaypoint($"ReceiveWaypointDeleted: Sender={sender}, RemovedIndex={removedIndex}, " +
                    $"WaypointCount={Waypoints.Count}, NetMode={Main.netMode}");

        if (removedIndex < 0 || removedIndex >= Waypoints.Count)
        {
            LogWaypoint($"ReceiveWaypointDeleted: Index {removedIndex} out of range [0, {Waypoints.Count}). Ignoring.");
            return;
        }

        string removedName = Waypoints[removedIndex].Name;
        Waypoints.RemoveAt(removedIndex);
        ClampSelectedWaypointIndex();
        LogWaypoint($"ReceiveWaypointDeleted: Removed \"{removedName}\". TotalWaypoints={Waypoints.Count}");

        if (Main.netMode == NetmodeID.Server)
        {
            BroadcastWaypointSync(ignoreClient: sender);
        }
        else
        {
            RescheduleLocalPingAfterSync();
        }
    }

    private static void ReceiveCustomTargetAdded(BinaryReader reader, int sender)
    {
        LogWaypoint($"ReceiveCustomTargetAdded: Sender={sender}, NetMode={Main.netMode}, " +
                    $"CurrentCustomTargetCount={CustomTargets.Count}");

        if (!TryReadCustomFilter(reader, CustomTargets.Count, "custom target add packet", out CustomGuidanceFilter target))
        {
            LogWaypoint("ReceiveCustomTargetAdded: Failed to read custom target from packet.");
            return;
        }

        CustomTargets.Add(target);
        ClampSelectedCustomIndex();
        LogWaypoint($"ReceiveCustomTargetAdded: Added \"{target.Label}\" of kind {target.Kind}. " +
                    $"TotalCustomTargets={CustomTargets.Count}");

        if (Main.netMode == NetmodeID.Server)
        {
            BroadcastWaypointSync(ignoreClient: sender);
        }
        else
        {
            RescheduleLocalPingAfterSync();
        }
    }

    private static void ReceiveCustomTargetDeleted(BinaryReader reader, int sender)
    {
        int removedIndex = reader.ReadInt32();
        LogWaypoint($"ReceiveCustomTargetDeleted: Sender={sender}, RemovedIndex={removedIndex}, " +
                    $"CustomTargetCount={CustomTargets.Count}, NetMode={Main.netMode}");

        if (removedIndex < 0 || removedIndex >= CustomTargets.Count)
        {
            LogWaypoint($"ReceiveCustomTargetDeleted: Index {removedIndex} out of range [0, {CustomTargets.Count}). Ignoring.");
            return;
        }

        string removedName = CustomTargets[removedIndex].Label;
        CustomTargets.RemoveAt(removedIndex);
        ClampSelectedCustomIndex();
        LogWaypoint($"ReceiveCustomTargetDeleted: Removed \"{removedName}\". TotalCustomTargets={CustomTargets.Count}");

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
        (List<Waypoint> waypoints,
            List<CustomGuidanceFilter> customTargets,
            _,
            _,
            _) = BuildSerializableWaypointState("network sync", normalizeRuntime: true);

        writer.Write(waypoints.Count);
        foreach (Waypoint waypoint in waypoints)
        {
            writer.Write(waypoint.Name);
            writer.Write(waypoint.WorldPosition.X);
            writer.Write(waypoint.WorldPosition.Y);
        }

        writer.Write(customTargets.Count);
        foreach (CustomGuidanceFilter customTarget in customTargets)
        {
            WriteCustomFilter(writer, customTarget);
        }
    }

    private static void ReadWaypointState(BinaryReader reader, bool announceSelection)
    {
        SelectionMode previousSelectionMode = _selectionMode;
        int previousWaypointIndex = _selectedIndex;
        int previousCustomIndex = _selectedCustomIndex;
        Waypoint? previousWaypoint = previousSelectionMode == SelectionMode.Waypoint &&
            previousWaypointIndex >= 0 && previousWaypointIndex < Waypoints.Count
                ? Waypoints[previousWaypointIndex]
                : null;
        CustomGuidanceFilter? previousCustomTarget = previousSelectionMode == SelectionMode.Custom &&
            previousCustomIndex >= 0 && previousCustomIndex < CustomTargets.Count
                ? CustomTargets[previousCustomIndex]
                : null;

        if (!TryReadWaypointCount(reader, out int waypointCount))
        {
            return;
        }

        List<Waypoint> syncedWaypoints = new(waypointCount);
        for (int i = 0; i < waypointCount; i++)
        {
            if (!TryReadWaypoint(reader, i, "network sync", out Waypoint waypoint))
            {
                return;
            }

            syncedWaypoints.Add(waypoint);
        }

        if (!TryReadWaypointCount(reader, out int customTargetCount))
        {
            return;
        }

        List<CustomGuidanceFilter> syncedCustomTargets = new(customTargetCount);
        for (int i = 0; i < customTargetCount; i++)
        {
            if (!TryReadCustomFilter(reader, i, "network sync custom target", out CustomGuidanceFilter customTarget))
            {
                return;
            }

            syncedCustomTargets.Add(customTarget);
        }

        Waypoints.Clear();
        Waypoints.AddRange(syncedWaypoints);
        CustomTargets.Clear();
        CustomTargets.AddRange(syncedCustomTargets);
        RestoreLocalSelectionAfterSharedSync(
            previousSelectionMode,
            previousWaypointIndex,
            previousWaypoint,
            previousCustomIndex,
            previousCustomTarget);

        NearbyCustomMatches.Clear();
        SweepOrder.Clear();
        SweepScheduler.Reset();
        ResetProximityProgress();
        _nextPingUpdateFrame = -1;
        _arrivalAnnounced = false;
        _lastTargetRefreshFrame = 0;
        _lastTargetRefreshPlayerIndex = -1;

        if (!announceSelection)
        {
            return;
        }

        if (Main.LocalPlayer is null || !Main.LocalPlayer.active)
        {
            return;
        }

        if ((_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count) ||
            (_selectionMode == SelectionMode.Custom && CustomTargets.Count > 0))
        {
            RescheduleGuidancePing(Main.LocalPlayer);
        }
    }

    private static void RestoreLocalSelectionAfterSharedSync(
        SelectionMode previousSelectionMode,
        int previousWaypointIndex,
        Waypoint? previousWaypoint,
        int previousCustomIndex,
        CustomGuidanceFilter? previousCustomTarget)
    {
        _selectionMode = previousSelectionMode;

        if (_selectionMode == SelectionMode.Waypoint)
        {
            if (Waypoints.Count == 0)
            {
                _selectionMode = SelectionMode.None;
                _selectedIndex = -1;
            }
            else if (previousWaypoint.HasValue &&
                     TryFindMatchingWaypoint(previousWaypoint.Value, out int matchedWaypointIndex))
            {
                _selectedIndex = matchedWaypointIndex;
            }
            else
            {
                _selectedIndex = Math.Clamp(previousWaypointIndex, 0, Waypoints.Count - 1);
            }
        }
        else
        {
            _selectedIndex = Math.Clamp(previousWaypointIndex, -1, Waypoints.Count - 1);
        }

        if (_selectionMode == SelectionMode.Custom)
        {
            if (CustomTargets.Count == 0)
            {
                _selectionMode = SelectionMode.None;
                _selectedCustomIndex = -1;
            }
            else if (previousCustomIndex < 0)
            {
                _selectedCustomIndex = -1;
            }
            else if (previousCustomTarget.HasValue &&
                     TryFindMatchingCustomTarget(previousCustomTarget.Value, out int matchedCustomIndex))
            {
                _selectedCustomIndex = matchedCustomIndex;
            }
            else
            {
                _selectedCustomIndex = Math.Clamp(previousCustomIndex, 0, CustomTargets.Count - 1);
            }
        }
        else
        {
            _selectedCustomIndex = Math.Clamp(previousCustomIndex, -1, CustomTargets.Count - 1);
        }
    }

    private static bool TryFindMatchingWaypoint(Waypoint target, out int index)
    {
        for (int i = 0; i < Waypoints.Count; i++)
        {
            Waypoint candidate = Waypoints[i];
            if (string.Equals(candidate.Name, target.Name, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(candidate.WorldPosition.X - target.WorldPosition.X) < 0.5f &&
                Math.Abs(candidate.WorldPosition.Y - target.WorldPosition.Y) < 0.5f)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryFindMatchingCustomTarget(CustomGuidanceFilter target, out int index)
    {
        for (int i = 0; i < CustomTargets.Count; i++)
        {
            CustomGuidanceFilter candidate = CustomTargets[i];
            if (candidate.Kind == target.Kind &&
                candidate.TypeId == target.TypeId &&
                candidate.StyleId == target.StyleId &&
                candidate.RequireLabelMatch == target.RequireLabelMatch &&
                string.Equals(candidate.Label, target.Label, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static void BroadcastWaypointSync(int toClient = -1, int ignoreClient = -1)
    {
        if (Main.netMode != NetmodeID.Server || !CanUseNetworkSync())
        {
            return;
        }

        ModPacket? packet = TerrariaAccess.Instance?.GetPacket();
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
            LogWaypoint($"SendWaypointAddedToServer: Skipped (NetMode={Main.netMode}, " +
                        $"CanUseNetworkSync={CanUseNetworkSync()})");
            return;
        }

        ModPacket? packet = TerrariaAccess.Instance?.GetPacket();
        if (packet is null)
        {
            LogWaypoint("SendWaypointAddedToServer: Failed to get ModPacket.");
            return;
        }

        string name = ResolveWaypointName(waypoint.Name, Waypoints.Count);

        packet.Write((byte)GuidancePacketType.WaypointAdded);
        packet.Write(name);
        packet.Write(waypoint.WorldPosition.X);
        packet.Write(waypoint.WorldPosition.Y);
        packet.Send();
        LogWaypoint($"SendWaypointAddedToServer: Sent waypoint \"{name}\" at ({waypoint.WorldPosition.X:F1}, {waypoint.WorldPosition.Y:F1})");
    }

    private static void SendWaypointDeletedToServer(int index)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !CanUseNetworkSync())
        {
            LogWaypoint($"SendWaypointDeletedToServer: Skipped (NetMode={Main.netMode}, " +
                        $"CanUseNetworkSync={CanUseNetworkSync()})");
            return;
        }

        ModPacket? packet = TerrariaAccess.Instance?.GetPacket();
        if (packet is null)
        {
            LogWaypoint("SendWaypointDeletedToServer: Failed to get ModPacket.");
            return;
        }

        packet.Write((byte)GuidancePacketType.WaypointDeleted);
        packet.Write(index);
        packet.Send();
        LogWaypoint($"SendWaypointDeletedToServer: Sent delete for index {index}.");
    }

    private static void SendCustomTargetAddedToServer(CustomGuidanceFilter target)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !CanUseNetworkSync())
        {
            LogWaypoint($"SendCustomTargetAddedToServer: Skipped (NetMode={Main.netMode}, " +
                        $"CanUseNetworkSync={CanUseNetworkSync()})");
            return;
        }

        ModPacket? packet = TerrariaAccess.Instance?.GetPacket();
        if (packet is null)
        {
            LogWaypoint("SendCustomTargetAddedToServer: Failed to get ModPacket.");
            return;
        }

        packet.Write((byte)GuidancePacketType.CustomTargetAdded);
        WriteCustomFilter(packet, target);
        packet.Send();
        LogWaypoint($"SendCustomTargetAddedToServer: Sent custom target \"{target.Label}\" of kind {target.Kind}");
    }

    private static void SendCustomTargetDeletedToServer(int index)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !CanUseNetworkSync())
        {
            LogWaypoint($"SendCustomTargetDeletedToServer: Skipped (NetMode={Main.netMode}, " +
                        $"CanUseNetworkSync={CanUseNetworkSync()})");
            return;
        }

        ModPacket? packet = TerrariaAccess.Instance?.GetPacket();
        if (packet is null)
        {
            LogWaypoint("SendCustomTargetDeletedToServer: Failed to get ModPacket.");
            return;
        }

        packet.Write((byte)GuidancePacketType.CustomTargetDeleted);
        packet.Write(index);
        packet.Send();
        LogWaypoint($"SendCustomTargetDeletedToServer: Sent delete for index {index}.");
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

    private static void ClampSelectedCustomIndex()
    {
        if (_selectionMode != SelectionMode.Custom)
        {
            _selectedCustomIndex = Math.Clamp(_selectedCustomIndex, -1, CustomTargets.Count - 1);
            return;
        }

        if (CustomTargets.Count == 0)
        {
            _selectionMode = SelectionMode.None;
            _selectedCustomIndex = -1;
            return;
        }

        _selectedCustomIndex = Math.Clamp(_selectedCustomIndex, -1, CustomTargets.Count - 1);
    }

    private static void RescheduleLocalPingAfterSync()
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
        {
            return;
        }

        if ((_selectionMode == SelectionMode.Waypoint && _selectedIndex >= 0 && _selectedIndex < Waypoints.Count) ||
            (_selectionMode == SelectionMode.Custom && CustomTargets.Count > 0))
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

    private static void WriteCustomFilter(BinaryWriter writer, CustomGuidanceFilter filter)
    {
        writer.Write((byte)filter.Kind);
        writer.Write(filter.TypeId);
        writer.Write(filter.Label ?? string.Empty);
        writer.Write(filter.RequireLabelMatch);
        writer.Write(filter.StyleId);
    }

    private static bool TryReadCustomFilter(BinaryReader reader, int fallbackIndex, string source, out CustomGuidanceFilter filter)
    {
        filter = default;

        if (!HasRemainingBytes(reader, sizeof(byte) + sizeof(int)))
        {
            LogWaypointWarning($"Custom target payload missing kind/type for {source}.");
            return false;
        }

        CustomFilterKind kind = (CustomFilterKind)reader.ReadByte();
        int typeId = reader.ReadInt32();
        if (!TryReadStringSafe(reader, out string label))
        {
            return false;
        }

        bool requireLabelMatch = true;
        if (HasRemainingBytes(reader, sizeof(bool)))
        {
            requireLabelMatch = reader.ReadBoolean();
        }

        int styleId = -1;
        if (HasRemainingBytes(reader, sizeof(int)))
        {
            styleId = reader.ReadInt32();
        }

        filter = new CustomGuidanceFilter(kind, typeId, ResolveCustomFilterLabel(label, fallbackIndex), requireLabelMatch, styleId);
        return true;
    }

    private static bool TryReadWaypointSelection(BinaryReader reader, out SelectionMode selectionMode, out int selectedWaypointIndex, out int selectedCustomIndex)
    {
        selectionMode = SelectionMode.None;
        selectedWaypointIndex = -1;
        selectedCustomIndex = -1;

        if (!HasRemainingBytes(reader, sizeof(byte) + sizeof(int) + sizeof(int)))
        {
            LogWaypointWarning("Waypoint sync payload missing selection data.");
            return false;
        }

        selectionMode = (SelectionMode)reader.ReadByte();
        selectedWaypointIndex = reader.ReadInt32();
        selectedCustomIndex = reader.ReadInt32();
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
