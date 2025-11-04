#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

public sealed class ModBrowserGamepadSystem : ModSystem
{
    private const int BaseLinkId = 3200;
    private const string RowTop = "Top";
    private const string RowFilters = "Filters";
    private const string RowDropdown = "Dropdown";
    private const string RowCategories = "Categories";
    private const string RowMods = "Mods";
    private static readonly string[] RowOrder = [RowTop, RowFilters, RowDropdown, RowCategories, RowMods];

    private static Type? _modBrowserType;

    private static Type? ModBrowserType => _modBrowserType ??= Type.GetType("Terraria.ModLoader.UI.ModBrowser.UIModBrowser, tModLoader");

    public override void UpdateUI(GameTime gameTime)
    {
        if (!Main.gameMenu)
        {
            return;
        }

        UIState? current = Main.MenuUI?.CurrentState;
        Type? modBrowserType = ModBrowserType;
        if (current is null || modBrowserType is null || !modBrowserType.IsInstanceOfType(current))
        {
            return;
        }

        try
        {
            EnsureSnapPoints(current);
            ConfigureLinks(current);
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[ModBrowserGamepad] Unable to configure gamepad focus: {ex}");
        }
    }

    private static void EnsureSnapPoints(UIState browser)
    {
        TrySetSnap(FieldAccessor.FilterTextBox, browser, "FilterText", 0);
        TrySetSnap(FieldAccessor.ClearButton, browser, "ActionButton", 0);
        TrySetSnap(FieldAccessor.ReloadButton, browser, "ActionButton", 1);
        TrySetSnap(FieldAccessor.DownloadAllButton, browser, "ActionButton", 2);
        TrySetSnap(FieldAccessor.UpdateAllButton, browser, "ActionButton", 3);
        TrySetSnap(FieldAccessor.BackButton, browser, "ActionButton", 4);

        TrySetSnap(FieldAccessor.SearchFilterToggle, browser, "FilterToggle", 0);
        TrySetSnap(FieldAccessor.SortModeToggle, browser, "FilterToggle", 1);
        TrySetSnap(FieldAccessor.TimePeriodToggle, browser, "FilterToggle", 2);
        TrySetSnap(FieldAccessor.UpdateFilterToggle, browser, "FilterToggle", 3);
        TrySetSnap(FieldAccessor.TagFilterToggle, browser, "FilterToggle", 4);
        TrySetSnap(FieldAccessor.ModSideFilterToggle, browser, "FilterToggle", 5);
        TrySetSnap(FieldAccessor.ModTagFilterDropdown, browser, "FilterDropdown", 0);

        if (FieldAccessor.CategoryButtons?.GetValue(browser) is IEnumerable categoryButtons)
        {
            int index = 0;
            foreach (object? entry in categoryButtons)
            {
                if (entry is UIElement element)
                {
                    element.SetSnapPoint("CategoryButton", index++);
                }
            }
        }

        SetModItemSnapPoints(browser);
    }

    private static void TrySetSnap(FieldInfo? field, UIState browser, string name, int id)
    {
        if (field?.GetValue(browser) is UIElement element)
        {
            element.SetSnapPoint(name, id);
        }
    }

    private static void SetModItemSnapPoints(UIState browser)
    {
        if (FieldAccessor.ModList?.GetValue(browser) is not UIList list)
        {
            return;
        }

        if (FieldAccessor.ItemsField?.GetValue(list) is not IEnumerable items)
        {
            return;
        }

        CalculatedStyle view = list.GetInnerDimensions();
        float top = view.Y;
        float bottom = view.Y + view.Height;

        int index = 0;
        foreach (object? entry in items)
        {
            if (entry is not UIElement element)
            {
                continue;
            }

            CalculatedStyle dims = element.GetDimensions();
            float centerY = dims.Y + dims.Height * 0.5f;
            if (centerY < top - 10f || centerY > bottom + 10f)
            {
                continue;
            }

            element.SetSnapPoint("ModItem", index++);
        }
    }

