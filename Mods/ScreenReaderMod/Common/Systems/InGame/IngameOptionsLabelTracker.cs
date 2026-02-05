#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;

namespace ScreenReaderMod.Common.Systems.InGame;

/// <summary>
/// Tracks labels captured from the ingame options menu drawing.
/// Single responsibility: capturing and exposing option labels.
/// </summary>
internal sealed class IngameOptionsLabelTracker
{
    private readonly Dictionary<int, string> _leftLabels = new();
    private readonly Dictionary<int, int> _leftIndexToCategory = new();
    private readonly Dictionary<int, string> _categoryLabels = new();
    private readonly Dictionary<(int category, int optionIndex), string> _optionLabels = new();
    private bool[] _skipRightSlotSnapshot = Array.Empty<bool>();

    private FieldInfo? _leftSideCategoryMappingField;
    private FieldInfo? _skipRightSlotField;
    private FieldInfo? _categoryField;

    private static readonly Dictionary<int, int> EmptyMapping = new();

    public void Configure(FieldInfo? leftMapping, FieldInfo? skipField, FieldInfo? categoryField)
    {
        if (leftMapping is not null)
        {
            _leftSideCategoryMappingField = leftMapping;
        }

        if (skipField is not null)
        {
            _skipRightSlotField = skipField;
        }

        if (categoryField is not null)
        {
            _categoryField = categoryField;
        }
    }

    public void BeginFrame()
    {
        _leftLabels.Clear();
        _leftIndexToCategory.Clear();
        _categoryLabels.Clear();
        _optionLabels.Clear();

        UpdateSkipSnapshot();
    }

    public void EndFrame()
    {
        UpdateSkipSnapshot();
    }

    public void RecordLeft(int index, string? label)
    {
        string sanitized = TextSanitizer.Clean(label ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            _leftLabels[index] = sanitized;
        }

        foreach (KeyValuePair<int, int> kvp in GetLeftSideCategoryMapping())
        {
            if (kvp.Key != index)
            {
                continue;
            }

            _leftIndexToCategory[index] = kvp.Value;
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                _categoryLabels[kvp.Value] = sanitized;
            }
            break;
        }
    }

    public void RecordRight(int index, string? label)
    {
        UpdateSkipSnapshot();

        int category = GetCurrentCategory();
        if (category < 0)
        {
            return;
        }

        if ((uint)index < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[index])
        {
            return;
        }

        string sanitized = TextSanitizer.Clean(label ?? string.Empty);
        string lower = sanitized.ToLowerInvariant();
        if (Main.gameMenu && (lower.Contains("volume") || lower.Contains("audio") || lower.Contains("tmodloader")))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            _optionLabels[(category, index)] = sanitized;
        }
    }

    public bool TryGetLeftLabel(int index, out string label)
    {
        return _leftLabels.TryGetValue(index, out label!);
    }

    public bool TryGetCategoryLabel(int category, out string label)
    {
        return _categoryLabels.TryGetValue(category, out label!);
    }

    public bool TryMapLeftToCategory(int leftIndex, out int category)
    {
        return _leftIndexToCategory.TryGetValue(leftIndex, out category);
    }

    public bool TryGetOptionLabel(int category, int optionIndex, out string label)
    {
        return _optionLabels.TryGetValue((category, optionIndex), out label!);
    }

    public bool TryGetCurrentOptionLabel(int optionIndex, out string label)
    {
        int category = GetCurrentCategory();
        if (category < 0)
        {
            label = string.Empty;
            return false;
        }

        return _optionLabels.TryGetValue((category, optionIndex), out label!);
    }

    public bool IsOptionSkipped(int optionIndex)
    {
        return (uint)optionIndex < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[optionIndex];
    }

    public IReadOnlyDictionary<int, int> GetLeftMappingSnapshot()
    {
        return _leftIndexToCategory.Count == 0 ? EmptyMapping : new Dictionary<int, int>(_leftIndexToCategory);
    }

    public int GetCurrentCategory()
    {
        if (_categoryField?.GetValue(null) is int value)
        {
            return value;
        }

        return -1;
    }

    private void UpdateSkipSnapshot()
    {
        if (_skipRightSlotField?.GetValue(null) is bool[] array)
        {
            _skipRightSlotSnapshot = array.Length == 0 ? Array.Empty<bool>() : (bool[])array.Clone();
        }
        else
        {
            _skipRightSlotSnapshot = Array.Empty<bool>();
        }
    }

    private Dictionary<int, int> GetLeftSideCategoryMapping()
    {
        if (_leftSideCategoryMappingField?.GetValue(null) is Dictionary<int, int> mapping)
        {
            return mapping;
        }

        return EmptyMapping;
    }
}
