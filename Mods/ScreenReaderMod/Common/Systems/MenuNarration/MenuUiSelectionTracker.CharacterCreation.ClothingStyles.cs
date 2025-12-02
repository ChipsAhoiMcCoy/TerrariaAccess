#nullable enable
#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static readonly string?[] ClothingStyleDescriptions =
    {
        "Masculine outfit featuring a long-sleeved primary-color shirt layered under a contrasting vest, paired with straight-leg pants and sturdy boots mostly hidden beneath the cuffs,",
        "Casual masculine look with a short-sleeved shirt under a secondary-color vest, matched with relaxed-fit jeans that drape over rugged boots,",
        "Sporty masculine style with a fitted tank top, skinny jeans tucked into boots, and simple armbands at the wrists,",
        "Edgy masculine ensemble with a basic T-shirt layered under a long trenchcoat, finished with jeans and utilitarian boots,",
        "Feminine outfit with a long-sleeved T-shirt topped by a form-fitting chestplate that subtly shows cleavage, worn with shorts and flats,",
        "Relaxed feminine style with a short-sleeved shirt, a secondary-color chestplate, loose-fit jeans, and sturdy boots,",
        "Sleek feminine look featuring a tank top, delicate arm bands, skinny jeans, and stylish boots,",
        "Layered feminine ensemble with a long-sleeved shirt, a short-sleeved trenchcoat, skinny jeans, and boots,",
        "Elegant, wizard-like masculine attire with a Roman-inspired draped shirt, one shoulder pad, and a long flowing skirt paired with simple flats,",
        "Soft, flowing feminine outfit with a tank top, detached sleeves, and a long, graceful skirt paired with flats,",
    };

    // Ordinal order for the clothing styles as presented in-game.
    // Index is styleId (zero-based), value is the spoken position.
    private static readonly int[] ClothingStylePositions =
    {
        1,  // style 1
        3,  // style 2
        2,  // style 3
        4,  // style 4
        6,  // style 5
        8,  // style 6
        7,  // style 7
        9,  // style 8
        5,  // style 9
        10, // style 10
    };

    private static bool TryGetClothingStyleDescription(int styleId, [NotNullWhen(true)] out string? description)
    {
        if ((uint)styleId < (uint)ClothingStyleDescriptions.Length)
        {
            description = ClothingStyleDescriptions[styleId];
            return !string.IsNullOrWhiteSpace(description);
        }

        description = null;
        return false;
    }

    private static bool TryGetClothingStylePosition(int styleId, out int position)
    {
        if ((uint)styleId < (uint)ClothingStylePositions.Length)
        {
            position = ClothingStylePositions[styleId];
            return position > 0;
        }

        position = 0;
        return false;
    }
}
