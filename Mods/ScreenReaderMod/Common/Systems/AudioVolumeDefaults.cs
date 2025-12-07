#nullable enable

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Centralizes global volume scaling for in-world audio cues so tones share a consistent loudness ceiling.
/// </summary>
internal static class AudioVolumeDefaults
{
    internal const float WorldCueVolumeScale = 0.85f;
}
