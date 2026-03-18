#nullable enable
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.UI;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Provides runtime context information about the current game state.
/// This interface abstracts context retrieval to enable testing and alternative implementations.
/// </summary>
public interface IRuntimeContextProvider
{
    /// <summary>
    /// Gets a snapshot of the current runtime context.
    /// </summary>
    /// <returns>A snapshot containing current game state information.</returns>
    RuntimeContextSnapshot GetSnapshot();
}

/// <summary>
/// A snapshot of the runtime context at a point in time.
/// Provides comprehensive information about the current game state.
/// </summary>
public readonly record struct RuntimeContextSnapshot(
    /// <summary>Whether running as a dedicated server.</summary>
    bool IsServer,

    /// <summary>Whether the main menu is displayed.</summary>
    bool InMenu,

    /// <summary>Whether a game UI overlay is open (InGameUI or ingame options).</summary>
    bool InGameUiOpen,

    /// <summary>Whether there is an active local player.</summary>
    bool HasActivePlayer,

    /// <summary>Whether the game is paused (includes menu and settings).</summary>
    bool IsPaused,

    /// <summary>Whether a world is loaded and active.</summary>
    bool WorldActive,

    /// <summary>The current menu mode identifier.</summary>
    int MenuMode,

    /// <summary>Whether the inventory is open.</summary>
    bool InventoryOpen,

    /// <summary>Whether a chest interface is open.</summary>
    bool ChestOpen,

    /// <summary>The current chest ID if a chest is open, -1 otherwise.</summary>
    int ChestId,

    /// <summary>Whether talking to an NPC.</summary>
    bool TalkingToNpc,

    /// <summary>The NPC index being talked to, -1 if not talking.</summary>
    int TalkNpcIndex,

    /// <summary>Whether the player sign interface is open.</summary>
    bool SignOpen,

    /// <summary>Whether the crafting interface is available.</summary>
    bool CraftingAvailable,

    /// <summary>Whether the map is in fullscreen mode.</summary>
    bool MapFullscreen,

    /// <summary>Whether smart cursor is enabled.</summary>
    bool SmartCursorEnabled,

    /// <summary>The current UI state type name, if any.</summary>
    string? CurrentUiStateName,

    /// <summary>The current game update frame count.</summary>
    uint GameUpdateCount)
{
    /// <summary>
    /// Gets whether gameplay narration can be performed.
    /// </summary>
    public bool CanNarrateGameplay => WorldActive && !IsPaused && !InGameUiOpen;

    /// <summary>
    /// Gets whether the player is in any kind of container interface.
    /// </summary>
    public bool InContainerInterface => ChestOpen || InventoryOpen;

    /// <summary>
    /// Gets whether the player is in a dialogue or shop interface.
    /// </summary>
    public bool InDialogueInterface => TalkingToNpc && TalkNpcIndex >= 0;
}

/// <summary>
/// Default implementation of <see cref="IRuntimeContextProvider"/> that reads from Terraria's Main class.
/// </summary>
internal sealed class TerrariaRuntimeContextProvider : IRuntimeContextProvider
{
    /// <summary>
    /// Singleton instance for backward compatibility.
    /// </summary>
    public static TerrariaRuntimeContextProvider Instance { get; } = new();

    private TerrariaRuntimeContextProvider() { }

    public RuntimeContextSnapshot GetSnapshot()
    {
        bool isServer = Main.dedServ;
        bool inMenu = Main.gameMenu;

        // Determine UI state
        UserInterface? inGameUI = Main.InGameUI;
        UIState? currentState = inGameUI?.CurrentState;
        bool inGameUi = currentState is not null || Main.ingameOptionsWindow;
        string? uiStateName = currentState?.GetType().Name;

        // Player state
        Player? localPlayer = isServer ? null : Main.LocalPlayer;
        bool hasActivePlayer = localPlayer is { active: true };
        bool worldActive = hasActivePlayer && !inMenu;

        // Pause state includes ingameOptionsWindow so settings narration works in multiplayer
        bool paused = Main.gamePaused || inMenu || Main.ingameOptionsWindow;

        // Inventory and container state
        bool inventoryOpen = hasActivePlayer && Main.playerInventory;
        bool chestOpen = hasActivePlayer && localPlayer!.chest != -1;
        int chestId = hasActivePlayer ? localPlayer!.chest : -1;

        // NPC interaction state
        bool talkingToNpc = hasActivePlayer && localPlayer!.talkNPC != -1;
        int talkNpcIndex = hasActivePlayer ? localPlayer!.talkNPC : -1;

        // Sign state
        bool signOpen = hasActivePlayer && Main.editSign;

        // Crafting availability
        bool craftingAvailable = inventoryOpen && Main.numAvailableRecipes > 0;

        // Map state
        bool mapFullscreen = Main.mapFullscreen;

        // Smart cursor
        bool smartCursorEnabled = Main.SmartCursorIsUsed;

        return new RuntimeContextSnapshot(
            IsServer: isServer,
            InMenu: inMenu,
            InGameUiOpen: inGameUi,
            HasActivePlayer: hasActivePlayer,
            IsPaused: paused,
            WorldActive: worldActive,
            MenuMode: Main.menuMode,
            InventoryOpen: inventoryOpen,
            ChestOpen: chestOpen,
            ChestId: chestId,
            TalkingToNpc: talkingToNpc,
            TalkNpcIndex: talkNpcIndex,
            SignOpen: signOpen,
            CraftingAvailable: craftingAvailable,
            MapFullscreen: mapFullscreen,
            SmartCursorEnabled: smartCursorEnabled,
            CurrentUiStateName: uiStateName,
            GameUpdateCount: Main.GameUpdateCount);
    }
}

/// <summary>
/// Static facade for backward compatibility with existing code.
/// Delegates to <see cref="TerrariaRuntimeContextProvider.Instance"/>.
/// </summary>
internal static class RuntimeContext
{
    private static IRuntimeContextProvider _provider = TerrariaRuntimeContextProvider.Instance;

    /// <summary>
    /// Gets or sets the runtime context provider.
    /// Allows injection of alternative providers for testing.
    /// </summary>
    public static IRuntimeContextProvider Provider
    {
        get => _provider;
        set => _provider = value ?? TerrariaRuntimeContextProvider.Instance;
    }

    /// <summary>
    /// Resets the provider to the default Terraria implementation.
    /// </summary>
    public static void ResetProvider()
    {
        _provider = TerrariaRuntimeContextProvider.Instance;
    }

    /// <summary>
    /// Gets a snapshot of the current runtime context.
    /// </summary>
    public static RuntimeContextSnapshot GetSnapshot() => _provider.GetSnapshot();
}
