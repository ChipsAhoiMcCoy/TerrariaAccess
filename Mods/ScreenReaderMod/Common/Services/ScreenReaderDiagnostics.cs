#nullable enable
using System;
using System.Reflection;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Services;

internal static class ScreenReaderDiagnostics
{
    private const string TraceEnvVariable = "SCREENREADERMOD_TRACE";

    private static bool? _traceEnabled;
    private static bool _langSnapshotPrinted;

    internal static void DumpStartupSnapshot()
    {
        if (!IsTraceEnabled())
        {
            return;
        }

        try
        {
            DumpMenuState();
            DumpLangMenuSnapshot();
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[Diagnostics] Failed to gather startup snapshot: {ex.Message}");
        }
    }

    private static bool IsTraceEnabled()
    {
        if (_traceEnabled.HasValue)
        {
            return _traceEnabled.Value;
        }

        string? value = Environment.GetEnvironmentVariable(TraceEnvVariable);
        _traceEnabled = !string.IsNullOrWhiteSpace(value) &&
            (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

        return _traceEnabled.Value;
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
}
