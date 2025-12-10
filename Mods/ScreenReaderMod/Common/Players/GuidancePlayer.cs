#nullable enable
using System.Collections.Generic;
using ScreenReaderMod.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ScreenReaderMod.Common.Players;

public sealed class GuidancePlayer : ModPlayer
{
    private const string WaypointCacheKey = "screenReaderWaypointCache";
    private const string WaypointCacheWorldIdKey = "worldId";
    private const string WaypointCacheDataKey = "data";

    private readonly Dictionary<string, TagCompound> _waypointCache = new();

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        GuidanceSystem.HandleKeybinds(Player);
    }

    public override void OnEnterWorld()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            return;
        }

        if (GuidanceSystem.CanUseNetworkSync() || GuidanceSystem.HasWaypointState)
        {
            return;
        }

        string cacheKey = BuildWorldCacheKey();
        if (_waypointCache.TryGetValue(cacheKey, out TagCompound? cached) && cached is not null)
        {
            GuidanceSystem.LoadWaypointData(cached, "player cache", announceSelection: true);
        }
    }

    public override void SaveData(TagCompound tag)
    {
        CacheWaypointState();

        if (_waypointCache.Count == 0)
        {
            return;
        }

        List<TagCompound> entries = new(_waypointCache.Count);
        foreach (KeyValuePair<string, TagCompound> entry in _waypointCache)
        {
            entries.Add(new TagCompound
            {
                [WaypointCacheWorldIdKey] = entry.Key,
                [WaypointCacheDataKey] = entry.Value
            });
        }

        tag[WaypointCacheKey] = entries;
    }

    public override void LoadData(TagCompound tag)
    {
        _waypointCache.Clear();

        if (!tag.ContainsKey(WaypointCacheKey))
        {
            return;
        }

        foreach (TagCompound entry in tag.GetList<TagCompound>(WaypointCacheKey))
        {
            if (!entry.ContainsKey(WaypointCacheWorldIdKey) || !entry.ContainsKey(WaypointCacheDataKey))
            {
                continue;
            }

            string worldId = entry.GetString(WaypointCacheWorldIdKey);
            TagCompound data = entry.GetCompound(WaypointCacheDataKey);
            _waypointCache[worldId] = data;
        }
    }

    internal void CacheWaypointState()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            return;
        }

        if (GuidanceSystem.CanUseNetworkSync())
        {
            return;
        }

        string cacheKey = BuildWorldCacheKey();
        TagCompound serialized = new();
        if (!GuidanceSystem.SaveWaypointData(serialized, "player cache", normalizeRuntime: false))
        {
            return;
        }

        _waypointCache[cacheKey] = serialized;
    }

    private static string BuildWorldCacheKey()
    {
        string worldName = string.IsNullOrWhiteSpace(Main.worldName) ? "unknown" : Main.worldName;
        return $"{Main.worldID}:{worldName}";
    }
}
