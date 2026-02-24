#nullable enable
using System;
using Terraria.UI;

namespace TerrariaAccess.Common.Systems.MenuNarration;

/// <summary>
/// Represents the possible states of the mod config menu narrator.
/// </summary>
internal enum ModConfigNarrationState
{
    /// <summary>No mod config screen is active.</summary>
    Inactive,

    /// <summary>In UIModConfigList, navigating the mod list on the left.</summary>
    ModListFocused,

    /// <summary>In UIModConfigList, navigating the config list on the right.</summary>
    ConfigListFocused,

    /// <summary>In UIModConfig, navigating config elements.</summary>
    ConfigElementsFocused,

    /// <summary>Currently adjusting a slider value with hold-to-repeat.</summary>
    SliderAdjusting
}

/// <summary>
/// Manages state transitions for the mod config menu narrator.
/// Replaces the many boolean/int state tracking fields with explicit state management.
/// </summary>
internal sealed class ModConfigNarrationStateMachine
{
    private ModConfigNarrationState _currentState = ModConfigNarrationState.Inactive;
    private ModConfigNarrationState _previousState = ModConfigNarrationState.Inactive;
    private UIState? _uiState;
    private int _framesInState;

    /// <summary>The current narrator state.</summary>
    public ModConfigNarrationState Current => _currentState;

    /// <summary>The previous state before the last transition.</summary>
    public ModConfigNarrationState Previous => _previousState;

    /// <summary>Number of frames spent in the current state.</summary>
    public int FramesInState => _framesInState;

    /// <summary>The current UI state being tracked.</summary>
    public UIState? UiState => _uiState;

    /// <summary>True if the state just changed this frame.</summary>
    public bool JustTransitioned => _framesInState == 0;

    /// <summary>True if the narrator is currently active (not Inactive).</summary>
    public bool IsActive => _currentState != ModConfigNarrationState.Inactive;

    /// <summary>True if currently in a list selection screen (UIModConfigList).</summary>
    public bool IsInListScreen => _currentState == ModConfigNarrationState.ModListFocused
                                || _currentState == ModConfigNarrationState.ConfigListFocused;

    /// <summary>True if currently in the config editing screen (UIModConfig).</summary>
    public bool IsInConfigScreen => _currentState == ModConfigNarrationState.ConfigElementsFocused
                                  || _currentState == ModConfigNarrationState.SliderAdjusting;

    /// <summary>
    /// Transitions to a new state and updates the associated UI state.
    /// </summary>
    /// <param name="newState">The state to transition to.</param>
    /// <param name="uiState">The UI state associated with this transition (optional).</param>
    public void TransitionTo(ModConfigNarrationState newState, UIState? uiState = null)
    {
        if (newState == _currentState && ReferenceEquals(uiState, _uiState))
        {
            // Same state and same UI - just increment frame counter
            _framesInState++;
            return;
        }

        _previousState = _currentState;
        _currentState = newState;
        _framesInState = 0;

        if (uiState is not null)
        {
            _uiState = uiState;
        }

        // Clear UI state when going inactive
        if (newState == ModConfigNarrationState.Inactive)
        {
            _uiState = null;
        }

        LogTransition();
    }

    /// <summary>
    /// Updates the UI state reference without changing the narrator state.
    /// </summary>
    public void UpdateUiState(UIState? uiState)
    {
        if (!ReferenceEquals(uiState, _uiState))
        {
            _uiState = uiState;
        }
    }

    /// <summary>
    /// Increments the frame counter. Call once per frame when state doesn't change.
    /// </summary>
    public void Tick()
    {
        _framesInState++;
    }

    /// <summary>
    /// Returns true if we've been in the current state for at least the specified number of frames.
    /// </summary>
    public bool HasBeenInStateFor(int frames)
    {
        return _framesInState >= frames;
    }

    /// <summary>
    /// Resets the state machine to inactive state.
    /// </summary>
    public void Reset()
    {
        _previousState = _currentState;
        _currentState = ModConfigNarrationState.Inactive;
        _uiState = null;
        _framesInState = 0;
    }

    private void LogTransition()
    {
        TerrariaAccess.Instance?.Logger.Debug(
            $"[ModConfigStateMachine] {_previousState} -> {_currentState}");
    }
}

/// <summary>
/// Tracks frame-based suppression timers for hover and input processing.
/// </summary>
internal sealed class NarrationFrameTimers
{
    private int _suppressHoverFrames;
    private int _skipInputFrames;
    private int _navigationSuppressionFrames;
    private int _lastMouseX;
    private int _lastMouseY;

    // Minimum frames to suppress hover after navigation, regardless of mouse movement.
    // This prevents Terraria's per-frame mouse position reset from triggering false "movement".
    private const int MinNavigationSuppressionFrames = 45; // ~750ms at 60fps

    // Pixels of INTENTIONAL movement required to release suppression early (after minimum period).
    // This is high because Terraria resets mouse position each frame in main menu, causing large jumps.
    private const int MouseMoveThreshold = 50;

    /// <summary>Remaining frames to suppress hover announcements.</summary>
    public int SuppressHoverFrames => _suppressHoverFrames;

    /// <summary>Remaining frames to skip input processing.</summary>
    public int SkipInputFrames => _skipInputFrames;

    /// <summary>True if hover announcements should be suppressed this frame.</summary>
    public bool ShouldSuppressHover => _suppressHoverFrames > 0 || _navigationSuppressionFrames > 0;

