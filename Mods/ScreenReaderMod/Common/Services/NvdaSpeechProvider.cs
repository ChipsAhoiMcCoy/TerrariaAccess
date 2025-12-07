#nullable enable
#if !OSX && !MACOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Terraria;

namespace ScreenReaderMod.Common.Services;

internal sealed class NvdaSpeechProvider : ISpeechProvider
{
    private const string NvdaLibraryName = "nvdaControllerClient64.dll";

    private delegate int NvdaSpeakDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);
    private delegate int NvdaCancelDelegate();
    private delegate int NvdaTestDelegate();

    private readonly object _syncRoot = new();

    private bool _initialized;
    private bool _available;
    private IntPtr _libraryHandle;
    private NvdaSpeakDelegate? _speak;
    private NvdaCancelDelegate? _cancel;
    private NvdaTestDelegate? _test;
    private string? _lastMessage;
    private string? _lastError;

    public string Name => "NVDA";

    public bool IsAvailable => _available;

    public bool IsInitialized => _initialized;

    public void Interrupt()
    {
        lock (_syncRoot)
        {
            if (!_available || _cancel is null)
            {
                return;
            }

            try
            {
                _cancel();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Debug($"[NVDA] Cancel threw {ex.Message}. Disabling NVDA output.");
                _available = false;
            }
        }
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
        }

        try
        {
            foreach (string candidate in EnumerateCandidatePaths())
            {
                if (TryLoad(candidate))
                {
                    return;
                }
            }

            _lastError = $"Unable to locate {NvdaLibraryName}";
            ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Unable to locate {NvdaLibraryName}. Copy it next to tModLoader.exe or into Mods/ScreenReaderMod/Libraries.");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            ScreenReaderMod.Instance?.Logger.Error($"[NVDA] Initialization failed: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        lock (_syncRoot)
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                try
                {
                    _cancel?.Invoke();
                }
                catch
                {
                    // ignore shutdown errors
                }

                NativeLibrary.Free(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            _available = false;
            _speak = null;
            _cancel = null;
            _test = null;
            _lastMessage = null;
            _lastError = null;
            _initialized = false;
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

            if (!_available || _speak is null)
            {
                return;
            }

            try
            {
                int result = _speak(message);
                _lastMessage = message;
                if (result != 0)
                {
                    _lastError = $"Speak returned code {result}";
                    ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Speak returned code {result}. Disabling NVDA output until restart.");
                    _available = false;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Speak threw {ex.Message}. Disabling NVDA output.");
                _available = false;
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

    private bool TryLoad(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return false;
        }

        lock (_syncRoot)
        {
            try
            {
                if (!NativeLibrary.TryLoad(libraryPath, out IntPtr handle))
                {
                    return false;
                }

                _libraryHandle = handle;
                _speak = Marshal.GetDelegateForFunctionPointer<NvdaSpeakDelegate>(NativeLibrary.GetExport(handle, "nvdaController_speakText"));
                _cancel = Marshal.GetDelegateForFunctionPointer<NvdaCancelDelegate>(NativeLibrary.GetExport(handle, "nvdaController_cancelSpeech"));
                _test = Marshal.GetDelegateForFunctionPointer<NvdaTestDelegate>(NativeLibrary.GetExport(handle, "nvdaController_testIfRunning"));

                int status = _test();
                if (status == 0)
                {
                    _available = true;
                    _lastError = null;
                    ScreenReaderMod.Instance?.Logger.Info($"[NVDA] Connected via {libraryPath}.");
                    return true;
                }

                _lastError = $"NVDA not running (code {status})";
                ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Library loaded from {libraryPath}, but NVDA not running (code {status}).");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ScreenReaderMod.Instance?.Logger.Debug($"[NVDA] Failed to load {libraryPath}: {ex.Message}");
            }

            if (_libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            _speak = null;
            _cancel = null;
            _test = null;
            _available = false;
            return false;
        }
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        yield return NvdaLibraryName;

        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, NvdaLibraryName);

        string? savePath = Main.SavePath;
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            yield return Path.Combine(savePath, NvdaLibraryName);
            yield return Path.Combine(savePath, "ModSources", "ScreenReaderMod", "Libraries", NvdaLibraryName);
            yield return Path.Combine(savePath, "Mods", NvdaLibraryName);
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "NVDA", NvdaLibraryName);
        }

        string? gameDir = Path.GetDirectoryName(typeof(Main).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            yield return Path.Combine(gameDir, NvdaLibraryName);
        }
    }
}
#endif
