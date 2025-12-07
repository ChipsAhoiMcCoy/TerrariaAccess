#nullable enable
#if OSX || MACOS || (!WINDOWS && !LINUX)
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Terraria;

namespace ScreenReaderMod.Common.Services;

internal sealed class AvFoundationSpeechProvider : ISpeechProvider
{
    private const string LibraryName = "libAVSpeechBridge.dylib";

    // Delegates to replace DllImport extern methods
    private delegate IntPtr AVSpeechSynthesizer_NewDelegate(int providerNumber);
    private delegate void AVSpeechSynthesizer_SpeakDelegate(IntPtr synthesizer, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    private delegate void AVSpeechSynthesizer_StopSpeakingDelegate(IntPtr synthesizer);
    private delegate void AVSpeechSynthesizer_ReleaseDelegate(IntPtr synthesizer);
    private delegate void AVSpeechSynthesizer_SetRateDelegate(IntPtr synthesizer, float rate);
    private delegate void AVSpeechSynthesizer_SetVolumeDelegate(IntPtr synthesizer, float volume);
    private delegate void AVSpeechSynthesizer_SetVoiceDelegate(IntPtr synthesizer, [MarshalAs(UnmanagedType.LPUTF8Str)] string voiceIdentifier);

    private readonly object _syncRoot = new();
    private readonly int _providerNumber;

    private bool _initialized;
    private bool _available;
    
    // Fields for manual loading
    private IntPtr _libraryHandle; 
    private AVSpeechSynthesizer_NewDelegate? _synthesizerNew;
    private AVSpeechSynthesizer_SpeakDelegate? _synthesizerSpeak;
    private AVSpeechSynthesizer_StopSpeakingDelegate? _synthesizerStopSpeaking;
    private AVSpeechSynthesizer_ReleaseDelegate? _synthesizerRelease;
    private AVSpeechSynthesizer_SetRateDelegate? _synthesizerSetRate;
    private AVSpeechSynthesizer_SetVolumeDelegate? _synthesizerSetVolume;
    private AVSpeechSynthesizer_SetVoiceDelegate? _synthesizerSetVoice;

    private IntPtr _synthesizer;
    private string? _lastMessage;
    private string? _lastError;

    public string Name => "AVFoundation";

    public bool IsAvailable => _available;

    public bool IsInitialized => _initialized;

    public AvFoundationSpeechProvider(int providerNumber = 1)
    {
        _providerNumber = providerNumber;
    }

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

            if (!TryLoadLibrary())
            {
                return;
            }

            try
            {
                // Use the loaded delegate with provider number
                _synthesizer = _synthesizerNew!(_providerNumber); 
                if (_synthesizer == IntPtr.Zero)
                {
                    _lastError = "Failed to create AVSpeechSynthesizer";
                    ScreenReaderMod.Instance?.Logger.Warn("[AVFoundation] Failed to create AVSpeechSynthesizer.");
                    Unload();
                    return;
                }

                _available = true;
                _lastError = null;
                
                ScreenReaderMod.Instance?.Logger.Info($"[AVFoundation] Connected using AVSpeechSynthesizer (provider: {_providerNumber}).");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Error($"[AVFoundation] Initialization failed: {ex.Message}");
                Unload();
            }
        }
    }
    
    public void Unload()
    {
        lock (_syncRoot)
        {
            DisposeSynthesizer();
        }
    }
    
    public void Shutdown()
    {
        Unload();
    }

    public void Speak(string message)
    {
        lock (_syncRoot)
        {
            // Use the loaded delegate
            if (!_available || _synthesizerSpeak is null) 
            {
                return;
            }

            try
            {
                _synthesizerSpeak(_synthesizer, message); 
                _lastMessage = message;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] Speak failed: {ex.Message}");
            }
        }
    }

    public void Interrupt()
    {
        lock (_syncRoot)
        {
            // Use the loaded delegate
            if (!_available || _synthesizerStopSpeaking is null) 
            {
                return;
            }

            try
            {
                _synthesizerStopSpeaking(_synthesizer);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] Interrupt failed: {ex.Message}");
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

    private void DisposeSynthesizer()
    {
        // Check both synthesizer and library handle for cleanup
        if (_synthesizer == IntPtr.Zero && _libraryHandle == IntPtr.Zero) 
        {
            return;
        }

        try
        {
            // Check if synthesizer was created and delegate exists
            if (_synthesizer != IntPtr.Zero && _synthesizerRelease is not null) 
            {
                _synthesizerRelease(_synthesizer);
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] Release failed: {ex.Message}");
        }
        finally
        {
            _synthesizer = IntPtr.Zero;

            if (_libraryHandle != IntPtr.Zero)
            {
                // Free the native library handle
                NativeLibrary.Free(_libraryHandle); 
            }
            
            // Clear all fields
            _libraryHandle = IntPtr.Zero;
            _synthesizerNew = null;
            _synthesizerSpeak = null;
            _synthesizerStopSpeaking = null;
            _synthesizerRelease = null;
            _synthesizerSetRate = null;
            _synthesizerSetVolume = null;
            _synthesizerSetVoice = null;
        }
    }

    public void SetRate(float rate)
    {
        lock (_syncRoot)
        {
            if (!_available || _synthesizerSetRate is null)
            {
                return;
            }

            try
            {
                _synthesizerSetRate(_synthesizer, rate);
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] SetRate failed: {ex.Message}");
            }
        }
    }

    public void SetVolume(float volume)
    {
        lock (_syncRoot)
        {
            if (!_available || _synthesizerSetVolume is null)
            {
                return;
            }

            try
            {
                _synthesizerSetVolume(_synthesizer, volume);
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] SetVolume failed: {ex.Message}");
            }
        }
    }

    public void SetVoice(string voiceIdentifier)
    {
        lock (_syncRoot)
        {
            if (!_available || _synthesizerSetVoice is null || string.IsNullOrEmpty(voiceIdentifier))
            {
                return;
            }

            try
            {
                _synthesizerSetVoice(_synthesizer, voiceIdentifier);
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] SetVoice failed: {ex.Message}");
            }
        }
    }

    private bool TryLoadLibrary()
    {
        foreach (string libraryPath in EnumerateCandidatePaths())
        {
            try
            {
                if (NativeLibrary.TryLoad(libraryPath, out _libraryHandle))
                {
                    // Map the function pointers once the library is loaded
                    IntPtr newPtr = NativeLibrary.GetExport(_libraryHandle, "AVSpeechSynthesizer_New");
                    IntPtr speakPtr = NativeLibrary.GetExport(_libraryHandle, "AVSpeechSynthesizer_Speak");
                    IntPtr stopPtr = NativeLibrary.GetExport(_libraryHandle, "AVSpeechSynthesizer_StopSpeaking");
                    IntPtr releasePtr = NativeLibrary.GetExport(_libraryHandle, "AVSpeechSynthesizer_Release");
                    
                    // Load setter functions
                    if (NativeLibrary.TryGetExport(_libraryHandle, "AVSpeechSynthesizer_SetRate", out IntPtr setRatePtr))
                    {
                        _synthesizerSetRate = GetDelegate<AVSpeechSynthesizer_SetRateDelegate>(setRatePtr);
                    }
                    if (NativeLibrary.TryGetExport(_libraryHandle, "AVSpeechSynthesizer_SetVolume", out IntPtr setVolumePtr))
                    {
                        _synthesizerSetVolume = GetDelegate<AVSpeechSynthesizer_SetVolumeDelegate>(setVolumePtr);
                    }
                    if (NativeLibrary.TryGetExport(_libraryHandle, "AVSpeechSynthesizer_SetVoice", out IntPtr setVoicePtr))
                    {
                        _synthesizerSetVoice = GetDelegate<AVSpeechSynthesizer_SetVoiceDelegate>(setVoicePtr);
                    }

                    // Use the internal helper method for delegate conversion
                    _synthesizerNew = GetDelegate<AVSpeechSynthesizer_NewDelegate>(newPtr);
                    _synthesizerSpeak = GetDelegate<AVSpeechSynthesizer_SpeakDelegate>(speakPtr);
                    _synthesizerStopSpeaking = GetDelegate<AVSpeechSynthesizer_StopSpeakingDelegate>(stopPtr);
                    _synthesizerRelease = GetDelegate<AVSpeechSynthesizer_ReleaseDelegate>(releasePtr);

                    ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] Successfully loaded {libraryPath}.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[AVFoundation] Failed to load {libraryPath}: {ex.Message}");
            }
        }

        _lastError = $"Failed to load native library '{LibraryName}' using candidate paths.";
        ScreenReaderMod.Instance?.Logger.Warn($"[AVFoundation] Failed to load native library '{LibraryName}' from any candidate path.");
        _available = false;
        return false;
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        yield return LibraryName; // 1. Plain name for OS/default search

        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, LibraryName); // 2. Game executable directory

        string? savePath = Main.SavePath;
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            // 3. Path in ModSources/ScreenReaderMod/Libraries (for development)
            yield return Path.Combine(savePath, "ModSources", "ScreenReaderMod", "Libraries", LibraryName);
            
            // 4. Path in Mods/ScreenReaderMod/Content/Libraries (for built/installed mod)
            yield return Path.Combine(savePath, "Mods", "ScreenReaderMod", "Libraries", LibraryName);
            
            // 5. Simpler paths for broader searching
            yield return Path.Combine(savePath, "Mods", LibraryName);
            yield return Path.Combine(savePath, "ModSources", "ScreenReaderMod", LibraryName);
            yield return Path.Combine(savePath, LibraryName); 
        }

        string? gameDir = Path.GetDirectoryName(typeof(Main).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            yield return Path.Combine(gameDir, LibraryName); // 6. Game folder check
        }
    }
    
    /// <summary>
    /// Converts a native function pointer to a managed delegate.
    /// </summary>
    private static T GetDelegate<T>(IntPtr ptr) where T : Delegate
    {
        if (ptr == IntPtr.Zero)
        {
            throw new Exception($"Failed to get function pointer for delegate of type {typeof(T).Name}");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr); 
    }
}
#endif