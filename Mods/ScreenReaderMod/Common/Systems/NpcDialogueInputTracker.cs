#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Systems;

internal static class NpcDialogueInputTracker
{
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

    public static bool IsNavigationPressed => PlayerInput.UsingGamepadUI && _navigationPressed;

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
}
