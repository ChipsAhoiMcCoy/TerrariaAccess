#nullable enable
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ScreenReaderMod.Common.Services;

internal sealed class SapiSpeechProvider : ISpeechProvider
{
    private const int SpeechVoiceSpeakFlagsAsync = 1;
    private const int SpeechVoiceSpeakFlagsPurgeBeforeSpeak = 2;

    private readonly object _syncRoot = new();

    private bool _initialized;
    private bool _available;
    private object? _voice;
    private Type? _voiceType;
    private string? _lastMessage;
    private string? _lastError;

    public string Name => "SAPI";

    public bool IsAvailable => _available;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        lock (_syncRoot)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _available = false;
            _lastMessage = null;
            _lastError = null;

            try
            {
                Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType is null)
                {
                    _lastError = "SpVoice ProgID unavailable";
                    ScreenReaderMod.Instance?.Logger.Warn("[SAPI] SpVoice ProgID unavailable. World announcements disabled.");
                    return;
                }

                object? instance = Activator.CreateInstance(voiceType);
                if (instance is null)
                {
                    _lastError = "Failed to create SpVoice instance";
                    ScreenReaderMod.Instance?.Logger.Warn("[SAPI] Failed to create SpVoice instance. World announcements disabled.");
                    return;
                }

                _voiceType = voiceType;
                _voice = instance;
                _available = true;
                _lastError = null;
                ScreenReaderMod.Instance?.Logger.Info("[SAPI] Connected using SpVoice.");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Warn($"[SAPI] Initialization failed: {ex.Message}");
                _available = false;
            }

            if (!_available)
            {
                DisposeVoice();
            }
        }
    }

    public void Shutdown()
    {
        lock (_syncRoot)
        {
            DisposeVoice();
            _initialized = false;
            _available = false;
            _lastMessage = null;
            _lastError = null;
        }
    }

    public void Speak(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_syncRoot)
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
                _lastMessage = message;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Warn($"[SAPI] Speak failed: {ex.Message}");
                _available = false;
                DisposeVoice();
            }
        }
    }

    public void Interrupt()
    {
        lock (_syncRoot)
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
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Debug($"[SAPI] Interrupt failed: {ex.Message}");
            }
        }
    }

    public SpeechProviderSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new SpeechProviderSnapshot(Name, _initialized, _available, _lastMessage, _lastError);
        }
    }

    private void DisposeVoice()
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
