#nullable enable
using Microsoft.Xna.Framework;

namespace ScreenReaderMod.Common.Systems.Guidance;

/// <summary>
/// Category of guidance entry for distinguishing between different scannable target types.
/// </summary>
internal enum GuidanceCategory
{
    Npc,
    Interactable,
    Player,
    DroppedItem,
    Critter,
    Plantlife
}

/// <summary>
/// Unified guidance entry representing any scannable target in the world.
/// Replaces the previous 6 separate struct types (NpcGuidanceEntry, InteractableGuidanceEntry, etc.)
/// </summary>
internal readonly struct GuidanceEntry
{
    /// <summary>
    /// The category of this guidance entry.
    /// </summary>
    public readonly GuidanceCategory Category;

    /// <summary>
    /// Entity index for Npc/Player/DroppedItem/Critter categories.
    /// For Interactable/Plantlife categories, this is -1.
    /// </summary>
    public readonly int Index;

    /// <summary>
    /// Display name shown to the user.
    /// </summary>
    public readonly string DisplayName;

    /// <summary>
    /// World position of the target.
    /// For Npc/Player entries, this is calculated at creation time and may need refresh.
    /// </summary>
    public readonly Vector2 WorldPosition;

    /// <summary>
    /// Distance from the player in tiles at the time of scanning.
    /// </summary>
    public readonly float DistanceTiles;

    /// <summary>
    /// Tile anchor point for Interactable/Plantlife categories.
    /// For other categories, this is Point.Zero.
    /// </summary>
    public readonly Point Anchor;

    public GuidanceEntry(
        GuidanceCategory category,
        int index,
        string displayName,
        Vector2 worldPosition,
        float distanceTiles,
        Point anchor = default)
    {
        Category = category;
        Index = index;
        DisplayName = displayName;
        WorldPosition = worldPosition;
        DistanceTiles = distanceTiles;
        Anchor = anchor;
    }

    /// <summary>
    /// Creates an NPC guidance entry.
    /// </summary>
    public static GuidanceEntry CreateNpc(int npcIndex, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.Npc, npcIndex, displayName, worldPosition, distanceTiles);

    /// <summary>
    /// Creates a player guidance entry.
    /// </summary>
    public static GuidanceEntry CreatePlayer(int playerIndex, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.Player, playerIndex, displayName, worldPosition, distanceTiles);

    /// <summary>
    /// Creates an interactable (crafting station) guidance entry.
    /// </summary>
    public static GuidanceEntry CreateInteractable(Point anchor, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.Interactable, -1, displayName, worldPosition, distanceTiles, anchor);

    /// <summary>
    /// Creates a dropped item guidance entry.
    /// </summary>
    public static GuidanceEntry CreateDroppedItem(int itemIndex, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.DroppedItem, itemIndex, displayName, worldPosition, distanceTiles);

    /// <summary>
    /// Creates a critter guidance entry.
    /// </summary>
    public static GuidanceEntry CreateCritter(int npcIndex, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.Critter, npcIndex, displayName, worldPosition, distanceTiles);

    /// <summary>
    /// Creates a plantlife guidance entry.
    /// </summary>
    public static GuidanceEntry CreatePlantlife(Point anchor, string displayName, Vector2 worldPosition, float distanceTiles)
        => new(GuidanceCategory.Plantlife, -1, displayName, worldPosition, distanceTiles, anchor);
}
