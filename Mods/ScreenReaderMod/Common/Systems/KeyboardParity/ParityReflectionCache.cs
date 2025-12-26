#nullable enable
using System;
using System.Reflection;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.KeyboardParity;

/// <summary>
/// Caches reflection handles for Terraria internals used by the keyboard parity system.
/// Uses lazy initialization to defer reflection until first access.
/// </summary>
internal static class ParityReflectionCache
{
    private static readonly Lazy<FieldInfo?> _bindsKeyboard = new(() =>
        typeof(UIManageControls).GetField("_bindsKeyboard", BindingFlags.NonPublic | BindingFlags.Instance));

    private static readonly Lazy<FieldInfo?> _bindsKeyboardUi = new(() =>
        typeof(UIManageControls).GetField("_bindsKeyboardUI", BindingFlags.NonPublic | BindingFlags.Instance));

    private static readonly Lazy<MethodInfo?> _createBindingGroup = new(() =>
        typeof(UIManageControls).GetMethod("CreateBindingGroup", BindingFlags.NonPublic | BindingFlags.Instance));

    private static readonly Lazy<MethodInfo?> _assembleBindPanels = new(() =>
        typeof(UIManageControls).GetMethod("AssembleBindPanels", BindingFlags.NonPublic | BindingFlags.Instance));

    private static readonly Lazy<MethodInfo?> _drawRadialCircular = new(() =>
        typeof(ItemSlot).GetMethod("DrawRadialCircular", BindingFlags.Public | BindingFlags.Static));

    private static readonly Lazy<MethodInfo?> _drawRadialQuicks = new(() =>
        typeof(ItemSlot).GetMethod("DrawRadialQuicks", BindingFlags.Public | BindingFlags.Static));

    private static readonly Lazy<MethodInfo?> _usingGamepadGetter = new(() =>
        typeof(PlayerInput).GetMethod("get_UsingGamepad", BindingFlags.Public | BindingFlags.Static));

    private static readonly Lazy<MethodInfo?> _usingGamepadUiGetter = new(() =>
        typeof(PlayerInput).GetMethod("get_UsingGamepadUI", BindingFlags.Public | BindingFlags.Static));

    private static readonly Lazy<MethodInfo?> _gamepadInput = new(() =>
        typeof(PlayerInput).GetMethod("GamePadInput", BindingFlags.NonPublic | BindingFlags.Static));

    /// <summary>
    /// UIManageControls._bindsKeyboard field for accessing keyboard binding groups.
    /// </summary>
    internal static FieldInfo? BindsKeyboard => _bindsKeyboard.Value;

    /// <summary>
    /// UIManageControls._bindsKeyboardUI field for accessing keyboard UI binding groups.
    /// </summary>
    internal static FieldInfo? BindsKeyboardUi => _bindsKeyboardUi.Value;

    /// <summary>
    /// UIManageControls.CreateBindingGroup method for creating new binding panels.
    /// </summary>
    internal static MethodInfo? CreateBindingGroup => _createBindingGroup.Value;

    /// <summary>
    /// UIManageControls.AssembleBindPanels method for hook targeting.
    /// </summary>
    internal static MethodInfo? AssembleBindPanels => _assembleBindPanels.Value;

    /// <summary>
    /// ItemSlot.DrawRadialCircular method for radial hotbar hook targeting.
    /// </summary>
    internal static MethodInfo? DrawRadialCircular => _drawRadialCircular.Value;

    /// <summary>
    /// ItemSlot.DrawRadialQuicks method for radial quickbar hook targeting.
    /// </summary>
    internal static MethodInfo? DrawRadialQuicks => _drawRadialQuicks.Value;

    /// <summary>
    /// PlayerInput.get_UsingGamepad getter for hook targeting.
    /// </summary>
    internal static MethodInfo? UsingGamepadGetter => _usingGamepadGetter.Value;

    /// <summary>
    /// PlayerInput.get_UsingGamepadUI getter for hook targeting.
    /// </summary>
    internal static MethodInfo? UsingGamepadUiGetter => _usingGamepadUiGetter.Value;

    /// <summary>
    /// PlayerInput.GamePadInput method for virtual stick injection hook targeting.
    /// </summary>
    internal static MethodInfo? GamepadInput => _gamepadInput.Value;

    /// <summary>
    /// Returns true if all required reflection handles for UI controls are available.
    /// </summary>
    internal static bool HasUiControlsHandles =>
        BindsKeyboard is not null && BindsKeyboardUi is not null && CreateBindingGroup is not null;

    /// <summary>
    /// Logs a warning if any required reflection handles are missing.
    /// Call this during initialization to detect compatibility issues early.
    /// </summary>
    internal static void LogMissingHandles()
    {
        if (!HasUiControlsHandles)
        {
            global::ScreenReaderMod.ScreenReaderMod.Instance?.Logger.Warn(
                "[KeyboardInputParity] Failed to cache UIManageControls reflection handles; controller extras will be skipped.");
        }
    }
}
