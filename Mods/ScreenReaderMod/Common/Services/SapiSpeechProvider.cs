#nullable enable
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ScreenReaderMod.Common.Services;

internal static class SapiSpeechProvider
{
    private const int SpeechVoiceSpeakFlagsAsync = 1;
    private const int SpeechVoiceSpeakFlagsPurgeBeforeSpeak = 2;

    private static readonly object SyncRoot = new();

    private static bool _initialized;
    private static bool _available;
    private static object? _voice;
    private static Type? _voiceType;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            try
            {
                Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType is null)
                {
                    ScreenReaderMod.Instance?.Logger.Warn("[SAPI] SpVoice ProgID unavailable. World announcements disabled.");
                    return;
                }

                object? instance = Activator.CreateInstance(voiceType);
                if (instance is null)
                {
                    ScreenReaderMod.Instance?.Logger.Warn("[SAPI] Failed to create SpVoice instance. World announcements disabled.");
                    return;
                }

                _voiceType = voiceType;
                _voice = instance;
                _available = true;
                ScreenReaderMod.Instance?.Logger.Info("[SAPI] Connected using SpVoice.");
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Warn($"[SAPI] Initialization failed: {ex.Message}");
                _available = false;
            }

            if (!_available)
            {
                DisposeVoice();
            }
        }
    }

    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            DisposeVoice();
            _initialized = false;
            _available = false;
        }
    }

    public static void Speak(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!_available || _voice is null || _voiceType is null)
            {
                return;
            }

            try
            {
                object[] args = { message, SpeechVoiceSpeakFlagsAsync };
                _voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, binder: null, target: _voice, args: args);
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Warn($"[SAPI] Speak failed: {ex.Message}");
                _available = false;
                DisposeVoice();
            }
        }
    }

    public static void Interrupt()
    {
        lock (SyncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            if (!_available || _voice is null || _voiceType is null)
            {
                return;
            }

            try
            {
                object[] args = { string.Empty, SpeechVoiceSpeakFlagsPurgeBeforeSpeak };
                _voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, binder: null, target: _voice, args: args);
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[SAPI] Interrupt failed: {ex.Message}");
            }
        }
    }

    private static void DisposeVoice()
    {
        if (_voice is null)
        {
            _voiceType = null;
            return;
        }

        try
        {
            if (Marshal.IsComObject(_voice))
            {
                Marshal.ReleaseComObject(_voice);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[SAPI] Release failed: {ex.Message}");
        }
        finally
        {
            _voice = null;
            _voiceType = null;
        }
    }
}
