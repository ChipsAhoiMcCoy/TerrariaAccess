#nullable enable

namespace ScreenReaderMod.Common.Services;

internal interface ISpeechProvider
{
    string Name { get; }

    bool IsAvailable { get; }

    bool IsInitialized { get; }

    void Initialize();

    void Shutdown();

    void Speak(string message);

    void Interrupt();

    SpeechProviderSnapshot GetSnapshot();
}

internal readonly record struct SpeechProviderSnapshot(
    string Name,
    bool Initialized,
    bool Available,
    string? LastMessage,
    string? LastError);
