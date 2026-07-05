#nullable enable
#pragma warning disable CS0618 // Tests intentionally cover low-level spatial positioning math; production code uses SpatializedSoundEngine.

using TerrariaAccess.Common.Services;
using Terraria;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

/// <summary>
/// Tests for SpatialAudioPositioner's 2D accessibility spatial mapping.
/// Primary world-space behavior maps target X against the visible viewport:
/// - Left edge = -1
/// - Center = 0
/// - Right edge = 1
/// Fallback world-space behavior uses:
/// - Horizontal position: offsetX / 960 pixels, clamped to normalized screen X -1..1
/// - Pitch: -offsetY / 960 pixels for elevation
/// - Volume: full volume nearby, then nonlinear distance falloff to 2500 pixels
/// - ITD: normalized screen X maps to delayed opposite ear with edge attenuation
/// </summary>
public class SpatialAudioPositionerTests
{
    private const float FullVolumeRadiusPixels = 96f;
    private const float VolumeFalloffPixels = 2500f;
    private const float MaxPitchClamp = 0.65f;
    private const float EdgeDelayedEarGain = 0.3f;
    private const float HalfRightDelayedEarGain = 0.75251263f;

    public SpatialAudioPositionerTests()
    {
        ResetViewport();
    }

    #region Normalized Screen X Tests

