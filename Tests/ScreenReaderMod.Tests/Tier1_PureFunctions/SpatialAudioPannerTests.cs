#nullable enable
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Tests.Tier1_PureFunctions;

public class SpatialAudioPannerTests
{
    private const float DefaultPitchScale = 200f;
    private const float DefaultPanScale = 200f;

    #region ComputeDirection Tests

    [Fact]
    public void ComputeDirection_SamePosition_ReturnsZeroPitchAndPan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pitch.Should().Be(0f);
        result.Pan.Should().Be(0f);
        result.DistanceTiles.Should().Be(0f);
    }

    [Fact]
    public void ComputeDirection_TargetAbove_ReturnsPositivePitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 0); // 100 pixels above (negative Y = up)

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pitch.Should().BePositive();
        result.Pan.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void ComputeDirection_TargetBelow_ReturnsNegativePitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 200); // 100 pixels below (positive Y = down)

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pitch.Should().BeNegative();
        result.Pan.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void ComputeDirection_TargetToRight_ReturnsPositivePan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(200, 100); // 100 pixels to the right

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pan.Should().BePositive();
        result.Pitch.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void ComputeDirection_TargetToLeft_ReturnsNegativePan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(0, 100); // 100 pixels to the left

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pan.Should().BeNegative();
        result.Pitch.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void ComputeDirection_HalfScaleOffset_ReturnsMidRangeValues()
    {
        var listener = new Vector2(100, 100);
        // Offset by half the scale in both directions
        var target = new Vector2(200, 0); // +100 X, -100 Y

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.Pitch.Should().BeApproximately(0.5f, 0.01f);
        result.Pan.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void ComputeDirection_ExceedsScale_ClampedToMaxValues()
    {
        var listener = new Vector2(100, 100);
        // Very far offset that exceeds scale
        var target = new Vector2(1000, -800);

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        // Pan is clamped to [-1, 1]
        result.Pan.Should().Be(1f);
        // Pitch is clamped to [-pitchClamp, pitchClamp] (default 0.8)
        result.Pitch.Should().Be(0.8f);
    }

    [Fact]
    public void ComputeDirection_CustomPitchClamp_RespectsClampValue()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, -800); // Very far above

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale, pitchClamp: 0.5f);

        result.Pitch.Should().Be(0.5f);
    }

    [Fact]
    public void ComputeDirection_CalculatesDistanceInTiles()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(160, 0); // 160 pixels = 10 tiles (16 pixels per tile)

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.DistanceTiles.Should().BeApproximately(10f, 0.001f);
    }

    [Fact]
    public void ComputeDirection_DiagonalDistance_CalculatesPythagorean()
    {
        var listener = new Vector2(0, 0);
        // 48 pixels X, 64 pixels Y = 80 pixels distance (3-4-5 triangle scaled by 16)
        // 80 pixels / 16 = 5 tiles
        var target = new Vector2(48, 64);

        var result = SpatialAudioPanner.ComputeDirection(
            listener, target, DefaultPitchScale, DefaultPanScale);

        result.DistanceTiles.Should().BeApproximately(5f, 0.001f);
    }

    #endregion

    #region ComputeSample Tests

    [Fact]
    public void ComputeSample_ZeroDistance_ReturnsFullVolume()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);
        var profile = new SpatialAudioPanner.SpatialAudioProfile(
            PitchScalePixels: DefaultPitchScale,
            PanScalePixels: DefaultPanScale,
            DistanceReferenceTiles: 40f,
            MinVolume: 0.2f);

        var result = SpatialAudioPanner.ComputeSample(listener, target, profile, soundVolume: 1.0f);

        // At zero distance: factor = 1/(1+0) = 1
        // volume = min(0.2 + 1 * 0.85, 1) * 1.0 = 1.0 (clamped)
        result.Volume.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void ComputeSample_AtReferenceDistance_ReturnsReducedVolume()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(640, 0); // 40 tiles away (640/16 = 40)
        var profile = new SpatialAudioPanner.SpatialAudioProfile(
            PitchScalePixels: DefaultPitchScale,
            PanScalePixels: DefaultPanScale,
            DistanceReferenceTiles: 40f,
            MinVolume: 0.2f);

        var result = SpatialAudioPanner.ComputeSample(listener, target, profile, soundVolume: 1.0f);

        // At reference distance (40 tiles): factor = 1/(1+40/40) = 1/2 = 0.5
        // volume = (0.2 + 0.5 * 0.85) * 1.0 = 0.625
        result.Volume.Should().BeApproximately(0.625f, 0.01f);
    }

    [Fact]
    public void ComputeSample_SoundVolumeScalesResult()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);
        var profile = new SpatialAudioPanner.SpatialAudioProfile(
            PitchScalePixels: DefaultPitchScale,
            PanScalePixels: DefaultPanScale,
            DistanceReferenceTiles: 40f,
            MinVolume: 0.2f);

        var result = SpatialAudioPanner.ComputeSample(listener, target, profile, soundVolume: 0.5f);

        // At zero distance with 0.5 sound volume
        result.Volume.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void ComputeSample_IncludesPitchAndPan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(200, 0); // Right and above
        var profile = new SpatialAudioPanner.SpatialAudioProfile(
            PitchScalePixels: DefaultPitchScale,
            PanScalePixels: DefaultPanScale,
            DistanceReferenceTiles: 40f,
            MinVolume: 0.2f);

        var result = SpatialAudioPanner.ComputeSample(listener, target, profile, soundVolume: 1.0f);

        result.Pitch.Should().BePositive();
        result.Pan.Should().BePositive();
    }

    #endregion

    #region SpatialDirection Record Tests

    [Fact]
    public void SpatialDirection_RecordEquality_WorksCorrectly()
    {
        var a = new SpatialAudioPanner.SpatialDirection(0.5f, 0.3f, 10f);
        var b = new SpatialAudioPanner.SpatialDirection(0.5f, 0.3f, 10f);
        var c = new SpatialAudioPanner.SpatialDirection(0.5f, 0.4f, 10f);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion
}
