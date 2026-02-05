#nullable enable
using System.Collections.Generic;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Interface for menu-specific narration handlers.
/// Each handler is responsible for a specific menu type or group of related menus.
/// </summary>
internal interface IMenuHandler
{
    /// <summary>
    /// Determines if this handler can handle the given menu context.
    /// </summary>
    /// <param name="menuMode">The current Terraria menu mode.</param>
    /// <param name="uiState">The current UI state, if any.</param>
    /// <returns>True if this handler should handle the menu.</returns>
    bool CanHandle(int menuMode, UIState? uiState);

    /// <summary>
    /// Handler priority. Higher values are checked first.
    /// Use this to ensure specialized handlers take precedence over general ones.
    /// </summary>
    /// <remarks>
    /// Suggested ranges:
    /// - 100+: Highly specific handlers (mod config, virtual keyboard)
    /// - 50-99: Menu-type specific handlers (settings, multiplayer, character creation)
    /// - 1-49: General handlers
    /// - 0: Fallback/default handler
    /// </remarks>
    int Priority { get; }

    /// <summary>
    /// Called when entering a menu this handler manages.
    /// </summary>
    /// <param name="context">The current menu context.</param>
    void OnEntered(MenuNarrationContext context);

    /// <summary>
    /// Called when leaving a menu this handler was managing.
    /// </summary>
    void OnLeft();

    /// <summary>
    /// Called each frame to generate narration events.
    /// </summary>
    /// <param name="context">The current menu context.</param>
    /// <returns>Zero or more narration events to announce.</returns>
    IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context);
}
