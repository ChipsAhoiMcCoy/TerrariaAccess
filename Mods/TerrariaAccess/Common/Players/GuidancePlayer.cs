#nullable enable
using System.Collections.Generic;
using TerrariaAccess.Common.Systems;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerrariaAccess.Common.Players;

public sealed class GuidancePlayer : ModPlayer
{
    private const string WaypointCacheKey = "screenReaderWaypointCache";
    private const string WaypointCacheWorldIdKey = "worldId";
    private const string WaypointCacheDataKey = "data";

    private readonly Dictionary<string, TagCompound> _waypointCache = new();

    public override void PreUpdate()
    {
        // While the waypoint naming dialog is active, suppress the Inventory trigger
        // (Escape key) so it doesn't open the ingame settings / pause menu.
        // PreUpdate runs before Player.UpdateNPC() → TryOpeningInGameOptionsBasedOnInput(),
        // so clearing controlInv here prevents Escape from bypassing the naming dialog.
        if (GuidanceSystem.IsNamingActive)
        {
            Player.controlInv = false;
        }
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        GuidanceSystem.HandleKeybinds(Player);
    }

    public override void OnEnterWorld()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            Mod.Logger.Info($"[Waypoint] GuidancePlayer.OnEnterWorld: Skipped cache load (NetMode={Main.netMode}, not multiplayer client).");
            return;
        }

        // Reset any stale state from previous world before loading cache
        GuidanceSystem.ResetTrackingState();

        string cacheKey = BuildWorldCacheKey();
        bool hasCachedData = _waypointCache.TryGetValue(cacheKey, out TagCompound? cached) && cached is not null;
        Mod.Logger.Info($"[Waypoint] GuidancePlayer.OnEnterWorld: Player={Player.whoAmI}, CacheKey=\"{cacheKey}\", " +
                        $"HasCachedData={hasCachedData}, TotalCacheEntries={_waypointCache.Count}");

        if (hasCachedData)
        {
            GuidanceSystem.LoadWaypointData(cached!, "player cache", announceSelection: true);
        }
        else
        {
            Mod.Logger.Info("[Waypoint] GuidancePlayer.OnEnterWorld: No cached waypoints for this world.");
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
            Mod.Logger.Info($"[Waypoint] CacheWaypointState: Skipped (NetMode={Main.netMode}, not multiplayer client).");
            return;
        }

        if (GuidanceSystem.CanUseNetworkSync())
        {
            Mod.Logger.Info("[Waypoint] CacheWaypointState: Skipped (network sync available, server handles state).");
            return;
        }

        string cacheKey = BuildWorldCacheKey();
        TagCompound serialized = new();
        bool hasData = GuidanceSystem.SaveWaypointData(serialized, "player cache", normalizeRuntime: false);
        Mod.Logger.Info($"[Waypoint] CacheWaypointState: CacheKey=\"{cacheKey}\", HasData={hasData}");

        if (!hasData)
        {
            return;
        }

        _waypointCache[cacheKey] = serialized;
        Mod.Logger.Info($"[Waypoint] CacheWaypointState: Saved to cache. TotalCacheEntries={_waypointCache.Count}");
    }

    private static string BuildWorldCacheKey()
    {
        string worldName = string.IsNullOrWhiteSpace(Main.worldName) ? "unknown" : Main.worldName;
        return $"{Main.worldID}:{worldName}";
    }
}
