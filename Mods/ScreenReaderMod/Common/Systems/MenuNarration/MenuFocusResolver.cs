#nullable enable
using System;
using System.Reflection;
using Terraria;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed class MenuFocusResolver
{
    private static readonly FieldInfo? FocusMenuField = typeof(Main).GetField("focusMenu", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? SelectedMenuField = typeof(Main).GetField("selectedMenu", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? MenuItemScaleField = typeof(Main).GetField("menuItemScale", BindingFlags.NonPublic | BindingFlags.Instance);

    private float[]? _previousMenuScales;
    private int _lastMenuFocus = -1;

    public void Reset()
    {
        _previousMenuScales = null;
        _lastMenuFocus = -1;
    }

    public bool TryGetFocus(Main main, out MenuFocus focus)
    {
        float[]? scales = ExtractMenuScales(main);
        
        if (FocusMenuField?.GetValue(main) is int focusMenu && focusMenu >= 0)
        {
            focus = new MenuFocus(focusMenu, "Main.focusMenu");
            Snapshot(scales);
            return true;
        }

        if (SelectedMenuField?.GetValue(main) is int selectedMenu && selectedMenu >= 0)
        {
            focus = new MenuFocus(selectedMenu, "Main.selectedMenu");
            Snapshot(scales);
            return true;
        }

        if (TryResolveFromScales(scales, out focus))
        {
            Snapshot(scales);
            return true;
        }

        int menuFocus = Main.menuFocus;
        if (menuFocus >= 0 && menuFocus != _lastMenuFocus)
        {
            focus = new MenuFocus(menuFocus, "Main.menuFocus");
            Snapshot(scales);
            _lastMenuFocus = menuFocus;
            return true;
        }

        _lastMenuFocus = menuFocus;
        Snapshot(scales);
        focus = default;
        return false;
    }

    private static float[]? ExtractMenuScales(Main main)
    {
        return MenuItemScaleField?.GetValue(main) as float[];
    }

    private bool TryResolveFromScales(float[]? scales, out MenuFocus focus)
    {
        focus = default;
        if (scales is null || scales.Length == 0)
        {
            return false;
        }

        int deltaIndex = DetectFocusFromScaleDeltas(scales);
        if (deltaIndex >= 0)
        {
            focus = new MenuFocus(deltaIndex, "menuItemScaleDelta");
            return true;
        }

        int bestIndex = -1;
        float bestScale = float.MinValue;
        for (int i = 0; i < scales.Length; i++)
        {
            float scale = scales[i];
            if (scale > bestScale)
            {
                bestScale = scale;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0 && bestScale > 0f)
        {
            focus = new MenuFocus(bestIndex, "menuItemScale");
            return true;
        }

        return false;
    }

    private int DetectFocusFromScaleDeltas(float[] scales)
    {
        float[]? previous = _previousMenuScales;
        if (previous is null || previous.Length == 0 || previous.Length != scales.Length)
        {
            return -1;
        }

        const float epsilon = 0.001f;
        int bestIndex = -1;
        float bestDelta = epsilon;

        for (int i = 0; i < scales.Length; i++)
        {
            float delta = scales[i] - previous[i];
            if (delta > bestDelta && scales[i] > 0f)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void Snapshot(float[]? scales)
    {
        if (scales is null || scales.Length == 0)
        {
            _previousMenuScales = null;
            return;
        }

        if (_previousMenuScales is null || _previousMenuScales.Length != scales.Length)
        {
            _previousMenuScales = new float[scales.Length];
        }

        Array.Copy(scales, _previousMenuScales, scales.Length);
    }
}

internal readonly record struct MenuFocus(int Index, string Source);
