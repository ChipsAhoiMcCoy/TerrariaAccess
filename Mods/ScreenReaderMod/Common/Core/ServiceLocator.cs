#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// A lightweight service locator for managing shared services.
/// Provides a cleaner alternative to scattered static references while
/// avoiding the complexity of full dependency injection.
/// </summary>
/// <remarks>
/// Design rationale:
/// - Centralizes service access without requiring constructor injection
/// - Supports hot-reloading by allowing service re-registration
/// - Provides lifecycle management hooks for registered services
///
/// Usage example:
/// <code>
/// // During mod load:
/// ServiceLocator.Register&lt;IScreenReader&gt;(new TolkScreenReader());
///
/// // In consuming code:
/// if (ServiceLocator.TryGet&lt;IScreenReader&gt;(out var reader))
/// {
///     reader.Speak("Hello");
/// }
/// </code>
///
/// Thread safety: This implementation is NOT thread-safe. All registrations
/// and lookups should occur on the main game thread.
/// </remarks>
public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();
    private static readonly List<ILifecycle> _lifecycleComponents = new();

    /// <summary>
    /// Registers a service instance for the specified type.
    /// If a service of the same type is already registered, it will be replaced.
    /// </summary>
    /// <typeparam name="T">The service type (interface or class).</typeparam>
    /// <param name="service">The service instance to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if service is null.</exception>
    /// <remarks>
    /// If the service implements <see cref="ILifecycle"/>, it will be tracked
    /// for automatic lifecycle management.
    /// </remarks>
    public static void Register<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);

        Type serviceType = typeof(T);

        // If replacing an existing lifecycle component, remove it from tracking
        if (_services.TryGetValue(serviceType, out object? existing) && existing is ILifecycle oldLifecycle)
        {
            _lifecycleComponents.Remove(oldLifecycle);
        }

        _services[serviceType] = service;

        // Track lifecycle components for automatic management
        if (service is ILifecycle lifecycle && !_lifecycleComponents.Contains(lifecycle))
        {
            _lifecycleComponents.Add(lifecycle);
        }
    }

    /// <summary>
    /// Retrieves a registered service by type.
    /// </summary>
    /// <typeparam name="T">The service type to retrieve.</typeparam>
    /// <returns>The registered service instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no service of the specified type is registered.
    /// </exception>
    /// <remarks>
    /// Use this method when the service is required and its absence indicates
    /// a programming error. For optional services, use <see cref="TryGet{T}"/>.
    /// </remarks>
    public static T Get<T>() where T : class
    {
        if (!TryGet<T>(out T? service))
        {
            throw new InvalidOperationException(
                $"No service registered for type {typeof(T).Name}. " +
                $"Ensure the service is registered during mod initialization.");
        }

        return service;
    }

    /// <summary>
    /// Attempts to retrieve a registered service by type.
    /// </summary>
    /// <typeparam name="T">The service type to retrieve.</typeparam>
    /// <param name="service">
    /// When this method returns, contains the service instance if found;
    /// otherwise, null.
    /// </param>
    /// <returns>True if the service was found; otherwise, false.</returns>
    public static bool TryGet<T>([NotNullWhen(true)] out T? service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out object? value) && value is T typedService)
        {
            service = typedService;
            return true;
        }

        service = null;
        return false;
    }

    /// <summary>
    /// Checks whether a service of the specified type is registered.
    /// </summary>
    /// <typeparam name="T">The service type to check.</typeparam>
    /// <returns>True if a service is registered; otherwise, false.</returns>
    public static bool IsRegistered<T>() where T : class
    {
        return _services.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Unregisters a service by type.
    /// </summary>
    /// <typeparam name="T">The service type to unregister.</typeparam>
    /// <returns>True if the service was found and removed; otherwise, false.</returns>
    public static bool Unregister<T>() where T : class
    {
        Type serviceType = typeof(T);

        if (_services.TryGetValue(serviceType, out object? existing))
        {
            if (existing is ILifecycle lifecycle)
            {
                _lifecycleComponents.Remove(lifecycle);
            }

            return _services.Remove(serviceType);
        }

        return false;
    }

    /// <summary>
    /// Calls <see cref="ILifecycle.Load"/> on all registered lifecycle components.
    /// Should be called during mod initialization after all services are registered.
    /// </summary>
    public static void LoadAll()
    {
        // Create a copy to avoid issues if Load() registers new components
        var components = new List<ILifecycle>(_lifecycleComponents);
        foreach (ILifecycle component in components)
        {
            component.Load();
        }
    }

    /// <summary>
    /// Calls <see cref="ILifecycle.Unload"/> on all registered lifecycle components
    /// and clears all registrations.
    /// Should be called during mod unload.
    /// </summary>
    public static void UnloadAll()
    {
        // Unload in reverse order of registration for proper cleanup
        for (int i = _lifecycleComponents.Count - 1; i >= 0; i--)
        {
            try
            {
                _lifecycleComponents[i].Unload();
            }
            catch (Exception)
            {
                // Log error but continue unloading other components
                // Don't let one component's failure prevent cleanup of others
            }
        }

        _lifecycleComponents.Clear();
        _services.Clear();
    }

    /// <summary>
    /// Clears all registered services without calling Unload.
    /// Primarily intended for testing scenarios.
    /// </summary>
    internal static void Reset()
    {
        _lifecycleComponents.Clear();
        _services.Clear();
    }
}
