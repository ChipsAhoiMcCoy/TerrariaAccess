#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using ScreenReaderMod.Common.Services;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

internal interface INarrationScheduler
{
    void Clear();
    void Register(NarrationServiceRegistration registration);
    void Update(NarrationSchedulerContext context);
}

internal sealed class NarrationScheduler : INarrationScheduler
{
    private readonly List<NarrationServiceRegistration> _registrations = new();
    private static readonly double TicksToMilliseconds = 1000d / Stopwatch.Frequency;
    private readonly NarrationInstrumentation _instrumentation = new();

    public void Clear()
    {
        _registrations.Clear();
    }

    public void Register(NarrationServiceRegistration registration)
    {
        if (registration.Service is null)
        {
            throw new ArgumentNullException(nameof(registration.Service));
        }

        _registrations.Add(registration);
    }

    public void Update(NarrationSchedulerContext context)
    {
        if (_registrations.Count == 0)
        {
            return;
        }

        foreach (NarrationServiceRegistration registration in _registrations)
        {
            long start = context.TraceEnabled ? Stopwatch.GetTimestamp() : 0;
            if (!registration.ShouldRun(context))
            {
                continue;
            }

            using (NarrationInstrumentationContext.BeginScope(registration.Service.Name, _instrumentation))
            {
                registration.Service.Update(new NarrationServiceContext(context, registration.Gating, _instrumentation));
                if (context.TraceEnabled)
                {
                    LogDuration(registration.Service.Name, start);
                }
            }

            _instrumentation.Record(registration.Service.Name);
        }
    }

    private static void LogDuration(string name, long startTicks)
    {
        if (ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - startTicks) * TicksToMilliseconds;
        logger.Info($"[NarrationScheduler][Timing] service={name} elapsedMs={elapsedMs:0.###}");
    }
}

internal readonly struct NarrationSchedulerContext
{
    public NarrationSchedulerContext(
        RuntimeContextSnapshot runtime,
        Player player,
        bool isPaused,
        bool requirePaused,
        bool traceEnabled,
        bool traceOnly,
        NarrationInstrumentation instrumentation)
    {
        Runtime = runtime;
        Player = player;
        IsPaused = isPaused;
        RequirePaused = requirePaused;
        TraceEnabled = traceEnabled;
        TraceOnly = traceOnly;
        Instrumentation = instrumentation;
    }

    public RuntimeContextSnapshot Runtime { get; }
    public Player Player { get; }
    public bool IsPaused { get; }
    public bool RequirePaused { get; }
    public bool TraceEnabled { get; }
    public bool TraceOnly { get; }
    public NarrationInstrumentation Instrumentation { get; }
}

internal readonly struct NarrationServiceGating
{
    public bool RequiresPaused { get; init; }
    public bool SkipWhenPaused { get; init; }
    public bool AllowWhenMenu { get; init; }
    public ScreenReaderService.AnnouncementCategory? Category { get; init; }
}

internal readonly struct NarrationServiceRegistration
{
    public NarrationServiceRegistration(INarrationService service, NarrationServiceGating gating)
    {
        Service = service;
        Gating = gating;
    }

    public INarrationService Service { get; }
    public NarrationServiceGating Gating { get; }

    public bool ShouldRun(NarrationSchedulerContext context)
    {
        if (context.RequirePaused && !context.IsPaused)
        {
            return false;
        }

        if (!Gating.AllowWhenMenu && context.Runtime.InMenu)
        {
            return false;
        }

        if (Gating.RequiresPaused && !context.IsPaused)
        {
            return false;
        }

        if (Gating.SkipWhenPaused && context.IsPaused)
        {
            return false;
        }

        return true;
    }
}

internal interface INarrationService
{
    string Name { get; }
    void Update(NarrationServiceContext context);
}

internal readonly struct NarrationServiceContext
{
    public NarrationServiceContext(
        NarrationSchedulerContext schedulerContext,
        NarrationServiceGating gating,
        NarrationInstrumentation instrumentation)
    {
        SchedulerContext = schedulerContext;
        Gating = gating;
        Instrumentation = instrumentation;
    }

    public NarrationSchedulerContext SchedulerContext { get; }
    public NarrationServiceGating Gating { get; }
    public NarrationInstrumentation Instrumentation { get; }

    public RuntimeContextSnapshot Runtime => SchedulerContext.Runtime;
    public Player Player => SchedulerContext.Player;
    public bool IsPaused => SchedulerContext.IsPaused;
    public bool RequirePaused => SchedulerContext.RequirePaused;
    public bool TraceEnabled => SchedulerContext.TraceEnabled;
    public bool TraceOnly => SchedulerContext.TraceOnly;
    public ScreenReaderService.AnnouncementCategory? Category => Gating.Category;
}

internal static class NarrationSchedulerSettings
{
    private const string TraceOnlyEnvVariable = "SCREENREADERMOD_SCHEDULER_TRACE_ONLY";

    internal static bool IsTraceOnlyEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(TraceOnlyEnvVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal abstract class NarrationServiceBase : INarrationService
{
    protected NarrationServiceBase(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public abstract void Update(NarrationServiceContext context);

    protected void LogTrace(NarrationServiceContext context, string? detail = null)
    {
        if (!context.TraceEnabled || ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail={detail}";
        string category = context.Category?.ToString() ?? "none";
        logger.Info($"[NarrationScheduler] service={Name} paused={context.IsPaused} requirePaused={context.RequirePaused} category={category} traceOnly={context.TraceOnly}{suffix}");
    }
}
