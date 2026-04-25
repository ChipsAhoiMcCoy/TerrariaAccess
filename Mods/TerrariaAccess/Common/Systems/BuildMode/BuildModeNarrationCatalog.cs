#nullable enable
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Systems.BuildMode;

internal static class BuildModeNarrationCatalog
{
    public static string Enabled(bool outlineMode) => outlineMode ? "Build Mode: Outline." : "Build Mode: Fill.";
    public static string Disabled() => "Build Mode: Disabled.";
    public static string CursorOutOfBounds() => "Build mode: cursor is out of world bounds.";
    public static string PointOneSet() => "Build mode: point one set.";
    public static string SelectionReset() => "Build mode: selection reset. Point one set.";
    public static string SelectionSet(int widthTiles, int heightTiles) => $"Build mode: points set. Selection is {widthTiles} by {heightTiles} tiles.";
    public static string ClearedBlocks(int count, int selectedPositions, string itemName)
    {
        string cleanItemName = TextSanitizer.Clean(itemName);
        return ShouldIncludeSelectionTotal(count, selectedPositions)
            ? $"Build mode: cleared {count} blocks with {cleanItemName} from {selectedPositions} selected tiles."
            : $"Build mode: cleared {count} blocks with {cleanItemName}.";
    }

    public static string NothingToClear() => "Build mode: nothing to clear in the selected area.";
    public static string PlacedTiles(int count, int selectedPositions, string blockName)
    {
        string cleanBlockName = TextSanitizer.Clean(blockName);
        return ShouldIncludeSelectionTotal(count, selectedPositions)
            ? $"Build mode: placed {count} of {selectedPositions} selected tiles of {cleanBlockName}."
            : $"Build mode: placed {count} tiles of {cleanBlockName}.";
    }

    public static string CannotPlaceTiles() => "Build mode: could not place tiles in the selected area.";
    public static string PlacedWalls(int count, int selectedPositions, string wallName)
    {
        string cleanWallName = TextSanitizer.Clean(wallName);
        return ShouldIncludeSelectionTotal(count, selectedPositions)
            ? $"Build mode: placed {count} of {selectedPositions} selected walls of {cleanWallName}."
            : $"Build mode: placed {count} walls of {cleanWallName}.";
    }

    public static string CannotPlaceWalls() => "Build mode: could not place walls in the selected area.";

    // Housing announcements
    public static string HousingSuitable() => "Suitable housing.";
    public static string HousingOccupied(string npcName) => $"{TextSanitizer.Clean(npcName)}'s house.";
    public static string HousingMissingFurniture(string missingItems) => $"Unsuitable housing: missing {missingItems}.";

    private static bool ShouldIncludeSelectionTotal(int count, int selectedPositions) =>
        selectedPositions > 0 && selectedPositions != count;
}
