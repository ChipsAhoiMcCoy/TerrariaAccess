#nullable enable
using System;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Common.Systems.BuildMode;

/// <summary>
/// Announces housing suitability when walking through rooms in build mode.
/// Automatically detects when the player enters a valid housing area
/// and triggers the housing check, which outputs messages caught by the
/// existing TryAnnounceHousingQuery hook in InGameNarrationSystem.
/// </summary>
internal sealed class BuildModeHousingAnnouncer
{
    /// <summary>
    /// Minimum frames between housing checks while moving.
    /// </summary>
    private const int CheckCooldownFrames = 30;

    /// <summary>
    /// Minimum distance in tiles the player must move before re-checking.
    /// </summary>
    private const int MinMoveDistanceTiles = 3;

    private int _checkCooldown;
    private Point _lastCheckPosition;
    private Rectangle _lastAnnouncedRoom;

    /// <summary>
    /// Checks housing suitability at the player's current position and announces
    /// if the player has entered a new room area.
    /// </summary>
    /// <param name="player">The local player.</param>
    public void Update(Player player)
    {
        if (player is null || !player.active)
        {
            return;
        }

        // Cooldown check
        if (_checkCooldown > 0)
        {
            _checkCooldown--;
            return;
        }

        Point playerTile = player.Center.ToTileCoordinates();

        // Check if player has moved enough to warrant a new check
        int dx = Math.Abs(playerTile.X - _lastCheckPosition.X);
        int dy = Math.Abs(playerTile.Y - _lastCheckPosition.Y);
        if (dx < MinMoveDistanceTiles && dy < MinMoveDistanceTiles)
        {
            return;
        }

        _lastCheckPosition = playerTile;
        _checkCooldown = CheckCooldownFrames;

        // Perform the housing check
        CheckAndAnnounceHousing(playerTile.X, playerTile.Y);
    }

    /// <summary>
    /// Resets the announcer state. Call when build mode is toggled off.
    /// </summary>
    public void Reset()
    {
        _checkCooldown = 0;
        _lastCheckPosition = Point.Zero;
        _lastAnnouncedRoom = Rectangle.Empty;
    }

    /// <summary>
    /// Gets the initial housing status for the player's current position without announcing it.
    /// This is intended to be called when build mode is first enabled so the housing status
    /// can be included in the initial announcement. Also initializes state to prevent
    /// the Update method from immediately re-announcing.
    /// </summary>
    /// <param name="player">The local player.</param>
    /// <returns>The housing status string, or null if not in valid housing.</returns>
    public string? GetInitialHousingStatus(Player player)
    {
        if (player is null || !player.active)
        {
            return null;
        }

        Point playerTile = player.Center.ToTileCoordinates();

        // Initialize state to prevent immediate re-announcement in Update
        _lastCheckPosition = playerTile;
        _checkCooldown = CheckCooldownFrames;

        if (!WorldGen.InWorld(playerTile.X, playerTile.Y, 10))
        {
            return null;
        }

        // Check if this position is in a valid room structure
        bool isValidRoom = WorldGen.StartRoomCheck(playerTile.X, playerTile.Y);

        if (!isValidRoom)
        {
            _lastAnnouncedRoom = Rectangle.Empty;
            return null;
        }

        // We're in a valid room - get the bounds and store it
        Rectangle currentRoom = new(WorldGen.roomX1, WorldGen.roomY1,
            WorldGen.roomX2 - WorldGen.roomX1, WorldGen.roomY2 - WorldGen.roomY1);
        _lastAnnouncedRoom = currentRoom;

        // First check if an NPC lives here
        string? occupantName = FindRoomOccupant();
        if (occupantName != null)
        {
            return BuildModeNarrationCatalog.HousingOccupied(occupantName);
        }

        // No occupant - do the full housing check
        if (WorldGen.MoveTownNPC(playerTile.X, playerTile.Y, -1))
        {
            return BuildModeNarrationCatalog.HousingSuitable();
        }

        // Housing check failed - error messages were output via Main.NewText
        // which will be announced separately by the existing hook
        return null;
    }

    private void CheckAndAnnounceHousing(int tileX, int tileY)
    {
        if (!WorldGen.InWorld(tileX, tileY, 10))
        {
            return;
        }

        // Check if this position is in a valid room structure
        bool isValidRoom = WorldGen.StartRoomCheck(tileX, tileY);

        if (!isValidRoom)
        {
            // Player is not in a valid room - clear state but don't announce
            _lastAnnouncedRoom = Rectangle.Empty;
            return;
        }

        // We're in a valid room - get the bounds
        Rectangle currentRoom = new(WorldGen.roomX1, WorldGen.roomY1,
            WorldGen.roomX2 - WorldGen.roomX1, WorldGen.roomY2 - WorldGen.roomY1);

        // Check if this is a different room than last announced
        if (IsSameRoom(currentRoom, _lastAnnouncedRoom))
        {
            return;
        }

        _lastAnnouncedRoom = currentRoom;

        // First check if an NPC lives here so we can announce their name
        string? occupantName = FindRoomOccupant();
        if (occupantName != null)
        {
            // Announce the occupant's name directly
            ScreenReaderService.Announce(BuildModeNarrationCatalog.HousingOccupied(occupantName), force: true);
            return;
        }

        // No occupant - trigger the full housing check using MoveTownNPC with -1 for query mode.
        // MoveTownNPC outputs error messages via Main.NewText which are caught by
        // TryAnnounceHousingQuery in InGameNarrationSystem. However, it returns true
        // silently on success without outputting a message, so we announce success directly.
        if (WorldGen.MoveTownNPC(tileX, tileY, -1))
        {
            // Housing is valid - announce success
            ScreenReaderService.Announce(BuildModeNarrationCatalog.HousingSuitable(), force: true);
        }
    }

    /// <summary>
    /// Finds the name of the NPC who lives in the current room (after StartRoomCheck has been called).
    /// </summary>
    /// <returns>The NPC's name, or null if no one lives here.</returns>
    private static string? FindRoomOccupant()
    {
        // Check all active town NPCs to see if any have their home in this room
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc is null || !npc.active || !npc.townNPC || npc.homeless)
            {
                continue;
            }

            int homeX = npc.homeTileX;
            int homeY = npc.homeTileY;

            // Check if NPC's home tile is within the current room tiles
            // (populated by StartRoomCheck)
            for (int j = 0; j < WorldGen.numRoomTiles; j++)
            {
                // NPCs stand on their home tile, so check both the tile and one above
                if (WorldGen.roomX[j] == homeX &&
                    (WorldGen.roomY[j] == homeY || WorldGen.roomY[j] == homeY - 1))
                {
                    return npc.GivenOrTypeName;
                }
            }
        }

        return null;
    }

    private static bool IsSameRoom(Rectangle a, Rectangle b)
    {
        // Consider rooms the same if they overlap significantly
        if (a == Rectangle.Empty || b == Rectangle.Empty)
        {
            return false;
        }

        // Check if the centers are within a small distance
        Point centerA = new(a.X + a.Width / 2, a.Y + a.Height / 2);
        Point centerB = new(b.X + b.Width / 2, b.Y + b.Height / 2);

        int dx = Math.Abs(centerA.X - centerB.X);
        int dy = Math.Abs(centerA.Y - centerB.Y);

        return dx < 5 && dy < 5;
    }
}
