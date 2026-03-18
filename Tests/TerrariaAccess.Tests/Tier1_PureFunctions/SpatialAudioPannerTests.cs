#nullable enable
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

/// <summary>
/// Tests for SpatialAudioPanner which matches Terraria's native spatial audio system.
/// Uses Terraria's exact constants:
/// - Pan: offsetX / 960 pixels
/// - Pitch: -offsetY / 960 pixels (added for vertical awareness)
/// - Volume: 1 - distance / 2500 pixels
/// </summary>
public class SpatialAudioPannerTests
{
    // Terraria's constants
    private const float PanScalePixels = 960f;
    private const float PitchScalePixels = 960f;
    private const float VolumeFalloffPixels = 2500f;
    private const float MaxPitchClamp = 0.7f;

    #region Pan Tests

    [Fact]
    public void Compute_SamePosition_ReturnsZeroPan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pan.Should().Be(0f);
    }

    [Fact]
    public void Compute_TargetToRight_ReturnsPositivePan()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(580, 100); // 480 pixels right = 0.5 pan (480/960)

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pan.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Compute_TargetToLeft_ReturnsNegativePan()
    {
        var listener = new Vector2(580, 100);
        var target = new Vector2(100, 100); // 480 pixels left = -0.5 pan

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pan.Should().BeApproximately(-0.5f, 0.01f);
    }

    [Fact]
    public void Compute_FullPanScale_ReturnsMaxPan()
    {
        var listener = new Vector2(0, 100);
        var target = new Vector2(960, 100); // Exactly at pan scale = 1.0

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pan.Should().Be(1f);
    }

    [Fact]
    public void Compute_ExceedsPanScale_ClampedToOne()
    {
        var listener = new Vector2(0, 100);
        var target = new Vector2(2000, 100); // Beyond pan scale

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pan.Should().Be(1f);
    }

    #endregion

    #region Pitch Tests

    [Fact]
    public void Compute_SamePosition_ReturnsZeroPitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pitch.Should().Be(0f);
    }

    [Fact]
    public void Compute_TargetAbove_ReturnsPositivePitch()
    {
        var listener = new Vector2(100, 580);
        var target = new Vector2(100, 100); // 480 pixels above (negative Y offset)

        var result = SpatialAudioPanner.Compute(listener, target);

        // Pitch = -offsetY / 960 = -(-480) / 960 = 0.5
        result.Pitch.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Compute_TargetBelow_ReturnsNegativePitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 580); // 480 pixels below (positive Y offset)

        var result = SpatialAudioPanner.Compute(listener, target);

        // Pitch = -offsetY / 960 = -(480) / 960 = -0.5
        result.Pitch.Should().BeApproximately(-0.5f, 0.01f);
    }

    [Fact]
    public void Compute_VeryFarAbove_ClampedToMaxPitch()
    {
        var listener = new Vector2(100, 2000);
        var target = new Vector2(100, 0); // Very far above

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pitch.Should().Be(MaxPitchClamp);
    }

    [Fact]
    public void Compute_VeryFarBelow_ClampedToMinPitch()
    {
        var listener = new Vector2(100, 0);
        var target = new Vector2(100, 2000); // Very far below

        var result = SpatialAudioPanner.Compute(listener, target);

        result.Pitch.Should().Be(-MaxPitchClamp);
    }

    #endregion

    #region Volume Tests

    [Fact]
    public void Compute_ZeroDistance_ReturnsFullVolume()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(1f);
    }

    [Fact]
    public void Compute_HalfFalloffDistance_ReturnsHalfVolume()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(1250, 0); // Half of 2500 pixels falloff

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 1.0f);

        // Volume = 1 - 1250/2500 = 0.5
        result.Volume.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Compute_AtFalloffDistance_ReturnsZeroVolume()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(2500, 0); // Exactly at falloff distance

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(0f);
    }

    [Fact]
    public void Compute_BeyondFalloffDistance_ClampedToZero()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(5000, 0); // Beyond falloff distance

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(0f);
    }

    [Fact]
    public void Compute_BaseVolumeScalesResult()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(0, 0);

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 0.5f);

        result.Volume.Should().Be(0.5f);
    }

    [Fact]
    public void Compute_BaseVolumeAndDistanceCombined()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(1250, 0); // Half falloff = 0.5 distance factor

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 0.8f);

        // Volume = 0.5 * 0.8 = 0.4
        result.Volume.Should().BeApproximately(0.4f, 0.01f);
    }

    #endregion

    #region Distance Tests

    [Fact]
    public void Compute_CalculatesDistanceInTiles()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(160, 0); // 160 pixels = 10 tiles (16 pixels per tile)

        var result = SpatialAudioPanner.Compute(listener, target);

        result.DistanceTiles.Should().BeApproximately(10f, 0.001f);
    }

    [Fact]
    public void Compute_DiagonalDistance_CalculatesPythagorean()
    {
        var listener = new Vector2(0, 0);
        // 48 pixels X, 64 pixels Y = 80 pixels distance (3-4-5 triangle scaled by 16)
        // 80 pixels / 16 = 5 tiles
        var target = new Vector2(48, 64);

        var result = SpatialAudioPanner.Compute(listener, target);

        result.DistanceTiles.Should().BeApproximately(5f, 0.001f);
    }

    #endregion

    #region Combined Behavior Tests

    [Fact]
    public void Compute_DiagonalOffset_ReturnsCorrectPanPitchAndVolume()
    {
        var listener = new Vector2(0, 480);
        var target = new Vector2(480, 0); // Right and above

        var result = SpatialAudioPanner.Compute(listener, target, baseVolume: 1.0f);

        // Pan: 480 / 960 = 0.5
        result.Pan.Should().BeApproximately(0.5f, 0.01f);

        // Pitch: -(-480) / 960 = 0.5
        result.Pitch.Should().BeApproximately(0.5f, 0.01f);

        // Distance: sqrt(480^2 + 480^2) ≈ 679 pixels
        // Volume: 1 - 679/2500 ≈ 0.73
        result.Volume.Should().BeApproximately(0.73f, 0.02f);
    }

    [Fact]
    public void Compute_DefaultBaseVolume_IsOne()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPanner.Compute(listener, target);

        // Default baseVolume should be 1.0
        result.Volume.Should().Be(1f);
    }

    #endregion

    #region SpatialAudioSample Record Tests

    [Fact]
    public void SpatialAudioSample_RecordEquality_WorksCorrectly()
    {
        var a = new SpatialAudioPanner.SpatialAudioSample(0.3f, 0.5f, 0.8f, 10f);
        var b = new SpatialAudioPanner.SpatialAudioSample(0.3f, 0.5f, 0.8f, 10f);
        var c = new SpatialAudioPanner.SpatialAudioSample(0.4f, 0.5f, 0.8f, 10f);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion
}