    [Fact]
    public void Compute_SamePosition_ReturnsCenteredScreenX()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.NormalizedScreenX.Should().Be(0f);
    }

    [Fact]
    public void Compute_TargetToRight_ReturnsPositiveScreenX()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(580, 100); // 480 pixels right = 0.5 normalized X (480/960)

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.NormalizedScreenX.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Compute_TargetToLeft_ReturnsNegativeScreenX()
    {
        var listener = new Vector2(580, 100);
        var target = new Vector2(100, 100); // 480 pixels left = -0.5 normalized X

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.NormalizedScreenX.Should().BeApproximately(-0.5f, 0.01f);
    }

    [Fact]
    public void Compute_FullFallbackScale_ReturnsRightEdgeScreenX()
    {
        var listener = new Vector2(0, 100);
        var target = new Vector2(960, 100); // Exactly at fallback scale = right edge

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.NormalizedScreenX.Should().Be(1f);
    }

    [Fact]
    public void Compute_ExceedsFallbackScale_ClampedToRightEdge()
    {
        var listener = new Vector2(0, 100);
        var target = new Vector2(2000, 100); // Beyond fallback scale

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.NormalizedScreenX.Should().Be(1f);
    }

    #endregion

    #region Viewport Mapping Tests

    [Fact]
    public void ComputeScreenNormalizedX_LeftCenterRight_MapToMinusOneZeroOne()
    {
        ResetViewport(width: 1920, height: 1080);

        SpatialAudioPositioner.ComputeScreenNormalizedX(0f).Should().Be(-1f);
        SpatialAudioPositioner.ComputeScreenNormalizedX(960f).Should().Be(0f);
        SpatialAudioPositioner.ComputeScreenNormalizedX(1920f).Should().Be(1f);
    }

    [Fact]
    public void ComputeScreenNormalizedX_WithNonFiniteInput_ReturnsCenter()
    {
        ResetViewport(width: 1920, height: 1080);

        SpatialAudioPositioner.ComputeScreenNormalizedX(float.NaN).Should().Be(0f);
    }

    [Fact]
    public void Compute_WithViewport_UsesVisibleScreenXInsteadOfListenerOffset()
    {
        ResetViewport(width: 1000, height: 800);
        Main.screenPosition = new Vector2(2000f, 0f);
        Main.GameViewMatrix.Zoom = Vector2.One;

        var listener = new Vector2(5000f, 100f);
        var leftEdge = SpatialAudioPositioner.Compute(listener, new Vector2(2000f, 100f));
        var center = SpatialAudioPositioner.Compute(listener, new Vector2(2500f, 100f));
        var rightEdge = SpatialAudioPositioner.Compute(listener, new Vector2(3000f, 100f));

        leftEdge.NormalizedScreenX.Should().Be(-1f);
        center.NormalizedScreenX.Should().Be(0f);
        rightEdge.NormalizedScreenX.Should().Be(1f);

        ResetViewport();
    }

    [Fact]
    public void Compute_WithViewportZoom_MapsVisibleRightEdgeToOne()
    {
        ResetViewport(width: 1000, height: 800);
        Main.screenPosition = new Vector2(2000f, 0f);
        Main.GameViewMatrix.Zoom = new Vector2(2f, 2f);

        var listener = new Vector2(2500f, 100f);
        var result = SpatialAudioPositioner.Compute(listener, new Vector2(2500f, 100f));

        result.NormalizedScreenX.Should().Be(1f);

        ResetViewport();
    }

    [Fact]
    public void Compute_WithViewportZoom_UsesZoomAdjustedVisibleWorldBounds()
    {
        ResetViewport(width: 1000, height: 800);
        Main.screenPosition = new Vector2(2000f, 0f);
        Main.GameViewMatrix.Zoom = new Vector2(2f, 2f);

        var listener = new Vector2(2250f, 100f);
        var leftEdge = SpatialAudioPositioner.Compute(listener, new Vector2(2000f, 100f));
        var center = SpatialAudioPositioner.Compute(listener, new Vector2(2250f, 100f));
        var rightEdge = SpatialAudioPositioner.Compute(listener, new Vector2(2500f, 100f));

        leftEdge.NormalizedScreenX.Should().Be(-1f);
        center.NormalizedScreenX.Should().Be(0f);
        rightEdge.NormalizedScreenX.Should().Be(1f);

        ResetViewport();
    }

    [Fact]
    public void Compute_WithInvalidViewportZoom_FallsBackToListenerOffset()
    {
        ResetViewport(width: 1000, height: 800);
        Main.screenPosition = new Vector2(2000f, 0f);
        Main.GameViewMatrix.Zoom = Vector2.Zero;

        var listener = new Vector2(100f, 100f);
        var result = SpatialAudioPositioner.Compute(listener, new Vector2(1060f, 100f));

        result.NormalizedScreenX.Should().Be(1f);
        result.Pitch.Should().Be(0f);

        ResetViewport();
    }

    [Fact]
    public void Compute_WithNonFiniteWorldPosition_ReturnsCenteredNeutralSilentSample()
    {
        ResetViewport(width: 1000, height: 800);

        var result = SpatialAudioPositioner.Compute(
            new Vector2(100f, 100f),
            new Vector2(float.NaN, float.NaN));

        result.NormalizedScreenX.Should().Be(0f);
        result.Pitch.Should().Be(0f);
        result.Volume.Should().Be(0f);

        ResetViewport();
    }

    #endregion

    #region Pitch Tests

    [Fact]
    public void Compute_SamePosition_ReturnsZeroPitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.Pitch.Should().Be(0f);
    }

    [Fact]
    public void Compute_TargetAbove_ReturnsPositivePitch()
    {
        var listener = new Vector2(100, 580);
        var target = new Vector2(100, 100); // 480 pixels above (negative Y offset)

        var result = SpatialAudioPositioner.Compute(listener, target);

        // Pitch = -offsetY / 960 = -(-480) / 960 = 0.5
        result.Pitch.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Compute_TargetBelow_ReturnsNegativePitch()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 580); // 480 pixels below (positive Y offset)

        var result = SpatialAudioPositioner.Compute(listener, target);

        // Pitch = -offsetY / 960 = -(480) / 960 = -0.5
        result.Pitch.Should().BeApproximately(-0.5f, 0.01f);
    }

    [Fact]
    public void Compute_VeryFarAbove_ClampedToMaxPitch()
    {
        var listener = new Vector2(100, 2000);
        var target = new Vector2(100, 0); // Very far above

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.Pitch.Should().Be(MaxPitchClamp);
    }

    [Fact]
    public void Compute_VeryFarBelow_ClampedToMinPitch()
    {
        var listener = new Vector2(100, 0);
        var target = new Vector2(100, 2000); // Very far below

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.Pitch.Should().Be(-MaxPitchClamp);
    }

    #endregion

    #region Volume Tests

    [Fact]
    public void Compute_ZeroDistance_ReturnsFullVolume()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(1f);
    }

    [Fact]
    public void Compute_MidFalloffDistance_UsesNonlinearVolumeFalloff()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(1250, 0); // Mid-range distance; nonlinear falloff applies.

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().BeApproximately(ExpectedVolumeAtDistance(1250f), 0.01f);
    }

    [Fact]
    public void Compute_AtFalloffDistance_ReturnsZeroVolume()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(2500, 0); // Exactly at falloff distance

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(0f);
    }

    [Fact]
    public void Compute_BeyondFalloffDistance_ClampedToZero()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(5000, 0); // Beyond falloff distance

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 1.0f);

        result.Volume.Should().Be(0f);
    }

    [Fact]
    public void Compute_BaseVolumeScalesResult()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(0, 0);

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 0.5f);

        result.Volume.Should().Be(0.5f);
    }

    [Fact]
    public void Compute_BaseVolumeAndDistanceCombined()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(1250, 0); // Mid-range distance; nonlinear falloff applies.

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 0.8f);

        result.Volume.Should().BeApproximately(ExpectedVolumeAtDistance(1250f) * 0.8f, 0.01f);
    }

    #endregion

    #region Distance Tests

    [Fact]
    public void Compute_CalculatesDistanceInTiles()
    {
        var listener = new Vector2(0, 0);
        var target = new Vector2(160, 0); // 160 pixels = 10 tiles (16 pixels per tile)

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.DistanceTiles.Should().BeApproximately(10f, 0.001f);
    }

    [Fact]
    public void Compute_DiagonalDistance_CalculatesPythagorean()
    {
        var listener = new Vector2(0, 0);
        // 48 pixels X, 64 pixels Y = 80 pixels distance (3-4-5 triangle scaled by 16)
        // 80 pixels / 16 = 5 tiles
        var target = new Vector2(48, 64);

        var result = SpatialAudioPositioner.Compute(listener, target);

        result.DistanceTiles.Should().BeApproximately(5f, 0.001f);
    }

    #endregion

    #region Combined Behavior Tests

    [Fact]
    public void Compute_DiagonalOffset_ReturnsCorrectScreenXPitchAndVolume()
    {
        var listener = new Vector2(0, 480);
        var target = new Vector2(480, 0); // Right and above

        var result = SpatialAudioPositioner.Compute(listener, target, baseVolume: 1.0f);

        // Normalized screen X: 480 / 960 = 0.5
        result.NormalizedScreenX.Should().BeApproximately(0.5f, 0.01f);

        // Pitch: -(-480) / 960 = 0.5
        result.Pitch.Should().BeApproximately(0.5f, 0.01f);

        // Distance: sqrt(480^2 + 480^2) ≈ 679 pixels
        result.Volume.Should().BeApproximately(ExpectedVolumeAtDistance(result.DistanceTiles * 16f), 0.02f);
    }

    [Fact]
    public void Compute_DefaultBaseVolume_IsOne()
    {
        var listener = new Vector2(100, 100);
        var target = new Vector2(100, 100);

        var result = SpatialAudioPositioner.Compute(listener, target);

        // Default baseVolume should be 1.0
        result.Volume.Should().Be(1f);
    }

    #endregion

    #region Interaural Time Delay Tests

    [Fact]
    public void ComputeInterauralParameters_Center_HasNoDelayAndEqualGain()
    {
        var result = SpatialAudioPositioner.ComputeInterauralParameters(0f, 48000);

        result.LeftDelaySamples.Should().Be(0);
        result.RightDelaySamples.Should().Be(0);
        result.LeftDelayFraction.Should().Be(0f);
        result.RightDelayFraction.Should().Be(0f);
        result.LeftGain.Should().Be(1f);
        result.RightGain.Should().Be(1f);
    }

    [Fact]
    public void ComputeInterauralParameters_LeftEdge_DelaysAndAttenuatesRightEarOnly()
    {
        var result = SpatialAudioPositioner.ComputeInterauralParameters(-1f, 48000);

        result.LeftDelaySamples.Should().Be(0);
        result.RightDelaySamples.Should().Be(38);
        result.LeftDelayFraction.Should().Be(0f);
        result.RightDelayFraction.Should().BeApproximately(0.4f, 0.001f);
        result.LeftGain.Should().Be(1f);
        result.RightGain.Should().BeApproximately(EdgeDelayedEarGain, 0.001f);
    }

    [Fact]
    public void ComputeInterauralParameters_RightEdge_DelaysAndAttenuatesLeftEarOnly()
    {
        var result = SpatialAudioPositioner.ComputeInterauralParameters(1f, 48000);

        result.LeftDelaySamples.Should().Be(38);
        result.RightDelaySamples.Should().Be(0);
        result.LeftDelayFraction.Should().BeApproximately(0.4f, 0.001f);
        result.RightDelayFraction.Should().Be(0f);
        result.LeftGain.Should().BeApproximately(EdgeDelayedEarGain, 0.001f);
        result.RightGain.Should().Be(1f);
    }

    [Fact]
    public void ComputeInterauralParameters_HalfRight_UsesHalfDelayAndMildDelayedEarAttenuation()
    {
        var result = SpatialAudioPositioner.ComputeInterauralParameters(0.5f, 48000);

        result.LeftDelaySamples.Should().Be(19);
        result.RightDelaySamples.Should().Be(0);
        result.LeftDelayFraction.Should().BeApproximately(0.2f, 0.001f);
        result.RightDelayFraction.Should().Be(0f);
        result.LeftGain.Should().BeApproximately(HalfRightDelayedEarGain, 0.001f);
        result.RightGain.Should().Be(1f);
    }

    [Fact]
    public void ComputeInterauralParameters_WithNonFiniteX_ReturnsCenteredParameters()
    {
        var result = SpatialAudioPositioner.ComputeInterauralParameters(float.NaN, 48000);

        result.LeftDelaySamples.Should().Be(0);
        result.RightDelaySamples.Should().Be(0);
        result.LeftDelayFraction.Should().Be(0f);
        result.RightDelayFraction.Should().Be(0f);
        result.LeftGain.Should().Be(1f);
        result.RightGain.Should().Be(1f);
    }

    #endregion

    #region Normalized X Quantization Tests

    [Fact]
    public void QuantizeNormalizedScreenX_DefaultResolution_UsesFineSteps()
    {
        int key = SpatialAudioPositioner.QuantizeNormalizedScreenX(0.5f);
        float dequantized = SpatialAudioPositioner.DequantizeNormalizedScreenX(key);

        key.Should().Be(128);
        dequantized.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void QuantizeNormalizedScreenX_DefaultResolution_PreservesSmallPositionChanges()
    {
        int leftKey = SpatialAudioPositioner.QuantizeNormalizedScreenX(0.10f);
        int rightKey = SpatialAudioPositioner.QuantizeNormalizedScreenX(0.11f);

        rightKey.Should().BeGreaterThan(leftKey);
    }

    [Fact]
    public void QuantizeNormalizedScreenX_WithNonFiniteInput_ReturnsCenterKey()
    {
        int key = SpatialAudioPositioner.QuantizeNormalizedScreenX(float.NaN);
        float dequantized = SpatialAudioPositioner.DequantizeNormalizedScreenX(key);

        key.Should().Be(0);
        dequantized.Should().Be(0f);
    }

    #endregion

    #region Normalized Pitch Tests

    [Theory]
    [InlineData(0f, 0.65f)]
    [InlineData(0.5f, 0f)]
    [InlineData(1f, -0.65f)]
    [InlineData(-1f, 0.65f)]
    [InlineData(2f, -0.65f)]
    public void ComputeNormalizedScreenPitch_MapsTopCenterBottomWithClamping(float normalizedY, float expectedPitch)
    {
        float result = SpatialAudioPositioner.ComputeNormalizedScreenPitch(normalizedY);

        result.Should().BeApproximately(expectedPitch, 0.0001f);
    }

    [Fact]
    public void ComputeNormalizedScreenPitch_WithNonFiniteInput_ReturnsNeutralPitch()
    {
        float result = SpatialAudioPositioner.ComputeNormalizedScreenPitch(float.NaN);

        result.Should().Be(0f);
    }

    #endregion

    #region SpatialAudioSample Record Tests

    [Fact]
    public void SpatialAudioSample_RecordEquality_WorksCorrectly()
    {
        var a = new SpatialAudioPositioner.SpatialAudioSample(0.3f, 0.5f, 0.8f, 10f);
        var b = new SpatialAudioPositioner.SpatialAudioSample(0.3f, 0.5f, 0.8f, 10f);
        var c = new SpatialAudioPositioner.SpatialAudioSample(0.4f, 0.5f, 0.8f, 10f);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion

    private static float ExpectedVolumeAtDistance(float distancePixels)
    {
        if (distancePixels <= FullVolumeRadiusPixels)
        {
            return 1f;
        }

        float range = Math.Max(1f, VolumeFalloffPixels - FullVolumeRadiusPixels);
        float normalized = Math.Clamp((distancePixels - FullVolumeRadiusPixels) / range, 0f, 1f);
        return Math.Clamp(1f - MathF.Pow(normalized, 1.25f), 0f, 1f);
    }

    private static void ResetViewport(int width = 0, int height = 0)
    {
        Main.screenWidth = width;
        Main.screenHeight = height;
        Main.screenPosition = Vector2.Zero;
        Main.GameViewMatrix = new Terraria.GameViewMatrix();
    }
}
