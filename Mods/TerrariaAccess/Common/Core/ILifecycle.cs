#nullable enable

namespace TerrariaAccess.Common.Core;

/// <summary>
/// Interface for components that participate in the mod's lifecycle.
/// Provides a standard contract for initialization and cleanup phases.
/// </summary>
/// <remarks>
/// Components implementing this interface can be registered with the ServiceLocator
/// and will have their lifecycle methods called at appropriate times.
///
/// Typical usage:
/// <code>
/// public class MyService : ILifecycle
/// {
///     public void Load()
///     {
///         // Initialize resources, subscribe to events
///     }
///
///     public void Unload()
///     {
///         // Release resources, unsubscribe from events
///     }
/// }
/// </code>
/// </remarks>
public interface ILifecycle
{
    /// <summary>
    /// Called when the component should initialize its resources.
    /// This is called during mod load, after core services are available.
    /// </summary>
    /// <remarks>
    /// Implementations should:
    /// - Initialize any required resources
    /// - Subscribe to relevant events via EventBus
    /// - Register any services with ServiceLocator
    ///
    /// This method may be called on the main thread. Avoid blocking operations.
    /// </remarks>
    void Load();

    /// <summary>
    /// Called when the component should release its resources.
    /// This is called during mod unload, before core services are destroyed.
    /// </summary>
    /// <remarks>
    /// Implementations should:
    /// - Release all managed and unmanaged resources
    /// - Unsubscribe from all events
    /// - Clear any static state
    ///
    /// This method must not throw exceptions. Handle cleanup errors gracefully.
    /// </remarks>
    void Unload();
}
