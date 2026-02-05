#nullable enable

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// Interface for components that need to perform per-frame updates.
/// </summary>
/// <remarks>
/// Components implementing this interface can be registered with an update scheduler
/// to receive regular update calls during the game loop.
///
/// Typical usage:
/// <code>
/// public class MyNarrator : IUpdatable
/// {
///     public void Update()
///     {
///         // Check state changes, announce if needed
///     }
/// }
/// </code>
/// </remarks>
public interface IUpdatable
{
    /// <summary>
    /// Called each frame to update the component's state.
    /// </summary>
    /// <remarks>
    /// Implementations should:
    /// - Keep update logic lightweight (called every frame)
    /// - Avoid allocations in hot paths
    /// - Use rate limiting for expensive operations
    ///
    /// This method is called on the main thread during the game update loop.
    /// </remarks>
    void Update();
}

/// <summary>
/// Extended update interface that receives timing information.
/// </summary>
/// <remarks>
/// Use this interface when your component needs to know about game timing,
/// such as for animations, cooldowns, or time-based state transitions.
/// </remarks>
public interface IUpdatableWithContext
{
    /// <summary>
    /// Called each frame with timing context.
    /// </summary>
    /// <param name="context">Timing and game state information for this frame.</param>
    void Update(UpdateContext context);
}

/// <summary>
/// Context information passed to updatable components each frame.
/// </summary>
/// <param name="DeltaTime">Time elapsed since the last frame in seconds.</param>
/// <param name="TotalTime">Total game time elapsed since startup in seconds.</param>
/// <param name="IsPaused">Whether the game is currently paused.</param>
/// <param name="IsInMenu">Whether the player is in the main menu.</param>
public readonly record struct UpdateContext(
    float DeltaTime,
    double TotalTime,
    bool IsPaused,
    bool IsInMenu);
