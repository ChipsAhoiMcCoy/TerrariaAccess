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
    public static string PlacedWiring(int wireSegments, int actuators, int selectedPositions)
    {
        string placed = FormatWiringCounts(wireSegments, actuators);
        return ShouldIncludeSelectionTotal(wireSegments + actuators, selectedPositions)
            ? $"Build mode: placed {placed} across {selectedPositions} selected tiles."
            : $"Build mode: placed {placed}.";
    }

    public static string CannotPlaceWiring() => "Build mode: could not place wiring in the selected area.";
    public static string RemovedWiring(int wireSegments, int actuators, int selectedPositions)
    {
        string removed = FormatWiringCounts(wireSegments, actuators);
        return ShouldIncludeSelectionTotal(wireSegments + actuators, selectedPositions)
            ? $"Build mode: removed {removed} from {selectedPositions} selected tiles."
            : $"Build mode: removed {removed}.";
    }

    public static string NoWiringToRemove() => "Build mode: no wiring to remove in the selected area.";
    public static string ActuatedTiles(int count, int selectedPositions)
    {
        return ShouldIncludeSelectionTotal(count, selectedPositions)
            ? $"Build mode: toggled actuators on {count} of {selectedPositions} selected tiles."
            : $"Build mode: toggled actuators on {count} tiles.";
    }

    public static string NoActuatorsToToggle() => "Build mode: no actuators to toggle in the selected area.";

    // Housing announcements
    public static string HousingSuitable() => "Suitable housing.";
    public static string HousingOccupied(string npcName) => $"{TextSanitizer.Clean(npcName)}'s house.";
    public static string HousingMissingFurniture(string missingItems) => $"Unsuitable housing: missing {missingItems}.";

    private static bool ShouldIncludeSelectionTotal(int count, int selectedPositions) =>
        selectedPositions > 0 && selectedPositions != count;

    private static string FormatWiringCounts(int wireSegments, int actuators)
    {
        string wireText = wireSegments == 1 ? "1 wire segment" : $"{wireSegments} wire segments";
        string actuatorText = actuators == 1 ? "1 actuator" : $"{actuators} actuators";

        if (wireSegments > 0 && actuators > 0)
        {
            return $"{wireText} and {actuatorText}";
        }

        return wireSegments > 0 ? wireText : actuatorText;
    }
}
