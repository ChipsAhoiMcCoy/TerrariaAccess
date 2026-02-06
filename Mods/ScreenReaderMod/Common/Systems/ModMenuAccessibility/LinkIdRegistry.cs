#nullable enable

namespace ScreenReaderMod.Common.Systems.ModMenuAccessibility;

/// <summary>
/// Central registry of base link IDs for mod menu accessibility systems.
/// Each system gets a unique range of 100 IDs to prevent collisions in UILinkPointNavigator.
/// </summary>
internal static class LinkIdRegistry
{
    /// <summary>
    /// Base link ID for ManageModsAccessibilitySystem (UIMods screen).
    /// Range: 3100-3199
    /// </summary>
    internal const int ManageMods = 3100;

    /// <summary>
    /// Base link ID for DownloadModsAccessibilitySystem (UIModBrowser screen).
    /// Range: 3200-3299
    /// Note: Changed from 3300 to avoid collision with ModInfo.
    /// </summary>
    internal const int DownloadMods = 3200;

    /// <summary>
    /// Base link ID for ModInfoAccessibilitySystem (UIModInfo screen).
    /// Range: 3300-3399
    /// </summary>
    internal const int ModInfo = 3300;

    /// <summary>
    /// Reserved range for dialog-specific link points.
    /// Range: 3400-3499
    /// </summary>
    internal const int DialogBase = 3400;

    /// <summary>
    /// Safe link point ID for dialog Yes button.
    /// </summary>
    internal const int DialogYes = 3400;

    /// <summary>
    /// Safe link point ID for dialog No button.
    /// </summary>
    internal const int DialogNo = 3401;

    /// <summary>
    /// Base link ID for ModPacksAccessibilitySystem (UIModPacks screen).
    /// Range: 3500-3599
    /// </summary>
    internal const int ModPacks = 3500;

    /// <summary>
    /// Base link ID for ModSourcesAccessibilitySystem (UIModSources screen).
    /// Range: 3600-3699
    /// </summary>
    internal const int ModSources = 3600;
}
