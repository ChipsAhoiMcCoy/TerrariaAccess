#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Handles slider adjustment and enum cycling for config elements.
/// </summary>
internal static class ConfigSliderHandler
{
    /// <summary>
    /// Attempts to adjust a slider element.
    /// </summary>
    /// <param name="element">The config element to adjust.</param>
    /// <param name="direction">-1 for decrease, 1 for increase.</param>
    /// <param name="newPercentValue">The new percentage value after adjustment.</param>
    /// <returns>True if adjustment was successful.</returns>
    public static bool TryAdjustSlider(UIElement element, int direction, out float newPercentValue)
    {
        newPercentValue = 0f;

        Type type = element.GetType();
        ConfigElementAccessors accessors = ConfigElementDescriber.GetAccessors(type);

        if (accessors.ProportionProperty is null || !accessors.ProportionProperty.CanRead || !accessors.ProportionProperty.CanWrite)
        {
            return false;
        }

        try
        {
            object? proportionObj = accessors.ProportionProperty.GetValue(element);
            if (proportionObj is not float currentProportion)
            {
                return false;
            }

            float tickIncrement = 0.05f; // Default 5% step
            if (accessors.TickIncrementProperty?.GetValue(element) is float tick && tick > 0f)
            {
                tickIncrement = tick;
            }

            float newProportion = currentProportion + (direction * tickIncrement);
            newProportion = Math.Clamp(newProportion, 0f, 1f);

            if (Math.Abs(newProportion - currentProportion) < 0.0001f)
            {
                return false;
            }

            accessors.ProportionProperty.SetValue(element, newProportion);
            newPercentValue = MathF.Round(newProportion * 100f);
            return true;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Failed to adjust slider: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to toggle a boolean config element.
    /// </summary>
    public static bool TryToggleBoolean(UIElement element)
    {
        Type type = element.GetType();
        string? typeName = type.FullName;

        if (typeName is null || !typeName.Contains("BooleanElement", StringComparison.Ordinal))
        {
            return false;
        }

        // Suppress native click audio; callers provide explicit Terraria Access UI feedback.
        return TryInvokeClick(element);
    }

    /// <summary>
    /// Attempts to cycle an enum config element.
    /// </summary>
    public static bool TryCycleEnum(UIElement element)
    {
        Type type = element.GetType();
        string? typeName = type.FullName;

        if (typeName is null || !typeName.Contains("EnumElement", StringComparison.Ordinal))
        {
            return false;
        }

        // Try EnumElement2 first (modern tModLoader)
        if (TryCycleEnumElement2(element, type))
        {
            return true;
        }

        // Fall back to EnumElement (RangeElement-based)
        return TryCycleEnumViaPropotion(element, type);
    }

    private static bool TryCycleEnumElement2(UIElement element, Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        try
        {
            FieldInfo? getIndexField = type.GetField("_getIndex", flags);
            FieldInfo? setValueField = type.GetField("_setValue", flags);
            FieldInfo? maxField = type.GetField("max", flags);

            if (getIndexField is null || setValueField is null || maxField is null)
            {
                return false;
            }

            object? getIndexDelegate = getIndexField.GetValue(element);
            object? setValueDelegate = setValueField.GetValue(element);
            object? maxObj = maxField.GetValue(element);

            if (getIndexDelegate is not Func<int> getIndex ||
                setValueDelegate is not Action<int> setValue ||
                maxObj is not int max || max <= 0)
            {
                return false;
            }

            int currentIndex = getIndex();
            int newIndex = (currentIndex + 1) % max;

            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Cycling EnumElement2: {currentIndex} -> {newIndex} (max={max})");

            setValue(newIndex);

            // Trigger UI update
            FieldInfo? updateNeededField = type.GetField("UpdateNeeded", flags);
            PropertyInfo? updateNeededProp = type.GetProperty("UpdateNeeded", flags);
            if (updateNeededProp is not null && updateNeededProp.CanWrite)
            {
                updateNeededProp.SetValue(element, true);
            }
            else if (updateNeededField is not null)
            {
                updateNeededField.SetValue(element, true);
            }

            TryMarkPendingChanges();
            return true;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Failed to cycle EnumElement2: {ex.Message}");
            return false;
        }
    }

    private static bool TryCycleEnumViaPropotion(UIElement element, Type type)
    {
        ConfigElementAccessors accessors = ConfigElementDescriber.GetAccessors(type);

        if (accessors.ProportionProperty is null || !accessors.ProportionProperty.CanRead || !accessors.ProportionProperty.CanWrite)
        {
            return false;
        }

        try
        {
            object? proportionObj = accessors.ProportionProperty.GetValue(element);
            if (proportionObj is not float currentProportion)
            {
                return false;
            }

            float tickIncrement = 0.5f;
            if (accessors.TickIncrementProperty?.GetValue(element) is float tick && tick > 0f)
            {
                tickIncrement = tick;
            }

            float newProportion = currentProportion + tickIncrement;
            if (newProportion > 1.0f + 0.001f)
            {
                newProportion = 0f;
            }
            newProportion = Math.Clamp(newProportion, 0f, 1f);

            accessors.ProportionProperty.SetValue(element, newProportion);
            return true;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Failed to cycle enum via proportion: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Invokes a left click on a UI element.
    /// </summary>
    public static bool TryInvokeClick(UIElement element)
    {
        try
        {
            CalculatedStyle dims = element.GetDimensions();
            var mousePosition = new Vector2(dims.X + dims.Width / 2, dims.Y + dims.Height / 2);
            var evt = new UIMouseEvent(element, mousePosition);
            global::TerrariaAccess.Common.Services.ProgrammaticUiClickInvoker.LeftClick(element, evt);
            return true;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Failed to invoke click: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Marks the config UI as having pending changes so tModLoader will save on exit.
    /// This avoids clicking the save button which plays a menu sound.
    /// </summary>
    private static void TryMarkPendingChanges()
    {
        try
        {
            Type? interfaceType = Type.GetType("Terraria.ModLoader.UI.Interface, tModLoader");
            if (interfaceType is null)
            {
                return;
            }

            FieldInfo? modConfigField = interfaceType.GetField("modConfig", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (modConfigField is null)
            {
                return;
            }

            object? modConfigUI = modConfigField.GetValue(null);
            if (modConfigUI is null)
            {
                return;
            }

            Type modConfigUIType = modConfigUI.GetType();
            MethodInfo? setPendingMethod = modConfigUIType.GetMethod("SetPendingChanges", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (setPendingMethod is not null)
            {
                setPendingMethod.Invoke(modConfigUI, new object[] { true });
                TerrariaAccess.Instance?.Logger.Debug("[ConfigSliderHandler] Marked config as having pending changes");
                return;
            }

            // Fallback: set the pendingChanges field directly
            FieldInfo? pendingChangesField = modConfigUIType.GetField("pendingChanges", BindingFlags.Instance | BindingFlags.NonPublic);
            if (pendingChangesField is not null)
            {
                pendingChangesField.SetValue(modConfigUI, true);
                TerrariaAccess.Instance?.Logger.Debug("[ConfigSliderHandler] Set pendingChanges field directly");
            }
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Debug($"[ConfigSliderHandler] Failed to mark pending changes: {ex.Message}");
        }
    }
}
