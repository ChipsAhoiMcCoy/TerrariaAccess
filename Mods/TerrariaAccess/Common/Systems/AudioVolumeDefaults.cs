#nullable enable

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Centralizes global volume scaling for in-world audio cues so tones share a consistent loudness ceiling.
/// All mod sounds respect Terraria's main sound volume setting (Main.soundVolume).
/// </summary>
internal static class AudioVolumeDefaults
{
    /// <summary>
    /// Global scaling factor applied to all mod audio cues for consistent loudness.
    /// </summary>
    internal const float WorldCueVolumeScale = 0.85f;
}
