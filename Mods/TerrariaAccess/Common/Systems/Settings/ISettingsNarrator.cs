#nullable enable
namespace TerrariaAccess.Common.Systems.Settings;

/// <summary>
/// Interface for settings menu narrators. Provides a unified API for
/// narrating settings across in-game options, controls menu, and mod config.
/// </summary>
internal interface ISettingsNarrator
{
    /// <summary>
    /// Called when the settings menu is opened.
    /// </summary>
    void OnMenuOpened();

    /// <summary>
    /// Called when the settings menu is closed.
    /// </summary>
    void OnMenuClosed();

    /// <summary>
    /// Updates the narrator state. Called each frame when the menu is active.
    /// </summary>
    void Update();

    /// <summary>
    /// Resets all narrator state.
    /// </summary>
    void Reset();

    /// <summary>
    /// Returns true if this narrator is currently active and handling a menu.
    /// </summary>
    bool IsActive { get; }
}
