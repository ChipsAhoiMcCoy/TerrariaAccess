#nullable enable

using FluentAssertions;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using TerrariaAccess.Common.Services;
using Xunit;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class SpatializedSoundEngineTests
{
    [Theory]
    [InlineData(0.8f, 0.5f, 0.4f)]
    [InlineData(0.8f, 2f, 1f)]
    [InlineData(0.8f, -1f, 0f)]
    public void SpatialAudioSample_ScaleVolume_ClampsFiniteScaledVolume(float sampleVolume, float localVolume, float expected)
    {
        var sample = new SpatializedSoundEngine.SpatialAudioSample(0f, 0f, sampleVolume, 0f);

        sample.ScaleVolume(localVolume).Should().BeApproximately(expected, 0.0001f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void SpatialAudioSample_ScaleVolume_WithNonFiniteLocalVolume_ReturnsSilence(float localVolume)
    {
        var sample = new SpatializedSoundEngine.SpatialAudioSample(0f, 0f, 0.8f, 0f);

        sample.ScaleVolume(localVolume).Should().Be(0f);
        sample.IsAudible(localVolume).Should().BeFalse();
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void SpatialAudioSample_ScaleVolume_WithNonFiniteSampleVolume_ReturnsSilence(float sampleVolume)
    {
        var sample = new SpatializedSoundEngine.SpatialAudioSample(0f, 0f, sampleVolume, 0f);

        sample.ScaleVolume(1f).Should().Be(0f);
        sample.IsAudible(1f).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.4f, 0.4f)]
    [InlineData(2f, 1f)]
    [InlineData(-1f, 0f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void NormalizeVolume_ClampsFiniteAndSilencesNonFiniteValues(float volume, float expected)
    {
        SpatializedSoundEngine.NormalizeVolume(volume).Should().Be(expected);
    }

    [Fact]
    public void CanPlay_WithNonFiniteLocalOrMasterVolume_ReturnsFalse()
    {
        float originalSoundVolume = Main.soundVolume;
        bool originalDedServ = Main.dedServ;
        try
        {
            Main.dedServ = false;
            Main.soundVolume = 1f;

            SpatializedSoundEngine.CanPlay(float.NaN).Should().BeFalse();

            Main.soundVolume = float.NaN;
            SpatializedSoundEngine.CanPlay(1f).Should().BeFalse();
        }
        finally
        {
            Main.soundVolume = originalSoundVolume;
            Main.dedServ = originalDedServ;
        }
    }

    [Fact]
    public void CleanupStopped_WithNullList_DoesNotThrow()
    {
        Action act = () => SpatializedSoundEngine.CleanupStopped(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void StopAndDisposeAll_WithNullList_DoesNotThrow()
    {
        Action act = () => SpatializedSoundEngine.StopAndDisposeAll(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void CleanupStopped_RemovesNullEntries()
    {
        var instances = new List<SoundEffectInstance> { null! };

        SpatializedSoundEngine.CleanupStopped(instances);

        instances.Should().BeEmpty();
    }

    [Fact]
    public void PlayAlreadySpatializedWorldCue_WithNullSound_ReturnsNull()
    {
        SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(null, 1f)
            .Should()
            .BeNull();
    }

    [Fact]
    public void PlayAlreadySpatializedInterfaceCue_WithNullSound_ReturnsNull()
    {
        SpatializedSoundEngine.PlayAlreadySpatializedInterfaceCue(null, 1f)
            .Should()
            .BeNull();
    }

    [Fact]
    public void PlayWorldCue_WithNullSound_ReturnsNull()
    {
        var sample = new SpatializedSoundEngine.SpatialAudioSample(0f, 0f, 1f, 0f);

        SpatializedSoundEngine.PlayWorldCue(null, sample, 1f)
            .Should()
            .BeNull();
    }
}
