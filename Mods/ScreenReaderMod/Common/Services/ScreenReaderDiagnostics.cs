#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Services;

internal static class ScreenReaderDiagnostics
{
    private const string TraceEnvVariable = "SCREENREADERMOD_TRACE";
    private const string SpeechLogEnvVariable = "SCREENREADERMOD_SPEECH_LOG_ONLY";

    // Default trace to on so chat debugging works without setting env vars.
    private const bool DefaultTraceEnabled = true;
    private static bool? _traceEnabled;
    private static bool? _speechLogOnlyEnabled;
    private static bool _langSnapshotPrinted;

    internal static void DumpStartupSnapshot(SpeechControllerSnapshot? speechSnapshot = null)
    {
        if (!IsTraceEnabled())
        {
            return;
        }

        try
        {
            DumpMenuState();
            DumpLangMenuSnapshot();
            if (speechSnapshot.HasValue)
            {
                DumpSpeechSnapshot(speechSnapshot.Value);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[Diagnostics] Failed to gather startup snapshot: {ex.Message}");
        }
    }

    internal static bool IsTraceEnabled()
    {
        if (_traceEnabled.HasValue)
        {
            return _traceEnabled.Value;
        }

        string? value = Environment.GetEnvironmentVariable(TraceEnvVariable);
        _traceEnabled = value is null ? DefaultTraceEnabled : ParseFlag(value);

        return _traceEnabled.Value;
    }

    internal static bool IsSpeechLogOnlyEnabled()
    {
        if (_speechLogOnlyEnabled.HasValue)
        {
            return _speechLogOnlyEnabled.Value;
        }

        string? value = Environment.GetEnvironmentVariable(SpeechLogEnvVariable);
        _speechLogOnlyEnabled = ParseFlag(value);
        return _speechLogOnlyEnabled.Value;
    }

    internal static void LogSpeechEvent(SpeechRequest request, string providerName, bool logOnly)
    {
        if (!IsTraceEnabled() || ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        logger.Info($"[Diagnostics][Speech] provider={providerName} channel={request.Channel} category={request.Category} force={request.Force} allowWhenMuted={request.AllowWhenMuted} logOnly={logOnly} text={request.Text}");
    }

    internal static void LogSpeechSuppressed(SpeechRequest request, string reason)
    {
        if (!IsTraceEnabled() || ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        logger.Info($"[Diagnostics][Speech] suppressed reason={reason} channel={request.Channel} category={request.Category} text={request.Text}");
    }

    internal static void DumpSpeechSnapshot(SpeechControllerSnapshot snapshot)
    {
        if (!IsTraceEnabled() || ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        logger.Info($"[Diagnostics][Speech] initialized={snapshot.Initialized} muted={snapshot.Muted} interruptEnabled={snapshot.InterruptEnabled} logOnly={snapshot.LogOnly}");

        foreach (KeyValuePair<ScreenReaderService.AnnouncementCategory, string?> kvp in snapshot.LastCategoryMessages)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value))
            {
                logger.Info($"[Diagnostics][Speech] last[{kvp.Key}]={kvp.Value}");
            }
        }

        if (snapshot.RecentMessages.Count > 0)
        {
            logger.Info($"[Diagnostics][Speech] recent={string.Join(" | ", snapshot.RecentMessages.Take(10))}");
        }

        foreach (SpeechProviderSnapshot provider in snapshot.Providers)
        {
            string lastMessage = string.IsNullOrWhiteSpace(provider.LastMessage) ? "<none>" : provider.LastMessage!;
            string lastError = string.IsNullOrWhiteSpace(provider.LastError) ? "<none>" : provider.LastError!;
            logger.Info($"[Diagnostics][Speech] provider={provider.Name} initialized={provider.Initialized} available={provider.Available} lastMessage={lastMessage} lastError={lastError}");
        }
    }

    private static void DumpMenuState()
    {
        if (ScreenReaderMod.Instance?.Logger is not { } logger || Main.instance is null)
        {
            return;
        }

        logger.Info($"[Diagnostics] menuMode={Main.menuMode}, menuFocus={Main.menuFocus}, gameMenu={Main.gameMenu}");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        FieldInfo? focusMenuField = typeof(Main).GetField("focusMenu", flags);
        if (focusMenuField is not null)
        {
            object? focusMenu = focusMenuField.GetValue(Main.instance);
            logger.Info($"[Diagnostics] Main.focusMenu={focusMenu}");
        }

        FieldInfo? selectedMenuField = typeof(Main).GetField("selectedMenu", flags);
        if (selectedMenuField is not null)
        {
            object? selectedMenu = selectedMenuField.GetValue(Main.instance);
            logger.Info($"[Diagnostics] Main.selectedMenu={selectedMenu}");
        }

        FieldInfo? menuItemScale = typeof(Main).GetField("menuItemScale", flags);
        if (menuItemScale?.GetValue(Main.instance) is float[] scales)
        {
            logger.Info($"[Diagnostics] menuItemScale.Length={scales.Length}");
        }
    }

    private static void DumpLangMenuSnapshot()
    {
        if (_langSnapshotPrinted || ScreenReaderMod.Instance?.Logger is not { } logger)
        {
            return;
        }

        _langSnapshotPrinted = true;

        LocalizedText[] entries = Lang.menu;
        int limit = Math.Min(100, entries.Length);

        logger.Info($"[Diagnostics] Lang.menu snapshot ({limit} entries)");
        for (int i = 0; i < limit; i++)
        {
            string value = TextSanitizer.Clean(entries[i].Value);
            logger.Info($"[Diagnostics] Lang.menu[{i}] = {value}");
        }
    }

    private static bool ParseFlag(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }
}
