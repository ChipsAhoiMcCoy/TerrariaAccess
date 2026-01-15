#nullable enable
using ScreenReaderMod.Common.Abstractions;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier2_TextProcessing;

// Disable parallel execution with CoinFormatterTests since both modify static state
[Collection("CoinFormatterStaticState")]
public class NarrationStringCatalogTests : IDisposable
{
    private readonly Mock<ITerrariaLocalization> _mockLocalization;

    public NarrationStringCatalogTests()
    {
        _mockLocalization = new Mock<ITerrariaLocalization>();
        // Index 15 = Platinum, 16 = Gold, 17 = Silver, 18 = Copper
        _mockLocalization.Setup(m => m.GetCoinLabel(15)).Returns("platinum");
        _mockLocalization.Setup(m => m.GetCoinLabel(16)).Returns("gold");
        _mockLocalization.Setup(m => m.GetCoinLabel(17)).Returns("silver");
        _mockLocalization.Setup(m => m.GetCoinLabel(18)).Returns("copper");
        CoinFormatter.DefaultLocalization = _mockLocalization.Object;
    }

    public void Dispose()
    {
        CoinFormatter.DefaultLocalization = null;
    }

    #region SliderValue Tests

    [Fact]
    public void SliderValue_WithLabel_IncludesLabelAndPercent()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 75f, includeLabel: true);

        result.Should().Be("Volume 75 percent");
    }

    [Fact]
    public void SliderValue_WithoutLabel_ReturnsPercentOnly()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 75f, includeLabel: false);

        result.Should().Be("75 percent");
    }

    [Fact]
    public void SliderValue_ZeroPercent_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 0f, includeLabel: true);

        result.Should().Be("Volume 0 percent");
    }

    [Fact]
    public void SliderValue_HundredPercent_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 100f, includeLabel: true);

        result.Should().Be("Volume 100 percent");
    }

    [Fact]
    public void SliderValue_FractionalPercent_RoundsToWholeNumber()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 33.333f, includeLabel: true);

        result.Should().Be("Volume 33 percent");
    }

    [Fact]
    public void SliderValue_EmptyLabel_ReturnsPercentOnly()
    {
        var result = NarrationStringCatalog.SliderValue("", 50f, includeLabel: true);

        result.Should().Be("50 percent");
    }

    [Fact]
    public void SliderValue_WhitespaceLabel_ReturnsPercentOnly()
    {
        var result = NarrationStringCatalog.SliderValue("   ", 50f, includeLabel: true);

        result.Should().Be("50 percent");
    }

    [Fact]
    public void SliderValue_LabelWithFormatting_CleansLabel()
    {
        var result = NarrationStringCatalog.SliderValue("[c/FF0000:Volume]", 50f, includeLabel: true);

        result.Should().Be("Volume 50 percent");
    }

    [Fact]
    public void SliderValue_NegativePercent_FormatsAsNegative()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", -10f, includeLabel: true);

        result.Should().Be("Volume -10 percent");
    }

    [Fact]
    public void SliderValue_OverHundredPercent_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.SliderValue("Volume", 150f, includeLabel: true);

        result.Should().Be("Volume 150 percent");
    }

    #endregion

    #region Coordinates Tests - Vector2 Overload

    [Fact]
    public void Coordinates_Vector2_ConvertsToTileCoordinates()
    {
        // 160 pixels / 16 pixels per tile = 10 tiles
        var position = new Vector2(160f, 320f);

        var result = NarrationStringCatalog.Coordinates(position);

        result.Should().Be("X 10, Y 20");
    }

    [Fact]
    public void Coordinates_Vector2_RoundsToNearestTile()
    {
        // 24 pixels / 16 = 1.5, rounds to 2
        var position = new Vector2(24f, 24f);

        var result = NarrationStringCatalog.Coordinates(position);

        result.Should().Be("X 2, Y 2");
    }

    [Fact]
    public void Coordinates_Vector2_Zero_ReturnsZeroCoordinates()
    {
        var position = new Vector2(0f, 0f);

        var result = NarrationStringCatalog.Coordinates(position);

        result.Should().Be("X 0, Y 0");
    }

    [Fact]
    public void Coordinates_Vector2_LargeValues_FormatsCorrectly()
    {
        // Large world position: 100000 pixels = 6250 tiles
        var position = new Vector2(100000f, 50000f);

        var result = NarrationStringCatalog.Coordinates(position);

        result.Should().Be("X 6250, Y 3125");
    }

    [Fact]
    public void Coordinates_Vector2_NegativeValues_FormatsCorrectly()
    {
        var position = new Vector2(-160f, -320f);

        var result = NarrationStringCatalog.Coordinates(position);

        result.Should().Be("X -10, Y -20");
    }

    #endregion

    #region Coordinates Tests - Int Overload

    [Fact]
    public void Coordinates_Int_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.Coordinates(10, 20);

        result.Should().Be("X 10, Y 20");
    }

    [Fact]
    public void Coordinates_Int_Zero_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.Coordinates(0, 0);

        result.Should().Be("X 0, Y 0");
    }

    [Fact]
    public void Coordinates_Int_NegativeValues_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.Coordinates(-5, -10);

        result.Should().Be("X -5, Y -10");
    }

    [Fact]
    public void Coordinates_Int_LargeValues_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.Coordinates(99999, 88888);

        result.Should().Be("X 99999, Y 88888");
    }

    #endregion

    #region Price Tests

    [Fact]
    public void Price_WithLabel_IncludesLabelAndCoinValue()
    {
        var result = NarrationStringCatalog.Price("Buy price", 10000);

        result.Should().Be("Buy price: 1 gold");
    }

    [Fact]
    public void Price_WithEmptyLabel_ReturnsCoinValueOnly()
    {
        var result = NarrationStringCatalog.Price("", 10000);

        result.Should().Be("1 gold");
    }

    [Fact]
    public void Price_WithWhitespaceLabel_ReturnsCoinValueOnly()
    {
        var result = NarrationStringCatalog.Price("   ", 10000);

        result.Should().Be("1 gold");
    }

    [Fact]
    public void Price_ZeroValue_ReturnsZeroAsNumber()
    {
        var result = NarrationStringCatalog.Price("Cost", 0);

        // CoinFormatter returns empty for 0, so we fall back to numeric
        result.Should().Be("Cost: 0");
    }

    [Fact]
    public void Price_NegativeValue_ReturnsNegativeAsNumber()
    {
        var result = NarrationStringCatalog.Price("Value", -100);

        // CoinFormatter returns empty for negative, so we fall back to numeric
        result.Should().Be("Value: -100");
    }

    [Fact]
    public void Price_LabelWithFormatting_CleansLabel()
    {
        var result = NarrationStringCatalog.Price("[c/FF0000:Buy price]", 10000);

        result.Should().Be("Buy price: 1 gold");
    }

    [Fact]
    public void Price_ComplexCoinValue_FormatsAllDenominations()
    {
        // 1 platinum + 2 gold + 3 silver + 4 copper
        // = 1,000,000 + 20,000 + 300 + 4 = 1,020,304
        var result = NarrationStringCatalog.Price("Value", 1_020_304);

        result.Should().Be("Value: 1 platinum 2 gold 3 silver 4 copper");
    }

    #endregion

    #region ItemLabel Tests

    [Fact]
    public void ItemLabel_SingleItem_ReturnsNameOnly()
    {
        var result = NarrationStringCatalog.ItemLabel("Iron Pickaxe", 1, favorited: false);

        result.Should().Be("Iron Pickaxe");
    }

    [Fact]
    public void ItemLabel_StackOfItems_IncludesCount()
    {
        var result = NarrationStringCatalog.ItemLabel("Wood", 99, favorited: false);

        result.Should().Be("99 Wood");
    }

    [Fact]
    public void ItemLabel_FavoritedItem_IncludesFavoritedSuffix()
    {
        var result = NarrationStringCatalog.ItemLabel("Zenith", 1, favorited: true);

        result.Should().Be("Zenith, favorited");
    }

    [Fact]
    public void ItemLabel_FavoritedStack_IncludesCountAndFavorited()
    {
        var result = NarrationStringCatalog.ItemLabel("Potion", 30, favorited: true);

        result.Should().Be("30 Potion, favorited");
    }

    [Fact]
    public void ItemLabel_SingleItemWithIncludeCount_IncludesOne()
    {
        var result = NarrationStringCatalog.ItemLabel("Sword", 1, favorited: false, includeCountWhenSingular: true);

        result.Should().Be("1 Sword");
    }

    [Fact]
    public void ItemLabel_ZeroStack_WithIncludeCount_IncludesOne()
    {
        var result = NarrationStringCatalog.ItemLabel("Sword", 0, favorited: false, includeCountWhenSingular: true);

        // Math.Max(1, 0) = 1
        result.Should().Be("1 Sword");
    }

    [Fact]
    public void ItemLabel_NegativeStack_WithIncludeCount_IncludesOne()
    {
        var result = NarrationStringCatalog.ItemLabel("Sword", -5, favorited: false, includeCountWhenSingular: true);

        // Math.Max(1, -5) = 1
        result.Should().Be("1 Sword");
    }

    [Fact]
    public void ItemLabel_NameWithFormatting_CleansName()
    {
        var result = NarrationStringCatalog.ItemLabel("[c/FF0000:Legendary Sword]", 1, favorited: false);

        result.Should().Be("Legendary Sword");
    }

    [Fact]
    public void ItemLabel_EmptyName_ReturnsEmpty()
    {
        var result = NarrationStringCatalog.ItemLabel("", 1, favorited: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ItemLabel_EmptyNameButFavorited_ReturnsFavoritedOnly()
    {
        var result = NarrationStringCatalog.ItemLabel("", 1, favorited: true);

        result.Should().Be("favorited");
    }

    [Fact]
    public void ItemLabel_LargeStack_FormatsCorrectly()
    {
        var result = NarrationStringCatalog.ItemLabel("Coin", 9999, favorited: false);

        result.Should().Be("9999 Coin");
    }

    #endregion
}
