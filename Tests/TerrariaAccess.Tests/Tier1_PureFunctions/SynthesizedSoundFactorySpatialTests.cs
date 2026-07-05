#nullable enable
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class SynthesizedSoundFactorySpatialTests
{
    private const float EdgeDelayedEarGain = 0.3f;

    [Fact]
    public void CreateSpatialPcm16_Center_WritesSameSamplesToBothEars()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.25f, -0.25f },
            sampleRate: 1000,
            normalizedScreenX: 0f);

        ReadFrame(buffer, 0).Should().Be((Quantize(0.25f), Quantize(0.25f)));
        ReadFrame(buffer, 1).Should().Be((Quantize(-0.25f), Quantize(-0.25f)));
    }

    [Fact]
    public void CreateSpatialPcm16_RightEdge_DelaysAndAttenuatesLeftEarOnly()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.5f, 0f },
            sampleRate: 1250,
            normalizedScreenX: 1f);

        buffer.Length.Should().Be(3 * sizeof(short) * 2);
        ReadFrame(buffer, 0).Should().Be(((short)0, Quantize(0.5f)));
        ReadFrame(buffer, 1).Should().Be((Quantize(0.5f * EdgeDelayedEarGain), (short)0));
        ReadFrame(buffer, 2).Should().Be(((short)0, (short)0));
    }

    [Fact]
    public void CreateSpatialPcm16_LeftEdge_DelaysAndAttenuatesRightEarOnly()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.5f, 0f },
            sampleRate: 1250,
            normalizedScreenX: -1f);

        buffer.Length.Should().Be(3 * sizeof(short) * 2);
        ReadFrame(buffer, 0).Should().Be((Quantize(0.5f), (short)0));
        ReadFrame(buffer, 1).Should().Be(((short)0, Quantize(0.5f * EdgeDelayedEarGain)));
        ReadFrame(buffer, 2).Should().Be(((short)0, (short)0));
    }

    [Fact]
    public void CreateSpatialPcm16_LoopedRightEdge_WrapsDelayedEar()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.25f, 0.5f },
            sampleRate: 1250,
            normalizedScreenX: 1f,
            wrapDelay: true);

        buffer.Length.Should().Be(2 * sizeof(short) * 2);
        ReadFrame(buffer, 0).Should().Be((Quantize(0.5f * EdgeDelayedEarGain), Quantize(0.25f)));
        ReadFrame(buffer, 1).Should().Be((Quantize(0.25f * EdgeDelayedEarGain), Quantize(0.5f)));
    }

    [Fact]
    public void CreateSpatialPcm16_FractionalRightEdge_DelaysLeftEarWithInterpolation()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.5f, 0f },
            sampleRate: 1000,
            normalizedScreenX: 1f);

        buffer.Length.Should().Be(3 * sizeof(short) * 2);
        ReadFrame(buffer, 0).Should().Be((Quantize(0.1f * EdgeDelayedEarGain), Quantize(0.5f)));
        ReadFrame(buffer, 1).Should().Be((Quantize(0.4f * EdgeDelayedEarGain), (short)0));
        ReadFrame(buffer, 2).Should().Be(((short)0, (short)0));
    }

    [Fact]
    public void CreateSpatialPcm16_InvalidSourceSamples_WritesSilenceForInvalidFrames()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { float.NaN, float.PositiveInfinity },
            sampleRate: 1000,
            normalizedScreenX: 0f);

        ReadFrame(buffer, 0).Should().Be(((short)0, (short)0));
        ReadFrame(buffer, 1).Should().Be(((short)0, (short)0));
    }

    [Fact]
    public void CreateSpatialPcm16_ExtremeFiniteSourceSamples_SaturatesFrames()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { float.MaxValue, -float.MaxValue },
            sampleRate: 1000,
            normalizedScreenX: 0f);

        ReadFrame(buffer, 0).Should().Be((short.MaxValue, short.MaxValue));
        ReadFrame(buffer, 1).Should().Be((short.MinValue, short.MinValue));
    }

    [Fact]
    public void CreateSpatialPcm16_WithHugeSampleRate_CapsDelayGrowth()
    {
        byte[] buffer = SynthesizedSoundFactory.CreateSpatialPcm16(
            new[] { 0.5f },
            sampleRate: int.MaxValue,
            normalizedScreenX: 1f);

        buffer.Length.Should().Be(155 * sizeof(short) * 2);
    }

    [Theory]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(-1f, 1)]
    [InlineData(10f, 5000)]
    public void ComputeGeneratedSampleCount_SanitizesDuration(float durationSeconds, int expectedSampleCount)
    {
        SynthesizedSoundFactory.ComputeGeneratedSampleCount(1000, durationSeconds)
            .Should()
            .Be(expectedSampleCount);
    }

    [Fact]
    public void ComputeGeneratedSampleCount_WithHugeSampleRate_CapsSampleRate()
    {
        SynthesizedSoundFactory.ComputeGeneratedSampleCount(int.MaxValue, 1f)
            .Should()
            .Be(192000);
    }

    [Theory]
    [InlineData(440f, 440f)]
    [InlineData(0f, 0f)]
    [InlineData(-440f, 0f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(50000f, 20000f)]
    public void SanitizeFrequency_AllowsPositiveFiniteOnly(float frequency, float expected)
    {
        SynthesizedSoundFactory.SanitizeFrequency(frequency).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(-0.5f, -0.5f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    public void SanitizeGain_SilencesNonFiniteValues(float gain, float expected)
    {
        SynthesizedSoundFactory.SanitizeGain(gain).Should().Be(expected);
    }

    [Fact]
    public void SanitizePartialMultipliers_RemovesNonFiniteAndNonPositiveValues()
    {
        float[] result = SynthesizedSoundFactory.SanitizePartialMultipliers(
            new[] { 2f, 0f, -1f, float.NaN, float.PositiveInfinity, 3.5f, 100f });

        result.Should().Equal(2f, 3.5f, 64f);
    }

    private static (short Left, short Right) ReadFrame(byte[] buffer, int frameIndex)
    {
        int index = frameIndex * sizeof(short) * 2;
        return (ReadInt16(buffer, index), ReadInt16(buffer, index + sizeof(short)));
    }

    private static short ReadInt16(byte[] buffer, int index) =>
        (short)(buffer[index] | (buffer[index + 1] << 8));

    private static short Quantize(float sample) =>
        (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
}
