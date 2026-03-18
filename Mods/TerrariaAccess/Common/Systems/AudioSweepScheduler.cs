using System;
using Terraria;

namespace TerrariaAccess.Common.Systems;

internal sealed class AudioSweepScheduler
{
    private const int TargetSweepDurationFrames = 60;
    private const int MinSweepStepFrames = 3;
    private const int SweepCycleGapFrames = 15;

    private int _cursor;
    private int _nextFrame;
    private bool _cycleActive;

    public int Cursor => _cursor;
    public bool IsCycleActive => _cycleActive;

    public void Reset()
    {
        _cursor = 0;
        _nextFrame = 0;
        _cycleActive = false;
    }

    public bool EnsureCycleStarted(int itemCount)
    {
        if (_cycleActive)
        {
            return itemCount > 0;
        }

        if (itemCount <= 0)
        {
            Reset();
            return false;
        }

        _cursor = 0;
        _cycleActive = true;
        return true;
    }

    public bool IsWaiting(ulong currentFrame)
    {
        return currentFrame < (uint)Math.Max(0, _nextFrame);
    }

    public bool HasCompletedCycle(int itemCount)
    {
        return _cursor >= itemCount;
    }

    public void PauseUntilNextCycle(ulong currentFrame)
    {
        _cycleActive = false;
        _nextFrame = ScheduleNextFrame(currentFrame, SweepCycleGapFrames);
    }

    public void SkipCurrentStep()
    {
        _cursor++;
    }

    public void Advance(ulong currentFrame, int itemCount)
    {
        _cursor++;
        int interval = Math.Max(MinSweepStepFrames, TargetSweepDurationFrames / Math.Max(1, itemCount));
        _nextFrame = ScheduleNextFrame(currentFrame, interval);
    }

    public void Advance(ulong currentFrame, int itemCount, int delayFrames)
    {
        _cursor++;
        _nextFrame = delayFrames <= 0 ? 0 : ScheduleNextFrame(currentFrame, delayFrames);
    }

    private static int ScheduleNextFrame(ulong currentFrame, int delayFrames)
    {
        int safeDelay = Math.Max(1, delayFrames);
        ulong target = currentFrame + (ulong)safeDelay;
        if (target > int.MaxValue)
        {
            target = int.MaxValue;
        }

        return (int)target;
    }
}
