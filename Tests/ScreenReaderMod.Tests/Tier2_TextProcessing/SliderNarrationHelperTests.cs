#nullable enable
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Tests.Tier2_TextProcessing;

public class SliderNarrationHelperTests
{
    #region GetDefaultSliderLabel Tests

    [Fact]
    public void GetDefaultSliderLabel_Music_ReturnsMusicVolume()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Music).Should().Be("Music volume");
    }

    [Fact]
    public void GetDefaultSliderLabel_Sound_ReturnsSoundVolume()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Sound).Should().Be("Sound volume");
    }

    [Fact]
    public void GetDefaultSliderLabel_Ambient_ReturnsAmbientVolume()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Ambient).Should().Be("Ambient volume");
    }

    [Fact]
    public void GetDefaultSliderLabel_Zoom_ReturnsZoom()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Zoom).Should().Be("Zoom");
    }

    [Fact]
    public void GetDefaultSliderLabel_InterfaceScale_ReturnsInterfaceScale()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.InterfaceScale).Should().Be("Interface scale");
    }

    [Fact]
    public void GetDefaultSliderLabel_Parallax_ReturnsBackgroundParallax()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Parallax).Should().Be("Background parallax");
    }

    [Fact]
    public void GetDefaultSliderLabel_Unknown_ReturnsSlider()
    {
        SliderNarrationHelper.GetDefaultSliderLabel(MenuSliderKind.Unknown).Should().Be("Slider");
    }

    [Fact]
    public void GetDefaultSliderLabel_UnknownEnumValue_ReturnsSlider()
    {
        var result = SliderNarrationHelper.GetDefaultSliderLabel((MenuSliderKind)999);

        result.Should().Be("Slider");
    }

    #endregion

    #region TrimTrailingNumber Tests

    [Fact]
    public void TrimTrailingNumber_NoTrailingNumber_ReturnsUnchanged()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_TrailingNumber_TrimsNumber()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume 50");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_TrailingNumberWithColon_TrimsAll()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume: 50");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_TrailingNumberWithPeriod_TrimsAll()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume: 50.");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_TrailingDecimalNumber_TrimsAll()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume: 50.5");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_OnlyNumber_ReturnsEmpty()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("50");

        result.Should().BeEmpty();
    }

    [Fact]
    public void TrimTrailingNumber_OnlyWhitespaceAndNumber_ReturnsEmpty()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("   50");

        result.Should().BeEmpty();
    }

    [Fact]
    public void TrimTrailingNumber_LeadingNumberUntouched_ReturnsCorrectly()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("100% Volume: 50");

        result.Should().Be("100% Volume");
    }

    [Fact]
    public void TrimTrailingNumber_MultipleTrailingNumbers_TrimsAll()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume: 100: 50");

        result.Should().Be("Volume");
    }

    [Fact]
    public void TrimTrailingNumber_EmptyString_ReturnsEmpty()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void TrimTrailingNumber_TrailingWhitespaceOnly_TrimsWhitespace()
    {
        var result = SliderNarrationHelper.TrimTrailingNumber("Volume   ");

        result.Should().Be("Volume");
    }

    #endregion

    #region ExtractBaseLabel Tests

    [Fact]
    public void ExtractBaseLabel_PlainLabel_ReturnsLabel()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithPercent_StripsPercent()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music: 50%", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithPercentWord_StripsPercentWord()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music: 50 percent", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithPercentWordCaseInsensitive_StripsPercentWord()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music: 50 Percent", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithTrailingNumber_StripsNumber()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music: 50", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithTrailingColon_StripsColon()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music:", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabel_ReturnsDefault()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Music);

        result.Should().Be("Music volume");
    }

    [Fact]
    public void ExtractBaseLabel_WhitespaceLabel_ReturnsDefault()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("   ", MenuSliderKind.Music);

        result.Should().Be("Music volume");
    }

    [Fact]
    public void ExtractBaseLabel_OnlyNumberLabel_ReturnsDefault()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("50", MenuSliderKind.Sound);

        result.Should().Be("Sound volume");
    }

    [Fact]
    public void ExtractBaseLabel_LabelWithFormatting_CleansAndReturns()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("[c/FF0000:Music]", MenuSliderKind.Music);

        result.Should().Be("Music");
    }

    [Fact]
    public void ExtractBaseLabel_ComplexLabel_ExtractsBaseOnly()
    {
        var result = SliderNarrationHelper.ExtractBaseLabel("Music Volume: 75%", MenuSliderKind.Music);

        result.Should().Be("Music Volume");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithMusicKind_ReturnsMusicVolume()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Music).Should().Be("Music volume");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithSoundKind_ReturnsSoundVolume()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Sound).Should().Be("Sound volume");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithAmbientKind_ReturnsAmbientVolume()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Ambient).Should().Be("Ambient volume");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithZoomKind_ReturnsZoom()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Zoom).Should().Be("Zoom");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithInterfaceScaleKind_ReturnsInterfaceScale()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.InterfaceScale).Should().Be("Interface scale");
    }

    [Fact]
    public void ExtractBaseLabel_EmptyLabelWithParallaxKind_ReturnsBackgroundParallax()
    {
        SliderNarrationHelper.ExtractBaseLabel("", MenuSliderKind.Parallax).Should().Be("Background parallax");
    }

    #endregion

    #region BuildSliderAnnouncement Tests

    [Fact]
    public void BuildSliderAnnouncement_WithLabel_BuildsFullAnnouncement()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Music", MenuSliderKind.Music, 75f, includeLabel: true);

        result.Should().Be("Music 75 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_WithoutLabel_BuildsValueOnly()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Music", MenuSliderKind.Music, 75f, includeLabel: false);

        result.Should().Be("75 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_EmptyLabel_UsesDefault()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("", MenuSliderKind.Sound, 50f, includeLabel: true);

        result.Should().Be("Sound volume 50 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_LabelWithPercent_CleansLabel()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Volume: 25%", MenuSliderKind.Music, 50f, includeLabel: true);

        result.Should().Be("Volume 50 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_ZeroPercent_FormatsCorrectly()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Volume", MenuSliderKind.Music, 0f, includeLabel: true);

        result.Should().Be("Volume 0 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_HundredPercent_FormatsCorrectly()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Volume", MenuSliderKind.Music, 100f, includeLabel: true);

        result.Should().Be("Volume 100 percent");
    }

    [Fact]
    public void BuildSliderAnnouncement_FractionalPercent_RoundsToWhole()
    {
        var result = SliderNarrationHelper.BuildSliderAnnouncement("Volume", MenuSliderKind.Music, 33.7f, includeLabel: true);

        result.Should().Be("Volume 34 percent");
    }

    #endregion
}
