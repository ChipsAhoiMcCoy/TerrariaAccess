#nullable enable

using TerrariaAccess.Common.Systems.GamepadEmulation;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class GamepadEmulationStateTests
{
    [Fact]
    public void Enabled_IsAlwaysTrue()
    {
        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Fact]
    public void LegacyToggle_DoesNotDisableEmulation()
    {
        GamepadEmulationState.Toggle();

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacySetters_DoNotChangeAlwaysOnState(bool requestedEnabled)
    {
        GamepadEmulationState.SetEnabled(requestedEnabled);
        GamepadEmulationState.SetEnabledSilent(requestedEnabled);

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Fact]
    public void LegacyStateChangedEvent_IsNeverRaised()
    {
        bool eventRaised = false;
        void Handler(bool _) => eventRaised = true;

        GamepadEmulationState.StateChanged += Handler;
        try
        {
            GamepadEmulationState.Toggle();
            GamepadEmulationState.SetEnabled(false);
            GamepadEmulationState.SetEnabled(true);
        }
        finally
        {
            GamepadEmulationState.StateChanged -= Handler;
        }

        eventRaised.Should().BeFalse();
    }
}
