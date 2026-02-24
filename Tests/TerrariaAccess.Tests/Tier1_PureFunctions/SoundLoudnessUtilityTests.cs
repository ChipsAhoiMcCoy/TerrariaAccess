#nullable enable
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class SoundLoudnessUtilityTests
{
    #region ApplyDistanceFalloff Tests

    [Fact]
    public void ApplyDistanceFalloff_ZeroBaseVolume_ReturnsZero()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 0f,
            distanceTiles: 10f,
            referenceTiles: 40f);

        result.Should().Be(0f);
    }

    [Fact]
    public void ApplyDistanceFalloff_NegativeBaseVolume_ReturnsZero()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: -0.5f,
            distanceTiles: 10f,
            referenceTiles: 40f);

        result.Should().Be(0f);
    }

    [Fact]
    public void ApplyDistanceFalloff_ZeroDistance_ReturnsFullVolume()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 1.0f,
            distanceTiles: 0f,
            referenceTiles: 40f);

        result.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void ApplyDistanceFalloff_AtReferenceDistance_ReturnsMinFactor()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 1.0f,
            distanceTiles: 40f, // At reference distance
            referenceTiles: 40f,
            minFactor: 0.3f);

        // At reference distance, normalized = 1, shaped = 0, so result = minFactor
        result.Should().BeApproximately(0.3f, 0.01f);
    }

    [Fact]
    public void ApplyDistanceFalloff_BeyondReferenceDistance_ClampedToMinFactor()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 1.0f,
            distanceTiles: 100f, // Beyond reference
            referenceTiles: 40f,
            minFactor: 0.3f);

        result.Should().BeApproximately(0.3f, 0.01f);
    }

    [Fact]
    public void ApplyDistanceFalloff_HalfDistance_ReturnsIntermediateValue()
    {
        var result = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 1.0f,
            distanceTiles: 20f, // Half of reference
            referenceTiles: 40f,
            minFactor: 0.3f,
            exponent: 1.0f); // Linear for predictable calculation

        // normalized = 0.5, shaped = (1-0.5)^1 = 0.5
        // result = Lerp(0.3, 1.0, 0.5) = 0.65
        result.Should().BeApproximately(0.65f, 0.01f);
    }

    [Fact]
    public void ApplyDistanceFalloff_ScalesByBaseVolume()
    {
        var fullVolume = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 1.0f,
            distanceTiles: 20f,
            referenceTiles: 40f);

        var halfVolume = SoundLoudnessUtility.ApplyDistanceFalloff(
            baseVolume: 0.5f,
            distanceTiles: 20f,
            referenceTiles: 40f);

        halfVolume.Should().BeApproximately(fullVolume * 0.5f, 0.01f);
    }

    #endregion

    #region ComputeAttenuation Tests

    [Fact]
    public void ComputeAttenuation_ZeroReferenceDistance_ReturnsOne()
    {
        var result = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 10f,
            referenceTiles: 0f);

        result.Should().Be(1f);
    }

    [Fact]
    public void ComputeAttenuation_ZeroDistance_ReturnsOne()
    {
        var result = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 0f,
            referenceTiles: 40f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void ComputeAttenuation_HigherExponent_SteeperFalloff()
    {
        float lowExponent = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 20f,
            referenceTiles: 40f,
            minFactor: 0.3f,
            exponent: 1.0f);

        float highExponent = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 20f,
            referenceTiles: 40f,
            minFactor: 0.3f,
            exponent: 2.0f);

        // Higher exponent = steeper falloff = lower value at same distance
        highExponent.Should().BeLessThan(lowExponent);
    }

    [Fact]
    public void ComputeAttenuation_MinFactorClamped_BetweenZeroAndOne()
    {
        var resultNegative = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 40f,
            referenceTiles: 40f,
            minFactor: -0.5f);

        var resultOverOne = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: 40f,
            referenceTiles: 40f,
            minFactor: 1.5f);

        resultNegative.Should().Be(0f); // minFactor clamped to 0
        resultOverOne.Should().Be(1f); // minFactor clamped to 1
    }

    [Theory]
    [InlineData(0f, 40f, 1.0f)]     // Zero distance = full
    [InlineData(40f, 40f, 0.3f)]   // Reference distance = min
    [InlineData(80f, 40f, 0.3f)]   // Beyond reference = min (clamped)
    public void ComputeAttenuation_VariousDistances_ReturnsExpected(
        float distance, float reference, float expected)
    {
        var result = SoundLoudnessUtility.ComputeAttenuation(
            distanceTiles: distance,
            referenceTiles: reference,
            minFactor: 0.3f,
            exponent: 1.0f);

        result.Should().BeApproximately(expected, 0.01f);
    }

    #endregion
}
