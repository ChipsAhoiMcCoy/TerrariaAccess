#nullable enable
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.BuildMode;

internal static class BuildModeNarrationCatalog
{
    public static string Enabled() => "Build mode enabled. Mark two corners, then hold use to act.";
    public static string Disabled() => "Build mode disabled.";
    public static string CursorOutOfBounds() => "Build mode: cursor is out of world bounds.";
    public static string PointOneSet() => "Build mode: point one set.";
    public static string SelectionReset() => "Build mode: selection reset. Point one set.";
    public static string SelectionSet(int widthTiles, int heightTiles) => $"Build mode: points set. Selection is {widthTiles} by {heightTiles} tiles.";
    public static string ClearedBlocks(int count, string itemName) => $"Build mode: cleared {count} blocks with {TextSanitizer.Clean(itemName)}.";
    public static string NothingToClear() => "Build mode: nothing to clear in the selected area.";
    public static string PlacedTiles(int count, string blockName) => $"Build mode: placed {count} tiles of {TextSanitizer.Clean(blockName)}.";
    public static string CannotPlaceTiles() => "Build mode: could not place tiles in the selected area.";
    public static string PlacedWalls(int count, string wallName) => $"Build mode: placed {count} walls of {TextSanitizer.Clean(wallName)}.";
    public static string CannotPlaceWalls() => "Build mode: could not place walls in the selected area.";
}
