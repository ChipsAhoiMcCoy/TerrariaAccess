#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Services;

public static class ScreenReaderService
{
    private static readonly Queue<string> RecentMessages = new();
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(300);
    private static DateTime _lastAnnouncedAt = DateTime.MinValue;
    private static string? _lastMessage;
    private static bool _diagnosticsPrinted;
    private static bool _langSnapshotPrinted;

    public static IReadOnlyCollection<string> Snapshot => RecentMessages.ToArray();

    public static void Initialize()
    {
        RecentMessages.Clear();
        _lastAnnouncedAt = DateTime.MinValue;
        _lastMessage = null;
        DumpMenuReflectionDiagnostics();
        DumpLangMenuSnapshot();
        NvdaSpeechProvider.Initialize();
    }

    public static void Unload()
    {
        RecentMessages.Clear();
        _lastMessage = null;
        _diagnosticsPrinted = false;
        _langSnapshotPrinted = false;
        NvdaSpeechProvider.Interrupt();
        NvdaSpeechProvider.Shutdown();
    }

    public static void Interrupt()
    {
        NvdaSpeechProvider.Interrupt();
    }

    public static void Announce(string? message, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string trimmed = message.Trim();
        DateTime now = DateTime.UtcNow;
        if (!force && string.Equals(trimmed, _lastMessage, StringComparison.OrdinalIgnoreCase) && now - _lastAnnouncedAt < RepeatWindow)
        {
            return;
        }

        _lastMessage = trimmed;
        _lastAnnouncedAt = now;
        RecentMessages.Enqueue(trimmed);
        while (RecentMessages.Count > 25)
        {
            RecentMessages.Dequeue();
        }

        NvdaSpeechProvider.Interrupt();
        NvdaSpeechProvider.Speak(trimmed);
        ScreenReaderMod.Instance?.Logger.Info($"[Narration] {trimmed}");

        if (!Main.dedServ)
        {
            try
            {
                Main.NewText($"[Narration] {trimmed}", 255, 255, 160);
            }
            catch
            {
                // Swallow UI exceptions in menu contexts where chat output is unavailable.
            }
        }
    }

    private static void DumpMenuReflectionDiagnostics()
    {
        if (_diagnosticsPrinted)
        {
            return;
        }

        _diagnosticsPrinted = true;
        try
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            foreach (var field in typeof(Main).GetFields(flags))
            {
                if (field.Name.Contains("menu", StringComparison.OrdinalIgnoreCase))
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[MenuReflection] Field: {field.Name} -> {field.FieldType.FullName}");
                }
            }

            foreach (var property in typeof(Main).GetProperties(flags))
            {
                if (property.Name.Contains("menu", StringComparison.OrdinalIgnoreCase))
                {
                    ScreenReaderMod.Instance?.Logger.Info($"[MenuReflection] Property: {property.Name} -> {property.PropertyType.FullName}");
                }
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[MenuReflection] Failed: {ex.Message}");
        }
    }

    private static void DumpLangMenuSnapshot()
    {
        if (_langSnapshotPrinted)
        {
            return;
        }

        _langSnapshotPrinted = true;
        try
        {
            for (int i = 0; i < Math.Min(100, Lang.menu.Length); i++)
            {
                ScreenReaderMod.Instance?.Logger.Info($"[LangMenu] {i}: {Lang.menu[i].Value}");
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[LangMenu] Snapshot failed: {ex.Message}");
        }
    }
}
