#nullable enable
using ScreenReaderMod.Common.Services;

namespace ScreenReaderMod.Tests.Tier1_PureFunctions;

public class ToneEnvelopeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ClampsAttackFractionToValidRange()
    {
        var envelope = new ToneEnvelope(attackFraction: 1.5f, releaseFraction: 0.5f, applyHannWindow: false);

        envelope.AttackFraction.Should().Be(1f);
    }

    [Fact]
    public void Constructor_ClampsNegativeAttackFractionToZero()
    {
        var envelope = new ToneEnvelope(attackFraction: -0.5f, releaseFraction: 0.5f, applyHannWindow: false);

        envelope.AttackFraction.Should().Be(0f);
    }

    [Fact]
    public void Constructor_ClampsReleaseFractionToValidRange()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 2f, applyHannWindow: false);

        envelope.ReleaseFraction.Should().Be(1f);
    }

    [Fact]
    public void Constructor_ClampsNegativeReleaseFractionToZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: -0.5f, applyHannWindow: false);

        envelope.ReleaseFraction.Should().Be(0f);
    }

    [Fact]
    public void Constructor_PreservesApplyHannWindowTrue()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.1f, releaseFraction: 0.3f, applyHannWindow: true);

        envelope.ApplyHannWindow.Should().BeTrue();
    }

    [Fact]
    public void Constructor_PreservesApplyHannWindowFalse()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.1f, releaseFraction: 0.3f, applyHannWindow: false);

        envelope.ApplyHannWindow.Should().BeFalse();
    }

    #endregion

    #region Evaluate Tests - Basic

    [Fact]
    public void Evaluate_AtZero_WithNoAttackOrRelease_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(0f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_AtMiddle_WithNoAttackOrRelease_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(0.5f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_AtOne_WithNoAttackOrRelease_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(1f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_ClampsNegativeIndexToZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(-1f);

        // At index 0, attack progress is 0, so envelope is 0
        result.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Evaluate_ClampsIndexAboveOneToOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0.5f, applyHannWindow: false);

        var result = envelope.Evaluate(2f);

        // At index 1 (clamped from 2), release progress is 0, so envelope is 0
        result.Should().BeApproximately(0f, 0.001f);
    }

    #endregion

    #region Evaluate Tests - Attack Phase

    [Fact]
    public void Evaluate_DuringAttack_RampsUpLinearly()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0f, applyHannWindow: false);

        // At quarter point (half of attack phase)
        var result = envelope.Evaluate(0.25f);

        // Attack progress = 0.25 / 0.5 = 0.5
        result.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Evaluate_AtEndOfAttack_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(0.5f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_AfterAttack_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.3f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(0.6f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_AtStartWithAttack_ReturnsZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0f, applyHannWindow: false);

        var result = envelope.Evaluate(0f);

        result.Should().BeApproximately(0f, 0.001f);
    }

    #endregion

    #region Evaluate Tests - Release Phase

    [Fact]
    public void Evaluate_DuringRelease_RampsDownLinearly()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0.5f, applyHannWindow: false);

        // At 0.75, we're halfway through release (release starts at 0.5)
        var result = envelope.Evaluate(0.75f);

        // Release progress = (1 - 0.75) / 0.5 = 0.5
        result.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Evaluate_AtStartOfRelease_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0.5f, applyHannWindow: false);

        var result = envelope.Evaluate(0.5f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_AtEnd_WithRelease_ReturnsZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0.5f, applyHannWindow: false);

        var result = envelope.Evaluate(1f);

        result.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Evaluate_BeforeRelease_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0.3f, applyHannWindow: false);

        var result = envelope.Evaluate(0.5f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    #endregion

    #region Evaluate Tests - Combined Attack and Release

    [Fact]
    public void Evaluate_WithAttackAndRelease_AttackPhaseWorks()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.2f, releaseFraction: 0.3f, applyHannWindow: false);

        // At 0.1 (half of attack phase)
        var result = envelope.Evaluate(0.1f);

        result.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Evaluate_WithAttackAndRelease_SustainPhaseWorks()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.2f, releaseFraction: 0.3f, applyHannWindow: false);

        // At 0.5 (after attack at 0.2, before release at 0.7)
        var result = envelope.Evaluate(0.5f);

        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_WithAttackAndRelease_ReleasePhaseWorks()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.2f, releaseFraction: 0.3f, applyHannWindow: false);

        // At 0.85 (halfway through release from 0.7 to 1.0)
        var result = envelope.Evaluate(0.85f);

        // Release progress = (1 - 0.85) / 0.3 = 0.5
        result.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Evaluate_FullAttackAndRelease_BothRampCorrectly()
    {
        // Attack for 50%, release for 50% - no sustain
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0.5f, applyHannWindow: false);

        envelope.Evaluate(0f).Should().BeApproximately(0f, 0.001f);
        envelope.Evaluate(0.25f).Should().BeApproximately(0.5f, 0.01f);
        envelope.Evaluate(0.5f).Should().BeApproximately(1f, 0.001f);
        envelope.Evaluate(0.75f).Should().BeApproximately(0.5f, 0.01f);
        envelope.Evaluate(1f).Should().BeApproximately(0f, 0.001f);
    }

    #endregion

    #region Evaluate Tests - Hann Window

    [Fact]
    public void Evaluate_WithHannWindow_AtZero_ReturnsZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: true);

        var result = envelope.Evaluate(0f);

        // Hann window: 0.5 - 0.5 * cos(2*PI*0) = 0.5 - 0.5 * 1 = 0
        result.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Evaluate_WithHannWindow_AtMiddle_ReturnsOne()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: true);

        var result = envelope.Evaluate(0.5f);

        // Hann window: 0.5 - 0.5 * cos(2*PI*0.5) = 0.5 - 0.5 * (-1) = 1
        result.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Evaluate_WithHannWindow_AtEnd_ReturnsZero()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: true);

        var result = envelope.Evaluate(1f);

        // Hann window: 0.5 - 0.5 * cos(2*PI*1) = 0.5 - 0.5 * 1 = 0
        result.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Evaluate_WithHannWindow_AtQuarter_ReturnsHalf()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: true);

        var result = envelope.Evaluate(0.25f);

        // Hann window: 0.5 - 0.5 * cos(2*PI*0.25) = 0.5 - 0.5 * 0 = 0.5
        result.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void Evaluate_WithHannWindow_Symmetric()
    {
        var envelope = new ToneEnvelope(attackFraction: 0f, releaseFraction: 0f, applyHannWindow: true);

        var atQuarter = envelope.Evaluate(0.25f);
        var atThreeQuarters = envelope.Evaluate(0.75f);

        atQuarter.Should().BeApproximately(atThreeQuarters, 0.001f);
    }

    [Fact]
    public void Evaluate_WithHannWindowAndAttack_CombinesMultiplicatively()
    {
        var envelope = new ToneEnvelope(attackFraction: 0.5f, releaseFraction: 0f, applyHannWindow: true);

        // At 0.25: attack progress = 0.5, Hann = 0.5, combined = 0.25
        var result = envelope.Evaluate(0.25f);

        result.Should().BeApproximately(0.25f, 0.01f);
    }

    #endregion

    #region Predefined Envelope Tests

    [Fact]
    public void CursorPing_HasExpectedParameters()
    {
        var envelope = SynthesizedSoundFactory.ToneEnvelopes.CursorPing;

        envelope.AttackFraction.Should().BeApproximately(0.1f, 0.001f);
        envelope.ReleaseFraction.Should().BeApproximately(0.35f, 0.001f);
        envelope.ApplyHannWindow.Should().BeTrue();
    }

    [Fact]
    public void WaypointPulse_HasExpectedParameters()
    {
        var envelope = SynthesizedSoundFactory.ToneEnvelopes.WaypointPulse;

        envelope.AttackFraction.Should().BeApproximately(0.3f, 0.001f);
        envelope.ReleaseFraction.Should().BeApproximately(1f, 0.001f);
        envelope.ApplyHannWindow.Should().BeTrue();
    }

    [Fact]
    public void WorldCue_HasExpectedParameters()
    {
        var envelope = SynthesizedSoundFactory.ToneEnvelopes.WorldCue;

        envelope.AttackFraction.Should().BeApproximately(0.18f, 0.001f);
        envelope.ReleaseFraction.Should().BeApproximately(0.4f, 0.001f);
        envelope.ApplyHannWindow.Should().BeTrue();
    }

    #endregion

    #region Struct Equality Tests

    [Fact]
    public void ToneEnvelope_StructEquality_WorksCorrectly()
    {
        var a = new ToneEnvelope(0.1f, 0.3f, true);
        var b = new ToneEnvelope(0.1f, 0.3f, true);
        var c = new ToneEnvelope(0.1f, 0.3f, false);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion
}
