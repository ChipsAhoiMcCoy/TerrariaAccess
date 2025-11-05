#nullable enable
using System;
using System.Collections.Generic;
using Terraria;

namespace ScreenReaderMod.Common.Services;

public static class ScreenReaderService
{
    private static readonly Queue<string> RecentMessages = new();
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(300);
    private static DateTime _lastAnnouncedAt = DateTime.MinValue;
    private static string? _lastMessage;

    public static IReadOnlyCollection<string> Snapshot => RecentMessages.ToArray();

    public static void Initialize()
    {
        RecentMessages.Clear();
        _lastAnnouncedAt = DateTime.MinValue;
        _lastMessage = null;
        ScreenReaderDiagnostics.DumpStartupSnapshot();
        NvdaSpeechProvider.Initialize();
    }

    public static void Unload()
    {
        RecentMessages.Clear();
        _lastMessage = null;
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
            Main.NewText($"[Narration] {trimmed}", 255, 255, 160);
        }
    }
}
