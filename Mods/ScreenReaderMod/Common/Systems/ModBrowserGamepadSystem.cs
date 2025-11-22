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
    private const string ModItemActionName = "ModItemAction";
    private const float ActionHitboxThreshold = 48f;
    private const int MaxModItemLogCount = 5;
    private static int _loggedModItems;
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
        if (FieldAccessor.ModList?.GetValue(browser) is not UIElement listElement)
        {
            return;
        }

        if (!FieldAccessor.TryGetItems(listElement, out IEnumerable? items) || items is null)
        {
            return;
        }

        CalculatedStyle view = listElement.GetInnerDimensions();
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
            TrySetModItemButtons(element, ref index);
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

        List<PointBinding>? modRow = rows.GetValueOrDefault(RowMods);
        List<PointBinding>? modMainRow = null;

        foreach (KeyValuePair<string, List<PointBinding>> entry in rows)
        {
            if (entry.Key == RowMods)
            {
                continue;
            }

            entry.Value.Sort(ComparePoints);
            LinkHorizontal(entry.Value);
        }

        if (modRow is not null && modRow.Count > 0)
        {
            modMainRow = ApplyModRowNavigation(modRow);
        }

        LogRowSummary(rows, modMainRow);

        var orderedRows = new List<List<PointBinding>>(RowOrder.Length);
        foreach (string rowKey in RowOrder)
        {
            if (rowKey == RowMods)
            {
                if (modMainRow is { Count: > 0 })
                {
                    orderedRows.Add(modMainRow);
                }
                continue;
            }

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

    private static void LogRowSummary(Dictionary<string, List<PointBinding>> rows, List<PointBinding>? modMainRow)
    {
        var logger = ScreenReaderMod.Instance?.Logger;
        if (logger is null)
        {
            return;
        }

        try
        {
            logger.Info($"[ModBrowserGamepad] Rows: {string.Join(", ", rows.Select(r => $"{r.Key}={r.Value.Count}"))}");

            if (modMainRow is not null)
            {
                logger.Info($"[ModBrowserGamepad] Mod mains: {modMainRow.Count}, actions total: {rows.GetValueOrDefault(RowMods)?.Count - modMainRow.Count ?? 0}");
            }
        }
        catch
        {
            // ignore logging failures
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
            "ModItem" or ModItemActionName => RowMods,
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

    private static List<PointBinding> ApplyModRowNavigation(List<PointBinding> modRow)
    {
        if (modRow.Count == 0)
        {
            return modRow;
        }

        var mains = new List<PointBinding>();
        var actions = new List<PointBinding>();

        foreach (PointBinding binding in modRow)
        {
            if (string.Equals(binding.Point.Name, "ModItem", StringComparison.OrdinalIgnoreCase))
            {
                mains.Add(binding);
            }
            else if (string.Equals(binding.Point.Name, ModItemActionName, StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(binding);
            }
        }

        if (mains.Count == 0)
        {
            return modRow;
        }

        ScreenReaderMod.Instance?.Logger.Debug($"[ModBrowserGamepad] Mod items: {mains.Count}, actions: {actions.Count}");

        mains.Sort((a, b) => a.Point.Position.Y.CompareTo(b.Point.Position.Y));
        for (int i = 0; i < mains.Count; i++)
        {
            UILinkPoint linkPoint = EnsureLinkPoint(mains[i].Id);
            linkPoint.Up = i > 0 ? mains[i - 1].Id : linkPoint.Up;
            linkPoint.Down = i < mains.Count - 1 ? mains[i + 1].Id : linkPoint.Down;
        }

        var groupedActions = GroupActionsByMain(actions, mains);

        var resultRow = new List<PointBinding>(mains.Count);

        foreach (PointBinding main in mains)
        {
            resultRow.Add(main);

            if (!groupedActions.TryGetValue(main.Id, out List<PointBinding>? children) || children.Count == 0)
            {
                continue;
            }

            children.Sort((a, b) => a.Point.Position.X.CompareTo(b.Point.Position.X));

            UILinkPoint mainLink = EnsureLinkPoint(main.Id);
            int left = main.Id;

            foreach (PointBinding child in children)
            {
                resultRow.Add(child);

                UILinkPoint childLink = EnsureLinkPoint(child.Id);
                childLink.Left = left;
                EnsureLinkPoint(left).Right = child.Id;
                left = child.Id;

                childLink.Up = mainLink.Up;
                childLink.Down = mainLink.Down;
            }
        }

        return resultRow;
    }

    private static Dictionary<int, List<PointBinding>> GroupActionsByMain(IEnumerable<PointBinding> actions, IReadOnlyList<PointBinding> mains)
    {
        var grouped = new Dictionary<int, List<PointBinding>>(mains.Count);

        foreach (PointBinding action in actions)
        {
            PointBinding main = FindClosestByDistance(action, mains);
            if (!grouped.TryGetValue(main.Id, out List<PointBinding>? list))
            {
                list = new List<PointBinding>();
                grouped[main.Id] = list;
            }

            list.Add(action);
        }

        return grouped;
    }

    private static PointBinding FindClosestByDistance(PointBinding target, IReadOnlyList<PointBinding> mains)
    {
        PointBinding best = mains[0];
        float bestScore = float.MaxValue;

        foreach (PointBinding main in mains)
        {
            float deltaX = Math.Abs(main.Point.Position.X - target.Point.Position.X);
            float deltaY = Math.Abs(main.Point.Position.Y - target.Point.Position.Y);
            float score = deltaY * 2f + deltaX * 0.05f;
            if (score < bestScore)
            {
                bestScore = score;
                best = main;
            }
        }

        return best;
    }

    private static void TrySetModItemButtons(UIElement modItem, ref int index)
    {
        // Stable order: main text, tML update banner, restart/update button, download/update-with-deps,
        // dependency hover icon, more info.
        int startIndex = index;

        if (FieldAccessor.TryGetModItemChild(modItem, ref FieldAccessor.ModItemTmlUpdateRequiredField, "tMLUpdateRequired", out UIElement? tmlUpdate) && tmlUpdate?.Parent is not null)
        {
            tmlUpdate.SetSnapPoint(ModItemActionName, index++);
        }

        if (FieldAccessor.TryGetModItemChild(modItem, ref FieldAccessor.ModItemUpdateButtonField, "_updateButton", out UIElement? update) && update?.Parent is not null)
        {
            update.SetSnapPoint(ModItemActionName, index++);
        }

        if (FieldAccessor.TryGetModItemChild(modItem, ref FieldAccessor.ModItemUpdateWithDepsField, "_updateWithDepsButton", out UIElement? download) && download?.Parent is not null)
        {
            download.SetSnapPoint(ModItemActionName, index++);
        }

        if (FieldAccessor.TryGetModItemChild(modItem, ref FieldAccessor.ModItemMoreInfoField, "_moreInfoButton", out UIElement? info) && info?.Parent is not null)
        {
            info.SetSnapPoint(ModItemActionName, index++);
        }

        if (modItem.Children is { } children)
        {
            foreach (UIElement child in children)
            {
                if (child is null)
                {
                    continue;
                }

                string? typeName = child.GetType().FullName;
                if (typeName is null)
                {
                    continue;
                }

                CalculatedStyle bounds = child.GetDimensions();
                if (bounds.Width > ActionHitboxThreshold || bounds.Height > ActionHitboxThreshold)
                {
                    continue;
                }

                if (typeName.Contains("UIHoverImage", StringComparison.OrdinalIgnoreCase))
                {
                    child.SetSnapPoint(ModItemActionName, index++);
                }
            }
        }

        if (_loggedModItems < MaxModItemLogCount && index > startIndex)
        {
            _loggedModItems++;
            var logger = ScreenReaderMod.Instance?.Logger;
            if (logger is not null)
            {
                logger.Info($"[ModBrowserGamepad] Mod item actions added: {index - startIndex} at Y={modItem.GetDimensions().Y:0.##}");
            }
        }
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
        private static FieldInfo? _modTagFilterDropdown;
        private static readonly Dictionary<Type, FieldInfo?> ItemsFieldCache = new();
        private static FieldInfo? _modItemMoreInfoField;
        private static FieldInfo? _modItemUpdateWithDepsField;
        private static FieldInfo? _modItemUpdateButtonField;
        private static FieldInfo? _modItemTmlUpdateRequiredField;

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
        public static FieldInfo? ModTagFilterDropdown => _modTagFilterDropdown ??= GetField("modTagFilterDropdown");

        public static bool TryGetItems(UIElement listElement, out IEnumerable? items)
        {
            items = null;

            FieldInfo? itemsField = ResolveItemsField(listElement.GetType());
            if (itemsField is null)
            {
                return false;
            }

            items = itemsField.GetValue(listElement) as IEnumerable;
            return items is not null;
        }

        private static FieldInfo? GetField(string name)
        {
            Type? type = ModBrowserType;
            return type?.GetField(name, InstanceFlags);
        }

        private static FieldInfo? ResolveItemsField(Type type)
        {
            if (ItemsFieldCache.TryGetValue(type, out FieldInfo? cached))
            {
                return cached;
            }

            FieldInfo? resolved = null;
            Type? current = type;
            while (current is not null && resolved is null)
            {
                resolved = current.GetField("_items", InstanceFlags);
                current = current.BaseType;
            }

            ItemsFieldCache[type] = resolved;
            return resolved;
        }

        public static bool TryGetModItemChild(UIElement modItem, ref FieldInfo? cache, string fieldName, out UIElement? child)
        {
            child = null;
            Type type = modItem.GetType();
            cache ??= type.GetField(fieldName, InstanceFlags);
            if (cache?.GetValue(modItem) is UIElement element)
            {
                child = element;
                return true;
            }

            return false;
        }

        public static ref FieldInfo? ModItemMoreInfoField => ref _modItemMoreInfoField;
        public static ref FieldInfo? ModItemUpdateWithDepsField => ref _modItemUpdateWithDepsField;
        public static ref FieldInfo? ModItemUpdateButtonField => ref _modItemUpdateButtonField;
        public static ref FieldInfo? ModItemTmlUpdateRequiredField => ref _modItemTmlUpdateRequiredField;
    }
}
