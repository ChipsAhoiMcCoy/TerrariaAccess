#nullable enable
using TerrariaAccess.Common.Systems.GamepadEmulation;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class GamepadEmulationStateTests : IDisposable
{
    public GamepadEmulationStateTests()
    {
        // Reset state before each test
        GamepadEmulationState.SetEnabledSilent(false);
    }

    public void Dispose()
    {
        // Clean up after each test
        GamepadEmulationState.SetEnabledSilent(false);
    }

    #region Toggle Tests

    [Fact]
    public void Toggle_WhenDisabled_BecomesEnabled()
    {
        GamepadEmulationState.SetEnabledSilent(false);

        GamepadEmulationState.Toggle();

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Toggle_WhenEnabled_BecomesDisabled()
    {
        GamepadEmulationState.SetEnabledSilent(true);

        GamepadEmulationState.Toggle();

        GamepadEmulationState.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Toggle_FiresStateChangedEvent()
    {
        bool eventFired = false;
        bool? receivedValue = null;
        GamepadEmulationState.StateChanged += OnStateChanged;

        try
        {
            GamepadEmulationState.Toggle();

            eventFired.Should().BeTrue();
            receivedValue.Should().BeTrue();
        }
        finally
        {
            GamepadEmulationState.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(bool enabled)
        {
            eventFired = true;
            receivedValue = enabled;
        }
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public void SetEnabled_True_SetsEnabledToTrue()
    {
        GamepadEmulationState.SetEnabledSilent(false);

        GamepadEmulationState.SetEnabled(true);

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Fact]
    public void SetEnabled_False_SetsEnabledToFalse()
    {
        GamepadEmulationState.SetEnabledSilent(true);

        GamepadEmulationState.SetEnabled(false);

        GamepadEmulationState.Enabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_SameValue_DoesNotFireEvent()
    {
        GamepadEmulationState.SetEnabledSilent(true);
        bool eventFired = false;
        GamepadEmulationState.StateChanged += OnStateChanged;

        try
        {
            GamepadEmulationState.SetEnabled(true); // Same value

            eventFired.Should().BeFalse();
        }
        finally
        {
            GamepadEmulationState.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(bool _) => eventFired = true;
    }

    [Fact]
    public void SetEnabled_DifferentValue_FiresEvent()
    {
        GamepadEmulationState.SetEnabledSilent(false);
        bool eventFired = false;
        bool? receivedValue = null;
        GamepadEmulationState.StateChanged += OnStateChanged;

        try
        {
            GamepadEmulationState.SetEnabled(true);

            eventFired.Should().BeTrue();
            receivedValue.Should().BeTrue();
        }
        finally
        {
            GamepadEmulationState.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(bool enabled)
        {
            eventFired = true;
            receivedValue = enabled;
        }
    }

    #endregion

    #region SetEnabledSilent Tests

    [Fact]
    public void SetEnabledSilent_True_SetsEnabledToTrue()
    {
        GamepadEmulationState.SetEnabledSilent(false);

        GamepadEmulationState.SetEnabledSilent(true);

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    [Fact]
    public void SetEnabledSilent_DoesNotFireEvent()
    {
        GamepadEmulationState.SetEnabledSilent(false);
        bool eventFired = false;
        GamepadEmulationState.StateChanged += OnStateChanged;

        try
        {
            GamepadEmulationState.SetEnabledSilent(true);

            eventFired.Should().BeFalse();
        }
        finally
        {
            GamepadEmulationState.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(bool _) => eventFired = true;
    }

    [Fact]
    public void SetEnabledSilent_CanSetSameValue()
    {
        GamepadEmulationState.SetEnabledSilent(true);

        // Should not throw
        GamepadEmulationState.SetEnabledSilent(true);

        GamepadEmulationState.Enabled.Should().BeTrue();
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public void StateChanged_MultipleHandlers_AllCalled()
    {
        int callCount = 0;
        void Handler1(bool _) => callCount++;
        void Handler2(bool _) => callCount++;

        GamepadEmulationState.StateChanged += Handler1;
        GamepadEmulationState.StateChanged += Handler2;

        try
        {
            GamepadEmulationState.Toggle();

            callCount.Should().Be(2);
        }
        finally
        {
            GamepadEmulationState.StateChanged -= Handler1;
            GamepadEmulationState.StateChanged -= Handler2;
        }
    }

    [Fact]
    public void StateChanged_RemovedHandler_NotCalled()
    {
        int callCount = 0;
        void Handler(bool _) => callCount++;

        GamepadEmulationState.StateChanged += Handler;
        GamepadEmulationState.StateChanged -= Handler;

        GamepadEmulationState.Toggle();

        callCount.Should().Be(0);
    }

    #endregion
}
