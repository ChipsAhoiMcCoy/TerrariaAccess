#nullable enable
using System;

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// UI narration areas used to coordinate which narrator should handle events.
/// This is a flags enum allowing combination of multiple areas.
/// </summary>
/// <remarks>
/// <para>
/// When multiple narrators could potentially respond to the same event (e.g., an ItemSlot draw),
/// the UI area helps determine which narrator has priority or should handle it.
/// </para>
/// <para>
/// Use <see cref="ResolveFromSlotContext"/> to map ItemSlot contexts to areas.
/// </para>
/// </remarks>
[Flags]
public enum UiNarrationArea
{
    /// <summary>
    /// Unknown or uninitialized area.
    /// Narrators should allow the event through when area is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Main player inventory (10x5 grid, coins, ammo, hotbar).
    /// </summary>
    Inventory = 1 << 0,

    /// <summary>
    /// Storage containers (chests, piggy bank, safe, void vault).
    /// </summary>
    Storage = 1 << 1,

    /// <summary>
    /// Crafting UI (recipe list, crafting materials display).
    /// </summary>
    Crafting = 1 << 2,

    /// <summary>
    /// Guide NPC crafting lookup interface.
    /// </summary>
    Guide = 1 << 3,

    /// <summary>
    /// Goblin Tinkerer reforge interface.
    /// </summary>
    Reforge = 1 << 4,

    /// <summary>
    /// Journey Mode / Creative mode interfaces.
    /// </summary>
    Creative = 1 << 5,

    /// <summary>
    /// NPC shop interfaces.
    /// </summary>
    Shop = 1 << 6,

    /// <summary>
    /// NPC dialogue and chat interfaces.
    /// </summary>
    Dialogue = 1 << 7,

    /// <summary>
    /// In-game settings/options menu (IngameFancyUI).
    /// </summary>
    Settings = 1 << 8,

    /// <summary>
    /// Equipment slots (armor, accessories, vanity, dyes).
    /// </summary>
    Equipment = 1 << 9,

    /// <summary>
    /// Hotbar (quick-access row of inventory).
    /// </summary>
    Hotbar = 1 << 10,

    /// <summary>
    /// Housing/NPC banner assignment interface.
    /// </summary>
    Housing = 1 << 11,

    /// <summary>
    /// Bestiary interface.
    /// </summary>
    Bestiary = 1 << 12,

    /// <summary>
    /// Achievements interface.
    /// </summary>
    Achievements = 1 << 13,

    /// <summary>
    /// Map interface (fullscreen or minimap).
    /// </summary>
    Map = 1 << 14,

    /// <summary>
    /// Emote menu.
    /// </summary>
    Emotes = 1 << 15,

    #region Common Combinations

    /// <summary>
    /// All inventory-related areas (main inventory, equipment, hotbar).
    /// </summary>
    AllInventory = Inventory | Equipment | Hotbar,

    /// <summary>
    /// All crafting-related areas (crafting, guide).
    /// </summary>
    AllCrafting = Crafting | Guide,

    /// <summary>
    /// All NPC interaction areas (dialogue, shop, guide, reforge).
    /// </summary>
    AllNpcInteraction = Dialogue | Shop | Guide | Reforge,

    /// <summary>
    /// All storage areas (chests, banks, etc.).
    /// </summary>
    AllStorage = Storage,

    #endregion
}

/// <summary>
/// Extension methods for UiNarrationArea.
/// </summary>
public static class UiNarrationAreaExtensions
{
    /// <summary>
    /// Checks if the area contains any of the specified flags.
    /// </summary>
    public static bool HasAnyFlag(this UiNarrationArea area, UiNarrationArea flags)
    {
        return (area & flags) != 0;
    }

    /// <summary>
    /// Checks if the area contains all of the specified flags.
    /// </summary>
    public static bool HasAllFlags(this UiNarrationArea area, UiNarrationArea flags)
    {
        return (area & flags) == flags;
    }

    /// <summary>
    /// Gets a human-readable name for the primary area.
    /// </summary>
    public static string GetDisplayName(this UiNarrationArea area)
    {
        // Handle single flags
        return area switch
        {
            UiNarrationArea.Unknown => "Unknown",
            UiNarrationArea.Inventory => "Inventory",
            UiNarrationArea.Storage => "Storage",
            UiNarrationArea.Crafting => "Crafting",
            UiNarrationArea.Guide => "Guide",
            UiNarrationArea.Reforge => "Reforge",
            UiNarrationArea.Creative => "Creative",
            UiNarrationArea.Shop => "Shop",
            UiNarrationArea.Dialogue => "Dialogue",
            UiNarrationArea.Settings => "Settings",
            UiNarrationArea.Equipment => "Equipment",
            UiNarrationArea.Hotbar => "Hotbar",
            UiNarrationArea.Housing => "Housing",
            UiNarrationArea.Bestiary => "Bestiary",
            UiNarrationArea.Achievements => "Achievements",
            UiNarrationArea.Map => "Map",
            UiNarrationArea.Emotes => "Emotes",
            _ => area.ToString()
        };
    }
}
