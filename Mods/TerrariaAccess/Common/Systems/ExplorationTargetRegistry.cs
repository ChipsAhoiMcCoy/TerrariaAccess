#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Shares exploration targets detected by the world interactable tracker so the guidance system can surface them.
/// </summary>
internal static class ExplorationTargetRegistry
{
    internal readonly record struct ExplorationTargetKey(int SourceId, int LocalId);

    internal readonly record struct ExplorationTarget(ExplorationTargetKey Key, string Label, Vector2 WorldPosition, float DistanceTiles);

    private static readonly List<ExplorationTarget> Targets = new();
    private static ExplorationTarget? _selectedTarget;
    private static bool _selectedTargetCueRequested;

    public static void UpdateTargets(IEnumerable<ExplorationTarget> entries)
    {
        Targets.Clear();
        Targets.AddRange(entries);
    }

    public static IReadOnlyList<ExplorationTarget> GetSnapshot()
    {
        return Targets.Count == 0 ? (IReadOnlyList<ExplorationTarget>)System.Array.Empty<ExplorationTarget>() : new List<ExplorationTarget>(Targets);
    }

    public static void SetSelectedTarget(ExplorationTarget? target)
    {
        _selectedTarget = target;
        if (!_selectedTarget.HasValue)
        {
            _selectedTargetCueRequested = false;
        }
    }

    public static bool TryGetSelectedTarget(out ExplorationTarget target)
    {
        if (_selectedTarget.HasValue)
        {
            target = _selectedTarget.Value;
            return true;
        }

        target = default;
        return false;
    }

    public static void RequestSelectedTargetCue()
    {
        _selectedTargetCueRequested = _selectedTarget.HasValue;
    }

    public static bool ConsumeSelectedTargetCueRequest()
    {
        bool requested = _selectedTargetCueRequested;
        _selectedTargetCueRequested = false;
        return requested;
    }
}
