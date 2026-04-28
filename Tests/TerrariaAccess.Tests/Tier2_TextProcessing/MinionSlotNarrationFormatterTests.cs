using FluentAssertions;
using TerrariaAccess.Common.Utilities;
using Xunit;

namespace TerrariaAccess.Tests.Tier2_TextProcessing;

public sealed class MinionSlotNarrationFormatterTests
{
    [Theory]
    [InlineData(1f, "1")]
    [InlineData(2f, "2")]
    [InlineData(0.5f, "0.5")]
    [InlineData(1.25f, "1.25")]
    public void FormatSlotValue_FormatsWholeAndFractionalSlots(float value, string expected)
    {
        MinionSlotNarrationFormatter.FormatSlotValue(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(1f, 1, "1 of 1 minion slot used")]
    [InlineData(2f, 3, "2 of 3 minion slots used")]
    [InlineData(1.5f, 2, "1.5 of 2 minion slots used")]
    public void BuildSummonedSlotUsage_UsesReadableSlotText(float usedSlots, int maxSlots, string expected)
    {
        MinionSlotNarrationFormatter.BuildSummonedSlotUsage(usedSlots, maxSlots).Should().Be(expected);
    }
}
