#nullable enable
using System;
using System.Reflection;
using Terraria;

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Captures and provides access to mouse text from Terraria's UI system.
/// </summary>
internal static class MouseTextCapture
{
    private static readonly Lazy<FieldInfo?> MouseTextCacheField = new(() =>
        typeof(Main).GetField("_mouseTextCache", BindingFlags.Instance | BindingFlags.NonPublic));

    private static FieldInfo? _mouseTextCursorField;
    private static FieldInfo? _mouseTextIsValidField;
    private static string? _capturedMouseText;
    private static uint _capturedMouseTextFrame;

    /// <summary>
    /// Records a mouse text snapshot from an external hook.
    /// </summary>
    public static void RecordSnapshot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _capturedMouseText = null;
            _capturedMouseTextFrame = 0;
            return;
        }

        _capturedMouseText = text.Trim();
        _capturedMouseTextFrame = Main.GameUpdateCount;
    }

    /// <summary>
    /// Resets the captured mouse text.
    /// </summary>
    public static void Reset()
    {
        _capturedMouseText = null;
        _capturedMouseTextFrame = 0;
    }

    /// <summary>
    /// Attempts to get the current mouse text from cache or Main.
    /// </summary>
    public static string? TryGetMouseText()
    {
        string? captured = TryGetCapturedMouseText();
        if (!string.IsNullOrWhiteSpace(captured))
        {
            return captured;
        }

        Main? main = Main.instance;
        if (main is null)
        {
            return null;
        }

        FieldInfo? cacheField = MouseTextCacheField.Value;
        if (cacheField is null)
        {
            return null;
        }

        object? cache = cacheField.GetValue(main);
        if (cache is null)
        {
            return null;
        }

        Type cacheType = cache.GetType();
        _mouseTextCursorField ??= cacheType.GetField("cursorText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _mouseTextIsValidField ??= cacheType.GetField("isValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (_mouseTextCursorField?.GetValue(cache) is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return null;
    }

    private static string? TryGetCapturedMouseText()
    {
        if (string.IsNullOrWhiteSpace(_capturedMouseText) || _capturedMouseTextFrame == 0)
        {
            return null;
        }

        uint current = Main.GameUpdateCount;
        uint frame = _capturedMouseTextFrame;
        uint age = current >= frame ? current - frame : uint.MaxValue - frame + current + 1;
        if (age <= 2)
        {
            return _capturedMouseText;
        }

        _capturedMouseText = null;
        _capturedMouseTextFrame = 0;
        return null;
    }
}
