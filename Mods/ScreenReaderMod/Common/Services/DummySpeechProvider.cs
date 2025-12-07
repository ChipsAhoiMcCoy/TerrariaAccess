#nullable enable

namespace ScreenReaderMod.Common.Services;

/// <summary>
/// A no-op speech provider used as a fallback on unsupported platforms.
/// This allows the mod to load without errors even when no speech synthesis is available.
/// </summary>
internal sealed class DummySpeechProvider : ISpeechProvider
{
    private bool _initialized;

    public string Name => "Dummy";

    public bool IsAvailable => false;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        _initialized = true;
        ScreenReaderMod.Instance?.Logger.Info("[Dummy] Speech provider initialized (no speech output available on this platform).");
    }

    public void Shutdown()
    {
        _initialized = false;
    }

    public void Speak(string message)
    {
        // No-op
    }

    public void Interrupt()
    {
        // No-op
    }

    public SpeechProviderSnapshot GetSnapshot()
    {
        return new SpeechProviderSnapshot(Name, _initialized, false, null, "Platform not supported");
    }
}
