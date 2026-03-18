#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Terraria;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Speech provider using Tolk library for universal screen reader support.
/// Supports JAWS, NVDA, Window-Eyes, SuperNova, System Access, ZoomText, and SAPI fallback.
/// </summary>
internal sealed class TolkSpeechProvider : ISpeechProvider
{
    private const string TolkLibraryName = "Tolk.dll";

    // Windows API for DLL search path manipulation
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string? lpPathName);

    // Tolk native API delegates
    private delegate void TolkLoadDelegate();
    private delegate void TolkUnloadDelegate();
    private delegate bool TolkIsLoadedDelegate();
    private delegate bool TolkHasSpeechDelegate();
    private delegate bool TolkHasBrailleDelegate();
    private delegate IntPtr TolkDetectScreenReaderDelegate();
    private delegate bool TolkOutputDelegate([MarshalAs(UnmanagedType.LPWStr)] string text, bool interrupt);
    private delegate bool TolkSpeakDelegate([MarshalAs(UnmanagedType.LPWStr)] string text, bool interrupt);
    private delegate bool TolkBrailleDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);
    private delegate bool TolkIsSpeakingDelegate();
    private delegate bool TolkSilenceDelegate();
    private delegate void TolkTrySAPIDelegate(bool trySAPI);

    private readonly object _syncRoot = new();

    private bool _initialized;
    private bool _available;
    private bool _unavailableLogged;
    private IntPtr _libraryHandle;
    private string? _detectedScreenReader;
    private string? _lastMessage;
    private string? _lastError;

    // Function pointers
    private TolkLoadDelegate? _load;
    private TolkUnloadDelegate? _unload;
    private TolkIsLoadedDelegate? _isLoaded;
    private TolkHasSpeechDelegate? _hasSpeech;
    private TolkDetectScreenReaderDelegate? _detectScreenReader;
    private TolkSpeakDelegate? _speak;
    private TolkSilenceDelegate? _silence;
    private TolkTrySAPIDelegate? _trySAPI;

    public string Name => _detectedScreenReader ?? "Tolk";

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
            _detectedScreenReader = null;
        }

        try
        {
            var triedPaths = new List<string>();
            foreach (string candidate in EnumerateCandidatePaths())
            {
                triedPaths.Add(candidate);
                if (TryLoad(candidate))
                {
                    return;
                }
            }

            _lastError = $"Unable to locate {TolkLibraryName}";
            TerrariaAccess.Instance?.Logger.Warn($"[Tolk] Unable to locate {TolkLibraryName}. Searched paths: {string.Join(", ", triedPaths)}");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            TerrariaAccess.Instance?.Logger.Error($"[Tolk] Initialization failed: {ex.Message}");
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
                    _silence?.Invoke();
                    _unload?.Invoke();
                }
                catch
                {
                    // ignore shutdown errors
                }

                NativeLibrary.Free(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            _available = false;
            _unavailableLogged = false;
            _load = null;
            _unload = null;
            _isLoaded = null;
            _hasSpeech = null;
            _detectScreenReader = null;
            _speak = null;
            _silence = null;
            _trySAPI = null;
            _lastMessage = null;
            _lastError = null;
            _detectedScreenReader = null;
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
                // Log once per session to help diagnose speech issues
                if (_lastError is not null && !_unavailableLogged)
                {
                    _unavailableLogged = true;
                    TerrariaAccess.Instance?.Logger.Warn($"[Tolk] Speech unavailable: {_lastError}");
                }

                return;
            }

            try
            {
                // interrupt=false means queue the speech
                bool result = _speak(message, false);
                _lastMessage = message;
                if (!result)
                {
                    _lastError = "Speak returned false";
                    TerrariaAccess.Instance?.Logger.Warn("[Tolk] Speak returned false. Disabling speech output until restart.");
                    _available = false;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                TerrariaAccess.Instance?.Logger.Warn($"[Tolk] Speak threw {ex.Message}. Disabling speech output.");
                _available = false;
            }
        }
    }

    public void Interrupt()
    {
        lock (_syncRoot)
        {
            if (!_available || _silence is null)
            {
                return;
            }

            try
            {
                _silence();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                TerrariaAccess.Instance?.Logger.Debug($"[Tolk] Silence threw {ex.Message}. Disabling speech output.");
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

                // Get function pointers
                _load = Marshal.GetDelegateForFunctionPointer<TolkLoadDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_Load"));
                _unload = Marshal.GetDelegateForFunctionPointer<TolkUnloadDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_Unload"));
                _isLoaded = Marshal.GetDelegateForFunctionPointer<TolkIsLoadedDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_IsLoaded"));
                _hasSpeech = Marshal.GetDelegateForFunctionPointer<TolkHasSpeechDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_HasSpeech"));
                _detectScreenReader = Marshal.GetDelegateForFunctionPointer<TolkDetectScreenReaderDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_DetectScreenReader"));
                _speak = Marshal.GetDelegateForFunctionPointer<TolkSpeakDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_Speak"));
                _silence = Marshal.GetDelegateForFunctionPointer<TolkSilenceDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_Silence"));
                _trySAPI = Marshal.GetDelegateForFunctionPointer<TolkTrySAPIDelegate>(
                    NativeLibrary.GetExport(handle, "Tolk_TrySAPI"));

                // Check companion DLL presence for diagnostics
                string tolkDir = Path.GetDirectoryName(libraryPath) ?? "";
                string nvdaDll = Path.Combine(tolkDir, "nvdaControllerClient64.dll");
                string sapiDll = Path.Combine(tolkDir, "SAAPI64.dll");

                bool hasNvdaDll = File.Exists(nvdaDll);
                bool hasSapiDll = File.Exists(sapiDll);

                TerrariaAccess.Instance?.Logger.Info($"[Tolk] Companion DLLs in {tolkDir}:");
                TerrariaAccess.Instance?.Logger.Info($"[Tolk]   nvdaControllerClient64.dll: {(hasNvdaDll ? "FOUND" : "MISSING")}");
                TerrariaAccess.Instance?.Logger.Info($"[Tolk]   SAAPI64.dll: {(hasSapiDll ? "FOUND" : "MISSING")}");

                // Check if NVDA process is running before attempting detection
                int nvdaProcessCount = 0;
                try
                {
                    Process[] nvdaProcesses = Process.GetProcessesByName("nvda");
                    nvdaProcessCount = nvdaProcesses.Length;
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] NVDA process check: {nvdaProcessCount} instance(s) found");
                    foreach (var proc in nvdaProcesses)
                    {
                        TerrariaAccess.Instance?.Logger.Info($"[Tolk]   - PID {proc.Id}, Started: {proc.StartTime:HH:mm:ss}, SessionId: {proc.SessionId}");
                        proc.Dispose();
                    }

                    // Log current process info for comparison
                    using var currentProc = Process.GetCurrentProcess();
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] Current process: PID {currentProc.Id}, 64-bit: {Environment.Is64BitProcess}");
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] Session ID: {currentProc.SessionId}");

                    // Warn if NVDA is running but companion DLL is missing
                    if (nvdaProcessCount > 0 && !hasNvdaDll)
                    {
                        TerrariaAccess.Instance?.Logger.Warn("[Tolk] NVDA is running but nvdaControllerClient64.dll is MISSING! " +
                            "Tolk cannot communicate with NVDA without this file. " +
                            $"Please copy nvdaControllerClient64.dll to: {tolkDir}");
                    }
                }
                catch (Exception ex)
                {
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] NVDA process check failed: {ex.Message}");
                }

                // Add Tolk directory to DLL search path so Tolk can find companion DLLs
                // (nvdaControllerClient64.dll, SAAPI64.dll, etc.) when it internally calls LoadLibrary
                bool dllDirWasSet = false;
                if (!string.IsNullOrEmpty(tolkDir))
                {
                    dllDirWasSet = SetDllDirectoryW(tolkDir);
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] SetDllDirectory('{tolkDir}') = {dllDirWasSet}");
                    if (!dllDirWasSet)
                    {
                        int error = Marshal.GetLastWin32Error();
                        TerrariaAccess.Instance?.Logger.Warn($"[Tolk] SetDllDirectory failed with error code {error}");
                    }
                }

                try
                {
                // First try without SAPI to prefer real screen readers
                TerrariaAccess.Instance?.Logger.Info("[Tolk] Calling Tolk_TrySAPI(false) to prefer real screen readers...");
                _trySAPI(false);

                TerrariaAccess.Instance?.Logger.Info("[Tolk] Calling Tolk_Load()...");
                _load();

                bool isLoaded = _isLoaded();
                TerrariaAccess.Instance?.Logger.Info($"[Tolk] Tolk_IsLoaded() = {isLoaded}");

                if (!isLoaded)
                {
                    _lastError = "Tolk_Load succeeded but Tolk_IsLoaded returned false";
                    TerrariaAccess.Instance?.Logger.Warn($"[Tolk] Library loaded from {libraryPath}, but Tolk_IsLoaded returned false.");
                    CleanupFailedLoad();
                    return false;
                }

                bool hasSpeechInitial = _hasSpeech();
                TerrariaAccess.Instance?.Logger.Info($"[Tolk] Tolk_HasSpeech() = {hasSpeechInitial} (without SAPI)");

                // Get detected screen reader name even if hasSpeech is false
                IntPtr initialNamePtr = _detectScreenReader();
                string? initialDetected = initialNamePtr != IntPtr.Zero ? Marshal.PtrToStringUni(initialNamePtr) : null;
                TerrariaAccess.Instance?.Logger.Info($"[Tolk] Tolk_DetectScreenReader() = '{initialDetected ?? "(null)"}' (without SAPI)");

                // If NVDA is running but hasSpeech is false, retry with delay
                // (NVDA's COM server may not be fully initialized when tModLoader starts)
                if (!hasSpeechInitial && nvdaProcessCount > 0 && hasNvdaDll)
                {
                    for (int retry = 1; retry <= 3; retry++)
                    {
                        TerrariaAccess.Instance?.Logger.Info($"[Tolk] NVDA detected but not responding. Retry {retry}/3 after 500ms...");
                        Thread.Sleep(500);

                        _unload();
                        _trySAPI(false);
                        _load();

                        hasSpeechInitial = _hasSpeech();
                        if (hasSpeechInitial)
                        {
                            TerrariaAccess.Instance?.Logger.Info($"[Tolk] NVDA responded on retry {retry}");
                            break;
                        }
                    }
                }

                // If no screen reader with speech found, try again with SAPI fallback
                if (!hasSpeechInitial)
                {
                    if (nvdaProcessCount > 0)
                    {
                        TerrariaAccess.Instance?.Logger.Warn("[Tolk] NVDA is running but Tolk cannot communicate with it. " +
                            "Falling back to SAPI. Check that nvdaControllerClient64.dll is in the same folder as Tolk.dll.");
                    }
                    else
                    {
                        TerrariaAccess.Instance?.Logger.Info("[Tolk] No screen reader detected, enabling SAPI fallback...");
                    }

                    _unload();
                    _trySAPI(true);
                    _load();

                    bool hasSpeechWithSapi = _hasSpeech();
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] Tolk_HasSpeech() = {hasSpeechWithSapi} (with SAPI)");

                    IntPtr sapiNamePtr = _detectScreenReader();
                    string? sapiDetected = sapiNamePtr != IntPtr.Zero ? Marshal.PtrToStringUni(sapiNamePtr) : null;
                    TerrariaAccess.Instance?.Logger.Info($"[Tolk] Tolk_DetectScreenReader() = '{sapiDetected ?? "(null)"}' (with SAPI)");
                }

                if (!_hasSpeech())
                {
                    _lastError = "No screen reader with speech capability detected";
                    TerrariaAccess.Instance?.Logger.Warn("[Tolk] No screen reader with speech capability detected.");
                    CleanupFailedLoad();
                    return false;
                }

                // Get detected screen reader name
                IntPtr namePtr = _detectScreenReader();
                _detectedScreenReader = namePtr != IntPtr.Zero
                    ? Marshal.PtrToStringUni(namePtr)
                    : null;

                _available = true;
                _lastError = null;
                TerrariaAccess.Instance?.Logger.Info($"[Tolk] Connected to {_detectedScreenReader ?? "unknown screen reader"} via {libraryPath}.");
                return true;
                }
                finally
                {
                    // Reset DLL directory to default to avoid affecting other code
                    if (dllDirWasSet)
                    {
                        SetDllDirectoryW(null);
                    }
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                TerrariaAccess.Instance?.Logger.Debug($"[Tolk] Failed to load {libraryPath}: {ex.Message}");
                CleanupFailedLoad();
                return false;
            }
        }
    }

    private void CleanupFailedLoad()
    {
        if (_libraryHandle != IntPtr.Zero)
        {
            try
            {
                _unload?.Invoke();
            }
            catch
            {
                // ignore cleanup errors
            }

            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
        }

        _load = null;
        _unload = null;
        _isLoaded = null;
        _hasSpeech = null;
        _detectScreenReader = null;
        _speak = null;
        _silence = null;
        _trySAPI = null;
        _available = false;
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        // App base directory (tModLoader.exe location) - most likely location
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, TolkLibraryName);

        // Game directory (Terraria.Main assembly location)
        string? gameDir = Path.GetDirectoryName(typeof(Main).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(gameDir) && !string.Equals(gameDir, baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(gameDir, TolkLibraryName);
        }

        // tModLoader save path locations
        string? savePath = Main.SavePath;
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            // ModSources location (for development)
            yield return Path.Combine(savePath, "ModSources", "TerrariaAccess", "Libraries", TolkLibraryName);

            // Save path root
            yield return Path.Combine(savePath, TolkLibraryName);

            // Mods folder
            yield return Path.Combine(savePath, "Mods", TolkLibraryName);
        }

        // Try loading by name last (system PATH fallback)
        yield return TolkLibraryName;
    }
}
