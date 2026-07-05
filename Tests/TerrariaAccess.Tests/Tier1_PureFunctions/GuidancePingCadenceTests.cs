#nullable enable

using TerrariaAccess.Common.Systems.Guidance;
using Xunit;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class GuidancePingCadenceTests
{
    [Fact]
    public void ComputeDistanceDelayFrames_AtArrivalThreshold_ReturnsDisabled()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: 4f,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(-1);
    }

    [Fact]
    public void ComputeDistanceDelayFrames_JustOutsideArrival_UsesFastDelay()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: 4.01f,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(10);
    }

    [Fact]
    public void ComputeDistanceDelayFrames_AtMaxDistance_UsesSlowDelay()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: 80f,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(54);
    }

    [Fact]
    public void ComputeDistanceDelayFrames_BeyondMaxDistance_ClampsToSlowDelay()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: 300f,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(54);
    }

    [Fact]
    public void ComputeDistanceDelayFrames_MidRange_InterpolatesDelay()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: 42f,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(32);
    }

    [Fact]
    public void ComputeDistanceDelayFrames_NonFiniteDistance_UsesSlowDelay()
    {
        GuidancePingCadence.ComputeDistanceDelayFrames(
                distanceTiles: float.NaN,
                arrivalThresholdTiles: 4f,
                minDelayFrames: 10,
                maxDelayFrames: 54,
                maxDistanceTiles: 80f)
            .Should()
            .Be(54);
    }

    [Fact]
    public void ComputeDistanceVolumeScale_AtArrivalThreshold_UsesFullVolume()
    {
        GuidancePingCadence.ComputeDistanceVolumeScale(
                distanceTiles: 4f,
                arrivalThresholdTiles: 4f,
                maxDistanceTiles: 52f,
                minVolumeScale: 0.35f)
            .Should()
            .Be(1f);
    }

    [Fact]
    public void ComputeDistanceVolumeScale_AtMaxDistance_UsesMinimumVolume()
    {
        GuidancePingCadence.ComputeDistanceVolumeScale(
                distanceTiles: 52f,
                arrivalThresholdTiles: 4f,
                maxDistanceTiles: 52f,
                minVolumeScale: 0.35f)
            .Should()
            .BeApproximately(0.35f, 0.001f);
    }

    [Fact]
    public void ComputeDistanceVolumeScale_MidRange_InterpolatesVolume()
    {
        GuidancePingCadence.ComputeDistanceVolumeScale(
                distanceTiles: 28f,
                arrivalThresholdTiles: 4f,
                maxDistanceTiles: 52f,
                minVolumeScale: 0.35f)
            .Should()
            .BeApproximately(0.675f, 0.001f);
    }

    [Fact]
    public void ComputeDistanceVolumeScale_NonFiniteDistance_UsesMinimumVolume()
    {
        GuidancePingCadence.ComputeDistanceVolumeScale(
                distanceTiles: float.NaN,
                arrivalThresholdTiles: 4f,
                maxDistanceTiles: 52f,
                minVolumeScale: 0.35f)
            .Should()
            .BeApproximately(0.35f, 0.001f);
    }
}
