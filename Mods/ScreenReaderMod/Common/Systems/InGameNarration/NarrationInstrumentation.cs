#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Services;

internal sealed class NarrationInstrumentation
{
    private readonly Dictionary<string, string> _lastKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastRun = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string serviceName, string? key = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _lastRun[serviceName] = now;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _lastKeys[serviceName] = key;
        }
    }

    public bool TryGetLastKey(string serviceName, out string key)
    {
        return _lastKeys.TryGetValue(serviceName, out key!);
    }

    public bool TryGetLastRun(string serviceName, out DateTimeOffset timestamp)
    {
        return _lastRun.TryGetValue(serviceName, out timestamp);
    }
}

internal static class NarrationInstrumentationContext
{
    private const int MaxKeyLength = 200;

    [ThreadStatic]
    private static NarrationInstrumentation? _currentInstrumentation;

    [ThreadStatic]
    private static string? _currentService;

    [ThreadStatic]
    private static string? _pendingKey;

    public static IDisposable BeginScope(string serviceName, NarrationInstrumentation instrumentation)
    {
        string? previousService = _currentService;
        NarrationInstrumentation? previousInstrumentation = _currentInstrumentation;
        _currentService = serviceName;
        _currentInstrumentation = instrumentation;
        _pendingKey = null;
        return new Scope(previousService, previousInstrumentation);
    }

    public static void SetPendingKey(string? key)
    {
        _pendingKey = Sanitize(key);
    }

    public static string? ConsumePendingKey()
    {
        string? key = _pendingKey;
        _pendingKey = null;
        return key;
    }

    public static void RecordKey(string? key)
    {
        string? sanitized = Sanitize(key);
        if (string.IsNullOrWhiteSpace(sanitized) ||
            _currentInstrumentation is null ||
            string.IsNullOrWhiteSpace(_currentService))
        {
            return;
        }

        _currentInstrumentation.Record(_currentService!, sanitized);
    }

    private static string? Sanitize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string trimmed = key.Trim();
        if (trimmed.Length > MaxKeyLength)
        {
            trimmed = trimmed[..MaxKeyLength];
        }

        return trimmed;
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previousService;
        private readonly NarrationInstrumentation? _previousInstrumentation;

        public Scope(string? previousService, NarrationInstrumentation? previousInstrumentation)
        {
            _previousService = previousService;
            _previousInstrumentation = previousInstrumentation;
        }

        public void Dispose()
        {
            _currentService = _previousService;
            _currentInstrumentation = _previousInstrumentation;
            _pendingKey = null;
        }
    }
}
