#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Systems;

internal static class NpcDialogueInputTracker
{
    private const uint TypedInputStabilizationFrames = 8;

    private static readonly string[] NavigationTriggerNames =
    {
        "MenuLeft",
        "MenuRight",
        "MenuUp",
        "MenuDown",
        "UILeft",
        "UIRight",
        "UIUp",
        "UIDown",
        "DpadLeft",
        "DpadRight",
        "DpadUp",
        "DpadDown"
    };

    private static readonly IReadOnlyList<FieldInfo> NavigationTriggerFields = ResolveNavigationTriggerFields();
    private static bool _navigationPressed;
    private static string? _typedBuffer;
    private static string? _lastAnnouncedTyped;
    private static uint _lastTypedChangeFrame;

    public static bool IsNavigationPressed => PlayerInput.UsingGamepadUI && _navigationPressed;

    public static void Reset()
    {
        _navigationPressed = false;
        ClearTypedInput(resetHistory: true);
    }

    public static void RecordNavigation(TriggersSet triggersSet)
    {
        _navigationPressed = false;

        if (triggersSet is null || NavigationTriggerFields.Count == 0)
        {
            return;
        }

        foreach (FieldInfo field in NavigationTriggerFields)
        {
            if (field.GetValue(triggersSet) is bool pressed && pressed)
            {
                _navigationPressed = true;
                return;
            }
        }
    }

    public static void RecordTypedInput(string? text, bool active)
    {
        if (!active)
        {
            ClearTypedInput(resetHistory: true);
            return;
        }

        string sanitized = TextSanitizer.Clean(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            ClearTypedInput(resetHistory: true);
            return;
        }

        if (!string.Equals(sanitized, _typedBuffer, StringComparison.Ordinal))
        {
            _typedBuffer = sanitized;
            _lastTypedChangeFrame = Main.GameUpdateCount;
        }
    }

    public static bool TryDequeueTypedInput(out string typedText)
    {
        typedText = string.Empty;

        if (string.IsNullOrWhiteSpace(_typedBuffer))
        {
            return false;
        }

        uint changeFrame = _lastTypedChangeFrame;
        if (changeFrame == 0 || Main.GameUpdateCount - changeFrame < TypedInputStabilizationFrames)
        {
            return false;
        }

        if (string.Equals(_typedBuffer, _lastAnnouncedTyped, StringComparison.Ordinal))
        {
            return false;
        }

        typedText = _typedBuffer;
        _lastAnnouncedTyped = _typedBuffer;
        return true;
    }

    private static IReadOnlyList<FieldInfo> ResolveNavigationTriggerFields()
    {
        var fields = new List<FieldInfo>();
        Type triggersType = typeof(TriggersSet);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in NavigationTriggerNames)
        {
            FieldInfo? field = triggersType.GetField(name, flags);
            if (field is not null && field.FieldType == typeof(bool))
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private static void ClearTypedInput(bool resetHistory)
    {
        _typedBuffer = null;
        _lastTypedChangeFrame = 0;
        if (resetHistory)
        {
            _lastAnnouncedTyped = null;
        }
    }
}
