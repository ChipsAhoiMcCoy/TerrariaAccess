#nullable enable

using FluentAssertions;
using Terraria;
using TerrariaAccess.Common.Services;
using Xunit;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class NativeSoundSuppressionTests
{
    public NativeSoundSuppressionTests()
    {
        Main.GameUpdateCount = 0;
        Main.soundVolume = 1f;
        NativeSoundSuppression.ResetState();
    }

    [Fact]
    public void ShouldSuppressItemSlotClick_AfterResetAtFrameZero_ReturnsFalse()
    {
        NativeSoundSuppression.ShouldSuppressItemSlotClick().Should().BeFalse();
    }

    [Fact]
    public void ShouldSuppressItemSlotClick_AfterRequestInSameFrame_ReturnsTrue()
    {
        NativeSoundSuppression.RequestItemSlotClickSuppression();

        NativeSoundSuppression.ShouldSuppressItemSlotClick().Should().BeTrue();
    }

    [Fact]
    public void ShouldSuppressItemSlotClick_AfterFrameAdvances_ReturnsFalse()
    {
        NativeSoundSuppression.RequestItemSlotClickSuppression();

        Main.GameUpdateCount++;

        NativeSoundSuppression.ShouldSuppressItemSlotClick().Should().BeFalse();
    }

    [Fact]
    public void RunSynchronous_RestoresSoundVolumeAfterAction()
    {
        Main.soundVolume = 0.7f;
        float volumeDuringAction = -1f;

        NativeSoundSuppression.RunSynchronous(() => volumeDuringAction = Main.soundVolume);

        volumeDuringAction.Should().Be(0f);
        Main.soundVolume.Should().Be(0.7f);
    }

    [Fact]
    public void RequestDeferredSuppressionForCurrentFrame_RestoresWhenFrameAdvances()
    {
        Main.soundVolume = 0.6f;

        NativeSoundSuppression.RequestDeferredSuppressionForCurrentFrame();

        Main.soundVolume.Should().Be(0f);
        NativeSoundSuppression.GetEffectiveSoundVolume().Should().Be(0.6f);

        Main.GameUpdateCount++;

        NativeSoundSuppression.GetEffectiveSoundVolume().Should().Be(0.6f);
        Main.soundVolume.Should().Be(0.6f);
    }

    [Fact]
    public void ExpiredDeferredSuppression_DuringSynchronousSuppression_KeepsNativeVolumeMutedUntilSynchronousEnd()
    {
        Main.soundVolume = 0.8f;
        NativeSoundSuppression.RequestDeferredSuppressionForCurrentFrame();
        float previousVolume = NativeSoundSuppression.BeginSynchronousSuppression();

        Main.GameUpdateCount++;

        NativeSoundSuppression.GetEffectiveSoundVolume().Should().Be(0.8f);
        Main.soundVolume.Should().Be(0f);

        NativeSoundSuppression.EndSynchronousSuppression(previousVolume);

        Main.soundVolume.Should().Be(0.8f);
    }

    [Fact]
    public void DeferredSuppression_RequestedDuringSynchronousSuppression_RestoresAfterSynchronousEndAndDeferredRestore()
    {
        Main.soundVolume = 0.75f;
        float previousVolume = NativeSoundSuppression.BeginSynchronousSuppression();

        NativeSoundSuppression.RequestDeferredSuppressionForCurrentFrame();
        NativeSoundSuppression.EndSynchronousSuppression(previousVolume);

        Main.soundVolume.Should().Be(0f);

        NativeSoundSuppression.RestoreDeferredSuppression();

        Main.soundVolume.Should().Be(0.75f);
    }
}