    private static void ConfigureLinks(UIState browser)
    {
        List<SnapPoint> snapPoints = browser.GetSnapPoints();
        if (snapPoints.Count == 0)
        {
            return;
        }

        int nextId = BaseLinkId;
        var bindings = new List<PointBinding>(snapPoints.Count);
        var rows = new Dictionary<string, List<PointBinding>>(StringComparer.Ordinal);

        foreach (SnapPoint? point in snapPoints)
        {
            if (point is null)
            {
                continue;
            }

            string? rowKey = GetRowKey(point);
            if (rowKey is null)
            {
                continue;
            }
            string key = rowKey;

            var binding = new PointBinding(nextId++, point);
            bindings.Add(binding);

            if (!rows.TryGetValue(key, out List<PointBinding>? rowList))
            {
                rowList = new List<PointBinding>();
                rows[key] = rowList;
            }

            rowList.Add(binding);
        }

        if (bindings.Count == 0)
        {
            return;
        }

        foreach (PointBinding binding in bindings)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(binding.Id);
            UILinkPointNavigator.SetPosition(binding.Id, binding.Point.Position);
            linkPoint.Unlink();
        }

        foreach (List<PointBinding> row in rows.Values)
        {
            row.Sort(ComparePoints);
            LinkHorizontal(row);
        }

        var orderedRows = new List<List<PointBinding>>(RowOrder.Length);
        foreach (string rowKey in RowOrder)
        {
            if (rows.TryGetValue(rowKey, out List<PointBinding>? row) && row.Count > 0)
            {
                orderedRows.Add(row);
            }
        }

        for (int i = 0; i < orderedRows.Count - 1; i++)
        {
            BridgeRows(orderedRows[i], orderedRows[i + 1]);
        }

        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX = bindings[^1].Id;

