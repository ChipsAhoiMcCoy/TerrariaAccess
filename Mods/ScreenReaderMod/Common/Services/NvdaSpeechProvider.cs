#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Terraria;

namespace ScreenReaderMod.Common.Services;

internal static class NvdaSpeechProvider
{
    private const string NvdaLibraryName = "nvdaControllerClient64.dll";

    private delegate int NvdaSpeakDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);
    private delegate int NvdaCancelDelegate();
    private delegate int NvdaTestDelegate();

    private static bool _initialized;
    private static bool _available;
    private static IntPtr _libraryHandle;
    private static NvdaSpeakDelegate? _speak;
    private static NvdaCancelDelegate? _cancel;
    private static NvdaTestDelegate? _test;

    public static void Interrupt()
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
            ScreenReaderMod.Instance?.Logger.Debug($"[NVDA] Cancel threw {ex.Message}. Disabling NVDA output.");
            _available = false;
        }
    }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            foreach (string candidate in EnumerateCandidatePaths())
            {
                if (TryLoad(candidate))
                {
                    return;
                }
            }

            ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Unable to locate {NvdaLibraryName}. Copy it next to tModLoader.exe or into Mods/ScreenReaderMod/Libraries.");
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Error($"[NVDA] Initialization failed: {ex.Message}");
        }
    }

    public static void Shutdown()
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
        _initialized = false;
    }

    public static void Speak(string message)
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
            if (result != 0)
            {
                ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Speak returned code {result}. Disabling NVDA output until restart.");
                _available = false;
            }
        }
        catch (Exception ex)
        {
            ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Speak threw {ex.Message}. Disabling NVDA output.");
            _available = false;
        }
    }

    private static bool TryLoad(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return false;
        }

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
                ScreenReaderMod.Instance?.Logger.Info($"[NVDA] Connected via {libraryPath}.");
                return true;
            }

            ScreenReaderMod.Instance?.Logger.Warn($"[NVDA] Library loaded from {libraryPath}, but NVDA not running (code {status}).");
        }
        catch (Exception ex)
        {
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
        return false;
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