    /// <summary>True if input processing should be skipped this frame.</summary>
    public bool ShouldSkipInput => _skipInputFrames > 0;

    /// <summary>Suppress hover announcements for the specified number of frames.</summary>
    public void SuppressHover(int frames)
    {
        _suppressHoverFrames = frames;
    }

    /// <summary>
    /// Suppress hover announcements after navigation.
    /// Uses a minimum frame-based suppression that cannot be bypassed by mouse position resets,
    /// then additionally waits for intentional mouse movement.
    /// </summary>
    public void SuppressHoverAfterNavigation(int currentMouseX, int currentMouseY)
    {
        _navigationSuppressionFrames = MinNavigationSuppressionFrames;
        _lastMouseX = currentMouseX;
        _lastMouseY = currentMouseY;
    }

    /// <summary>Skip input processing for the specified number of frames.</summary>
    public void SkipInput(int frames)
    {
        _skipInputFrames = frames;
    }

    /// <summary>Decrements all timers and checks for mouse movement. Call once per frame.</summary>
    public void Tick(int currentMouseX = -1, int currentMouseY = -1)
    {
        if (_suppressHoverFrames > 0)
        {
            _suppressHoverFrames--;
        }

        if (_skipInputFrames > 0)
        {
            _skipInputFrames--;
        }

        // Navigation suppression requires minimum time AND intentional mouse movement
        if (_navigationSuppressionFrames > 0)
        {
            _navigationSuppressionFrames--;

            // After minimum period, check for intentional mouse movement to release early
            // We DON'T check movement during minimum period because Terraria resets mouse position each frame
            if (_navigationSuppressionFrames == 0 && currentMouseX >= 0 && currentMouseY >= 0)
            {
                int deltaX = Math.Abs(currentMouseX - _lastMouseX);
                int deltaY = Math.Abs(currentMouseY - _lastMouseY);

                // If no significant movement after minimum period, continue suppressing
                // until user intentionally moves the mouse
                if (deltaX <= MouseMoveThreshold && deltaY <= MouseMoveThreshold)
                {
                    // Keep suppression active by resetting to 1, will check again next frame
                    _navigationSuppressionFrames = 1;
                }
                else
                {
                    TerrariaAccess.Instance?.Logger.Debug($"[ModConfig] Navigation suppression released: mouse delta ({deltaX}, {deltaY})");
                }
            }
        }
    }

    /// <summary>Resets all timers to zero.</summary>
    public void Reset()
    {
        _suppressHoverFrames = 0;
        _skipInputFrames = 0;
        _navigationSuppressionFrames = 0;
    }
}

/// <summary>
/// Tracks slider hold-to-repeat state for continuous value adjustment.
/// </summary>
internal sealed class SliderRepeatState
{
    /// <summary>Frames before repeat starts (~0.4 sec at 60fps).</summary>
    public const int RepeatDelay = 25;

    /// <summary>Frames between repeats (~20 adjustments/sec).</summary>
    public const int RepeatRate = 3;

    private int _repeatDirection; // -1 = left, 0 = none, 1 = right
    private int _repeatTimer;
    private int _lastElementIndex = -1;
    private float _lastValue = -1f;

    /// <summary>Current repeat direction: -1 (left), 0 (none), or 1 (right).</summary>
    public int RepeatDirection => _repeatDirection;

    /// <summary>Frames the direction has been held.</summary>
    public int RepeatTimer => _repeatTimer;

    /// <summary>Last element index that was being adjusted.</summary>
    public int LastElementIndex => _lastElementIndex;

    /// <summary>Last slider value that was announced.</summary>
    public float LastValue => _lastValue;

    /// <summary>True if currently in an active repeat cycle.</summary>
    public bool IsRepeating => _repeatDirection != 0 && _repeatTimer >= RepeatDelay;

    /// <summary>
    /// Updates repeat state and returns true if an adjustment should be made this frame.
    /// </summary>
    /// <param name="currentDirection">Current held direction: -1, 0, or 1.</param>
    /// <returns>True if a slider adjustment should occur this frame.</returns>
    public bool UpdateAndCheckShouldAdjust(int currentDirection)
    {
        if (currentDirection == 0)
        {
            // No direction held - reset
            _repeatDirection = 0;
            _repeatTimer = 0;
            return false;
        }

        if (currentDirection != _repeatDirection)
        {
            // Direction changed - immediate first adjustment
            _repeatDirection = currentDirection;
            _repeatTimer = 0;
            return true;
        }

        // Same direction held - check repeat timing
        _repeatTimer++;

        if (_repeatTimer >= RepeatDelay)
        {
            // Past initial delay - repeat at fast rate
            int framesSinceDelay = _repeatTimer - RepeatDelay;
            return framesSinceDelay % RepeatRate == 0;
        }

        return false;
    }

    /// <summary>Records slider element state for value change tracking.</summary>
    public void TrackElement(int elementIndex, float value)
    {
        _lastElementIndex = elementIndex;
        _lastValue = value;
    }

    /// <summary>Clears element tracking when navigating away.</summary>
    public void ClearElementTracking()
    {
        _lastElementIndex = -1;
        _lastValue = -1f;
    }

    /// <summary>Resets all repeat state.</summary>
    public void Reset()
    {
        _repeatDirection = 0;
        _repeatTimer = 0;
        _lastElementIndex = -1;
        _lastValue = -1f;
    }
}
