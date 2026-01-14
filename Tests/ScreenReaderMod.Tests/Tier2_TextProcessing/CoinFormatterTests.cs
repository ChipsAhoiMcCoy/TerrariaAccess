#nullable enable
using ScreenReaderMod.Common.Abstractions;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier2_TextProcessing;

// Disable parallel execution for this class since it modifies static state
[Collection("CoinFormatterStaticState")]
public class CoinFormatterTests : IDisposable
{
    private readonly Mock<ITerrariaLocalization> _mockLocalization;

    public CoinFormatterTests()
    {
        _mockLocalization = new Mock<ITerrariaLocalization>();
        _mockLocalization.Setup(l => l.GetCoinLabel(15)).Returns("platinum");
        _mockLocalization.Setup(l => l.GetCoinLabel(16)).Returns("gold");
        _mockLocalization.Setup(l => l.GetCoinLabel(17)).Returns("silver");
        _mockLocalization.Setup(l => l.GetCoinLabel(18)).Returns("copper");
    }

    public void Dispose()
    {
        // Reset static state
        CoinFormatter.DefaultLocalization = null;
    }

    #region Zero and Negative Values

    [Fact]
    public void ValueToCoinString_Zero_ReturnsEmpty()
    {
        var result = CoinFormatter.ValueToCoinString(0, _mockLocalization.Object);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ValueToCoinString_Negative_ReturnsEmpty()
    {
        var result = CoinFormatter.ValueToCoinString(-100, _mockLocalization.Object);

        result.Should().BeEmpty();
    }

    #endregion

    #region Single Denomination Tests

    [Fact]
    public void ValueToCoinString_OnlyCopper_FormatsCorrectly()
    {
        var result = CoinFormatter.ValueToCoinString(50, _mockLocalization.Object);

        result.Should().Be("50 copper");
    }

    [Fact]
    public void ValueToCoinString_OnlySilver_FormatsCorrectly()
    {
        // 1 silver = 100 copper
        var result = CoinFormatter.ValueToCoinString(100, _mockLocalization.Object);

        result.Should().Be("1 silver");
    }

    [Fact]
    public void ValueToCoinString_OnlyGold_FormatsCorrectly()
    {
        // 1 gold = 10,000 copper
        var result = CoinFormatter.ValueToCoinString(10_000, _mockLocalization.Object);

        result.Should().Be("1 gold");
    }

    [Fact]
    public void ValueToCoinString_OnlyPlatinum_FormatsCorrectly()
    {
        // 1 platinum = 1,000,000 copper
        var result = CoinFormatter.ValueToCoinString(1_000_000, _mockLocalization.Object);

        result.Should().Be("1 platinum");
    }

    #endregion

    #region Multiple Denomination Tests

    [Fact]
    public void ValueToCoinString_GoldAndSilver_FormatsCorrectly()
    {
        // 5 gold + 25 silver = 50,000 + 2,500 = 52,500
        var result = CoinFormatter.ValueToCoinString(52_500, _mockLocalization.Object);

        result.Should().Be("5 gold 25 silver");
    }

    [Fact]
    public void ValueToCoinString_AllDenominations_FormatsCorrectly()
    {
        // 2 platinum + 3 gold + 45 silver + 67 copper
        // = 2,000,000 + 30,000 + 4,500 + 67 = 2,034,567
        var result = CoinFormatter.ValueToCoinString(2_034_567, _mockLocalization.Object);

        result.Should().Be("2 platinum 3 gold 45 silver 67 copper");
    }

    [Fact]
    public void ValueToCoinString_SkipsZeroDenominations()
    {
        // 1 gold + 5 copper = 10,000 + 5 = 10,005 (no silver)
        var result = CoinFormatter.ValueToCoinString(10_005, _mockLocalization.Object);

        result.Should().Be("1 gold 5 copper");
        result.Should().NotContain("silver");
    }

    [Fact]
    public void ValueToCoinString_PlatinumAndCopper_SkipsMiddle()
    {
        // 1 platinum + 1 copper = 1,000,001
        var result = CoinFormatter.ValueToCoinString(1_000_001, _mockLocalization.Object);

        result.Should().Be("1 platinum 1 copper");
        result.Should().NotContain("gold");
        result.Should().NotContain("silver");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ValueToCoinString_SingleCopper_FormatsCorrectly()
    {
        var result = CoinFormatter.ValueToCoinString(1, _mockLocalization.Object);

        result.Should().Be("1 copper");
    }

    [Fact]
    public void ValueToCoinString_ExactSilver_NoRemainder()
    {
        // Exactly 5 silver = 500 copper
        var result = CoinFormatter.ValueToCoinString(500, _mockLocalization.Object);

        result.Should().Be("5 silver");
    }

    [Fact]
    public void ValueToCoinString_LargeValue_HandlesCorrectly()
    {
        // 100 platinum = 100,000,000 copper
        var result = CoinFormatter.ValueToCoinString(100_000_000, _mockLocalization.Object);

        result.Should().Be("100 platinum");
    }

    [Fact]
    public void ValueToCoinString_MaxReasonableValue_DoesNotOverflow()
    {
        // Very large but reasonable value: 999 platinum, 99 gold, 99 silver, 99 copper
        long value = 999_000_000 + 990_000 + 9_900 + 99;

        var result = CoinFormatter.ValueToCoinString(value, _mockLocalization.Object);

        result.Should().Be("999 platinum 99 gold 99 silver 99 copper");
    }

    #endregion

    #region Localization Tests

    [Fact]
    public void ValueToCoinString_UsesLocalizedLabels()
    {
        var customLocalization = new Mock<ITerrariaLocalization>();
        customLocalization.Setup(l => l.GetCoinLabel(15)).Returns("Platin");
        customLocalization.Setup(l => l.GetCoinLabel(16)).Returns("Or");
        customLocalization.Setup(l => l.GetCoinLabel(17)).Returns("Argent");
        customLocalization.Setup(l => l.GetCoinLabel(18)).Returns("Cuivre");

        var result = CoinFormatter.ValueToCoinString(1_010_101, customLocalization.Object);

        result.Should().Be("1 Platin 1 Or 1 Argent 1 Cuivre");
    }

    [Fact]
    public void ValueToCoinString_EmptyLabel_SkipsDenomination()
    {
        var customLocalization = new Mock<ITerrariaLocalization>();
        customLocalization.Setup(l => l.GetCoinLabel(15)).Returns("platinum");
        customLocalization.Setup(l => l.GetCoinLabel(16)).Returns(""); // Empty gold label
        customLocalization.Setup(l => l.GetCoinLabel(17)).Returns("silver");
        customLocalization.Setup(l => l.GetCoinLabel(18)).Returns("copper");

        var result = CoinFormatter.ValueToCoinString(1_010_101, customLocalization.Object);

        // Gold should be skipped because label is empty
        result.Should().Be("1 platinum 1 silver 1 copper");
    }

    #endregion

    #region Default Localization Tests

    [Fact]
    public void ValueToCoinString_NoProviderAndNoDefault_ThrowsException()
    {
        CoinFormatter.DefaultLocalization = null;

        Action act = () => CoinFormatter.ValueToCoinString(100);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*localization provider*");
    }

    [Fact]
    public void ValueToCoinString_UsesDefaultWhenNotProvided()
    {
        CoinFormatter.DefaultLocalization = _mockLocalization.Object;

        var result = CoinFormatter.ValueToCoinString(100);

        result.Should().Be("1 silver");
    }

    [Fact]
    public void ValueToCoinString_ExplicitProviderOverridesDefault()
    {
        var defaultMock = new Mock<ITerrariaLocalization>();
        defaultMock.Setup(l => l.GetCoinLabel(17)).Returns("default-silver");

        var explicitMock = new Mock<ITerrariaLocalization>();
        explicitMock.Setup(l => l.GetCoinLabel(17)).Returns("explicit-silver");

        CoinFormatter.DefaultLocalization = defaultMock.Object;

        var result = CoinFormatter.ValueToCoinString(100, explicitMock.Object);

        result.Should().Be("1 explicit-silver");
    }

    #endregion
}
