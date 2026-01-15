#nullable enable

namespace ScreenReaderMod.Common.Abstractions;

/// <summary>
/// Abstraction for Terraria's localization system to enable unit testing.
/// </summary>
public interface ITerrariaLocalization
{
    /// <summary>
    /// Gets the localized coin label for the given index.
    /// Index 15 = Platinum, 16 = Gold, 17 = Silver, 18 = Copper.
    /// </summary>
    string GetCoinLabel(int index);
}
