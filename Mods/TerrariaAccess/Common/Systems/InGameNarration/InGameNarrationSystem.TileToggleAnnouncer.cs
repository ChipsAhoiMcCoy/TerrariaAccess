#nullable enable
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Announces toggle state changes for interactive tiles like campfires, candles,
    /// fountains, music boxes, and monoliths when the player right-clicks them.
    /// </summary>
    private sealed class TileToggleAnnouncer
    {
        /// <summary>
        /// Checks if a tile type is toggleable via right-click interaction.
        /// This includes lighting tiles, music boxes, fountains, monoliths, and party centers.
        /// </summary>
        internal static bool IsToggleableTile(int tileType)
        {
            return tileType switch
            {
                // Campfire
                TileID.Campfire => true,
                // Candles
                TileID.Candles or TileID.WaterCandle or TileID.PeaceCandle or
                TileID.PlatinumCandle or TileID.ShadowCandle => true,
                // Fireplace
                TileID.Fireplace => true,
                // Music Box
                TileID.MusicBoxes => true,
                // Fountain
                TileID.WaterFountain => true,
                // Monoliths
                TileID.LunarMonolith or TileID.BloodMoonMonolith or TileID.VoidMonolith or
                TileID.EchoMonolith or TileID.ShimmerMonolith => true,
                // Party Center
                TileID.PartyMonolith => true,
                _ => false
            };
        }

        /// <summary>
        /// Captures the current on/off state of a toggleable tile based on its frame.
        /// </summary>
        internal static bool GetToggleState(Tile tile)
        {
            if (!tile.HasTile)
            {
                return false;
            }

            int tileType = tile.TileType;

            // Campfire: frameY < 36 = on, frameY >= 36 = off
            if (tileType == TileID.Campfire)
            {
                return tile.TileFrameY < 36;
            }

            // Candles: frameX 0 = on, frameX > 0 = off
            if (tileType == TileID.Candles || tileType == TileID.WaterCandle ||
                tileType == TileID.PeaceCandle || tileType == TileID.PlatinumCandle ||
                tileType == TileID.ShadowCandle)
            {
                return tile.TileFrameX == 0;
            }

            // Fireplace: frameY determines on/off state
            // Similar to campfire - lower frameY values indicate on
            if (tileType == TileID.Fireplace)
            {
                return tile.TileFrameY < 54;
            }

            // Music Box: frameX 0-35 = off, frameX 36+ = playing
            if (tileType == TileID.MusicBoxes)
            {
                return tile.TileFrameX >= 36;
            }

            // Fountain: frameY determines on/off (toggles by +/- 72)
            if (tileType == TileID.WaterFountain)
            {
                // When frameY is in the "on" range, the fountain animates
                int frameYMod = tile.TileFrameY % 144;
                return frameYMod < 72;
            }

            // Monoliths: frameY determines on/off state
            if (tileType == TileID.LunarMonolith)
            {
                return tile.TileFrameY >= 56;
            }

            if (tileType == TileID.BloodMoonMonolith || tileType == TileID.VoidMonolith ||
                tileType == TileID.EchoMonolith || tileType == TileID.ShimmerMonolith)
            {
                return tile.TileFrameY >= 54;
            }

            // Party Center: toggles party mode
            if (tileType == TileID.PartyMonolith)
            {
                return tile.TileFrameX >= 36;
            }

            return false;
        }

        /// <summary>
        /// Gets the display name for a tile at the given position.
        /// </summary>
        internal static string GetTileName(int tileX, int tileY, int tileType)
        {
            try
            {
                int lookup = MapHelper.TileToLookup(tileType, 0);
                string name = Lang.GetMapObjectName(lookup);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
                // Fall through to ID-based name
            }

            string? idName = TileID.Search.GetName(tileType);
            if (!string.IsNullOrWhiteSpace(idName))
            {
                return idName;
            }

            return $"Tile {tileType}";
        }

        /// <summary>
        /// Announces the toggle state change for a tile.
        /// </summary>
        internal static void AnnounceToggle(int tileX, int tileY, int tileType, bool isOn)
        {
            string tileName = GetTileName(tileX, tileY, tileType);
            string stateKey = isOn
                ? "Mods.TerrariaAccess.TileStates.ToggledOn"
                : "Mods.TerrariaAccess.TileStates.ToggledOff";
            string stateText = Language.GetTextValue(stateKey);
            if (string.IsNullOrWhiteSpace(stateText) || stateText == stateKey)
            {
                stateText = isOn ? "on" : "off";
            }

            string message = $"{tileName}, {stateText}";
            ScreenReaderService.Announce(message, category: ScreenReaderService.AnnouncementCategory.Tile, force: true);
        }
    }

    // Static state for tracking tile toggle before/after interaction
    private static int _pendingToggleTileX = -1;
    private static int _pendingToggleTileY = -1;
    private static int _pendingToggleTileType = -1;
    private static bool _pendingToggleStateBefore;

    private static void HandleTileInteractionsUse(On_Player.orig_TileInteractionsUse orig, Player self, int myX, int myY)
    {
        // Capture state before interaction for toggleable tiles
        _pendingToggleTileX = -1;
        _pendingToggleTileY = -1;
        _pendingToggleTileType = -1;

        if (WorldGen.InWorld(myX, myY, 1))
        {
            Tile tile = Main.tile[myX, myY];
            if (tile.HasTile && TileToggleAnnouncer.IsToggleableTile(tile.TileType))
            {
                _pendingToggleTileX = myX;
                _pendingToggleTileY = myY;
                _pendingToggleTileType = tile.TileType;
                _pendingToggleStateBefore = TileToggleAnnouncer.GetToggleState(tile);
            }
        }

        // Call the original method
        orig(self, myX, myY);

        // Check if state changed after interaction
        if (_pendingToggleTileX >= 0 && _pendingToggleTileY >= 0 && _pendingToggleTileType >= 0)
        {
            if (WorldGen.InWorld(_pendingToggleTileX, _pendingToggleTileY, 1))
            {
                Tile tileAfter = Main.tile[_pendingToggleTileX, _pendingToggleTileY];
                if (tileAfter.HasTile && tileAfter.TileType == _pendingToggleTileType)
                {
                    bool stateAfter = TileToggleAnnouncer.GetToggleState(tileAfter);
                    if (stateAfter != _pendingToggleStateBefore)
                    {
                        TileToggleAnnouncer.AnnounceToggle(
                            _pendingToggleTileX,
                            _pendingToggleTileY,
                            _pendingToggleTileType,
                            stateAfter);
                    }
                }
            }

            _pendingToggleTileX = -1;
            _pendingToggleTileY = -1;
            _pendingToggleTileType = -1;
        }
    }
}
