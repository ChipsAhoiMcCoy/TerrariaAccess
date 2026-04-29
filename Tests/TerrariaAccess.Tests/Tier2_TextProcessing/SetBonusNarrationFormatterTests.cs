#nullable enable
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Tests.Tier2_TextProcessing;

public class SetBonusNarrationFormatterTests
{
    [Fact]
    public void BuildStatusLine_EmptyBonus_ReturnsNull()
    {
        SetBonusNarrationFormatter.BuildStatusLine("   ").Should().BeNull();
    }

    [Fact]
    public void BuildStatusLine_CollapsesMultilineBonus()
    {
        string? result = SetBonusNarrationFormatter.BuildStatusLine("Double tap Down\nfor stealth");

        result.Should().Be("Set bonus: Double tap Down for stealth");
    }

    [Fact]
    public void BuildActivatedAnnouncement_AddsActivePrefix()
    {
        string? result = SetBonusNarrationFormatter.BuildActivatedAnnouncement("Increases melee speed");

        result.Should().Be("Set bonus active: Increases melee speed");
    }

    [Fact]
    public void ContainsDescription_MatchesExistingTooltipText()
    {
        bool result = SetBonusNarrationFormatter.ContainsDescription(
            "5 defense. Set bonus: Increases melee speed.",
            "Increases melee speed");

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsDescription_MatchesMultilineBonusInFlattenedTooltipText()
    {
        bool result = SetBonusNarrationFormatter.ContainsDescription(
            "Set bonus (3/3) Double tap Down for stealth.",
            "Double tap Down\nfor stealth");

        result.Should().BeTrue();
    }
}
