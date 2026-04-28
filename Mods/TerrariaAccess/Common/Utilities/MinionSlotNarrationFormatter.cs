#nullable enable
using System;
using System.Globalization;

namespace TerrariaAccess.Common.Utilities;

internal static class MinionSlotNarrationFormatter
{
    private const float WholeNumberEpsilon = 0.001f;

    public static string FormatSlotValue(float value)
    {
        if (Math.Abs(value - MathF.Round(value)) < WholeNumberEpsilon)
        {
            return ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static bool UsesSingularSlotUnit(int maxSlots)
    {
        return maxSlots == 1;
    }

    public static string BuildSummonedSlotUsage(float usedSlots, int maxSlots)
    {
        string slotUnit = UsesSingularSlotUnit(maxSlots) ? "minion slot" : "minion slots";
        return $"{FormatSlotValue(usedSlots)} of {maxSlots} {slotUnit} used";
    }
}
