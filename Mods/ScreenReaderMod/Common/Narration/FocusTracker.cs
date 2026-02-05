#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Narration;

/// <summary>
/// Reusable component for tracking focus changes.
/// Provides generic focus tracking with change detection and history.
/// </summary>
/// <typeparam name="T">The type of focus value being tracked.</typeparam>
public sealed class FocusTracker<T> where T : IEquatable<T>
{
    private T? _current;
    private T? _previous;
    private bool _hasValue;
    private uint _changeFrame;

    /// <summary>
    /// The current focus value. May be default if no focus has been set.
    /// </summary>
    public T? Current => _current;

    /// <summary>
    /// The previous focus value before the last change.
    /// </summary>
    public T? Previous => _previous;

    /// <summary>
    /// Whether a focus value has been set.
    /// </summary>
    public bool HasValue => _hasValue;

    /// <summary>
    /// The frame number when the focus last changed.
    /// </summary>
    public uint ChangeFrame => _changeFrame;

    /// <summary>
    /// Updates the focus to a new value.
    /// Returns true if the focus changed (value is different from current).
    /// </summary>
    /// <param name="value">The new focus value.</param>
    /// <param name="frameNumber">The current frame number for tracking when the change occurred.</param>
    public bool Update(T? value, uint frameNumber)
    {
        bool wasNull = !_hasValue || _current is null;
        bool isNull = value is null;

        bool changed;
        if (wasNull && isNull)
        {
            changed = false;
        }
        else if (wasNull || isNull)
        {
            changed = true;
        }
        else
        {
            changed = !_current!.Equals(value);
        }

        if (changed)
        {
            _previous = _current;
            _current = value;
            _hasValue = value is not null;
            _changeFrame = frameNumber;
        }

        return changed;
    }

    /// <summary>
    /// Checks if the focus has changed and provides the previous value.
    /// </summary>
    /// <param name="newValue">The new value to compare against current focus.</param>
    /// <param name="previous">When focus has changed, contains the previous value.</param>
    /// <returns>True if focus would change to the new value.</returns>
    public bool HasFocusChanged(T? newValue, out T? previous)
    {
        previous = _current;

        bool wasNull = !_hasValue || _current is null;
        bool isNull = newValue is null;

        if (wasNull && isNull)
        {
            return false;
        }

        if (wasNull || isNull)
        {
            return true;
        }

        return !_current!.Equals(newValue);
    }

    /// <summary>
    /// Clears the focus state completely.
    /// </summary>
    public void Clear()
    {
        _previous = _current;
        _current = default;
        _hasValue = false;
    }

    /// <summary>
    /// Forces a refresh on the next update by marking current value as changed.
    /// Useful for forcing re-announcement after context changes.
    /// </summary>
    public void Invalidate()
    {
        _previous = _current;
        _current = default;
        _hasValue = false;
    }
}

/// <summary>
/// Specialized focus tracker for string-based focus keys.
/// Common pattern for tracking UI element focus by identifier.
/// </summary>
public sealed class StringFocusTracker
{
    private string? _current;
    private string? _previous;
    private uint _changeFrame;
    private readonly StringComparison _comparison;

    public StringFocusTracker(StringComparison comparison = StringComparison.Ordinal)
    {
        _comparison = comparison;
    }

    /// <summary>
    /// The current focus key.
    /// </summary>
    public string? Current => _current;

    /// <summary>
    /// The previous focus key.
    /// </summary>
    public string? Previous => _previous;

    /// <summary>
    /// Whether the tracker has a current focus value.
    /// </summary>
    public bool HasValue => !string.IsNullOrEmpty(_current);

    /// <summary>
    /// The frame when focus last changed.
    /// </summary>
    public uint ChangeFrame => _changeFrame;

    /// <summary>
    /// Updates the focus key and returns true if it changed.
    /// </summary>
    public bool Update(string? key, uint frameNumber)
    {
        bool changed = !string.Equals(_current, key, _comparison);

        if (changed)
        {
            _previous = _current;
            _current = key;
            _changeFrame = frameNumber;
        }

        return changed;
    }

    /// <summary>
    /// Checks if the key would represent a focus change.
    /// </summary>
    public bool HasFocusChanged(string? key, out string? previous)
    {
        previous = _current;
        return !string.Equals(_current, key, _comparison);
    }

    /// <summary>
    /// Clears the focus state.
    /// </summary>
    public void Clear()
    {
        _previous = _current;
        _current = null;
    }
}

/// <summary>
/// Multi-value focus tracker that can track multiple independent focus aspects.
/// Useful for narrators that need to track several related values
/// (e.g., slot + item type + context).
/// </summary>
public sealed class CompositeFocusTracker
{
    private readonly Dictionary<string, object?> _current = new();
    private readonly Dictionary<string, object?> _previous = new();
    private uint _changeFrame;

    /// <summary>
    /// Gets the current value for a tracked key.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (_current.TryGetValue(key, out object? value) && value is T typed)
        {
            return typed;
        }
        return default;
    }

    /// <summary>
    /// Gets the previous value for a tracked key.
    /// </summary>
    public T? GetPrevious<T>(string key)
    {
        if (_previous.TryGetValue(key, out object? value) && value is T typed)
        {
            return typed;
        }
        return default;
    }

    /// <summary>
    /// Updates a tracked value and returns true if it changed.
    /// </summary>
    public bool Update<T>(string key, T? value, uint frameNumber)
    {
        _current.TryGetValue(key, out object? currentValue);

        bool changed;
        if (currentValue is null && value is null)
        {
            changed = false;
        }
        else if (currentValue is null || value is null)
        {
            changed = true;
        }
        else
        {
            changed = !currentValue.Equals(value);
        }

        if (changed)
        {
            _previous[key] = currentValue;
            _current[key] = value;
            _changeFrame = frameNumber;
        }

        return changed;
    }

    /// <summary>
    /// Checks if any tracked values have changed.
    /// </summary>
    public bool HasAnyChange(uint sinceFrame)
    {
        return _changeFrame > sinceFrame;
    }

    /// <summary>
    /// Clears all tracked values.
    /// </summary>
    public void Clear()
    {
        foreach (var kvp in _current)
        {
            _previous[kvp.Key] = kvp.Value;
        }
        _current.Clear();
    }

    /// <summary>
    /// Clears a specific tracked value.
    /// </summary>
    public void Clear(string key)
    {
        if (_current.TryGetValue(key, out object? value))
        {
            _previous[key] = value;
            _current.Remove(key);
        }
    }
}