        if (PlayerInput.UsingGamepadUI && !bindings.Any(b => b.Id == UILinkPointNavigator.CurrentPoint))
        {
            UILinkPointNavigator.ChangePoint(bindings[0].Id);
        }
    }

    private static string? GetRowKey(SnapPoint point)
    {
        return point.Name switch
        {
            "FilterText" or "ActionButton" => RowTop,
            "FilterToggle" => RowFilters,
            "FilterDropdown" => RowDropdown,
            "CategoryButton" => RowCategories,
            "ModItem" => RowMods,
            _ => null,
        };
    }

    private static int ComparePoints(PointBinding left, PointBinding right)
    {
        int compareX = left.Point.Position.X.CompareTo(right.Point.Position.X);
        if (compareX != 0)
        {
            return compareX;
        }

        return left.Point.Position.Y.CompareTo(right.Point.Position.Y);
    }

    private static void LinkHorizontal(IReadOnlyList<PointBinding> row)
    {
        if (row.Count <= 1)
        {
            return;
        }

        for (int i = 0; i < row.Count; i++)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(row[i].Id);
            linkPoint.Left = i > 0 ? row[i - 1].Id : -1;
            linkPoint.Right = i < row.Count - 1 ? row[i + 1].Id : -1;
        }
    }

    private static void BridgeRows(IReadOnlyList<PointBinding> upperRow, IReadOnlyList<PointBinding> lowerRow)
    {
        if (upperRow.Count == 0 || lowerRow.Count == 0)
        {
            return;
        }

        var upperToLower = new Dictionary<int, (int LowerId, float Distance)>();

        foreach (PointBinding lower in lowerRow)
        {
            PointBinding closestUpper = FindClosestByPosition(lower, upperRow);
            UILinkPoint lowerPoint = EnsureLinkPoint(lower.Id);
            lowerPoint.Up = closestUpper.Id;

            float verticalDistance = Math.Abs(lower.Point.Position.Y - closestUpper.Point.Position.Y);
            if (!upperToLower.TryGetValue(closestUpper.Id, out (int LowerId, float Distance) existing) || verticalDistance < existing.Distance)
            {
                upperToLower[closestUpper.Id] = (lower.Id, verticalDistance);
            }
        }

        foreach (PointBinding upper in upperRow)
        {
            if (upperToLower.TryGetValue(upper.Id, out (int LowerId, float Distance) match))
            {
                UILinkPoint upperPoint = EnsureLinkPoint(upper.Id);
                upperPoint.Down = match.LowerId;
            }
        }

        foreach (PointBinding upper in upperRow)
        {
            UILinkPoint upperPoint = EnsureLinkPoint(upper.Id);
            if (upperPoint.Down != -1)
            {
                continue;
            }

            PointBinding nearest = FindClosestByPosition(upper, lowerRow);
            upperPoint.Down = nearest.Id;

            UILinkPoint lowerPoint = EnsureLinkPoint(nearest.Id);
            if (lowerPoint.Up == -1)
            {
                lowerPoint.Up = upper.Id;
            }
        }
    }

    private static PointBinding FindClosestByPosition(PointBinding target, IReadOnlyList<PointBinding> candidates)
    {
        PointBinding best = candidates[0];
        float bestScore = float.MaxValue;

        foreach (PointBinding candidate in candidates)
        {
            float deltaX = Math.Abs(candidate.Point.Position.X - target.Point.Position.X);
            float deltaY = Math.Abs(candidate.Point.Position.Y - target.Point.Position.Y);
            float score = deltaX * 1.5f + deltaY;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static UILinkPoint EnsureLinkPoint(int id)
    {
        if (!UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? linkPoint))
        {
            linkPoint = new UILinkPoint(id, true, -1, -1, -1, -1);
            UILinkPointNavigator.Points[id] = linkPoint;
        }

        return linkPoint;
    }

    private readonly record struct PointBinding(int Id, SnapPoint Point);

    private static class FieldAccessor
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo? _filterTextBox;
        private static FieldInfo? _clearButton;
        private static FieldInfo? _reloadButton;
        private static FieldInfo? _downloadAllButton;
        private static FieldInfo? _updateAllButton;
        private static FieldInfo? _backButton;
        private static FieldInfo? _searchFilterToggle;
        private static FieldInfo? _sortModeToggle;
        private static FieldInfo? _timePeriodToggle;
        private static FieldInfo? _updateFilterToggle;
        private static FieldInfo? _tagFilterToggle;
        private static FieldInfo? _modSideFilterToggle;
        private static FieldInfo? _categoryButtons;
        private static FieldInfo? _modList;
        private static FieldInfo? _itemsField;
        private static FieldInfo? _modTagFilterDropdown;

        public static FieldInfo? FilterTextBox => _filterTextBox ??= GetField("FilterTextBox");
        public static FieldInfo? ClearButton => _clearButton ??= GetField("_clearButton");
        public static FieldInfo? ReloadButton => _reloadButton ??= GetField("_reloadButton");
        public static FieldInfo? DownloadAllButton => _downloadAllButton ??= GetField("_downloadAllButton");
        public static FieldInfo? UpdateAllButton => _updateAllButton ??= GetField("_updateAllButton");
        public static FieldInfo? BackButton => _backButton ??= GetField("_backButton");
        public static FieldInfo? SearchFilterToggle => _searchFilterToggle ??= GetField("SearchFilterToggle");
        public static FieldInfo? SortModeToggle => _sortModeToggle ??= GetField("SortModeFilterToggle");
        public static FieldInfo? TimePeriodToggle => _timePeriodToggle ??= GetField("TimePeriodToggle");
        public static FieldInfo? UpdateFilterToggle => _updateFilterToggle ??= GetField("UpdateFilterToggle");
        public static FieldInfo? TagFilterToggle => _tagFilterToggle ??= GetField("TagFilterToggle");
        public static FieldInfo? ModSideFilterToggle => _modSideFilterToggle ??= GetField("ModSideFilterToggle");
        public static FieldInfo? CategoryButtons => _categoryButtons ??= GetField("CategoryButtons");
        public static FieldInfo? ModList => _modList ??= GetField("ModList");
        public static FieldInfo? ItemsField => _itemsField ??= typeof(UIList).GetField("_items", InstanceFlags);
        public static FieldInfo? ModTagFilterDropdown => _modTagFilterDropdown ??= GetField("modTagFilterDropdown");

        private static FieldInfo? GetField(string name)
        {
            Type? type = ModBrowserType;
            return type?.GetField(name, InstanceFlags);
        }
    }
}
